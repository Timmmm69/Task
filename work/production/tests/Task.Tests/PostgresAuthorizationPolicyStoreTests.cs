using Npgsql;
using Task.Application.Security;
using Task.Infrastructure.Persistence;
using Task.Infrastructure.Postgres;
using Xunit.Abstractions;

namespace Task.Tests;

public sealed class PostgresAuthorizationPolicyStoreTests
{
    private const string ConnectionEnvironmentVariable = "TASK_POSTGRES_TEST_ADMIN_CONNECTION";
    private readonly ITestOutputHelper _output;

    public PostgresAuthorizationPolicyStoreTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RealPostgres_AuthorizationPolicyStoreReadsRulesByOrgAndCode()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            _output.WriteLine(
                $"NOT RUN: set {ConnectionEnvironmentVariable} to execute the real PostgreSQL integration gate.");
            return;
        }

        var databaseName = $"task_authz_{Guid.NewGuid():N}";
        using var adminDataSource = NpgsqlDataSource.Create(adminConnectionString);
        CreateDatabase(adminDataSource, databaseName);

        try
        {
            var databaseConnection = new NpgsqlConnectionStringBuilder(adminConnectionString)
            {
                Database = databaseName,
            }.ConnectionString;

            using var dataSource = NpgsqlDataSource.Create(databaseConnection);
            new TaskPersistenceMigrator(dataSource).ApplyPending();

            var organizationA = Guid.NewGuid();
            var organizationB = Guid.NewGuid();
            var userA = Guid.NewGuid();
            var userB = Guid.NewGuid();
            SeedPermission(dataSource, "tasks.view");
            SeedPermission(dataSource, "tasks.edit");
            SeedUser(dataSource, organizationA, userA);
            SeedUser(dataSource, organizationB, userB);

            var grantRoleA = Guid.NewGuid();
            var denyRoleA = Guid.NewGuid();
            var grantRoleB = Guid.NewGuid();
            SeedRole(dataSource, grantRoleA, organizationA, "task_viewer");
            SeedRole(dataSource, denyRoleA, organizationA, "task_auditor");
            SeedRole(dataSource, grantRoleB, organizationB, "task_viewer_b");
            SeedRolePermission(dataSource, grantRoleA, "tasks.view", "grant");
            SeedRolePermission(dataSource, denyRoleA, "tasks.view", "deny");
            SeedRolePermission(dataSource, denyRoleA, "tasks.edit", "grant");
            SeedRolePermission(dataSource, grantRoleB, "tasks.view", "grant");
            SeedUserRole(dataSource, userA, grantRoleA);
            SeedUserRole(dataSource, userA, denyRoleA);
            SeedUserRole(dataSource, userB, grantRoleB);

            var store = new PostgresAuthorizationPolicyStore(dataSource);

            Assert.Equal(organizationA, await store.GetUserOrgAsync(userA));
            Assert.Equal(organizationB, await store.GetUserOrgAsync(userB));
            Assert.Null(await store.GetUserOrgAsync(Guid.NewGuid()));

            var grants = await store.GetUserGrantsAsync(organizationA, userA, "tasks.view");
            var grant = Assert.Single(grants);
            Assert.True(grant.HasDirectRoleMembership);

            var denies = await store.GetUserDeniesAsync(organizationA, userA, "tasks.view");
            var deny = Assert.Single(denies);
            Assert.True(deny.HasDirectRoleMembership);

            var otherCodeGrants = await store.GetUserGrantsAsync(organizationA, userA, "missing.code");
            Assert.Empty(otherCodeGrants);
            var otherCodeDenies = await store.GetUserDeniesAsync(organizationA, userA, "missing.code");
            Assert.Empty(otherCodeDenies);

            Assert.Empty(await store.GetUserGrantsAsync(organizationA, userB, "tasks.view"));
            Assert.Empty(await store.GetUserDeniesAsync(organizationA, userB, "tasks.view"));
            Assert.Empty(await store.GetUserGrantsAsync(organizationB, userA, "tasks.view"));
            Assert.Empty(await store.GetUserDeniesAsync(organizationB, userA, "tasks.view"));

            await Assert.ThrowsAsync<ArgumentException>(() => store.GetUserOrgAsync(Guid.Empty));
            await Assert.ThrowsAsync<ArgumentException>(() => store.GetUserGrantsAsync(Guid.Empty, userA, "tasks.view"));
            await Assert.ThrowsAsync<ArgumentException>(() => store.GetUserGrantsAsync(organizationA, Guid.Empty, "tasks.view"));
            await Assert.ThrowsAsync<ArgumentException>(() => store.GetUserGrantsAsync(organizationA, userA, " "));
            await Assert.ThrowsAsync<ArgumentException>(() => store.GetUserDeniesAsync(organizationA, Guid.Empty, "tasks.view"));
            await Assert.ThrowsAsync<ArgumentException>(() => store.GetUserDeniesAsync(organizationA, userA, string.Empty));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            DropDatabase(adminDataSource, databaseName);
        }
    }

    private static void CreateDatabase(NpgsqlDataSource adminDataSource, string databaseName)
    {
        using var command = adminDataSource.CreateCommand($"CREATE DATABASE {databaseName};");
        command.ExecuteNonQuery();
    }

    private static void DropDatabase(NpgsqlDataSource adminDataSource, string databaseName)
    {
        using var command = adminDataSource.CreateCommand($"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE);");
        command.ExecuteNonQuery();
    }

    private static void SeedPermission(NpgsqlDataSource dataSource, string code)
    {
        using var command = dataSource.CreateCommand(
            """
            INSERT INTO iam.permissions (code, description)
            VALUES ($1, 'Integration test permission.')
            ON CONFLICT (code) DO NOTHING;
            """);
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = code });
        command.ExecuteNonQuery();
    }

    private static void SeedRole(NpgsqlDataSource dataSource, Guid roleId, Guid organizationId, string code)
    {
        using var command = dataSource.CreateCommand(
            """
            INSERT INTO iam.roles (id, organization_id, code, display_name, is_system)
            VALUES ($1, $2, $3, $4, false);
            """);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = roleId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = code });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = code });
        command.ExecuteNonQuery();
    }

    private static void SeedRolePermission(
        NpgsqlDataSource dataSource,
        Guid roleId,
        string permissionCode,
        string effect)
    {
        using var command = dataSource.CreateCommand(
            """
            INSERT INTO iam.role_permissions (role_id, permission_code, effect)
            VALUES ($1, $2, $3);
            """);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = roleId });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = permissionCode });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = effect });
        command.ExecuteNonQuery();
    }

    private static void SeedUserRole(NpgsqlDataSource dataSource, Guid userId, Guid roleId)
    {
        using var command = dataSource.CreateCommand(
            """
            INSERT INTO iam.user_roles (user_account_id, role_id)
            VALUES ($1, $2);
            """);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = roleId });
        command.ExecuteNonQuery();
    }

    private static void SeedUser(NpgsqlDataSource dataSource, Guid organizationId, Guid userId)
    {
        using (var organizationCommand = dataSource.CreateCommand(
            """
            INSERT INTO core.organizations (id, code, name, default_time_zone)
            VALUES ($1, $2, $3, $4);
            """))
        {
            organizationCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
            organizationCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = $"org-{organizationId:N}" });
            organizationCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = "Authorization Integration Organization" });
            organizationCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = "Europe/Minsk" });
            organizationCommand.ExecuteNonQuery();
        }

        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        using (var profileObjectCommand = dataSource.CreateCommand(
            """
            INSERT INTO core.objects (
                id, organization_id, object_type, version, created_at, created_by, updated_at, updated_by)
            VALUES ($1, $2, 'employee_profile', 1, $3, $4, $3, $4);
            """))
        {
            profileObjectCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
            profileObjectCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
            profileObjectCommand.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = now });
            profileObjectCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
            profileObjectCommand.ExecuteNonQuery();
        }

        using (var profileCommand = dataSource.CreateCommand(
            """
            INSERT INTO org.employee_profiles (
                id, organization_id, first_name, last_name, display_name, preferred_time_zone, locale)
            VALUES ($1, $2, $3, $4, $5, $6, $7);
            """))
        {
            profileCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
            profileCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
            profileCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = "Authorization" });
            profileCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = "User" });
            profileCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = "Authorization User" });
            profileCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = "Europe/Minsk" });
            profileCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = "ru-RU" });
            profileCommand.ExecuteNonQuery();
        }

        using (var userObjectCommand = dataSource.CreateCommand(
            """
            INSERT INTO core.objects (
                id, organization_id, object_type, version, created_at, created_by, updated_at, updated_by)
            VALUES ($1, $2, 'user_account', 1, $3, $4, $3, $4);
            """))
        {
            userObjectCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
            userObjectCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
            userObjectCommand.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = now });
            userObjectCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
            userObjectCommand.ExecuteNonQuery();
        }

        using (var userCommand = dataSource.CreateCommand(
            """
            INSERT INTO iam.user_accounts (
                id, organization_id, employee_profile_id, login, password_hash,
                password_parameters, account_status, must_change_password)
            VALUES ($1, $2, $3, $4, $5, '{}'::jsonb, 'active', false);
            """))
        {
            userCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
            userCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
            userCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
            userCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = $"seed-{userId:N}" });
            userCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = new string('x', 64) });
            userCommand.ExecuteNonQuery();
        }

        using (var scopeCommand = dataSource.CreateCommand(
            """
            INSERT INTO iam.authorization_scope_versions (user_account_id, version)
            VALUES ($1, 1);
            """))
        {
            scopeCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
            scopeCommand.ExecuteNonQuery();
        }
    }
}