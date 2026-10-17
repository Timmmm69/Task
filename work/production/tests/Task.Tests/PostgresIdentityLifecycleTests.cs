using System.Security.Cryptography;
using System.Text;
using Npgsql;
using Task.Application.Security;
using Task.Infrastructure.Persistence;
using Task.Infrastructure.Postgres;

namespace Task.Tests;

public sealed class PostgresIdentityLifecycleTests
{
    [RequiresPostgresFact]
    public async global::System.Threading.Tasks.Task EveryTransitionStatePairUsesServerRulesAndReplaysWithoutExtraMutation()
    {
        using var db = new Database();
        var store = new PostgresUserAccountCommandStore(db.RuntimeSource);
        foreach (var status in Enum.GetValues<UserAccountStatus>())
        foreach (var action in Enum.GetValues<UserAccountTransition>())
        {
            var targetId = Guid.NewGuid(); db.SeedUser(targetId);
            var stored = status switch { UserAccountStatus.PendingActivation => "pending",
                UserAccountStatus.Active => "active", UserAccountStatus.Blocked => "blocked", _ => "deactivated" };
            db.Sql("UPDATE iam.user_accounts SET account_status=$2 WHERE id=$1", targetId, stored);
            var allowed = action switch
            {
                UserAccountTransition.Activate => status == UserAccountStatus.PendingActivation,
                UserAccountTransition.Block => status is UserAccountStatus.Active or UserAccountStatus.Blocked,
                UserAccountTransition.Unblock => status is UserAccountStatus.Blocked or UserAccountStatus.Active,
                UserAccountTransition.Deactivate => status != UserAccountStatus.Deactivated,
                UserAccountTransition.Reactivate => status is UserAccountStatus.Deactivated or UserAccountStatus.Active,
                _ => false,
            };
            var context = db.Context("user." + action.ToString().ToLowerInvariant());
            var result = await store.TransitionAsync(context, targetId, 1, action, "Lifecycle verification");
            Assert.Equal(allowed ? IdentityCommandDisposition.Executed : IdentityCommandDisposition.InvalidStateTransition, result.Disposition);
            if (!allowed) continue;
            var target = action == UserAccountTransition.Block ? UserAccountStatus.Blocked
                : action == UserAccountTransition.Deactivate ? UserAccountStatus.Deactivated : UserAccountStatus.Active;
            Assert.Equal(target, result.User!.AccountStatus);
            Assert.Equal(status == target ? 1 : 2, result.User.Version);
            var auditCount = db.Count("governance.audit_entries", targetId);
            var repeated = await store.TransitionAsync(context, targetId, 1, action, "Lifecycle verification");
            Assert.Equal(IdentityCommandDisposition.Replayed, repeated.Disposition);
            Assert.Equal(result.User, repeated.User);
            Assert.Equal(auditCount, db.Count("governance.audit_entries", targetId));
        }
    }

    [RequiresPostgresFact]
    public async global::System.Threading.Tasks.Task LastActiveAdministratorCannotBeBlockedOrDeactivated()
    {
        using var db = new Database();
        var role = Guid.NewGuid();
        db.Sql("INSERT INTO iam.roles(id,organization_id,code,display_name) VALUES($1,$2,'lifecycle_admin','Lifecycle admin')", role, db.Organization);
        db.Sql("INSERT INTO iam.role_permissions(role_id,permission_code,effect) VALUES($1,'organization.manage','grant')", role);
        db.Sql("INSERT INTO iam.user_roles(user_account_id,role_id) VALUES($1,$2)", db.User, role);
        db.Sql("UPDATE iam.user_accounts SET account_status='active' WHERE id=$1", db.User);
        var session = db.SeedSession(db.User);
        var store = new PostgresUserAccountCommandStore(db.RuntimeSource);
        foreach (var action in new[] { UserAccountTransition.Block, UserAccountTransition.Deactivate })
        {
            var result = await store.TransitionAsync(db.Context("user." + action.ToString().ToLowerInvariant()), db.User, 1, action, "Guard verification");
            Assert.Equal(IdentityCommandDisposition.InvalidStateTransition, result.Disposition);
            Assert.False(db.Bool("SELECT revoked_at IS NOT NULL FROM iam.sessions WHERE id=$1", session));
            var user = await new PostgresUserAccountReadStore(db.RuntimeSource).GetByIdAsync(db.Organization, db.User);
            Assert.Equal(UserAccountStatus.Active, user!.AccountStatus); Assert.Equal(1, user.Version);
            Assert.Equal(0, db.Count("governance.audit_entries", db.User));
        }
    }

