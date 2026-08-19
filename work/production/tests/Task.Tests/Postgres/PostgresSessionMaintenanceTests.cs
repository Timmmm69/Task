using Npgsql;
using Task.Application.Security;
using Task.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace Task.Tests.Postgres;

/// <summary>
/// Guarded integration gate for session/refresh-token maintenance purging over iam.sessions
/// and iam.refresh_tokens (migration 002). Tests run only when
/// TASK_POSTGRES_TEST_ADMIN_CONNECTION is set; each test creates an isolated throwaway
/// database, applies all migrations and drops it again.
/// </summary>
public sealed class PostgresSessionMaintenanceTests
{
    private const string ConnectionEnvironmentVariable = "TASK_POSTGRES_TEST_ADMIN_CONNECTION";
    private const int ExpiredSessionMaintenanceRetentionDays = 30;
    private readonly ITestOutputHelper _output;

    public PostgresSessionMaintenanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RealPostgres_PurgeRemovesExpiredRecordsAndKeepsFreshOnes()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            _output.WriteLine(
                $"NOT RUN: set {ConnectionEnvironmentVariable} to execute the real PostgreSQL integration gate.");
            return;
        }

        var databaseName = $"task_sessmnt_{Guid.NewGuid():N}";
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

            await using var runtime = new TaskPersistenceRuntime(databaseConnection, TimeSpan.FromSeconds(10));
            var repository = runtime.CreateSessionRepository();

            var organizationId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            SeedOrganizationAndUser(dataSource, organizationId, userId, Guid.NewGuid());

            var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var olderThan = now.AddDays(-ExpiredSessionMaintenanceRetentionDays);

            var expiredSessionId = Guid.NewGuid();
            var expiredTokenHash = HashOf('a');
            repository.CreateSession(
                CreateSnapshot(expiredSessionId, organizationId, userId, now),
                CreateToken(Guid.NewGuid(), expiredSessionId, expiredTokenHash, now));
            ExpireSession(
                dataSource,
                expiredSessionId,
                "clock_timestamp() - interval '31 days'",
                "clock_timestamp() - interval '31 days'");
            ExpireToken(dataSource, expiredSessionId, "clock_timestamp() - interval '31 days'");

            var freshSessionId = Guid.NewGuid();
            var freshTokenHash = HashOf('b');
            repository.CreateSession(
                CreateSnapshot(freshSessionId, organizationId, userId, now),
                CreateToken(Guid.NewGuid(), freshSessionId, freshTokenHash, now));

            var notOldEnoughSessionId = Guid.NewGuid();
            repository.CreateSession(
                CreateSnapshot(notOldEnoughSessionId, organizationId, userId, now),
                CreateToken(Guid.NewGuid(), notOldEnoughSessionId, HashOf('c'), now));
            ExpireSession(
                dataSource,
                notOldEnoughSessionId,
                "clock_timestamp() - interval '15 days'",
                "clock_timestamp() - interval '15 days'");

            await Assert.ThrowsAsync<ArgumentException>(() => repository.PurgeExpiredRefreshTokensAsync(olderThan, 0));
            await Assert.ThrowsAsync<ArgumentException>(() => repository.PurgeExpiredSessionsAsync(olderThan, -1));

            var tokensDeleted = await repository.PurgeExpiredRefreshTokensAsync(olderThan, 1000, CancellationToken.None);
            Assert.Equal(1, tokensDeleted);
            Assert.False(TokenExists(dataSource, expiredTokenHash));
            Assert.True(TokenExists(dataSource, freshTokenHash));

            var sessionsDeleted = await repository.PurgeExpiredSessionsAsync(olderThan, 1000, CancellationToken.None);
            Assert.Equal(1, sessionsDeleted);
            Assert.False(SessionExists(dataSource, expiredSessionId));
            Assert.True(SessionExists(dataSource, freshSessionId));
            Assert.True(SessionExists(dataSource, notOldEnoughSessionId));
            Assert.NotNull(repository.GetActiveSession(organizationId, freshSessionId));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            DropDatabase(adminDataSource, databaseName);
        }
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RealPostgres_PurgeHonorsBatchLimitAndCompletesInMultiplePasses()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            _output.WriteLine(
                $"NOT RUN: set {ConnectionEnvironmentVariable} to execute the real PostgreSQL integration gate.");
            return;
        }

        var databaseName = $"task_sessmnt_{Guid.NewGuid():N}";
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

            await using var runtime = new TaskPersistenceRuntime(databaseConnection, TimeSpan.FromSeconds(10));
            var repository = runtime.CreateSessionRepository();

            var organizationId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            SeedOrganizationAndUser(dataSource, organizationId, userId, Guid.NewGuid());

            var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var olderThan = now.AddDays(-ExpiredSessionMaintenanceRetentionDays);

            for (var i = 0; i < 3; i++)
            {
                var sessionId = Guid.NewGuid();
                repository.CreateSession(
                    CreateSnapshot(sessionId, organizationId, userId, now),
                    CreateToken(Guid.NewGuid(), sessionId, HashOf((char)('d' + i)), now));
                ExpireSession(
                    dataSource,
                    sessionId,
                    "clock_timestamp() - interval '31 days'",
                    "clock_timestamp() - interval '31 days'");
                ExpireToken(dataSource, sessionId, "clock_timestamp() - interval '31 days'");
            }

            var freshSessionId = Guid.NewGuid();
            var freshTokenHash = HashOf('z');
            repository.CreateSession(
                CreateSnapshot(freshSessionId, organizationId, userId, now),
                CreateToken(Guid.NewGuid(), freshSessionId, freshTokenHash, now));

            Assert.Equal(2, await repository.PurgeExpiredRefreshTokensAsync(olderThan, 2, CancellationToken.None));
            Assert.Equal(1, CountExpiredTokens(dataSource, olderThan));
            Assert.Equal(1, await repository.PurgeExpiredRefreshTokensAsync(olderThan, 2, CancellationToken.None));
            Assert.Equal(0, await repository.PurgeExpiredRefreshTokensAsync(olderThan, 2, CancellationToken.None));
            Assert.True(TokenExists(dataSource, freshTokenHash));

            Assert.Equal(2, await repository.PurgeExpiredSessionsAsync(olderThan, 2, CancellationToken.None));
            Assert.Equal(1, CountExpiredSessions(dataSource, olderThan));
            Assert.Equal(1, await repository.PurgeExpiredSessionsAsync(olderThan, 2, CancellationToken.None));
            Assert.Equal(0, await repository.PurgeExpiredSessionsAsync(olderThan, 2, CancellationToken.None));
            Assert.True(SessionExists(dataSource, freshSessionId));
            Assert.NotNull(repository.GetActiveSession(organizationId, freshSessionId));
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
        DateTimeOffset now) =>
        new(
            sessionId,
            organizationId,
            userId,
            null,
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

    private static void ExpireSession(
        NpgsqlDataSource dataSource,
        Guid sessionId,
        string idleExpression,
        string absoluteExpression)
    {
        using var command = dataSource.CreateCommand(
            $"""
            UPDATE iam.sessions
            SET idle_expires_at = {idleExpression}, absolute_expires_at = {absoluteExpression}
            WHERE id = $1;
            """);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = sessionId });
        command.ExecuteNonQuery();
    }

    private static void ExpireToken(NpgsqlDataSource dataSource, Guid sessionId, string expiresExpression)
    {
        using var command = dataSource.CreateCommand(
            $"""
            UPDATE iam.refresh_tokens
            SET expires_at = {expiresExpression}
            WHERE session_id = $1;
            """);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = sessionId });
        command.ExecuteNonQuery();
    }

    private static bool SessionExists(NpgsqlDataSource dataSource, Guid sessionId)
    {
        using var command = dataSource.CreateCommand(
            "SELECT 1 FROM iam.sessions WHERE id = $1 LIMIT 1;");
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = sessionId });
        return command.ExecuteScalar() is not null;
    }

    private static bool TokenExists(NpgsqlDataSource dataSource, string tokenHash)
    {
        using var command = dataSource.CreateCommand(
            "SELECT 1 FROM iam.refresh_tokens WHERE token_hash = $1 LIMIT 1;");
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = tokenHash });
        return command.ExecuteScalar() is not null;
    }

    private static int CountExpiredSessions(NpgsqlDataSource dataSource, DateTimeOffset olderThan)
    {
        using var command = dataSource.CreateCommand(
            "SELECT COUNT(*) FROM iam.sessions WHERE absolute_expires_at < $1;");
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = olderThan });
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static int CountExpiredTokens(NpgsqlDataSource dataSource, DateTimeOffset olderThan)
    {
        using var command = dataSource.CreateCommand(
            "SELECT COUNT(*) FROM iam.refresh_tokens WHERE expires_at < $1;");
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = olderThan });
        return Convert.ToInt32(command.ExecuteScalar());
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