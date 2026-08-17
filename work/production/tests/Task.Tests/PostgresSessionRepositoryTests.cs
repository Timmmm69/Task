using Npgsql;
using Task.Application.Security;
using Task.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace Task.Tests;

public sealed class PostgresSessionRepositoryTests
{
    private const string ConnectionEnvironmentVariable = "TASK_POSTGRES_TEST_ADMIN_CONNECTION";
    private readonly ITestOutputHelper _output;

    public PostgresSessionRepositoryTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RealPostgres_SessionRepositoryArgumentValidationAndFactory()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            _output.WriteLine(
                $"NOT RUN: set {ConnectionEnvironmentVariable} to execute the real PostgreSQL integration gate.");
            return;
        }

        var databaseName = $"task_session_{Guid.NewGuid():N}";
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
            Assert.NotNull(repository);

            var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var organizationId = Guid.NewGuid();
            var sessionId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var tokenId = Guid.NewGuid();
            var snapshot = CreateSnapshot(sessionId, organizationId, userId, now);
            var token = CreateToken(tokenId, sessionId, HashOf('a'), now);

            Assert.Throws<ArgumentException>(() => repository.GetActiveSession(Guid.Empty, sessionId));
            Assert.Throws<ArgumentException>(() => repository.GetActiveSession(organizationId, Guid.Empty));
            Assert.Throws<ArgumentException>(() => repository.CreateSession(
                snapshot with { OrganizationId = Guid.Empty }, token));
            Assert.Throws<ArgumentException>(() => repository.CreateSession(
                snapshot with { SessionId = Guid.Empty }, token));
            Assert.Throws<ArgumentException>(() => repository.CreateSession(
                snapshot with { UserAccountId = Guid.Empty }, token));
            Assert.Throws<ArgumentException>(() => repository.CreateSession(
                snapshot, token with { Id = Guid.Empty }));
            Assert.Throws<ArgumentException>(() => repository.CreateSession(
                snapshot, token with { SessionId = Guid.Empty }));
            Assert.Throws<ArgumentException>(() => repository.CreateSession(
                snapshot, token with { TokenHash = string.Empty }));
            Assert.Throws<ArgumentException>(() => repository.RotateRefreshToken(
                Guid.Empty, sessionId, token.TokenHash, token));
            Assert.Throws<ArgumentException>(() => repository.RotateRefreshToken(
                organizationId, Guid.Empty, token.TokenHash, token));
            Assert.Throws<ArgumentException>(() => repository.RotateRefreshToken(
                organizationId, sessionId, string.Empty, token));
            Assert.Throws<ArgumentException>(() => repository.RotateRefreshToken(
                organizationId, sessionId, token.TokenHash, token with { Id = Guid.Empty }));
            Assert.Throws<ArgumentException>(() => repository.RotateRefreshToken(
                organizationId, sessionId, token.TokenHash, token with { TokenHash = string.Empty }));
            Assert.Throws<ArgumentException>(() => repository.TouchSession(Guid.Empty, sessionId));
            Assert.Throws<ArgumentException>(() => repository.TouchSession(organizationId, Guid.Empty));
            Assert.Throws<ArgumentException>(() => repository.RevokeSession(Guid.Empty, sessionId, null));
            Assert.Throws<ArgumentException>(() => repository.RevokeSession(organizationId, Guid.Empty, null));
            Assert.Throws<ArgumentException>(() => repository.RevokeAllUserSessions(Guid.Empty, userId, null, null));
            Assert.Throws<ArgumentException>(() => repository.RevokeAllUserSessions(organizationId, Guid.Empty, null, null));

            Assert.Equal(0, repository.RevokeAllUserSessions(
                organizationId, userId, Guid.Empty, "no-exception-expected"));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            DropDatabase(adminDataSource, databaseName);
        }
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RealPostgres_SessionRepositoryRoundTripRotationAndRevocation()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            _output.WriteLine(
                $"NOT RUN: set {ConnectionEnvironmentVariable} to execute the real PostgreSQL integration gate.");
            return;
        }

        var databaseName = $"task_session_{Guid.NewGuid():N}";
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
            SeedOrganizationAndUser(dataSource, organizationId, userId, Guid.NewGuid());
            SeedOrganizationAndUser(dataSource, otherOrganizationId, otherUserId, Guid.NewGuid());

            await using var runtime = new TaskPersistenceRuntime(databaseConnection, TimeSpan.FromSeconds(10));
            var repository = runtime.CreateSessionRepository();

            var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var sessionA = Guid.NewGuid();
            var sessionB = Guid.NewGuid();
            var sessionC = Guid.NewGuid();
            var sessionD = Guid.NewGuid();
            var sessionE = Guid.NewGuid();
            var sessionF = Guid.NewGuid();
            var hashA = HashOf('a');
            var hashB = HashOf('b');
            var hashC = HashOf('c');
            var hashD = HashOf('d');
            var hashE = HashOf('e');
            var hashF = HashOf('f');

            var snapshotA = CreateSnapshot(sessionA, organizationId, userId, now);
            repository.CreateSession(snapshotA, CreateToken(Guid.NewGuid(), sessionA, hashA, now));

            var loadedA = repository.GetActiveSession(organizationId, sessionA);
            Assert.NotNull(loadedA);
            Assert.Equal(snapshotA, loadedA);
            Assert.Null(loadedA.DeviceId);
            Assert.Null(loadedA.RevokeReason);
            Assert.True(TokenExists(dataSource, hashA));
            Assert.Null(repository.GetActiveSession(organizationId, Guid.NewGuid()));
            Assert.Null(repository.GetActiveSession(otherOrganizationId, sessionA));

            repository.CreateSession(
                CreateSnapshot(sessionB, otherOrganizationId, otherUserId, now),
                CreateToken(Guid.NewGuid(), sessionB, hashB, now));
            Assert.NotNull(repository.GetActiveSession(otherOrganizationId, sessionB));
            Assert.Null(repository.GetActiveSession(organizationId, sessionB));

            repository.CreateSession(
                CreateSnapshot(sessionC, organizationId, userId, now),
                CreateToken(Guid.NewGuid(), sessionC, hashC, now));
            repository.RevokeSession(organizationId, sessionC, "voluntary");
            var revokedC = ReadSessionRevocation(dataSource, sessionC);
            Assert.True(revokedC.Revoked);
            Assert.Equal("voluntary", revokedC.Reason);
            Assert.Null(repository.GetActiveSession(organizationId, sessionC));
            repository.RevokeSession(organizationId, sessionC, "second-attempt");
            var revokedAgainC = ReadSessionRevocation(dataSource, sessionC);
            Assert.True(revokedAgainC.Revoked);
            Assert.Equal("voluntary", revokedAgainC.Reason);

            repository.CreateSession(
                CreateSnapshot(sessionD, organizationId, userId, now),
                CreateToken(Guid.NewGuid(), sessionD, hashD, now));
            ExpireSession(dataSource, sessionD, "clock_timestamp() - interval '1 hour'", "clock_timestamp() - interval '30 minutes'");
            Assert.Null(repository.GetActiveSession(organizationId, sessionD));

            repository.CreateSession(
                CreateSnapshot(sessionE, organizationId, userId, now),
                CreateToken(Guid.NewGuid(), sessionE, hashE, now));
            ExpireSession(dataSource, sessionE, "clock_timestamp() - interval '1 minute'", "clock_timestamp() + interval '8 hours'");
            Assert.Null(repository.GetActiveSession(organizationId, sessionE));

            var replacementToken = CreateToken(Guid.NewGuid(), sessionA, HashOf('1'), now);
            Assert.True(repository.RotateRefreshToken(organizationId, sessionA, hashA, replacementToken));
            var rotated = ReadTokenRotation(dataSource, sessionA, hashA);
            Assert.True(rotated.Consumed);
            Assert.Equal(replacementToken.Id, rotated.ReplacedById);
            Assert.True(TokenExists(dataSource, replacementToken.TokenHash));

            var replayToken = CreateToken(Guid.NewGuid(), sessionA, HashOf('2'), now);
            Assert.False(repository.RotateRefreshToken(organizationId, sessionA, hashA, replayToken));
            Assert.False(TokenExists(dataSource, replayToken.TokenHash));

            var unknownToken = CreateToken(Guid.NewGuid(), sessionA, HashOf('3'), now);
            Assert.False(repository.RotateRefreshToken(organizationId, sessionA, HashOf('9'), unknownToken));
            Assert.False(TokenExists(dataSource, unknownToken.TokenHash));

            repository.CreateSession(
                CreateSnapshot(sessionF, organizationId, userId, now),
                CreateToken(Guid.NewGuid(), sessionF, hashF, now));
            var foreignToken = CreateToken(Guid.NewGuid(), sessionA, HashOf('4'), now);
            Assert.False(repository.RotateRefreshToken(organizationId, sessionA, hashF, foreignToken));
            Assert.False(TokenExists(dataSource, foreignToken.TokenHash));

            var lastSeenBefore = repository.GetActiveSession(organizationId, sessionA)!.LastSeenAtUtc;
            repository.TouchSession(organizationId, sessionA);
            var lastSeenAfter = repository.GetActiveSession(organizationId, sessionA)!.LastSeenAtUtc;
            Assert.True(lastSeenAfter > lastSeenBefore);
            repository.TouchSession(organizationId, sessionD);

            Assert.Equal(3, repository.RevokeAllUserSessions(organizationId, userId, sessionF, "mass-revoke"));
            Assert.Null(repository.GetActiveSession(organizationId, sessionA));
            Assert.Null(repository.GetActiveSession(organizationId, sessionD));
            Assert.NotNull(repository.GetActiveSession(organizationId, sessionF));

            Assert.Equal(1, repository.RevokeAllUserSessions(
                organizationId, userId, Guid.Empty, "mass-revoke-2"));
            Assert.Null(repository.GetActiveSession(organizationId, sessionF));
            var revokedF = ReadSessionRevocation(dataSource, sessionF);
            Assert.True(revokedF.Revoked);
            Assert.Equal("mass-revoke-2", revokedF.Reason);
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

    private static bool TokenExists(NpgsqlDataSource dataSource, string tokenHash)
    {
        using var command = dataSource.CreateCommand(
            "SELECT 1 FROM iam.refresh_tokens WHERE token_hash = $1 LIMIT 1;");
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = tokenHash });
        return command.ExecuteScalar() is not null;
    }

    private static (bool Consumed, Guid? ReplacedById) ReadTokenRotation(
        NpgsqlDataSource dataSource,
        Guid sessionId,
        string tokenHash)
    {
        using var command = dataSource.CreateCommand(
            """
            SELECT consumed_at IS NOT NULL, replaced_by_id
            FROM iam.refresh_tokens
            WHERE session_id = $1 AND token_hash = $2;
            """);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = sessionId });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = tokenHash });
        using var reader = command.ExecuteReader();
        reader.Read();
        return (reader.GetBoolean(0), reader.IsDBNull(1) ? null : (Guid?)reader.GetGuid(1));
    }

    private static (bool Revoked, string? Reason) ReadSessionRevocation(
        NpgsqlDataSource dataSource,
        Guid sessionId)
    {
        using var command = dataSource.CreateCommand(
            "SELECT revoked_at IS NOT NULL, revoke_reason FROM iam.sessions WHERE id = $1;");
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = sessionId });
        using var reader = command.ExecuteReader();
        reader.Read();
        return (reader.GetBoolean(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

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