    [RequiresPostgresFact]
    public async global::System.Threading.Tasks.Task UserLifecycle_IsDurableVersionedIdempotentAndRevokesCredentials()
    {
        using var db = new Database();
        var users = new PostgresUserAccountCommandStore(db.RuntimeSource);
        var reads = new PostgresUserAccountReadStore(db.RuntimeSource);
        var create = db.Context("user.create");
        var input = new UserAccountCreateCommand("Test Employee", "Test", "Employee", "new.employee", "employee@example.test", null, null, new(new string('h', 64), "{}"));
        var result = await users.CreateAsync(create, input);
        Assert.Equal(IdentityCommandDisposition.Executed, result.Disposition);
        var user = result.User!;
        Assert.Equal(UserAccountStatus.PendingActivation, user.AccountStatus);
        Assert.Equal(user, (await users.CreateAsync(create, input)).User);
        Assert.Equal(1, db.Count("governance.audit_entries", user.Id));
        Assert.Equal(IdentityCommandDisposition.IdempotencyKeyReused, (await users.CreateAsync(create with { RequestHash = SHA256.HashData([42]) }, input)).Disposition);
        Assert.Equal(IdentityCommandDisposition.DuplicateResource, (await users.CreateAsync(db.Context("user.create"), input)).Disposition);
        Assert.Null(await reads.GetByIdAsync(Guid.NewGuid(), user.Id));
        var page = await reads.GetPageAsync(new(db.Organization, null, 1, null));
        Assert.Contains(page.Items, item => item.Id == user.Id);
        Assert.Equal(IdentityCommandDisposition.VersionConflict, (await users.TransitionAsync(db.Context("user.activate"), user.Id, 99, UserAccountTransition.Activate, null)).Disposition);
        result = await users.TransitionAsync(db.Context("user.activate"), user.Id, 1, UserAccountTransition.Activate, null);
        Assert.Equal(UserAccountStatus.Active, result.User!.AccountStatus);
        var patch = new UserAccountPatchCommand(new(true, "Renamed"), new(false, null), new(false, null), new(false, null), new(true, null), new(false, null), new(true, "Developer"));
        result = await users.UpdateAsync(db.Context("user.update"), user.Id, 2, patch);
        Assert.Equal("Renamed", result.User!.DisplayName); Assert.Null(result.User.WorkEmail);
        var session = db.SeedSession(user.Id);
        var reset = db.Context("user.reset-password");
        var receipt = await users.ResetPasswordAsync(reset, user.Id, 3, new(new string('p', 64), "{}"));
        Assert.Equal(IdentityCommandDisposition.Executed, receipt.Disposition);
        Assert.Equal(receipt.ExpiresAtUtc, (await users.ResetPasswordAsync(reset, user.Id, 3, new(new string('q', 64), "{}"))).ExpiresAtUtc);
        Assert.True(db.Bool("SELECT revoked_at IS NOT NULL FROM iam.sessions WHERE id=$1", session));
        Assert.True(db.Bool("SELECT bool_and(revoked_at IS NOT NULL) FROM iam.refresh_tokens WHERE session_id=$1", session));
        var lookup = await new PostgresAccountLookupStore(db.RuntimeSource).FindByLoginAsync("new.employee");
        Assert.True(lookup!.MustChangePassword); Assert.Equal(receipt.ExpiresAtUtc, lookup.TemporaryPasswordExpiresAtUtc);
        result = await users.TransitionAsync(db.Context("user.block"), user.Id, 4, UserAccountTransition.Block, "Security incident");
        Assert.Equal(UserAccountStatus.Blocked, result.User!.AccountStatus);
        result = await users.TransitionAsync(db.Context("user.unblock"), user.Id, 5, UserAccountTransition.Unblock, null);
        Assert.Equal(UserAccountStatus.Active, result.User!.AccountStatus);
        result = await users.TransitionAsync(db.Context("user.deactivate"), user.Id, 6, UserAccountTransition.Deactivate, "Employee left");
        Assert.Equal(UserAccountStatus.Deactivated, result.User!.AccountStatus);
        result = await users.TransitionAsync(db.Context("user.reactivate"), user.Id, 7, UserAccountTransition.Reactivate, null);
        Assert.Equal(UserAccountStatus.Active, result.User!.AccountStatus);
        Assert.Equal(db.Count("governance.domain_events", user.Id), db.Count("governance.audit_entries", user.Id));
        Assert.Equal(8, db.Count("governance.domain_events", user.Id));
    }

