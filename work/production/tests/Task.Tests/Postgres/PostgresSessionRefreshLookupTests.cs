using Npgsql;
using Task.Application.Security;
using Task.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace Task.Tests.Postgres;

/// <summary>
/// Guarded integration gate for FindSessionByRefreshTokenHash over iam.refresh_tokens JOIN
/// iam.sessions. Runs only when TASK_POSTGRES_TEST_ADMIN_CONNECTION is set.
/// </summary>
public sealed class PostgresSessionRefreshLookupTests
{
    private const string ConnectionEnvironmentVariable = "TASK_POSTGRES_TEST_ADMIN_CONNECTION";
    private readonly ITestOutputHelper _output;

    public PostgresSessionRefreshLookupTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RealPostgres_FindSessionByRefreshTokenHash_ActiveConsumedUnknownExpired()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            _output.WriteLine(
                $"NOT RUN: set {ConnectionEnvironmentVariable} to execute the real PostgreSQL integration gate.");
            return;
        }

        var databaseName = $"task_rtlookup_{Guid.NewGuid():N}";
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

            await using var runtime = new TaskPersistenceRuntime(databaseConnection, TimeSpan.FromSeconds(10));
            var repository = runtime.CreateSessionRepository();

            var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var sessionId = Guid.NewGuid();
            var activeHash = HashOf('a');
            var expiredHash = HashOf('e');

            Assert.Throws<ArgumentException>(() => repository.FindSessionByRefreshTokenHash(string.Empty));
            Assert.Throws<ArgumentException>(() => repository.FindSessionByRefreshTokenHash("   "));
            Assert.Null(repository.FindSessionByRefreshTokenHash(HashOf('z')));

            repository.CreateSession(
                CreateSnapshot(sessionId, organizationId, userId, now),
                CreateToken(Guid.NewGuid(), sessionId, activeHash, now));

            var active = repository.FindSessionByRefreshTokenHash(activeHash);
            Assert.NotNull(active);
            Assert.Equal(organizationId, active.OrganizationId);
            Assert.Equal(sessionId, active.SessionId);
            Assert.Equal(userId, active.UserAccountId);
            Assert.Null(active.DeviceId);
            Assert.Equal(1, active.CredentialVersion);
            Assert.Equal(1, active.AuthorizationScopeVersion);
            Assert.Equal(TokenStatus.Active, active.TokenStatus);

            var replacement = CreateToken(Guid.NewGuid(), sessionId, HashOf('b'), now);
            Assert.True(repository.RotateRefreshToken(organizationId, sessionId, activeHash, replacement));

            var consumed = repository.FindSessionByRefreshTokenHash(activeHash);
            Assert.NotNull(consumed);
            Assert.Equal(sessionId, consumed.SessionId);
            Assert.Equal(organizationId, consumed.OrganizationId);
            Assert.Equal(TokenStatus.Consumed, consumed.TokenStatus);

            var rotatedActive = repository.FindSessionByRefreshTokenHash(replacement.TokenHash);
            Assert.NotNull(rotatedActive);
            Assert.Equal(TokenStatus.Active, rotatedActive.TokenStatus);

            Assert.Null(repository.FindSessionByRefreshTokenHash(HashOf('9')));

            var expiredSessionId = Guid.NewGuid();
            repository.CreateSession(
                CreateSnapshot(expiredSessionId, organizationId, userId, now),
                CreateToken(Guid.NewGuid(), expiredSessionId, expiredHash, now));
            ExpireToken(dataSource, expiredSessionId, "clock_timestamp() - interval '1 hour'");

            var expired = repository.FindSessionByRefreshTokenHash(expiredHash);
            Assert.NotNull(expired);
            Assert.Equal(expiredSessionId, expired.SessionId);
            Assert.Equal(TokenStatus.Expired, expired.TokenStatus);
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