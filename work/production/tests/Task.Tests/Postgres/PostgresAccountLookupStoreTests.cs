using Npgsql;
using Task.Application.Security;
using Task.Infrastructure.Persistence;
using Task.Infrastructure.Postgres;
using Xunit.Abstractions;

namespace Task.Tests.Postgres;

public sealed class PostgresAccountLookupStoreTests
{
    private const string ConnectionEnvironmentVariable = "TASK_POSTGRES_TEST_ADMIN_CONNECTION";
    private readonly ITestOutputHelper _output;

    public PostgresAccountLookupStoreTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RealPostgres_FindByLogin_OneHitZeroHitCollisionAndDefaultScope()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            _output.WriteLine(
                $"NOT RUN: set {ConnectionEnvironmentVariable} to execute the real PostgreSQL integration gate.");
            return;
        }

        var databaseName = $"task_lookup_{Guid.NewGuid():N}";
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

            var store = new PostgresAccountLookupStore(dataSource);

            await Assert.ThrowsAsync<ArgumentException>(() => store.FindByLoginAsync(" "));
            await Assert.ThrowsAsync<ArgumentException>(() => store.FindByLoginAsync(string.Empty));

            Assert.Null(await store.FindByLoginAsync("nobody-exists"));

            var organizationId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            const string login = "alice.lookup";
            var passwordHash = new string('h', 64);
            const string passwordParameters = "{\"memoryKiB\":65536}";
            SeedOrganizationAndUser(
                dataSource,
                organizationId,
                userId,
                Guid.NewGuid(),
                login,
                passwordHash,
                passwordParameters,
                credentialVersion: 3,
                accountStatus: "blocked",
                failedLoginCount: 4,
                lockedUntilUtc: DateTimeOffset.Parse("2030-01-15T12:00:00Z"),
                authorizationScopeVersion: 7,
                mustChangePassword: true);

            var hit = await store.FindByLoginAsync(login);
            Assert.NotNull(hit);
            Assert.Equal(organizationId, hit.OrganizationId);
            Assert.Equal(userId, hit.UserId);
            Assert.Equal(login, hit.Login);
            Assert.Equal(passwordHash, hit.PasswordHash);
            Assert.Equal("{\"memoryKiB\": 65536}", hit.PasswordParameters);
            Assert.Equal(3, hit.CredentialVersion);
            Assert.Equal(7, hit.AuthorizationScopeVersion);
            Assert.Equal("blocked", hit.AccountStatus);
            Assert.Equal(4, hit.FailedLoginCount);
            Assert.NotNull(hit.LockedUntilUtc);
            Assert.Equal(
                DateTimeOffset.Parse("2030-01-15T12:00:00Z"),
                hit.LockedUntilUtc.Value);
            Assert.True(hit.MustChangePassword);
            Assert.InRange(
                hit.DbNowUtc,
                new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
                DateTimeOffset.UtcNow.AddDays(1));

            Assert.NotNull(await store.FindByLoginAsync("ALICE.LOOKUP"));

            var orgWithoutScope = Guid.NewGuid();
            var userWithoutScope = Guid.NewGuid();
            SeedOrganizationAndUser(
                dataSource,
                orgWithoutScope,
                userWithoutScope,
                Guid.NewGuid(),
                "bob.noscope",
                new string('b', 64),
                "{}",
                credentialVersion: 1,
                accountStatus: "active",
                failedLoginCount: 0,
                lockedUntilUtc: null,
                authorizationScopeVersion: null,
                mustChangePassword: false);

            var defaultScope = await store.FindByLoginAsync("bob.noscope");
            Assert.NotNull(defaultScope);
            Assert.Equal(orgWithoutScope, defaultScope.OrganizationId);
            Assert.Equal(userWithoutScope, defaultScope.UserId);
            Assert.Equal(1, defaultScope.AuthorizationScopeVersion);
            Assert.Equal("active", defaultScope.AccountStatus);
            Assert.Equal(0, defaultScope.FailedLoginCount);
            Assert.Null(defaultScope.LockedUntilUtc);
            Assert.False(defaultScope.MustChangePassword);

            var collisionOrgA = Guid.NewGuid();
            var collisionOrgB = Guid.NewGuid();
            const string sharedLogin = "shared.login";
            SeedOrganizationAndUser(
                dataSource,
                collisionOrgA,
                Guid.NewGuid(),
                Guid.NewGuid(),
                sharedLogin,
                new string('a', 64),
                "{}",
                credentialVersion: 1,
                accountStatus: "active",
                failedLoginCount: 0,
                lockedUntilUtc: null,
                authorizationScopeVersion: 1,
                mustChangePassword: false);
            SeedOrganizationAndUser(
                dataSource,
                collisionOrgB,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "SHARED.LOGIN",
                new string('c', 64),
                "{}",
                credentialVersion: 1,
                accountStatus: "active",
                failedLoginCount: 0,
                lockedUntilUtc: null,
                authorizationScopeVersion: 1,
                mustChangePassword: false);

            Assert.Null(await store.FindByLoginAsync(sharedLogin));
            Assert.Null(await store.FindByLoginAsync("SHARED.LOGIN"));
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

    private static void SeedOrganizationAndUser(
        NpgsqlDataSource dataSource,
        Guid organizationId,
        Guid userId,
        Guid employeeProfileId,
        string login,
        string passwordHash,
        string passwordParameters,
        long credentialVersion,
        string accountStatus,
        int failedLoginCount,
        DateTimeOffset? lockedUntilUtc,
        long? authorizationScopeVersion,
        bool mustChangePassword)
    {
        using (var organizationCommand = dataSource.CreateCommand(
            """
            INSERT INTO core.organizations (id, code, name, default_time_zone)
            VALUES ($1, $2, $3, $4);
            """))
        {
            organizationCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
            organizationCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = $"org-{organizationId:N}" });
            organizationCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = "Integration Test Organization" });
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
            profileObjectCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = employeeProfileId });
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
            profileCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = employeeProfileId });
            profileCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
            profileCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = "Test" });
            profileCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = "User" });
            profileCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = "Test User" });
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
                password_parameters, credential_version, account_status, must_change_password,
                failed_login_count, locked_until)
            VALUES ($1, $2, $3, $4, $5, $6::jsonb, $7, $8, $9, $10, $11);
            """))
        {
            userCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
            userCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
            userCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = employeeProfileId });
            userCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = login });
            userCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = passwordHash });
            userCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = passwordParameters });
            userCommand.Parameters.Add(new NpgsqlParameter<long> { TypedValue = credentialVersion });
            userCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = accountStatus });
            userCommand.Parameters.Add(new NpgsqlParameter<bool> { TypedValue = mustChangePassword });
            userCommand.Parameters.Add(new NpgsqlParameter<int> { TypedValue = failedLoginCount });
            userCommand.Parameters.Add(new NpgsqlParameter
            {
                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.TimestampTz,
                Value = lockedUntilUtc is null ? DBNull.Value : lockedUntilUtc.Value,
            });
            userCommand.ExecuteNonQuery();
        }

        if (authorizationScopeVersion is not null)
        {
            using var scopeCommand = dataSource.CreateCommand(
                """
                INSERT INTO iam.authorization_scope_versions (user_account_id, version)
                VALUES ($1, $2);
                """);
            scopeCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
            scopeCommand.Parameters.Add(new NpgsqlParameter<long> { TypedValue = authorizationScopeVersion.Value });
            scopeCommand.ExecuteNonQuery();
        }
    }
}