    [RequiresPostgresFact]
    public async global::System.Threading.Tasks.Task Devices_EnforceOwnershipAndRevokeSessionsAtomically()
    {
        using var db = new Database();
        var store = new PostgresDeviceRegistrationStore(db.RuntimeSource);
        var session = db.SeedSession(db.User);
        var device = db.GuidValue("SELECT device_id FROM iam.sessions WHERE id=$1", session);
        var sessions = new PostgresSessionRepository(db.RuntimeSource);
        var ownPage = sessions.GetSessionPage(db.Organization, db.User, 1);
        Assert.Equal(db.User, Assert.Single(ownPage.Items).UserAccountId);
        Assert.Equal(device, ownPage.Items[0].DeviceId);
        Assert.Empty(sessions.GetSessionPage(db.Organization, db.OtherUser, 1).Items);
        Assert.Single(sessions.GetSessionPage(db.Organization, null, 1).Items);
        var read = (await store.GetReadModelAsync(db.Organization, device))!;
        var command = db.Context("device.update") with { ActorUserId = db.OtherUser };
        Assert.Equal(IdentityCommandDisposition.NotFound, (await store.PatchAsync(command, device, read.Version, new("Hacked", null, null, true, false, false))).Disposition);
        Assert.Empty((await store.GetPageAsync(db.Organization, db.OtherUser, false, null, 1, null)).Items);
        command = command with { CanManageAllDevices = true };
        var patch = await store.PatchAsync(command, device, read.Version, new("Workstation", null, "1.2.3", true, false, true));
        Assert.Equal(IdentityCommandDisposition.Executed, patch.Disposition);
        Assert.Equal("Workstation", patch.Device!.DeviceName);
        Assert.Equal(IdentityCommandDisposition.Replayed, (await store.PatchAsync(command, device, read.Version, new("Workstation", null, "1.2.3", true, false, true))).Disposition);
        Assert.Equal(IdentityCommandDisposition.NotFound, (await store.PatchAsync(command with { CanManageAllDevices = false }, device, read.Version, new("Workstation", null, "1.2.3", true, false, true))).Disposition);
        Assert.False(await store.HeartbeatAsync(db.Organization, db.OtherUser, device, "1.2.4", null, DateTimeOffset.UtcNow));
        Assert.True(await store.HeartbeatAsync(db.Organization, db.User, device, "1.2.4", null, DateTimeOffset.UtcNow));
        var revoke = db.Context("device.revoke");
        var result = await store.RevokeAsync(revoke, device, patch.Device.Version, "Lost workstation");
        Assert.Equal(IdentityCommandDisposition.Executed, result.Disposition);
        Assert.NotNull(result.Device!.RevokedAtUtc);
        Assert.True(db.Bool("SELECT revoked_at IS NOT NULL FROM iam.sessions WHERE id=$1", session));
        Assert.True(db.Bool("SELECT bool_and(revoked_at IS NOT NULL) FROM iam.refresh_tokens WHERE session_id=$1", session));
        Assert.False(await store.HeartbeatAsync(db.Organization, db.User, device, "1.2.4", null, DateTimeOffset.UtcNow));
        Assert.Equal(SessionRequestState.SessionRevoked, sessions.GetSessionRequestState(db.Organization, session, 1, 1));
        Assert.Equal(IdentityCommandDisposition.Replayed, (await store.RevokeAsync(revoke, device, patch.Device.Version, "Lost workstation")).Disposition);
        Assert.Equal(2, db.Count("governance.audit_entries", device));
    }

