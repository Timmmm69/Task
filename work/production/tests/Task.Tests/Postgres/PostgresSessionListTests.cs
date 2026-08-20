using Npgsql;
using Task.Application.Security;
using Task.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace Task.Tests.Postgres;

public sealed class PostgresSessionListTests
{
    private const string ConnectionEnvironmentVariable = "TASK_POSTGRES_TEST_ADMIN_CONNECTION";
    private readonly ITestOutputHelper _output;

    public PostgresSessionListTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RealPostgres_GetUserSessions_OrdersAndJoinsDevices()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            _output.WriteLine(
                $"NOT RUN: set {ConnectionEnvironmentVariable} to execute the real PostgreSQL integration gate.");
            return;
        }

        var databaseName = $"task_sesslist_{Guid.NewGuid():N}";
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

            var organizationId = Guid.NewGuid();
            var otherOrganizationId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();
            var deviceA = Guid.NewGuid();
            var deviceB = Guid.NewGuid();
            SeedOrganizationAndUser(dataSource, organizationId, userId, Guid.NewGuid());
            SeedOrganizationAndUser(dataSource, otherOrganizationId, otherUserId, Guid.NewGuid());
            SeedDevice(dataSource, organizationId, userId, deviceA, "Work PC");
            SeedDevice(dataSource, organizationId, userId, deviceB, "Home Laptop");

            await using var runtime = new TaskPersistenceRuntime(databaseConnection, TimeSpan.FromSeconds(10));
            var repository = runtime.CreateSessionRepository();

            var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var sessionA = Guid.NewGuid();
            var sessionB = Guid.NewGuid();
            var sessionC = Guid.NewGuid();
            repository.CreateSession(
                CreateSnapshot(sessionA, organizationId, userId, deviceA, now),
                CreateToken(Guid.NewGuid(), sessionA, HashOf('a'), now));
            repository.CreateSession(
                CreateSnapshot(sessionB, organizationId, userId, null, now.AddSeconds(10)),
                CreateToken(Guid.NewGuid(), sessionB, HashOf('b'), now));
            repository.CreateSession(
                CreateSnapshot(sessionC, organizationId, userId, deviceB, now.AddSeconds(5)),
                CreateToken(Guid.NewGuid(), sessionC, HashOf('c'), now));
            repository.RevokeSession(organizationId, sessionC, "user-revoked");

            var items = repository.GetUserSessions(organizationId, userId);

            // Newest activity first: sessionB (device-less), then sessionC (revoked, with a
            // device name), then sessionA.
            Assert.Equal(3, items.Count);
            Assert.Equal(sessionB, items[0].SessionId);
            Assert.Null(items[0].DeviceDisplayName);
            Assert.Null(items[0].RevokedAtUtc);
            Assert.Equal(sessionC, items[1].SessionId);
            Assert.Equal("Home Laptop", items[1].DeviceDisplayName);
            Assert.NotNull(items[1].RevokedAtUtc);
            Assert.Equal("user-revoked", items[1].RevokeReason);
            Assert.Equal(sessionA, items[2].SessionId);
            Assert.Equal("Work PC", items[2].DeviceDisplayName);

            // The listing is scoped to one organization and one user.
            Assert.Empty(repository.GetUserSessions(otherOrganizationId, userId));
            Assert.Empty(repository.GetUserSessions(organizationId, otherUserId));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            DropDatabase(adminDataSource, databaseName);
        }
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RealPostgres_GetUserSessions_IsCappedAt200()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            _output.WriteLine(
                $"NOT RUN: set {ConnectionEnvironmentVariable} to execute the real PostgreSQL integration gate.");
            return;
        }

        var databaseName = $"task_sesslimit_{Guid.NewGuid():N}";
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

            var organizationId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            SeedOrganizationAndUser(dataSource, organizationId, userId, Guid.NewGuid());

            // 201 sessions of the same user, oldest first; the newest 200 must be returned.
            using (var command = dataSource.CreateCommand(
                """
                INSERT INTO iam.sessions (
                    id, organization_id, user_account_id, device_id, credential_version,
                    authorization_scope_version, created_at, last_seen_at, idle_expires_at,
                    absolute_expires_at, revoked_at, revoke_reason)
                SELECT
                    gen_random_uuid(),
                    $1,
                    $2,
                    NULL,
                    1,
                    1,
                    clock_timestamp() - (300 - generate_series) * interval '1 second',
                    clock_timestamp() - (300 - generate_series) * interval '1 second',
                    clock_timestamp() + interval '1 hour',
                    clock_timestamp() + interval '8 hours',
                    NULL,
                    NULL
                FROM generate_series(1, 201);
                """))
            {
                command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
                command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
                Assert.Equal(201, command.ExecuteNonQuery());
            }

            await using var runtime = new TaskPersistenceRuntime(databaseConnection, TimeSpan.FromSeconds(10));
            var repository = runtime.CreateSessionRepository();

            var items = repository.GetUserSessions(organizationId, userId);

            Assert.Equal(200, items.Count);
            // Newest activity first: the series was generated ascending, so the first listed
            // session is the one created 100 seconds ago (generate_series = 200).
            for (var index = 1; index < items.Count; index++)
            {
                Assert.True(items[index - 1].LastSeenAtUtc >= items[index].LastSeenAtUtc);
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            DropDatabase(adminDataSource, databaseName);
        }
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RealPostgres_GetSession_ReturnsAnyState()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            _output.WriteLine(
                $"NOT RUN: set {ConnectionEnvironmentVariable} to execute the real PostgreSQL integration gate.");
            return;
        }

        var databaseName = $"task_sessget_{Guid.NewGuid():N}";
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

            var organizationId = Guid.NewGuid();
            var otherOrganizationId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            SeedOrganizationAndUser(dataSource, organizationId, userId, Guid.NewGuid());
            SeedOrganizationAndUser(dataSource, otherOrganizationId, userId, Guid.NewGuid());

            await using var runtime = new TaskPersistenceRuntime(databaseConnection, TimeSpan.FromSeconds(10));
            var repository = runtime.CreateSessionRepository();

            var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var activeSession = Guid.NewGuid();
            var revokedSession = Guid.NewGuid();
            repository.CreateSession(
                CreateSnapshot(activeSession, organizationId, userId, null, now),
                CreateToken(Guid.NewGuid(), activeSession, HashOf('a'), now));
            repository.CreateSession(
                CreateSnapshot(revokedSession, organizationId, userId, null, now),
                CreateToken(Guid.NewGuid(), revokedSession, HashOf('b'), now));
            repository.RevokeSession(organizationId, revokedSession, "user-revoked");

            Assert.Throws<ArgumentException>(() => repository.GetSession(Guid.Empty, activeSession));
            Assert.Throws<ArgumentException>(() => repository.GetSession(organizationId, Guid.Empty));
            Assert.Throws<ArgumentException>(() => repository.GetUserSessions(Guid.Empty, userId));
            Assert.Throws<ArgumentException>(() => repository.GetUserSessions(organizationId, Guid.Empty));

            var loadedActive = repository.GetSession(organizationId, activeSession);
            Assert.NotNull(loadedActive);
            Assert.Equal(activeSession, loadedActive.SessionId);
            Assert.Equal(organizationId, loadedActive.OrganizationId);
            Assert.Equal(userId, loadedActive.UserAccountId);
            Assert.Null(loadedActive.RevokedAtUtc);

            // A revoked session is still returned; only an absent row yields null.
            var loadedRevoked = repository.GetSession(organizationId, revokedSession);
            Assert.NotNull(loadedRevoked);
            Assert.Equal(revokedSession, loadedRevoked.SessionId);
            Assert.NotNull(loadedRevoked.RevokedAtUtc);
            Assert.Equal("user-revoked", loadedRevoked.RevokeReason);

            Assert.Null(repository.GetSession(organizationId, Guid.NewGuid()));
            Assert.Null(repository.GetSession(otherOrganizationId, activeSession));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            DropDatabase(adminDataSource, databaseName);
        }
    }

    private static SessionSnapshot CreateSnapshot(
        Guid sessionId,
        Guid organizationId,
        Guid userId,
        Guid? deviceId,
        DateTimeOffset now) =>
        new(
            sessionId,
            organizationId,
            userId,
            deviceId,
            1,
            1,
            now,
            now,
            now.AddHours(1),
            now.AddHours(8),
            null,
            null);

    private static RefreshTokenRecord CreateToken(
        Guid id,
        Guid sessionId,
        string hash,
        DateTimeOffset now) =>
        new(id, sessionId, hash, now, now.AddHours(8), null, null, null);

    private static string HashOf(char character) => new string(character, 64);

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

    private static void SeedDevice(
        NpgsqlDataSource dataSource,
        Guid organizationId,
        Guid userId,
        Guid deviceId,
        string displayName)
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        using (var objectCommand = dataSource.CreateCommand(
            """
            INSERT INTO core.objects (
                id, organization_id, object_type, version, created_at, created_by, updated_at, updated_by)
            VALUES ($1, $2, 'device', 1, $3, $4, $3, $4);
            """))
        {
            objectCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = deviceId });
            objectCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
            objectCommand.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = now });
            objectCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
            objectCommand.ExecuteNonQuery();
        }

        using (var deviceCommand = dataSource.CreateCommand(
            """
            INSERT INTO iam.devices (
                id, organization_id, user_account_id, device_fingerprint_hash, display_name)
            VALUES ($1, $2, $3, $4, $5);
            """))
        {
            deviceCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = deviceId });
            deviceCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
            deviceCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
            deviceCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = new string('f', 64) });
            deviceCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = displayName });
            deviceCommand.ExecuteNonQuery();
        }
    }

    private static void SeedOrganizationAndUser(
        NpgsqlDataSource dataSource,
        Guid organizationId,
        Guid userId,
        Guid employeeProfileId)
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
                password_parameters, account_status, must_change_password)
            VALUES ($1, $2, $3, $4, $5, '{}'::jsonb, 'active', false);
            """))
        {
            userCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
            userCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
            userCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = employeeProfileId });
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