using Npgsql;
using Task.Application.Security;
using Task.Infrastructure.Persistence;
using Task.Infrastructure.Postgres;
using Xunit.Abstractions;

namespace Task.Tests.Postgres;

public sealed class PostgresDeviceRegistrationStoreTests
{
    private const string ConnectionEnvironmentVariable = "TASK_POSTGRES_TEST_ADMIN_CONNECTION";
    private readonly ITestOutputHelper _output;

    public PostgresDeviceRegistrationStoreTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RealPostgres_UpsertCreateSameFingerprintAndRevokedPreserved()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            _output.WriteLine(
                $"NOT RUN: set {ConnectionEnvironmentVariable} to execute the real PostgreSQL integration gate.");
            return;
        }

        var databaseName = $"task_device_{Guid.NewGuid():N}";
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

            var store = new PostgresDeviceRegistrationStore(dataSource);

            var organizationId = Guid.NewGuid();
            var otherOrganizationId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            SeedOrganizationAndUser(dataSource, organizationId, userId, Guid.NewGuid());

            var fingerprint = FakeHash("fp:desktop-a");

            await Assert.ThrowsAsync<ArgumentException>(
                () => store.UpsertAsync(Guid.Empty, userId, fingerprint, null));
            await Assert.ThrowsAsync<ArgumentException>(
                () => store.UpsertAsync(organizationId, Guid.Empty, fingerprint, null));
            await Assert.ThrowsAsync<ArgumentException>(
                () => store.UpsertAsync(organizationId, userId, new string('a', 31), null));
            await Assert.ThrowsAsync<ArgumentException>(
                () => store.UpsertAsync(organizationId, userId, new string('a', 257), null));
            await Assert.ThrowsAsync<ArgumentException>(
                () => store.GetByIdAsync(Guid.Empty, Guid.NewGuid()));
            await Assert.ThrowsAsync<ArgumentException>(
                () => store.GetByIdAsync(organizationId, Guid.Empty));

            Assert.Null(await store.GetByIdAsync(organizationId, Guid.NewGuid()));

            var createdId = await store.UpsertAsync(
                organizationId, userId, fingerprint, "Workstation A");
            Assert.NotEqual(Guid.Empty, createdId);

            var created = await store.GetByIdAsync(organizationId, createdId);
            Assert.NotNull(created);
            Assert.Equal(createdId, created.DeviceId);
            Assert.Equal(userId, created.UserId);
            Assert.Equal(fingerprint, created.FingerprintHash);
            Assert.Null(created.RevokedAtUtc);
            Assert.Null(await store.GetByIdAsync(otherOrganizationId, createdId));

            var firstSeen = ReadLastSeen(dataSource, createdId);
            await global::System.Threading.Tasks.Task.Delay(50);

            var sameId = await store.UpsertAsync(
                organizationId, userId, fingerprint, "Workstation A (renamed)");
            Assert.Equal(createdId, sameId);

            var afterUpsert = await store.GetByIdAsync(organizationId, createdId);
            Assert.NotNull(afterUpsert);
            Assert.Null(afterUpsert.RevokedAtUtc);

            var secondSeen = ReadLastSeen(dataSource, createdId);
            Assert.True(secondSeen > firstSeen, "last_seen_at must advance on upsert.");
            Assert.Equal("Workstation A (renamed)", ReadDisplayName(dataSource, createdId));

            var beforeNullNameUpsert = ReadDisplayName(dataSource, createdId);
            await store.UpsertAsync(organizationId, userId, fingerprint, null);
            Assert.Equal(beforeNullNameUpsert, ReadDisplayName(dataSource, createdId));

            RevokeDevice(dataSource, createdId);
            var revokedBefore = await store.GetByIdAsync(organizationId, createdId);
            Assert.NotNull(revokedBefore);
            Assert.NotNull(revokedBefore.RevokedAtUtc);

            var afterRevokedUpsert = await store.UpsertAsync(
                organizationId, userId, fingerprint, "Should not un-revoke");
            Assert.Equal(createdId, afterRevokedUpsert);

            var stillRevoked = await store.GetByIdAsync(organizationId, createdId);
            Assert.NotNull(stillRevoked);
            Assert.NotNull(stillRevoked.RevokedAtUtc);
            Assert.Equal(revokedBefore.RevokedAtUtc, stillRevoked.RevokedAtUtc);
            Assert.Equal("Should not un-revoke", ReadDisplayName(dataSource, createdId));

            var otherFingerprint = FakeHash("fp:desktop-b");
            var secondDeviceId = await store.UpsertAsync(
                organizationId, userId, otherFingerprint, "Workstation B");
            Assert.NotEqual(createdId, secondDeviceId);
            var second = await store.GetByIdAsync(organizationId, secondDeviceId);
            Assert.NotNull(second);
            Assert.Equal(otherFingerprint, second.FingerprintHash);
            Assert.Null(second.RevokedAtUtc);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            DropDatabase(adminDataSource, databaseName);
        }
    }

    private static string FakeHash(string prefix) =>
        prefix + new string('x', 64);

    private static DateTimeOffset ReadLastSeen(NpgsqlDataSource dataSource, Guid deviceId)
    {
        using var command = dataSource.CreateCommand(
            "SELECT last_seen_at FROM iam.devices WHERE id = $1;");
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = deviceId });
        var value = (DateTime)command.ExecuteScalar()!;
        return new DateTimeOffset(value, TimeSpan.Zero);
    }

    private static string? ReadDisplayName(NpgsqlDataSource dataSource, Guid deviceId)
    {
        using var command = dataSource.CreateCommand(
            "SELECT display_name FROM iam.devices WHERE id = $1;");
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = deviceId });
        return command.ExecuteScalar() as string;
    }

    private static void RevokeDevice(NpgsqlDataSource dataSource, Guid deviceId)
    {
        using var command = dataSource.CreateCommand(
            """
            UPDATE iam.devices
            SET revoked_at = timestamptz '2020-01-01T00:00:00Z'
            WHERE id = $1;
            """);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = deviceId });
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