    private sealed class Database : IDisposable
    {
        private readonly NpgsqlDataSource admin;
        private readonly NpgsqlDataSource source;
        private readonly string name = "task_api01_" + Guid.NewGuid().ToString("N");
        public NpgsqlDataSource RuntimeSource { get; }
        public Guid Organization { get; } = Guid.NewGuid();
        public Guid User { get; } = Guid.NewGuid();
        public Guid OtherUser { get; } = Guid.NewGuid();
        public Database()
        {
            var connection = Environment.GetEnvironmentVariable(TaskCreateCommandTests.ConnectionEnvironmentVariable)!;
            admin = NpgsqlDataSource.Create(connection);
            using (var cmd = admin.CreateCommand($"CREATE DATABASE {name}")) cmd.ExecuteNonQuery();
            var builder = new NpgsqlConnectionStringBuilder(connection) { Database = name };
            source = NpgsqlDataSource.Create(builder.ConnectionString);
            new TaskPersistenceMigrator(source).ApplyPending();
            Sql("INSERT INTO core.organizations(id,code,name,default_time_zone) VALUES($1,$2,'Identity test','UTC')", Organization, Organization.ToString("N"));
            SeedUser(User); SeedUser(OtherUser);
            Sql($"CREATE ROLE {name}_role LOGIN PASSWORD 'isolated-test-only' NOSUPERUSER NOCREATEDB NOCREATEROLE");
            var assembly = typeof(PostgresIdentityLifecycleTests).Assembly;
            using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("grant-runtime.sql")))!;
            using var reader = new StreamReader(stream);
            Sql(string.Join('\n', reader.ReadToEnd().Split('\n').Where(line => !line.TrimStart().StartsWith('\\'))).Replace("task_runtime", name + "_role"));
            builder.Username = name + "_role"; builder.Password = "isolated-test-only";
            RuntimeSource = NpgsqlDataSource.Create(builder.ConnectionString);
        }
        public void SeedUser(Guid user)
        {
            var profile = Guid.NewGuid();
            Sql("INSERT INTO core.objects(id,organization_id,object_type,created_by,updated_by,created_at,updated_at) VALUES($1,$3,'employee_profile',$2,$2,clock_timestamp(),clock_timestamp()),($2,$3,'user_account',$2,$2,clock_timestamp(),clock_timestamp())", profile, user, Organization);
            Sql("INSERT INTO org.employee_profiles(id,organization_id,first_name,last_name,display_name,preferred_time_zone) VALUES($1,$2,'Test','User','Test User','UTC')", profile, Organization);
            Sql("INSERT INTO iam.user_accounts(id,organization_id,employee_profile_id,login,password_hash,password_parameters) VALUES($1,$2,$3,$4,$5,'{}')", user, Organization, profile, user.ToString("N"), new string('h', 64));
            Sql("INSERT INTO iam.authorization_scope_versions(user_account_id,version) VALUES($1,1)", user);
        }
        public Guid SeedSession(Guid user)
        {
            var device = Guid.NewGuid(); var session = Guid.NewGuid();
            Sql("INSERT INTO core.objects(id,organization_id,object_type,created_by,updated_by,created_at,updated_at) VALUES($1,$2,'device',$3,$3,clock_timestamp(),clock_timestamp())", device, Organization, user);
            Sql("INSERT INTO iam.devices(id,organization_id,user_account_id,device_fingerprint_hash) VALUES($1,$2,$3,$4)", device, Organization, user, device.ToString("N"));
            Sql("INSERT INTO iam.sessions(id,organization_id,user_account_id,device_id,credential_version,authorization_scope_version,idle_expires_at,absolute_expires_at) SELECT $1,$2,$3,$4,credential_version,1,clock_timestamp()+interval '8 hours',clock_timestamp()+interval '30 days' FROM iam.user_accounts WHERE id=$3", session, Organization, user, device);
            Sql("INSERT INTO iam.refresh_tokens(id,session_id,token_hash,expires_at) VALUES($1,$2,$3,clock_timestamp()+interval '1 day')", Guid.NewGuid(), session, new string('a', 64));
            return session;
        }
        public IdentityCommandContext Context(string operation) => new(Organization, User, null, Guid.NewGuid(), operation, Guid.NewGuid().ToString("N"), SHA256.HashData(Encoding.UTF8.GetBytes(operation)));
        public void Sql(string sql, params object[] values) { using var cmd = source.CreateCommand(sql); foreach (var v in values) cmd.Parameters.Add(new NpgsqlParameter { Value = v }); cmd.ExecuteNonQuery(); }
        private object Scalar(string sql, object value) { using var cmd = source.CreateCommand(sql); cmd.Parameters.Add(new NpgsqlParameter { Value = value }); return cmd.ExecuteScalar()!; }
        public bool Bool(string sql, object value) => (bool)Scalar(sql, value);
        public Guid GuidValue(string sql, object value) => (Guid)Scalar(sql, value);
        public long Count(string table, Guid id) => (long)Scalar($"SELECT count(*) FROM {table} WHERE {(table.EndsWith("audit_entries") ? "object_id" : "aggregate_id")}=$1", id);
        public void Dispose() { RuntimeSource.Dispose(); source.Dispose(); using (var cmd = admin.CreateCommand($"DROP DATABASE {name} WITH (FORCE)")) cmd.ExecuteNonQuery(); using (var cmd = admin.CreateCommand($"DROP ROLE {name}_role")) cmd.ExecuteNonQuery(); admin.Dispose(); }
    }
}
