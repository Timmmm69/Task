using Npgsql;
using Task.Application.Security;
using Task.Infrastructure.Persistence;
using Task.Infrastructure.Postgres;
using Xunit.Abstractions;

namespace Task.Tests.Postgres;

public sealed class PostgresAccountLockoutStoreTests
{
    private const string ConnectionEnvironmentVariable = "TASK_POSTGRES_TEST_ADMIN_CONNECTION";
    private readonly ITestOutputHelper _output;

    public PostgresAccountLockoutStoreTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RealPostgres_LockoutStateAndFailedIncrement()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            _output.WriteLine(
                $"NOT RUN: set {ConnectionEnvironmentVariable} to execute the real PostgreSQL integration gate.");
            return;
        }

        var databaseName = $"task_lockout_{Guid.NewGuid():N}";
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

            var store = new PostgresAccountLockoutStore(dataSource);

            var organizationId = Guid.NewGuid();
            var otherOrganizationId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();
            SeedOrganizationAndUser(dataSource, organizationId, userId, Guid.NewGuid());
            SeedOrganizationAndUser(dataSource, otherOrganizationId, otherUserId, Guid.NewGuid());

            await Assert.ThrowsAsync<ArgumentException>(
                () => store.GetLockoutStateAsync(Guid.Empty, userId));
            await Assert.ThrowsAsync<ArgumentException>(
                () => store.GetLockoutStateAsync(organizationId, Guid.Empty));

            Assert.Null(await store.GetLockoutStateAsync(otherOrganizationId, userId));
            Assert.Null(await store.GetLockoutStateAsync(organizationId, Guid.NewGuid()));

            var initial = await store.GetLockoutStateAsync(organizationId, userId);
            Assert.NotNull(initial);
            Assert.Equal(0, initial.FailedLoginCount);
            Assert.Equal("active", initial.AccountStatus);
            Assert.Null(initial.LockedUntilUtc);
            Assert.InRange(
                initial.DbNowUtc,
                new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
                DateTimeOffset.UtcNow.AddDays(1));

            Assert.Equal(1, await store.RecordFailedLoginAsync(organizationId, userId, 1, null));
            var afterFirst = await store.GetLockoutStateAsync(organizationId, userId);
            Assert.NotNull(afterFirst);
            Assert.Equal(1, afterFirst.FailedLoginCount);
            Assert.Null(afterFirst.LockedUntilUtc);

            var nowUtc = afterFirst.DbNowUtc;
            Assert.Equal(
                5,
                await store.RecordFailedLoginAsync(
                    organizationId, userId, 5, nowUtc + TimeSpan.FromMinutes(15)));
            var afterThreshold = await store.GetLockoutStateAsync(organizationId, userId);
            Assert.NotNull(afterThreshold);
            Assert.Equal(5, afterThreshold.FailedLoginCount);
            Assert.NotNull(afterThreshold.LockedUntilUtc);
            Assert.InRange(
                afterThreshold.LockedUntilUtc.Value - nowUtc,
                TimeSpan.FromMinutes(14),
                TimeSpan.FromMinutes(16));

            Assert.Equal(
                6,
                await store.RecordFailedLoginAsync(
                    organizationId, userId, 6, nowUtc + TimeSpan.FromMinutes(60)));
            var afterEscalation = await store.GetLockoutStateAsync(organizationId, userId);
            Assert.NotNull(afterEscalation);
            Assert.Equal(6, afterEscalation.FailedLoginCount);
            Assert.InRange(
                afterEscalation.LockedUntilUtc!.Value - nowUtc,
                TimeSpan.FromMinutes(59),
                TimeSpan.FromMinutes(61));

            Assert.Equal(
                0,
                await store.RecordFailedLoginAsync(
                    otherOrganizationId, userId, 7, nowUtc + TimeSpan.FromMinutes(60)));
            Assert.Equal(
                0,
                await store.RecordFailedLoginAsync(
                    organizationId, Guid.NewGuid(), 7, nowUtc + TimeSpan.FromMinutes(60)));
            var unaffected = await store.GetLockoutStateAsync(organizationId, userId);
            Assert.NotNull(unaffected);
            Assert.Equal(6, unaffected.FailedLoginCount);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            DropDatabase(adminDataSource, databaseName);
        }
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RealPostgres_SuccessfulLoginResetsAndBlockedIsNeverReset()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            _output.WriteLine(
                $"NOT RUN: set {ConnectionEnvironmentVariable} to execute the real PostgreSQL integration gate.");
            return;
        }

        var databaseName = $"task_lockout_{Guid.NewGuid():N}";
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

            var store = new PostgresAccountLockoutStore(dataSource);

            var organizationId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            SeedOrganizationAndUser(dataSource, organizationId, userId, Guid.NewGuid());

            var initial = await store.GetLockoutStateAsync(organizationId, userId);
            Assert.NotNull(initial);

            await store.RecordFailedLoginAsync(organizationId, userId, 3, initial.DbNowUtc + TimeSpan.FromMinutes(5));
            await store.RecordSuccessfulLoginAsync(organizationId, userId);
            var afterSuccess = await store.GetLockoutStateAsync(organizationId, userId);
            Assert.NotNull(afterSuccess);
            Assert.Equal(0, afterSuccess.FailedLoginCount);
            Assert.Null(afterSuccess.LockedUntilUtc);

            var lockUntil = afterSuccess.DbNowUtc + TimeSpan.FromMinutes(15);
            await store.RecordFailedLoginAsync(organizationId, userId, 4, lockUntil);
            SetAccountStatus(dataSource, userId, "blocked");
            await store.RecordSuccessfulLoginAsync(organizationId, userId);
            await store.RecordSuccessfulLoginAsync(organizationId, userId);

            var blocked = await store.GetLockoutStateAsync(organizationId, userId);
            Assert.NotNull(blocked);
            Assert.Equal("blocked", blocked.AccountStatus);
            Assert.Equal(4, blocked.FailedLoginCount);
            Assert.NotNull(blocked.LockedUntilUtc);
            Assert.InRange(
                blocked.LockedUntilUtc.Value - lockUntil,
                TimeSpan.FromMinutes(-1),
                TimeSpan.FromMinutes(1));

            Assert.Equal(
                0,
                await store.RecordFailedLoginAsync(
                    organizationId, userId, 9, blocked.DbNowUtc + TimeSpan.FromMinutes(60)));
            var stillBlocked = await store.GetLockoutStateAsync(organizationId, userId);
            Assert.NotNull(stillBlocked);
            Assert.Equal("blocked", stillBlocked.AccountStatus);
            Assert.Equal(4, stillBlocked.FailedLoginCount);
            Assert.Equal(blocked.LockedUntilUtc, stillBlocked.LockedUntilUtc);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            DropDatabase(adminDataSource, databaseName);
        }
    }

    private static void SetAccountStatus(NpgsqlDataSource dataSource, Guid userId, string status)
    {
        using var command = dataSource.CreateCommand(
            "UPDATE iam.user_accounts SET account_status = $2 WHERE id = $1;");
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = status });
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
