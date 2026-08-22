using Npgsql;
using Task.Application.Security;
using Task.Infrastructure.Persistence;
using Task.Infrastructure.Postgres;
using Xunit.Abstractions;

namespace Task.Tests.Postgres;

public sealed class PostgresAccountCredentialStoreTests
{
    private const string ConnectionEnvironmentVariable = "TASK_POSTGRES_TEST_ADMIN_CONNECTION";
    private readonly ITestOutputHelper _output;

    public PostgresAccountCredentialStoreTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RealPostgres_CredentialUpdateAndHistoryWithLimit()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            _output.WriteLine(
                $"NOT RUN: set {ConnectionEnvironmentVariable} to execute the real PostgreSQL integration gate.");
            return;
        }

        var databaseName = $"task_credential_{Guid.NewGuid():N}";
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

            var store = new PostgresAccountCredentialStore(dataSource);

            var organizationId = Guid.NewGuid();
            var otherOrganizationId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            SeedOrganizationAndUser(dataSource, organizationId, userId, Guid.NewGuid());

            await Assert.ThrowsAsync<ArgumentException>(
                () => store.GetCredentialAsync(Guid.Empty, userId));
            await Assert.ThrowsAsync<ArgumentException>(
                () => store.GetCredentialAsync(organizationId, Guid.Empty));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => store.GetRecentPasswordHistoryAsync(organizationId, userId, 0));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => store.UpdateCredentialAsync(
                    organizationId, userId, new PasswordHashRecord("hash", "{}"), 0));

            Assert.Null(await store.GetCredentialAsync(otherOrganizationId, userId));
            Assert.Null(await store.GetCredentialAsync(organizationId, Guid.NewGuid()));

            Assert.True(await store.GetMustChangePasswordAsync(organizationId, userId));
            Assert.False(await store.GetMustChangePasswordAsync(otherOrganizationId, userId));
            Assert.False(await store.GetMustChangePasswordAsync(organizationId, Guid.NewGuid()));

            var initial = await store.GetCredentialAsync(organizationId, userId);
            Assert.NotNull(initial);
            Assert.Equal(new string('x', 64), initial.PasswordHash);
            Assert.Equal("{}", initial.PasswordParameters);
            Assert.Equal(1, initial.CredentialVersion);
            Assert.Equal("active", initial.AccountStatus);

            Assert.False(await store.UpdateCredentialAsync(
                organizationId, Guid.NewGuid(),
                new PasswordHashRecord(FakeHash("hash:fresh"), "{}"), 2));
            Assert.False(await store.UpdateCredentialAsync(
                otherOrganizationId, userId,
                new PasswordHashRecord(FakeHash("hash:fresh"), "{}"), 2));

            Assert.True(await store.UpdateCredentialAsync(
                organizationId, userId,
                new PasswordHashRecord(FakeHash("hash:fresh"), "{\"memoryKiB\":65536}"), 2));

            var rotated = await store.GetCredentialAsync(organizationId, userId);
            Assert.NotNull(rotated);
            Assert.Equal(FakeHash("hash:fresh"), rotated.PasswordHash);
            Assert.Equal("{\"memoryKiB\": 65536}", rotated.PasswordParameters);
            Assert.Equal(2, rotated.CredentialVersion);
            Assert.Equal("active", rotated.AccountStatus);

            var hashes = Enumerable.Range(1, 6)
                .Select(index => new PasswordHashRecord(FakeHash($"history:{index}"), "{}"))
                .ToArray();
            foreach (var hash in hashes)
            {
                await store.AddPasswordToHistoryAsync(organizationId, userId, hash);
            }

            await store.AddPasswordToHistoryAsync(otherOrganizationId, userId, new PasswordHashRecord(FakeHash("history:foreign-org"), "{}"));
            await store.AddPasswordToHistoryAsync(organizationId, Guid.NewGuid(), new PasswordHashRecord(FakeHash("history:unknown-user"), "{}"));
            await store.AddPasswordToHistoryAsync(otherOrganizationId, Guid.NewGuid(), new PasswordHashRecord(FakeHash("history:both-foreign"), "{}"));

            var recentFive = await store.GetRecentPasswordHistoryAsync(organizationId, userId, 5);
            Assert.Equal(5, recentFive.Count);
            Assert.Equal(FakeHash("history:6"), recentFive[0].Hash);
            Assert.Equal(FakeHash("history:5"), recentFive[1].Hash);
            Assert.Equal(FakeHash("history:4"), recentFive[2].Hash);
            Assert.Equal(FakeHash("history:3"), recentFive[3].Hash);
            Assert.Equal(FakeHash("history:2"), recentFive[4].Hash);
            Assert.All(recentFive, record => Assert.Equal("{}", record.Parameters));

            var recentThree = await store.GetRecentPasswordHistoryAsync(organizationId, userId, 3);
            Assert.Equal(3, recentThree.Count);
            Assert.Equal(FakeHash("history:6"), recentThree[0].Hash);
            Assert.Equal(FakeHash("history:5"), recentThree[1].Hash);
            Assert.Equal(FakeHash("history:4"), recentThree[2].Hash);

            Assert.Empty(await store.GetRecentPasswordHistoryAsync(otherOrganizationId, userId, 5));
            Assert.Empty(await store.GetRecentPasswordHistoryAsync(organizationId, Guid.NewGuid(), 5));

            Assert.True(await store.ResetMustChangePasswordAsync(organizationId, userId));
            Assert.False(await store.ResetMustChangePasswordAsync(organizationId, Guid.NewGuid()));
            Assert.False(await store.ResetMustChangePasswordAsync(otherOrganizationId, userId));
            Assert.False(await store.GetMustChangePasswordAsync(organizationId, userId));

            await using (var flagCommand = dataSource.CreateCommand(
                """
                SELECT must_change_password
                FROM iam.user_accounts
                WHERE organization_id = $1 AND id = $2;
                """))
            {
                flagCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
                flagCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
                var flag = await flagCommand.ExecuteScalarAsync();
                Assert.NotNull(flag);
                Assert.False((bool)flag);
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            DropDatabase(adminDataSource, databaseName);
        }
    }

    private static string FakeHash(string prefix) =>
        prefix + new string('x', 64);

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
            VALUES ($1, $2, $3, $4, $5, '{}'::jsonb, 'active', true);
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
