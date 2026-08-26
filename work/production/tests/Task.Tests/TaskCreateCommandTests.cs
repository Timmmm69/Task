using System.Text.Json;
using Npgsql;
using Task.Application;
using Task.Application.Security;
using Task.Domain;
using Task.Infrastructure.Persistence;

namespace Task.Tests;

public sealed class RequiresPostgresFactAttribute : FactAttribute
{
    public RequiresPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TaskCreateCommandTests.ConnectionEnvironmentVariable)))
        {
            Skip = $"Set {TaskCreateCommandTests.ConnectionEnvironmentVariable} to execute the real PostgreSQL create gate.";
        }
    }
}

public sealed class TaskCreateCommandTests
{
    public const string ConnectionEnvironmentVariable = "TASK_POSTGRES_TEST_ADMIN_CONNECTION";
    private static readonly Guid OrganizationId = Guid.Parse("751fa8ce-5cc3-4d98-8574-1108080b2ff4");
    private static readonly Guid ActorId = Guid.Parse("3077f0f8-536f-4988-bd73-6f26265d0b92");
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WithInitialPriorityAndSchedule_StaysAtVersionOne()
    {
        var schedule = TaskSchedule.Create(Now, Now.AddHours(2));
        var task = TaskAggregate.Create(
            Guid.Parse("b64fbeec-f0f4-4f5f-9967-ea2ce57be461"),
            OrganizationId,
            ActorId,
            "  Planned  ",
            Now,
            TaskPriority.High,
            schedule);

        Assert.Equal(1, task.Metadata.Version);
        Assert.Equal("Planned", task.Title);
        Assert.Equal(TaskPriority.High, task.Priority);
        Assert.Equal(schedule, task.Schedule);
        Assert.Equal(TaskWorkStatus.New, task.WorkStatus);
    }

    [Fact]
    public void CreateCommand_HashesCanonicalBodyAndOmitsTitleFromSafePayload()
    {
        var service = new TaskCreateCommandService(new RecordingExecutor());
        var context = CreateContext(OrganizationId, ActorId);
        var model = new TaskCreateModel("Secret title", TaskPriority.Critical, true, Now, Now.AddHours(1));

        var command = service.CreateCommand(
            context,
            "create-key-01",
            """{"priority":"critical","title":"Secret title"}""",
            model,
            CreateHttpResult,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Now);

        Assert.Equal(
            TaskWriteRequestHasher.ComputeSha256("""{"title":"Secret title","priority":"critical"}"""),
            command.RequestHash);
        Assert.Equal(["title", "priority", "startAtUtc", "deadlineAt"], command.ChangedFields);
        Assert.DoesNotContain("Secret title", command.SafePayloadJson, StringComparison.Ordinal);
        Assert.Contains("\"authorUserId\":", command.SafePayloadJson, StringComparison.Ordinal);
        Assert.Equal("task.create", command.AuditAction);
        Assert.Equal("TaskCreated", command.EventType);

        var mutation = command.Mutation(null);
        Assert.Equal(1, mutation.Aggregate.Metadata.Version);
        Assert.Equal(TaskPriority.Critical, mutation.Aggregate.Priority);
        Assert.DoesNotContain("password", command.SafePayloadJson, StringComparison.OrdinalIgnoreCase);
    }

