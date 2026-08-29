using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Task.Application;
using Task.Domain;
using Task.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace Task.Tests;

public sealed class PostgresTaskWriteCommandExecutorTests
{
    private const string ConnectionEnvironmentVariable = "TASK_POSTGRES_TEST_ADMIN_CONNECTION";
    private readonly ITestOutputHelper _output;

    public PostgresTaskWriteCommandExecutorTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RealPostgres16_TaskWriteIsAtomicDurableAndTenantScoped()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            _output.WriteLine(
                $"NOT RUN: set {ConnectionEnvironmentVariable} to execute the real PostgreSQL integration gate.");
            return;
        }

        var databaseName = $"task_write_{Guid.NewGuid():N}";
        using var adminDataSource = NpgsqlDataSource.Create(adminConnectionString);
        CreateDatabase(adminDataSource, databaseName);

        try
        {
            var databaseConnection = new NpgsqlConnectionStringBuilder(adminConnectionString)
            {
                Database = databaseName,
            }.ConnectionString;
            await using var dataSource = NpgsqlDataSource.Create(databaseConnection);

            await ApplySchemaThroughVersionFourAsync(dataSource);
            var migrator = new TaskPersistenceMigrator(dataSource);
            await migrator.ApplyPendingAsync();
            await migrator.ApplyPendingAsync();
            var inspection = await migrator.InspectAsync();
            Assert.Equal(TaskPersistenceMigrationStatus.Current, inspection.Status);
            Assert.Equal(6, inspection.ActualMigrationVersion);
            Assert.Equal(6, await ScalarAsync<int>(dataSource, "SELECT max(version) FROM infrastructure.schema_migrations;"));

            var organizationId = Guid.NewGuid();
            var otherOrganizationId = Guid.NewGuid();
            var actorUserId = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();
            var crossTenantUserId = Guid.NewGuid();
            await SeedOrganizationAndUserAsync(dataSource, organizationId, actorUserId, "actor");
            await SeedUserAsync(dataSource, organizationId, otherUserId, "other-user");
            await SeedOrganizationAndUserAsync(dataSource, otherOrganizationId, crossTenantUserId, "cross-tenant");

            var executor = new PostgresTaskWriteCommandExecutor(dataSource);
            var taskId = Guid.NewGuid();
            var initial = CreateCommand(
                organizationId,
                actorUserId,
                "POST_api_v1_tasks",
                "durable-key-0001",
                taskId,
                "Atomic task");

            var executed = await executor.ExecuteAsync(initial);
            Assert.Equal(TaskWriteCommandDisposition.Executed, executed.Disposition);
            Assert.NotNull(executed.HttpResult);
            Assert.False(executed.IsReplay);
            await AssertCommandEffectsAsync(dataSource, organizationId, taskId, expectedCount: 1);

            var startTransition = CreateTransitionCommand(
                organizationId,
                actorUserId,
                taskId,
                "transition-start-0001",
                TaskWorkStatus.InProgress);
            var completeTransition = CreateTransitionCommand(
                organizationId,
                actorUserId,
                taskId,
                "transition-complete-0001",
                TaskWorkStatus.Completed);
            var startTask = executor.ExecuteAsync(startTransition);
            var completeTask = executor.ExecuteAsync(completeTransition);
            TaskWriteCommandExecutionResult? startResult = null;
            TaskWriteCommandExecutionResult? completeResult = null;
            Exception? startError = null;
            Exception? completeError = null;
            try { startResult = await startTask; } catch (Exception exception) { startError = exception; }
            try { completeResult = await completeTask; } catch (Exception exception) { completeError = exception; }

            Assert.Equal(1, new[] { startResult, completeResult }.Count(result => result is not null));
            Assert.Equal(1, new[] { startError, completeError }.Count(error => error is TaskLifecycleConcurrencyException));
            var winningCommand = startResult is not null ? startTransition : completeTransition;
            Assert.Equal(
                TaskWriteCommandDisposition.Replayed,
                (await executor.ExecuteAsync(winningCommand)).Disposition);
            await AssertCommandEffectsAsync(dataSource, organizationId, taskId, expectedCount: 2, taskCount: 1);
            Assert.Equal(2L, await ScalarAsync<long>(
                dataSource,
                "SELECT version FROM core.objects WHERE organization_id = $1 AND id = $2;",
                organizationId,
                taskId));
            Assert.Equal(1, await CountAsync(
                dataSource,
                """
                SELECT count(*) FROM governance.domain_events
                WHERE organization_id = $1 AND aggregate_id = $2
                  AND event_type = 'TaskStatusChanged'
                  AND payload->>'fromStatus' = 'new'
                  AND (payload->>'aggregateVersion')::integer = 2
                  AND payload->>'actorId' = $3;
                """,
                organizationId,
                taskId,
                actorUserId.ToString("D")));

            var sameTransitionTaskId = Guid.NewGuid();
            Assert.Equal(
                TaskWriteCommandDisposition.Executed,
                (await executor.ExecuteAsync(CreateCommand(
                    organizationId,
                    actorUserId,
                    "POST_api_v1_tasks_same_transition_seed",
                    "same-transition-seed-01",
                    sameTransitionTaskId,
                    "Concurrent same transition"))).Disposition);
            var sameMutationCalls = 0;
            var sameTransition = CreateTransitionCommand(
                organizationId,
                actorUserId,
                sameTransitionTaskId,
                "same-transition-0001",
                TaskWorkStatus.InProgress,
                () => Interlocked.Increment(ref sameMutationCalls));
            var sameResults = await global::System.Threading.Tasks.Task.WhenAll(
                executor.ExecuteAsync(sameTransition),
                executor.ExecuteAsync(sameTransition));
            Assert.Contains(sameResults, result => result.Disposition == TaskWriteCommandDisposition.Executed);
            Assert.Contains(sameResults, result => result.Disposition == TaskWriteCommandDisposition.Replayed);
            Assert.Equal(1, sameMutationCalls);
            await AssertCommandEffectsAsync(
                dataSource,
                organizationId,
                sameTransitionTaskId,
                expectedCount: 2,
                taskCount: 1);

            var rollbackTransitionTaskId = Guid.NewGuid();
            Assert.Equal(
                TaskWriteCommandDisposition.Executed,
                (await executor.ExecuteAsync(CreateCommand(
                    organizationId,
                    actorUserId,
                    "POST_api_v1_tasks_rollback_transition_seed",
                    "rollback-transition-seed-01",
                    rollbackTransitionTaskId,
                    "Rollback transition"))).Disposition);
            await SeedConflictingDomainEventAsync(
                dataSource,
                organizationId,
                actorUserId,
                rollbackTransitionTaskId,
                aggregateVersion: 2,
                eventType: TaskStatusTransitionCommandService.EventType);
            var rollbackTransition = CreateTransitionCommand(
                organizationId,
                actorUserId,
                rollbackTransitionTaskId,
                "rollback-transition-0001",
                TaskWorkStatus.InProgress);
            Assert.Equal(
                PostgresErrorCodes.UniqueViolation,
                (await Assert.ThrowsAsync<PostgresException>(() => executor.ExecuteAsync(rollbackTransition))).SqlState);
            Assert.Equal(1L, await ScalarAsync<long>(
                dataSource,
                "SELECT version FROM core.objects WHERE organization_id = $1 AND id = $2;",
                organizationId,
                rollbackTransitionTaskId));
            Assert.Equal(0, await CountAsync(
                dataSource,
                "SELECT count(*) FROM iam.idempotency_records WHERE organization_id = $1 AND operation_id = $2 AND idempotency_key = $3;",
                organizationId,
                rollbackTransition.OperationId,
                rollbackTransition.IdempotencyKey));

            var replayed = await executor.ExecuteAsync(initial);
            Assert.Equal(TaskWriteCommandDisposition.Replayed, replayed.Disposition);
            Assert.True(replayed.IsReplay);
            Assert.NotNull(replayed.HttpResult);
            Assert.Equal(executed.HttpResult.StatusCode, replayed.HttpResult.StatusCode);
            Assert.Equal(executed.HttpResult.BodyJson, replayed.HttpResult.BodyJson);
            Assert.Equal(executed.HttpResult.ResourceId, replayed.HttpResult.ResourceId);
            Assert.Equal(
                executed.HttpResult.Headers.OrderBy(header => header.Key, StringComparer.Ordinal),
                replayed.HttpResult.Headers.OrderBy(header => header.Key, StringComparer.Ordinal));
            await AssertCommandEffectsAsync(dataSource, organizationId, taskId, expectedCount: 2, taskCount: 1);

            var reused = CreateCommand(
                organizationId,
                actorUserId,
                initial.OperationId,
                initial.IdempotencyKey,
                Guid.NewGuid(),
                "Different payload");
            var reuseResult = await executor.ExecuteAsync(reused);
            Assert.Equal(TaskWriteCommandDisposition.IdempotencyKeyReused, reuseResult.Disposition);
            Assert.Null(reuseResult.HttpResult);

            const string sharedKey = "scope-key-0001";
            var scopeCommands = new[]
            {
                CreateCommand(otherOrganizationId, crossTenantUserId, initial.OperationId, sharedKey, Guid.NewGuid(), "Other tenant"),
                CreateCommand(organizationId, otherUserId, initial.OperationId, sharedKey, Guid.NewGuid(), "Other user"),
                CreateCommand(organizationId, actorUserId, "POST_api_v1_tasks_alternate", sharedKey, Guid.NewGuid(), "Other operation"),
            };
            foreach (var scopedCommand in scopeCommands)
            {
                Assert.Equal(
                    TaskWriteCommandDisposition.Executed,
                    (await executor.ExecuteAsync(scopedCommand)).Disposition);
            }

            var leaseHash = TaskWriteRequestHasher.ComputeSha256("""{"title":"Leased"}""");
            await AcquireAndCommitLeaseAsync(
                dataSource,
                organizationId,
                actorUserId,
                "POST_api_v1_tasks_lease",
                "lease-key-0001",
                leaseHash);
            var leaseMutationCalls = 0;
            var leased = CreateCommand(
                organizationId,
                actorUserId,
                "POST_api_v1_tasks_lease",
                "lease-key-0001",
                Guid.NewGuid(),
                "Leased",
                _ =>
                {
                    Interlocked.Increment(ref leaseMutationCalls);
                    throw new InvalidOperationException("Active lease must prevent mutation.");
                });
            var inProgress = await executor.ExecuteAsync(leased);
            Assert.Equal(TaskWriteCommandDisposition.RequestInProgress, inProgress.Disposition);
            Assert.NotNull(inProgress.RetryAfter);
            Assert.Equal(0, leaseMutationCalls);

            var cancelledTaskId = Guid.NewGuid();
            using (var cancellation = new CancellationTokenSource())
            {
                var cancelled = CreateCommand(
                    organizationId,
                    actorUserId,
                    "POST_api_v1_tasks_cancelled",
                    "cancelled-key-0001",
                    cancelledTaskId,
                    "Cancelled",
                    current =>
                    {
                        cancellation.Cancel();
                        return CreateMutationResult(
                            organizationId,
                            actorUserId,
                            cancelledTaskId,
                            "Cancelled");
                    });
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    executor.ExecuteAsync(cancelled, cancellation.Token));
            }

            Assert.Equal(0, await CountAsync(
                dataSource,
                "SELECT count(*) FROM iam.idempotency_records WHERE organization_id = $1 AND operation_id = $2;",
                organizationId,
                "POST_api_v1_tasks_cancelled"));

            var concurrentTaskId = Guid.NewGuid();
            var concurrentMutationCalls = 0;
            var concurrent = CreateCommand(
                organizationId,
                actorUserId,
                "POST_api_v1_tasks_concurrent",
                "concurrent-key-0001",
                concurrentTaskId,
                "Concurrent",
                current =>
                {
                    Assert.Null(current);
                    Interlocked.Increment(ref concurrentMutationCalls);
                    return CreateMutationResult(
                        organizationId,
                        actorUserId,
                        concurrentTaskId,
                        "Concurrent");
                });
            var concurrentResults = await global::System.Threading.Tasks.Task.WhenAll(
                executor.ExecuteAsync(concurrent),
                executor.ExecuteAsync(concurrent));
            Assert.Contains(concurrentResults, result => result.Disposition == TaskWriteCommandDisposition.Executed);
            Assert.Contains(concurrentResults, result => result.Disposition == TaskWriteCommandDisposition.Replayed);
            Assert.Equal(1, concurrentMutationCalls);
            await AssertCommandEffectsAsync(dataSource, organizationId, concurrentTaskId, expectedCount: 1);

            var rollbackTaskId = Guid.NewGuid();
            await SeedConflictingDomainEventAsync(dataSource, organizationId, actorUserId, rollbackTaskId);
            var rollbackCommand = CreateCommand(
                organizationId,
                actorUserId,
                "POST_api_v1_tasks_rollback",
                "rollback-key-0001",
                rollbackTaskId,
                "Must roll back");
            var rollbackError = await Assert.ThrowsAsync<PostgresException>(() =>
                executor.ExecuteAsync(rollbackCommand));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, rollbackError.SqlState);
            Assert.Equal(0, await CountAsync(
                dataSource,
                "SELECT count(*) FROM work.tasks WHERE organization_id = $1 AND id = $2;",
                organizationId,
                rollbackTaskId));
            Assert.Equal(0, await CountAsync(
                dataSource,
                "SELECT count(*) FROM iam.idempotency_records WHERE organization_id = $1 AND operation_id = $2;",
                organizationId,
                rollbackCommand.OperationId));
            Assert.Equal(0, await CountAsync(
                dataSource,
                "SELECT count(*) FROM governance.audit_entries WHERE organization_id = $1 AND object_id = $2;",
                organizationId,
                rollbackTaskId));
            Assert.Equal(0, await CountAsync(
                dataSource,
                "SELECT count(*) FROM governance.domain_events WHERE organization_id = $1 AND operation_id = $2;",
                organizationId,
                rollbackCommand.OperationId));
            Assert.Equal(0, await CountAsync(
                dataSource,
                """
                SELECT count(*)
                FROM governance.outbox_messages AS outbox
                JOIN governance.domain_events AS event ON event.id = outbox.domain_event_id
                WHERE event.organization_id = $1 AND event.operation_id = $2;
                """,
                organizationId,
                rollbackCommand.OperationId));

            Assert.True(await ScalarAsync<bool>(
                dataSource,
                """
                SELECT NOT EXISTS (
                    SELECT 1 FROM governance.audit_entries
                    WHERE metadata::text ~* '(password|token|secret)'
                       OR coalesce(new_state::text, '') ~* '(password|token|secret)'
                    UNION ALL
                    SELECT 1 FROM governance.domain_events
                    WHERE payload::text ~* '(password|token|secret)'
                    UNION ALL
                    SELECT 1 FROM governance.outbox_messages
                    WHERE payload::text ~* '(password|token|secret)'
                );
                """));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            DropDatabase(adminDataSource, databaseName);
        }
    }

    private static TaskWriteCommand CreateCommand(
        Guid organizationId,
        Guid actorUserId,
        string operationId,
        string idempotencyKey,
        Guid taskId,
        string title,
        TaskWriteMutation? mutation = null)
    {
        var payload = JsonSerializer.Serialize(new { title });
        return new(
            organizationId,
            actorUserId,
            actorSessionId: null,
            operationId,
            Guid.NewGuid(),
            idempotencyKey,
            TaskWriteRequestHasher.ComputeSha256(payload),
            taskId,
            expectedVersion: null,
            "task.create",
            "TaskCreated",
            ["title"],
            payload,
            mutation ?? (current =>
            {
                Assert.Null(current);
                return CreateMutationResult(organizationId, actorUserId, taskId, title);
            }));
    }

    private static TaskWriteMutationResult CreateMutationResult(
        Guid organizationId,
        Guid actorUserId,
        Guid taskId,
        string title)
    {
        var aggregate = TaskAggregate.Create(
            taskId,
            organizationId,
            actorUserId,
            title,
            new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero));
        var body = JsonSerializer.Serialize(new { id = taskId, title, version = 1 });
        return new(
            aggregate,
            new TaskWriteHttpResult(
                201,
                new Dictionary<string, string>
                {
                    ["ETag"] = "\"v1\"",
                    ["Location"] = $"/api/v1/tasks/{taskId:D}",
                },
                body,
                taskId));
    }

    private static TaskWriteCommand CreateTransitionCommand(
        Guid organizationId,
        Guid actorUserId,
        Guid taskId,
        string idempotencyKey,
        TaskWorkStatus targetStatus,
        Action? onMutation = null)
    {
        var correlationId = Guid.NewGuid();
        var target = targetStatus == TaskWorkStatus.InProgress ? "in_progress" : "completed";
        return new TaskWriteCommand(
            organizationId,
            actorUserId,
            actorSessionId: null,
            TaskStatusTransitionCommandService.OperationId,
            correlationId,
            idempotencyKey,
            TaskWriteRequestHasher.ComputeSha256(JsonSerializer.Serialize(new { taskId, target })),
            taskId,
            expectedVersion: 1,
            TaskStatusTransitionCommandService.AuditAction,
            TaskStatusTransitionCommandService.EventType,
            targetStatus == TaskWorkStatus.Completed ? ["status", "completedAt", "completedBy"] : ["status"],
            JsonSerializer.Serialize(new { taskId, targetStatus = target }),
            current =>
            {
                onMutation?.Invoke();
                var existing = Assert.IsType<TaskAggregate>(current);
                var updated = targetStatus == TaskWorkStatus.InProgress
                    ? existing.Start(actorUserId, DateTimeOffset.Parse("2026-08-26T11:00:00Z"))
                    : existing.Complete(actorUserId, DateTimeOffset.Parse("2026-08-26T11:00:00Z"));
                var payload = JsonSerializer.Serialize(new
                {
                    taskId,
                    fromStatus = "new",
                    targetStatus = target,
                    aggregateVersion = updated.Metadata.Version,
                    correlationId,
                    actorId = actorUserId,
                });
                return new TaskWriteMutationResult(
                    updated,
                    new TaskWriteHttpResult(
                        200,
                        new Dictionary<string, string> { ["ETag"] = "\"v2\"" },
                        JsonSerializer.Serialize(new { id = taskId, status = target, version = 2 }),
                        taskId),
                    SafePayloadJson: payload);
            });
    }

    private static async global::System.Threading.Tasks.Task ApplySchemaThroughVersionFourAsync(
        NpgsqlDataSource dataSource)
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

        await SeedUserAsync(dataSource, organizationId, userId, login);
    }

    private static async global::System.Threading.Tasks.Task SeedUserAsync(
        NpgsqlDataSource dataSource,
        Guid organizationId,
        Guid userId,
        string login)
    {
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

    private static async global::System.Threading.Tasks.Task AcquireAndCommitLeaseAsync(
        NpgsqlDataSource dataSource,
        Guid organizationId,
        Guid userId,
        string operationId,
        string idempotencyKey,
        byte[] requestHash)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT disposition
            FROM iam.acquire_idempotency_record(
                $1, $2, $3, $4, $5, $6, $7, interval '5 minutes', interval '7 days');
            """);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(organizationId);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(operationId);
        command.Parameters.AddWithValue(idempotencyKey);
        command.Parameters.Add(new NpgsqlParameter<byte[]> { NpgsqlDbType = NpgsqlDbType.Bytea, TypedValue = requestHash });
        command.Parameters.AddWithValue(Guid.NewGuid());
        Assert.Equal("execute", (string?)await command.ExecuteScalarAsync());
    }

    private static async global::System.Threading.Tasks.Task SeedConflictingDomainEventAsync(
        NpgsqlDataSource dataSource,
        Guid organizationId,
        Guid actorUserId,
        Guid taskId,
        int aggregateVersion = 1,
        string eventType = "TaskCreated")
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO governance.domain_events (
                id, organization_id, aggregate_id, aggregate_type, aggregate_version,
                event_type, actor_user_id, correlation_id, operation_id, idempotency_key, payload)
            VALUES ($1, $2, $3, 'task', $4, $5, $6, $7, $8, $9, '{}'::jsonb);
            """);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(organizationId);
        command.Parameters.AddWithValue(taskId);
        command.Parameters.AddWithValue(aggregateVersion);
        command.Parameters.AddWithValue(eventType);
        command.Parameters.AddWithValue(actorUserId);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue($"seed.rollback.conflict.{taskId:N}");
        command.Parameters.AddWithValue($"seed-{taskId:N}");
        await command.ExecuteNonQueryAsync();
    }

    private static async global::System.Threading.Tasks.Task AssertCommandEffectsAsync(
        NpgsqlDataSource dataSource,
        Guid organizationId,
        Guid taskId,
        int expectedCount,
        int? taskCount = null)
    {
        Assert.Equal(taskCount ?? expectedCount, await CountAsync(
            dataSource,
            "SELECT count(*) FROM work.tasks WHERE organization_id = $1 AND id = $2;",
            organizationId,
            taskId));
        Assert.Equal(expectedCount, await CountAsync(
            dataSource,
            "SELECT count(*) FROM governance.audit_entries WHERE organization_id = $1 AND object_id = $2;",
            organizationId,
            taskId));
        Assert.Equal(expectedCount, await CountAsync(
            dataSource,
            "SELECT count(*) FROM governance.domain_events WHERE organization_id = $1 AND aggregate_id = $2;",
            organizationId,
            taskId));
        Assert.Equal(expectedCount, await CountAsync(
            dataSource,
            """
            SELECT count(*)
            FROM governance.outbox_messages AS outbox
            JOIN governance.domain_events AS event
              ON event.organization_id = outbox.organization_id AND event.id = outbox.domain_event_id
            WHERE event.organization_id = $1 AND event.aggregate_id = $2;
            """,
            organizationId,
            taskId));
        Assert.Equal(expectedCount, await CountAsync(
            dataSource,
            "SELECT count(*) FROM iam.idempotency_records WHERE organization_id = $1 AND resource_id = $2 AND state = 'completed';",
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

    private static async global::System.Threading.Tasks.Task<T> ScalarAsync<T>(
        NpgsqlDataSource dataSource,
        string sql,
        params object[] parameters)
    {
        await using var command = dataSource.CreateCommand(sql);
        foreach (var value in parameters)
        {
            command.Parameters.AddWithValue(value);
        }

        return (T)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Expected scalar value was null."));
    }

    private static void CreateDatabase(NpgsqlDataSource adminDataSource, string databaseName)
    {
        using var command = adminDataSource.CreateCommand($"CREATE DATABASE \"{databaseName}\";");
        command.ExecuteNonQuery();
    }

    private static void DropDatabase(NpgsqlDataSource adminDataSource, string databaseName)
    {
        using var command = adminDataSource.CreateCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);");
        command.ExecuteNonQuery();
    }
}
