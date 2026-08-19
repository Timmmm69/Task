using Npgsql;
using Task.Application.Audit;
using Task.Infrastructure.Persistence;
using Task.Infrastructure.Postgres;
using Xunit.Abstractions;

namespace Task.Tests.Postgres;

/// <summary>
/// Guarded integration gate for PostgresAuditEntryStore over governance.audit_entries
/// (migration 002). Tests run only when TASK_POSTGRES_TEST_ADMIN_CONNECTION is set; each
/// test creates an isolated throwaway database, applies all migrations and drops it again.
/// The append-only trigger trg_audit_entries_append_only already exists in migration 002,
/// so the UPDATE/DELETE rejection test asserts the real database behavior instead of
/// relying on a comment.
/// </summary>
public sealed class PostgresAuditEntryStoreTests
{
    private const string ConnectionEnvironmentVariable = "TASK_POSTGRES_TEST_ADMIN_CONNECTION";
    private readonly ITestOutputHelper _output;

    public PostgresAuditEntryStoreTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RealPostgres_AppendReadRoundTrip_ClientTimestampIgnored()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            _output.WriteLine(
                $"NOT RUN: set {ConnectionEnvironmentVariable} to execute the real PostgreSQL integration gate.");
            return;
        }

        var databaseName = $"task_audit_{Guid.NewGuid():N}";
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

            var store = new PostgresAuditEntryStore(dataSource);

            var organizationId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var employeeProfileId = Guid.NewGuid();
            var sessionId = Guid.NewGuid();
            SeedOrganization(dataSource, organizationId);
            SeedUser(dataSource, organizationId, userId, employeeProfileId);
            SeedSession(dataSource, organizationId, userId, sessionId);

            // The caller-supplied timestamp is deliberately forged: append-only requires the
            // store to ignore it and let the database assign occurred_at.
            var forgedOccurredAt = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

            var correlationId = Guid.NewGuid();
            var requestId = Guid.NewGuid();
            var entryId = Guid.NewGuid();
            await store.AppendAsync(
                new AuditEntryRecord(
                    entryId,
                    organizationId,
                    forgedOccurredAt,
                    userId,
                    sessionId,
                    "task.update",
                    "success",
                    "regular_flow",
                    correlationId,
                    requestId,
                    "{\"marker\": \"m1\"}",
                    "{\"status\": \"new\"}",
                    "{\"status\": \"in_progress\"}",
                    "standard"),
                CancellationToken.None);

            var bareId = Guid.NewGuid();
            await store.AppendAsync(
                new AuditEntryRecord(
                    bareId,
                    organizationId,
                    forgedOccurredAt,
                    null,
                    null,
                    "auth.login",
                    "denied",
                    null,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    AuditEntryRecord.DefaultMetadata,
                    null,
                    null,
                    "restricted"),
                CancellationToken.None);

            var oversizedMetadata = new string('x', 16 * 1024 + 1);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => store.AppendAsync(
                    new AuditEntryRecord(
                        Guid.NewGuid(),
                        organizationId,
                        forgedOccurredAt,
                        null,
                        null,
                        "task.delete",
                        "failure",
                        null,
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        oversizedMetadata,
                        null,
                        null,
                        "standard"),
                    CancellationToken.None));

            await Assert.ThrowsAsync<ArgumentException>(
                () => store.AppendAsync(
                    new AuditEntryRecord(
                        Guid.NewGuid(),
                        Guid.Empty,
                        forgedOccurredAt,
                        null,
                        null,
                        "task.delete",
                        "failure",
                        null,
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        AuditEntryRecord.DefaultMetadata,
                        null,
                        null,
                        "standard"),
                    CancellationToken.None));

            await Assert.ThrowsAsync<ArgumentException>(
                () => store.ReadAsync(
                    new AuditQuery(organizationId, PageToken: "not-a-base64-token!!"),
                    CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(
                () => store.ReadAsync(
                    new AuditQuery(organizationId, PageToken: Convert.ToBase64String("garbage"u8.ToArray())),
                    CancellationToken.None));

            var page = await store.ReadAsync(new AuditQuery(organizationId), CancellationToken.None);
            Assert.NotNull(page);
            Assert.Equal(2, page.Entries.Count);
            Assert.Null(page.NextPageToken);

                        // OccurredAt is server-assigned: not the forged value and sits on the
            // database clock (within a minute of it), not on the host clock.
            var dbClock = GetDatabaseClock(dataSource);
            Assert.All(page.Entries, entry => Assert.NotEqual(forgedOccurredAt, entry.OccurredAt));
            Assert.All(
                page.Entries,
                entry => Assert.InRange(
                    entry.OccurredAt,
                    dbClock.AddMinutes(-1),
                    dbClock.AddMinutes(1)));

            // Newest first by (occurred_at, id) descending.
            Assert.True(
                page.Entries[0].OccurredAt >= page.Entries[1].OccurredAt,
                "Page must be ordered by occurred_at descending.");

            var full = page.Entries.Single(e => e.Id == entryId);
            Assert.Equal(organizationId, full.OrganizationId);
            Assert.Equal(userId, full.ActorUserId);
            Assert.Equal(sessionId, full.ActorSessionId);
            Assert.Equal("task.update", full.ActionCode);
            Assert.Equal("success", full.Outcome);
            Assert.Equal("regular_flow", full.ReasonCode);
            Assert.Equal(correlationId, full.CorrelationId);
            Assert.Equal(requestId, full.RequestId);
            Assert.Equal("{\"marker\": \"m1\"}", full.Metadata);
            Assert.Equal("{\"status\": \"new\"}", full.OldState);
            Assert.Equal("{\"status\": \"in_progress\"}", full.NewState);
            Assert.Equal("standard", full.RedactionLevel);

            var bare = page.Entries.Single(e => e.Id == bareId);
            Assert.Null(bare.ActorUserId);
            Assert.Null(bare.ActorSessionId);
            Assert.Null(bare.ReasonCode);
            Assert.Equal(AuditEntryRecord.DefaultMetadata, bare.Metadata);
            Assert.Null(bare.OldState);
            Assert.Null(bare.NewState);
            Assert.Equal("restricted", bare.RedactionLevel);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            DropDatabase(adminDataSource, databaseName);
        }
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RealPostgres_KeysetPagination_PageSizeClamp_NoGapsOrDuplicates()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            _output.WriteLine(
                $"NOT RUN: set {ConnectionEnvironmentVariable} to execute the real PostgreSQL integration gate.");
            return;
        }

        var databaseName = $"task_audit_{Guid.NewGuid():N}";
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

            var store = new PostgresAuditEntryStore(dataSource);
            var organizationId = Guid.NewGuid();
            SeedOrganization(dataSource, organizationId);

            var insertedIds = new HashSet<Guid>();
            for (var index = 0; index < 60; index++)
            {
                var id = Guid.NewGuid();
                insertedIds.Add(id);
                await store.AppendAsync(
                    new AuditEntryRecord(
                        id,
                        organizationId,
                        DateTimeOffset.UtcNow,
                        null,
                        null,
                        "task.update",
                        "success",
                        null,
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        $"{{\"i\": {index}}}",
                        null,
                        null,
                        "standard"),
                    CancellationToken.None);
            }

            var seenIds = new HashSet<Guid>();
            var previousKey = (OccurredAt: DateTimeOffset.MaxValue, Id: Guid.Empty);
            string? pageToken = null;
            var pageCount = 0;
            do
            {
                var page = await store.ReadAsync(
                    new AuditQuery(organizationId, PageSize: 10, PageToken: pageToken),
                    CancellationToken.None);
                Assert.NotNull(page);
                Assert.InRange(page.Entries.Count, 1, 10);
                pageCount++;
                foreach (var entry in page.Entries)
                {
                    Assert.True(seenIds.Add(entry.Id), "No entry may appear on two pages.");
                    Assert.True(
                        entry.OccurredAt < previousKey.OccurredAt ||
                        (entry.OccurredAt == previousKey.OccurredAt &&
                         entry.Id.CompareTo(previousKey.Id) < 0),
                        "Keyset boundary must be strictly descending across pages.");
                    previousKey = (entry.OccurredAt, entry.Id);
                }

                pageToken = page.NextPageToken;
            }
            while (pageToken is not null);

            Assert.Equal(6, pageCount);
            Assert.Equal(insertedIds, seenIds);

            // PageSize < 1 clamps to the default of 50.
            var clampedLow = await store.ReadAsync(
                new AuditQuery(organizationId, PageSize: 0),
                CancellationToken.None);
            Assert.Equal(50, clampedLow.Entries.Count);
            Assert.NotNull(clampedLow.NextPageToken);
            var clampedLowNext = await store.ReadAsync(
                new AuditQuery(organizationId, PageSize: 0, PageToken: clampedLow.NextPageToken),
                CancellationToken.None);
            Assert.Equal(10, clampedLowNext.Entries.Count);
            Assert.Null(clampedLowNext.NextPageToken);

            // PageSize beyond the maximum clamps to 200.
            var clampedHigh = await store.ReadAsync(
                new AuditQuery(organizationId, PageSize: 100_000),
                CancellationToken.None);
            Assert.Equal(60, clampedHigh.Entries.Count);
            Assert.Null(clampedHigh.NextPageToken);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            DropDatabase(adminDataSource, databaseName);
        }
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RealPostgres_Filters_AndEmptyFilterIsNoFilter()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            _output.WriteLine(
                $"NOT RUN: set {ConnectionEnvironmentVariable} to execute the real PostgreSQL integration gate.");
            return;
        }

        var databaseName = $"task_audit_{Guid.NewGuid():N}";
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

            var store = new PostgresAuditEntryStore(dataSource);
            var organizationId = Guid.NewGuid();
            SeedOrganization(dataSource, organizationId);

            for (var index = 0; index < 30; index++)
            {
                await Append(dataSource, organizationId, "task.update", "success");
            }

            for (var index = 0; index < 20; index++)
            {
                await Append(dataSource, organizationId, "auth.login", "success");
            }

            for (var index = 0; index < 10; index++)
            {
                await Append(dataSource, organizationId, "auth.login", "denied");
            }

            var dbNow = GetDatabaseClock(dataSource);

            // All counts below exceed the default page size of 50, so every "full journal"
            // read requests the maximum page size explicitly.
            Assert.Equal(
                60,
                (await store.ReadAsync(
                    new AuditQuery(organizationId, PageSize: 200),
                    CancellationToken.None)).Entries.Count);
            Assert.Equal(
                60,
                (await store.ReadAsync(
                    new AuditQuery(organizationId, PageSize: 200, ActionFilter: ""),
                    CancellationToken.None)).Entries.Count);
            Assert.Equal(
                60,
                (await store.ReadAsync(
                    new AuditQuery(organizationId, PageSize: 200, OutcomeFilter: "   "),
                    CancellationToken.None)).Entries.Count);
            Assert.Equal(
                30,
                (await store.ReadAsync(
                    new AuditQuery(organizationId, ActionFilter: "auth.login"),
                    CancellationToken.None)).Entries.Count);
            Assert.Equal(
                10,
                (await store.ReadAsync(
                    new AuditQuery(organizationId, OutcomeFilter: "denied"),
                    CancellationToken.None)).Entries.Count);
            Assert.Equal(
                20,
                (await store.ReadAsync(
                    new AuditQuery(organizationId, ActionFilter: "auth.login", OutcomeFilter: "success"),
                    CancellationToken.None)).Entries.Count);
            Assert.Equal(
                60,
                (await store.ReadAsync(
                    new AuditQuery(organizationId, PageSize: 200, FromUtc: dbNow.AddHours(-1)),
                    CancellationToken.None)).Entries.Count);
            Assert.Empty(
                (await store.ReadAsync(
                    new AuditQuery(organizationId, PageSize: 200, ToUtc: dbNow.AddHours(-1)),
                    CancellationToken.None)).Entries);
            Assert.Empty(
                (await store.ReadAsync(
                    new AuditQuery(organizationId, PageSize: 200, FromUtc: dbNow.AddHours(1)),
                    CancellationToken.None)).Entries);
            Assert.Equal(
                60,
                (await store.ReadAsync(
                    new AuditQuery(organizationId, PageSize: 200, FromUtc: dbNow.AddHours(-1), ToUtc: dbNow.AddHours(1)),
                    CancellationToken.None)).Entries.Count);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            DropDatabase(adminDataSource, databaseName);
        }
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RealPostgres_ForeignOrganizationIsolation()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            _output.WriteLine(
                $"NOT RUN: set {ConnectionEnvironmentVariable} to execute the real PostgreSQL integration gate.");
            return;
        }

        var databaseName = $"task_audit_{Guid.NewGuid():N}";
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

            var store = new PostgresAuditEntryStore(dataSource);
            var organizationId = Guid.NewGuid();
            var otherOrganizationId = Guid.NewGuid();
            SeedOrganization(dataSource, organizationId);
            SeedOrganization(dataSource, otherOrganizationId);

            for (var index = 0; index < 5; index++)
            {
                await Append(dataSource, organizationId, "task.update", "success");
            }

            await Append(dataSource, otherOrganizationId, "auth.login", "denied");

            var own = await store.ReadAsync(new AuditQuery(organizationId), CancellationToken.None);
            Assert.Equal(5, own.Entries.Count);
            Assert.All(own.Entries, entry => Assert.Equal(organizationId, entry.OrganizationId));

            var foreign = await store.ReadAsync(new AuditQuery(otherOrganizationId), CancellationToken.None);
            var foreignEntry = Assert.Single(foreign.Entries);
            Assert.Equal(otherOrganizationId, foreignEntry.OrganizationId);

            var foreignFiltered = await store.ReadAsync(
                new AuditQuery(otherOrganizationId, ActionFilter: "task.update"),
                CancellationToken.None);
            Assert.Empty(foreignFiltered.Entries);

            var ownFiltered = await store.ReadAsync(
                new AuditQuery(organizationId, ActionFilter: "auth.login"),
                CancellationToken.None);
            Assert.Empty(ownFiltered.Entries);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            DropDatabase(adminDataSource, databaseName);
        }
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RealPostgres_UpdateAndDeleteRejectedByAppendOnlyTrigger()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            _output.WriteLine(
                $"NOT RUN: set {ConnectionEnvironmentVariable} to execute the real PostgreSQL integration gate.");
            return;
        }

        var databaseName = $"task_audit_{Guid.NewGuid():N}";
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

            var store = new PostgresAuditEntryStore(dataSource);
            var organizationId = Guid.NewGuid();
            SeedOrganization(dataSource, organizationId);

            var entryId = Guid.NewGuid();
            await store.AppendAsync(
                new AuditEntryRecord(
                    entryId,
                    organizationId,
                    DateTimeOffset.UtcNow,
                    null,
                    null,
                    "task.update",
                    "success",
                    "original_reason",
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    AuditEntryRecord.DefaultMetadata,
                    null,
                    null,
                    "standard"),
                CancellationToken.None);

            // UPDATE is rejected by trg_audit_entries_append_only (migration 002):
            // exception ERRCODE 42501, message 'APPEND_ONLY_AUDIT_ENTRIES'.
            using (var updateCommand = dataSource.CreateCommand(
                """
                UPDATE governance.audit_entries
                SET reason_code = 'tampered'
                WHERE id = $1;
                """))
            {
                updateCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = entryId });
                var updateException = await Assert.ThrowsAsync<PostgresException>(
                    () => updateCommand.ExecuteNonQueryAsync());
                Assert.Equal("42501", updateException.SqlState);
                Assert.Contains("APPEND_ONLY_AUDIT_ENTRIES", updateException.Message);
            }

            // DELETE is rejected the same way.
            using (var deleteCommand = dataSource.CreateCommand(
                "DELETE FROM governance.audit_entries WHERE id = $1;"))
            {
                deleteCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = entryId });
                var deleteException = await Assert.ThrowsAsync<PostgresException>(
                    () => deleteCommand.ExecuteNonQueryAsync());
                Assert.Equal("42501", deleteException.SqlState);
                Assert.Contains("APPEND_ONLY_AUDIT_ENTRIES", deleteException.Message);
            }

            // The journal is physically unchanged: the entry is still there with its
            // original payload, exactly as the append-only guarantee requires.
            var page = await store.ReadAsync(new AuditQuery(organizationId), CancellationToken.None);
            var entry = Assert.Single(page.Entries);
            Assert.Equal(entryId, entry.Id);
            Assert.Equal("original_reason", entry.ReasonCode);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            DropDatabase(adminDataSource, databaseName);
        }
    }

    private static async global::System.Threading.Tasks.Task Append(
        NpgsqlDataSource dataSource,
        Guid organizationId,
        string actionCode,
        string outcome)
    {
        using var command = dataSource.CreateCommand(
            """
            INSERT INTO governance.audit_entries (
                id, organization_id, action_code, outcome, correlation_id, request_id)
            VALUES ($1, $2, $3, $4, $5, $6);
            """);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = Guid.NewGuid() });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = actionCode });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = outcome });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = Guid.NewGuid() });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = Guid.NewGuid() });
        await command.ExecuteNonQueryAsync();
    }

    private static DateTimeOffset GetDatabaseClock(NpgsqlDataSource dataSource)
    {
        using var command = dataSource.CreateCommand("SELECT clock_timestamp();");
        var raw = (DateTime)command.ExecuteScalar()!;
        return new DateTimeOffset(DateTime.SpecifyKind(raw, DateTimeKind.Utc));
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

    private static void SeedOrganization(NpgsqlDataSource dataSource, Guid organizationId)
    {
        using var command = dataSource.CreateCommand(
            """
            INSERT INTO core.organizations (id, code, name, default_time_zone)
            VALUES ($1, $2, $3, $4);
            """);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = $"org-{organizationId:N}" });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = "Integration Test Organization" });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = "Europe/Minsk" });
        command.ExecuteNonQuery();
    }

    private static void SeedUser(
        NpgsqlDataSource dataSource,
        Guid organizationId,
        Guid userId,
        Guid employeeProfileId)
    {
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

    private static void SeedSession(
        NpgsqlDataSource dataSource,
        Guid organizationId,
        Guid userId,
        Guid sessionId)
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        using var command = dataSource.CreateCommand(
            """
            INSERT INTO iam.sessions (
                id, organization_id, user_account_id, credential_version,
                authorization_scope_version, idle_expires_at, absolute_expires_at)
            VALUES ($1, $2, $3, 1, 1, $4, $5);
            """);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = sessionId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = now.AddHours(1) });
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = now.AddDays(1) });
        command.ExecuteNonQuery();
    }
}