    [RequiresPostgresFact]
    public async global::System.Threading.Tasks.Task RealPostgres_CreateIsAtomicDurableAndReadable()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            throw new InvalidOperationException("The PostgreSQL test should have been skipped during discovery.");
        }

        var databaseName = $"task_create_{Guid.NewGuid():N}";
        using var adminDataSource = NpgsqlDataSource.Create(adminConnectionString);
        Execute(adminDataSource, $"CREATE DATABASE \"{databaseName}\";");
        try
        {
            var databaseConnection = new NpgsqlConnectionStringBuilder(adminConnectionString)
            {
                Database = databaseName,
            }.ConnectionString;
            await using var dataSource = NpgsqlDataSource.Create(databaseConnection);
            await ApplySchemaAsync(dataSource);

            var organizationId = Guid.NewGuid();
            var otherOrganizationId = Guid.NewGuid();
            var actorUserId = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();
            var sessionId = Guid.NewGuid();
            await SeedOrganizationAndUserAsync(dataSource, organizationId, actorUserId, "actor");
            await SeedOrganizationAndUserAsync(dataSource, otherOrganizationId, otherUserId, "other");
            await SeedSessionAsync(dataSource, sessionId, organizationId, actorUserId);

            var executor = new PostgresTaskWriteCommandExecutor(dataSource);
            var service = new TaskCreateCommandService(executor);
            var taskId = Guid.NewGuid();
            var context = CreateContext(organizationId, actorUserId, sessionId);
            var model = new TaskCreateModel("Create gate", TaskPriority.High, true, Now, Now.AddHours(3));
            var command = service.CreateCommand(
                context,
                "create-pg-key-01",
                """{"title":"Create gate","priority":"high"}""",
                model,
                CreateHttpResult,
                taskId,
                Now);

            var executed = await service.ExecuteAsync(command);
            Assert.Equal(TaskWriteCommandDisposition.Executed, executed.Disposition);
            Assert.False(executed.IsReplay);
            await AssertEffectsAsync(dataSource, organizationId, taskId, 1);

            var replayed = await service.ExecuteAsync(command);
            Assert.Equal(TaskWriteCommandDisposition.Replayed, replayed.Disposition);
            Assert.True(replayed.IsReplay);
            Assert.Equal(executed.HttpResult!.BodyJson, replayed.HttpResult!.BodyJson);
            await AssertEffectsAsync(dataSource, organizationId, taskId, 1);

            var readStore = new PostgresTaskReadStore(dataSource);
            var visible = await readStore.GetByIdAsync(organizationId, taskId);
            Assert.NotNull(visible);
            Assert.Equal("Create gate", visible.Title);
            Assert.Equal(TaskPriority.High, visible.Priority);
            Assert.Equal(actorUserId, visible.AuthorUserId);
            Assert.Null(await readStore.GetByIdAsync(otherOrganizationId, taskId));

            var reused = service.CreateCommand(
                context,
                "create-pg-key-01",
                """{"title":"Different"}""",
                new TaskCreateModel("Different", TaskPriority.Normal, false, null, null),
                CreateHttpResult,
                Guid.NewGuid(),
                Now);
            Assert.Equal(TaskWriteCommandDisposition.IdempotencyKeyReused, (await service.ExecuteAsync(reused)).Disposition);

            var rollbackId = Guid.NewGuid();
            await SeedConflictingDomainEventAsync(dataSource, organizationId, actorUserId, rollbackId);
            var rollback = service.CreateCommand(
                context,
                "create-pg-rollback",
                """{"title":"Rollback"}""",
                new TaskCreateModel("Rollback", TaskPriority.Normal, false, null, null),
                CreateHttpResult,
                rollbackId,
                Now);
            await Assert.ThrowsAsync<PostgresException>(() => service.ExecuteAsync(rollback));
            Assert.Equal(0, await CountAsync(dataSource, "SELECT count(*) FROM work.tasks WHERE id = $1;", rollbackId));
            Assert.Equal(0, await CountAsync(dataSource, "SELECT count(*) FROM iam.idempotency_records WHERE idempotency_key = $1;", "create-pg-rollback"));

            using (var cancellation = new CancellationTokenSource())
            {
                var cancelledId = Guid.NewGuid();
                var cancelled = service.CreateCommand(
                    context,
                    "create-pg-cancel",
                    """{"title":"Cancelled"}""",
                    new TaskCreateModel("Cancelled", TaskPriority.Normal, false, null, null),
                    aggregate =>
                    {
                        cancellation.Cancel();
                        return CreateHttpResult(aggregate);
                    },
                    cancelledId,
                    Now);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    service.ExecuteAsync(cancelled, cancellation.Token));
                Assert.Equal(0, await CountAsync(dataSource, "SELECT count(*) FROM work.tasks WHERE id = $1;", cancelledId));
            }

            Assert.True(await ScalarAsync<bool>(
                dataSource,
                """
                SELECT NOT EXISTS (
                    SELECT 1 FROM governance.audit_entries
                    WHERE metadata::text ~* '(password|token|secret)'
                       OR coalesce(new_state::text, '') ~* '(password|token|secret)'
                    UNION ALL
                    SELECT 1 FROM governance.domain_events WHERE payload::text ~* '(password|token|secret)'
                    UNION ALL
                    SELECT 1 FROM governance.outbox_messages WHERE payload::text ~* '(password|token|secret)'
                );
                """));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            Execute(adminDataSource, $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);");
        }
    }

    private static AuthenticatedRequestContext CreateContext(
        Guid organizationId,
        Guid userId,
        Guid? sessionId = null) =>
        new(userId, sessionId ?? Guid.Parse("22222222-2222-2222-2222-222222222222"), organizationId, 1, 1, Guid.NewGuid().ToString("D"), "trace");

    private static async global::System.Threading.Tasks.Task SeedSessionAsync(
        NpgsqlDataSource dataSource,
        Guid sessionId,
        Guid organizationId,
        Guid userId)
    {
        var expires = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO iam.sessions (
                id, organization_id, user_account_id, credential_version, authorization_scope_version,
                idle_expires_at, absolute_expires_at)
            VALUES ($1, $2, $3, 1, 1, $4, $4);
            """);
        command.Parameters.AddWithValue(sessionId);
        command.Parameters.AddWithValue(organizationId);
        command.Parameters.AddWithValue(userId);
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = expires });
        await command.ExecuteNonQueryAsync();
    }

    private static TaskWriteHttpResult CreateHttpResult(TaskAggregate aggregate) =>
        new(
            201,
            new Dictionary<string, string> { ["ETag"] = "\"v1\"" },
            JsonSerializer.Serialize(new { id = aggregate.Metadata.Id, title = aggregate.Title, version = 1 }),
            aggregate.Metadata.Id);

    private static async global::System.Threading.Tasks.Task ApplySchemaAsync(NpgsqlDataSource dataSource)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var bootstrap = new NpgsqlCommand(
            """
            CREATE SCHEMA infrastructure;
            CREATE TABLE infrastructure.schema_migrations (
                version integer PRIMARY KEY,
                name text NOT NULL,
                sha256 char(64) NOT NULL,
                applied_at timestamptz NOT NULL DEFAULT clock_timestamp()
            );
            """,
            connection,
            transaction))
        {
            await bootstrap.ExecuteNonQueryAsync();
        }

        foreach (var migration in TaskPersistenceMigrationCatalog.All.Take(4))
        {
            await using (var apply = new NpgsqlCommand(migration.Sql, connection, transaction))
            {
                await apply.ExecuteNonQueryAsync();
            }

            await using var record = new NpgsqlCommand(
                "INSERT INTO infrastructure.schema_migrations (version, name, sha256) VALUES ($1, $2, $3);",
                connection,
                transaction);
            record.Parameters.AddWithValue(migration.Version);
            record.Parameters.AddWithValue(migration.Name);
            record.Parameters.AddWithValue(migration.Sha256);
            await record.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        await new TaskPersistenceMigrator(dataSource).ApplyPendingAsync();
    }

    private static async global::System.Threading.Tasks.Task SeedOrganizationAndUserAsync(
        NpgsqlDataSource dataSource,
        Guid organizationId,
        Guid userId,
        string login)
    {
        await using (var organization = dataSource.CreateCommand(
            """
            INSERT INTO core.organizations (id, code, name, default_time_zone)
            VALUES ($1, $2, $3, 'Europe/Minsk');
            """))
        {
            organization.Parameters.AddWithValue(organizationId);
            organization.Parameters.AddWithValue($"org-{organizationId:N}");
            organization.Parameters.AddWithValue($"Organization {organizationId:N}");
            await organization.ExecuteNonQueryAsync();
        }

        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 8, 26, 9, 0, 0, TimeSpan.Zero);
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var objects = new NpgsqlCommand(
            """
            INSERT INTO core.objects (
                id, organization_id, object_type, lifecycle_state, version,
                created_at, created_by, updated_at, updated_by)
            VALUES ($1, $2, 'employee_profile', 'active', 1, $3, $4, $3, $4),
                   ($4, $2, 'user_account', 'active', 1, $3, $4, $3, $4);
            """,
            connection,
            transaction))
        {
            objects.Parameters.AddWithValue(employeeId);
            objects.Parameters.AddWithValue(organizationId);
            objects.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = now });
            objects.Parameters.AddWithValue(userId);
            await objects.ExecuteNonQueryAsync();
        }

        await using (var profile = new NpgsqlCommand(
            """
            INSERT INTO org.employee_profiles (
                id, organization_id, first_name, last_name, display_name, preferred_time_zone)
            VALUES ($1, $2, 'Test', 'Actor', 'Test Actor', 'Europe/Minsk');
            """,
            connection,
            transaction))
        {
            profile.Parameters.AddWithValue(employeeId);
            profile.Parameters.AddWithValue(organizationId);
            await profile.ExecuteNonQueryAsync();
        }

        await using (var user = new NpgsqlCommand(
            """
            INSERT INTO iam.user_accounts (
                id, organization_id, employee_profile_id, login, password_hash,
                password_parameters, account_status, must_change_password)
            VALUES ($1, $2, $3, $4, repeat('a', 64), '{}'::jsonb, 'active', false);
            """,
            connection,
            transaction))
        {
            user.Parameters.AddWithValue(userId);
            user.Parameters.AddWithValue(organizationId);
            user.Parameters.AddWithValue(employeeId);
            user.Parameters.AddWithValue(login);
            await user.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private static async global::System.Threading.Tasks.Task SeedConflictingDomainEventAsync(
        NpgsqlDataSource dataSource,
        Guid organizationId,
        Guid actorUserId,
        Guid taskId)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO governance.domain_events (
                id, organization_id, aggregate_id, aggregate_type, aggregate_version,
                event_type, actor_user_id, correlation_id, operation_id, idempotency_key, payload)
            VALUES ($1, $2, $3, 'task', 1, 'TaskCreated', $4, $5,
                    'seed.rollback.conflict', 'seed-key-0001', '{}'::jsonb);
            """);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(organizationId);
        command.Parameters.AddWithValue(taskId);
        command.Parameters.AddWithValue(actorUserId);
        command.Parameters.AddWithValue(Guid.NewGuid());
        await command.ExecuteNonQueryAsync();
    }

    private static async global::System.Threading.Tasks.Task AssertEffectsAsync(
        NpgsqlDataSource dataSource,
        Guid organizationId,
        Guid taskId,
        int expected)
    {
        Assert.Equal(expected, await CountAsync(dataSource, "SELECT count(*) FROM work.tasks WHERE organization_id = $1 AND id = $2;", organizationId, taskId));
        Assert.Equal(expected, await CountAsync(dataSource, "SELECT count(*) FROM governance.audit_entries WHERE organization_id = $1 AND object_id = $2 AND action_code = 'task.create';", organizationId, taskId));
        Assert.Equal(expected, await CountAsync(dataSource, "SELECT count(*) FROM governance.domain_events WHERE organization_id = $1 AND aggregate_id = $2 AND event_type = 'TaskCreated' AND aggregate_version = 1;", organizationId, taskId));
        Assert.Equal(expected, await CountAsync(
            dataSource,
            """
            SELECT count(*)
            FROM governance.outbox_messages AS outbox
            JOIN governance.domain_events AS event
              ON event.organization_id = outbox.organization_id AND event.id = outbox.domain_event_id
            WHERE event.organization_id = $1 AND event.aggregate_id = $2 AND outbox.destination = 'realtime';
            """,
            organizationId,
            taskId));
    }

    private static async global::System.Threading.Tasks.Task<int> CountAsync(
        NpgsqlDataSource dataSource,
        string sql,
        params object[] parameters)
    {
        await using var command = dataSource.CreateCommand(sql);
        foreach (var value in parameters)
        {
            command.Parameters.AddWithValue(value);
        }

        return checked((int)(long)(await command.ExecuteScalarAsync() ?? 0L));
    }

    private static async global::System.Threading.Tasks.Task<T> ScalarAsync<T>(NpgsqlDataSource dataSource, string sql)
    {
        await using var command = dataSource.CreateCommand(sql);
        return (T)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Expected scalar."));
    }

    private static void Execute(NpgsqlDataSource dataSource, string sql)
    {
        using var command = dataSource.CreateCommand(sql);
        command.ExecuteNonQuery();
    }

    private sealed class RecordingExecutor : ITaskWriteCommandExecutor
    {
        public global::System.Threading.Tasks.Task<TaskWriteCommandExecutionResult> ExecuteAsync(
            TaskWriteCommand command,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Recording executor does not execute.");
    }
}
