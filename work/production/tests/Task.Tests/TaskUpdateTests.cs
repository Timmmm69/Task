using System.Text.Json;
using Npgsql;
using Task.Application;
using Task.Application.Security;
using Task.Domain;
using Task.Infrastructure.Persistence;

namespace Task.Tests;

public sealed class TaskUpdateTests
{
    private static readonly Guid TaskId = Guid.Parse("b64fbeec-f0f4-4f5f-9967-ea2ce57be461");
    private static readonly Guid OrganizationId = Guid.Parse("751fa8ce-5cc3-4d98-8574-1108080b2ff4");
    private static readonly Guid CreatorId = Guid.Parse("3077f0f8-536f-4988-bd73-6f26265d0b92");
    private static readonly Guid EditorId = Guid.Parse("957c3a11-d6f2-4a2a-bb1b-0945a0f6a820");
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 15, 8, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EditedAt = new(2026, 8, 16, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void UpdateEditableFields_ChangesTitleOnlyAndIncrementsVersionOnce()
    {
        var updated = NewTask().UpdateEditableFields(
            EditorId, EditedAt, "  Updated title  ", null, OptionalInstant.Unspecified, OptionalInstant.Unspecified);

        Assert.Equal("Updated title", updated.Title);
        Assert.Equal(TaskPriority.Normal, updated.Priority);
        Assert.Equal(NewTask().Schedule, updated.Schedule);
        Assert.Equal(2, updated.Metadata.Version);
        Assert.Equal(EditorId, updated.Metadata.UpdatedBy);
    }

    [Fact]
    public void UpdateEditableFields_ChangesMultipleFieldsAndIncrementsVersionOnce()
    {
        var schedule = TaskSchedule.Create(CreatedAt.AddDays(1), CreatedAt.AddDays(2));
        var updated = NewTask().UpdateEditableFields(
            EditorId,
            EditedAt,
            "Multi field",
            TaskPriority.Critical,
            OptionalInstant.Set(schedule.StartsAtUtc!.Value),
            OptionalInstant.Set(schedule.DeadlineUtc!.Value));

        Assert.Equal("Multi field", updated.Title);
        Assert.Equal(TaskPriority.Critical, updated.Priority);
        Assert.Equal(schedule, updated.Schedule);
        Assert.Equal(2, updated.Metadata.Version);
    }

    [Fact]
    public void UpdateEditableFields_ClearsScheduleBoundsWhenExplicitNull()
    {
        var task = NewTask().Reschedule(
            EditorId,
            TaskSchedule.Create(CreatedAt.AddDays(1), CreatedAt.AddDays(2)),
            CreatedAt.AddMinutes(1));

        var updated = task.UpdateEditableFields(
            EditorId, EditedAt, null, null, OptionalInstant.Clear(), OptionalInstant.Clear());

        Assert.Null(updated.Schedule.StartsAtUtc);
        Assert.Null(updated.Schedule.DeadlineUtc);
        Assert.Equal(3, updated.Metadata.Version);
    }

    [Fact]
    public void UpdateEditableFields_IsNoOpWhenFinalValuesMatchCurrent()
    {
        var task = NewTask();

        var result = task.UpdateEditableFields(
            EditorId, EditedAt, "  Inbox task  ", TaskPriority.Normal, OptionalInstant.Unspecified, OptionalInstant.Unspecified);

        Assert.Same(task, result);
        Assert.Equal(1, result.Metadata.Version);
    }

    [Fact]
    public void UpdateEditableFields_RejectsInvalidFinalSchedule()
    {
        var task = NewTask().Reschedule(
            EditorId, TaskSchedule.Create(CreatedAt.AddDays(2), CreatedAt.AddDays(3)), CreatedAt.AddMinutes(1));

        Assert.Throws<ArgumentException>(() =>
            task.UpdateEditableFields(
                EditorId, EditedAt, null, null, OptionalInstant.Unspecified, OptionalInstant.Set(CreatedAt.AddDays(1))));
        Assert.Throws<ArgumentException>(() =>
            task.UpdateEditableFields(
                EditorId, EditedAt, null, null, OptionalInstant.Set(CreatedAt.AddDays(4)), OptionalInstant.Unspecified));
    }

    [Fact]
    public void UpdateEditableFields_RejectsUndefinedPriorityAndInvalidTitle()
    {
        var task = NewTask();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            task.UpdateEditableFields(
                EditorId, EditedAt, null, (TaskPriority)99, OptionalInstant.Unspecified, OptionalInstant.Unspecified));
        Assert.Throws<ArgumentException>(() =>
            task.UpdateEditableFields(EditorId, EditedAt, "   ", null, OptionalInstant.Unspecified, OptionalInstant.Unspecified));
        Assert.Throws<ArgumentException>(() =>
            task.UpdateEditableFields(EditorId, EditedAt, new string('x', 501), null, OptionalInstant.Unspecified, OptionalInstant.Unspecified));
    }

    [Fact]
    public void UpdateEditableFields_RejectedForArchivedTrashedAndTerminalTasks()
    {
        var completed = NewTask().Complete(CreatorId, CreatedAt.AddMinutes(1));
        var cancelled = NewTask().Cancel(CreatorId, CreatedAt.AddMinutes(1));
        var archived = completed.Archive(CreatorId, CreatedAt.AddMinutes(2));
        var trashed = cancelled.MoveToTrash(CreatorId, CreatedAt.AddMinutes(2));

        Assert.Throws<InvalidOperationException>(() =>
            completed.UpdateEditableFields(EditorId, EditedAt, "New", null, OptionalInstant.Unspecified, OptionalInstant.Unspecified));
        Assert.Throws<InvalidOperationException>(() =>
            cancelled.UpdateEditableFields(EditorId, EditedAt, "New", null, OptionalInstant.Unspecified, OptionalInstant.Unspecified));
        Assert.Throws<InvalidOperationException>(() =>
            archived.UpdateEditableFields(EditorId, EditedAt, "New", null, OptionalInstant.Unspecified, OptionalInstant.Unspecified));
        Assert.Throws<InvalidOperationException>(() =>
            trashed.UpdateEditableFields(EditorId, EditedAt, "New", null, OptionalInstant.Unspecified, OptionalInstant.Unspecified));
    }

    [Fact]
    public void UpdateEditableFields_RejectsNonUtcOrOutOfOrderTimestamp()
    {
        var task = NewTask();

        Assert.Throws<ArgumentException>(() =>
            task.UpdateEditableFields(
                EditorId,
                new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.FromHours(3)),
                "New",
                null,
                OptionalInstant.Unspecified,
                OptionalInstant.Unspecified));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            task.UpdateEditableFields(EditorId, CreatedAt.AddMinutes(-1), "New", null, OptionalInstant.Unspecified, OptionalInstant.Unspecified));
    }

    [Fact]
    public void CreateCommand_ComputesOnlyActuallyChangedFieldsAndOmitsTitleFromPayload()
    {
        var current = TaskAggregate.Create(
            TaskId,
            OrganizationId,
            CreatorId,
            "Current title",
            CreatedAt,
            TaskPriority.High,
            TaskSchedule.Create(CreatedAt.AddDays(1), CreatedAt.AddDays(2)));
        var service = new TaskUpdateCommandService(new RecordingExecutor());
        var model = new TaskUpdateModel(
            "  New title  ",
            TaskPriority.High,
            OptionalInstant.Set(CreatedAt.AddDays(1)),
            OptionalInstant.Clear());

        var preparation = service.CreateCommand(
            Context(OrganizationId, EditorId),
            "update-key-01",
            """{"title":"  New title  ","priority":"high","startAtUtc":"2026-08-16T08:30:00Z","deadlineAt":null}""",
            TaskId,
            1,
            model,
            CreateHttpResult,
            EditedAt);

        Assert.Equal(["title", "priority", "startAtUtc", "deadlineAt"], preparation.Command.ChangedFields);
        Assert.Equal(TaskUpdateCommandService.OperationId, preparation.Command.OperationId);
        Assert.Equal(TaskUpdateCommandService.AuditAction, preparation.Command.AuditAction);
        Assert.Equal(TaskUpdateCommandService.EventType, preparation.Command.EventType);
        Assert.Equal(1, preparation.Command.ExpectedVersion);
        Assert.DoesNotContain("New title", preparation.Command.SafePayloadJson, StringComparison.Ordinal);
        Assert.Contains("\"deadlineAt\":null", preparation.Command.SafePayloadJson, StringComparison.Ordinal);
        var mutated = preparation.Command.Mutation(current);
        Assert.Equal(["title", "deadlineAt"], mutated.ChangedFields);
        Assert.Equal(2, mutated.Aggregate.Metadata.Version);
        Assert.Equal("New title", mutated.Aggregate.Title);
        Assert.Null(mutated.Aggregate.Schedule.DeadlineUtc);
    }

    [Fact]
    public void CreateCommand_WhenPatchIsNoOp_ReturnsEmptyChangedFields()
    {
        var current = TaskAggregate.Create(
            TaskId,
            OrganizationId,
            CreatorId,
            "Same",
            CreatedAt,
            TaskPriority.Normal,
            TaskSchedule.Create(null, null));
        var service = new TaskUpdateCommandService(new RecordingExecutor());

        var preparation = service.CreateCommand(
            Context(OrganizationId, EditorId),
            "update-key-02",
            """{"title":"Same"}""",
            TaskId,
            1,
            new TaskUpdateModel("Same", null, OptionalInstant.Unspecified, OptionalInstant.Unspecified),
            CreateHttpResult,
            EditedAt);

        Assert.Equal(["title"], preparation.Command.ChangedFields);
        var mutation = preparation.Command.Mutation(current);
        Assert.Empty(mutation.ChangedFields!);
        Assert.Same(current, mutation.Aggregate);
    }

    [Fact]
    public void CreateCommand_RejectsInvalidFinalSchedule()
    {
        var current = TaskAggregate.Create(
            TaskId,
            OrganizationId,
            CreatorId,
            "Scheduled",
            CreatedAt,
            TaskPriority.Normal,
            TaskSchedule.Create(CreatedAt.AddDays(2), CreatedAt.AddDays(3)));
        var service = new TaskUpdateCommandService(new RecordingExecutor());

        var preparation = service.CreateCommand(
            Context(OrganizationId, EditorId),
            "update-key-03",
            """{"deadlineAt":"2026-08-16T08:30:00Z"}""",
            TaskId,
            1,
            new TaskUpdateModel(null, null, OptionalInstant.Unspecified, OptionalInstant.Set(CreatedAt.AddDays(1))),
            CreateHttpResult,
            EditedAt);
        Assert.Throws<ArgumentException>(() => preparation.Command.Mutation(current));
    }

    [Fact]
    public void CreateCommand_MissingTask_ThrowsNotFound()
    {
        var service = new TaskUpdateCommandService(new RecordingExecutor());

        var preparation = service.CreateCommand(
            Context(OrganizationId, EditorId),
            "update-key-04",
            """{"title":"New"}""",
            TaskId,
            1,
            new TaskUpdateModel("New", null, OptionalInstant.Unspecified, OptionalInstant.Unspecified),
            CreateHttpResult,
            EditedAt);
        Assert.Throws<KeyNotFoundException>(() => preparation.Command.Mutation(null));
    }

    [Fact]
    public void CreateCommand_ForeignOrganizationTask_ThrowsNotFound()
    {
        var current = TaskAggregate.Create(
            TaskId, OrganizationId, CreatorId, "Owned", CreatedAt, TaskPriority.Normal);
        var service = new TaskUpdateCommandService(new RecordingExecutor());

        var preparation = service.CreateCommand(
            Context(Guid.Parse("77777777-7777-7777-7777-777777777777"), EditorId),
            "update-key-05",
            """{"title":"New"}""",
            TaskId,
            1,
            new TaskUpdateModel("New", null, OptionalInstant.Unspecified, OptionalInstant.Unspecified),
            CreateHttpResult,
            EditedAt);
        Assert.Throws<KeyNotFoundException>(() => preparation.Command.Mutation(null));
    }

    [Fact]
    public void CreateCommand_DefersVersionCheckToIdempotencyFirstExecutor()
    {
        var current = TaskAggregate.Create(TaskId, OrganizationId, CreatorId, "Owned", CreatedAt, TaskPriority.Normal);
        var service = new TaskUpdateCommandService(new RecordingExecutor());

        var preparation = service.CreateCommand(
            Context(OrganizationId, EditorId),
            "update-key-06",
            """{"title":"New"}""",
            TaskId,
            2,
            new TaskUpdateModel("New", null, OptionalInstant.Unspecified, OptionalInstant.Unspecified),
            CreateHttpResult,
            EditedAt);

        Assert.Equal(2, preparation.Command.ExpectedVersion);
    }

    [Fact]
    public void CreateCommand_ClassifiesArchivedTrashedAndTerminalStates()
    {
        var baseTask = TaskAggregate.Create(TaskId, OrganizationId, CreatorId, "Owned", CreatedAt, TaskPriority.Normal);
        var completed = baseTask.Complete(CreatorId, CreatedAt.AddMinutes(1));
        var cancelled = TaskAggregate.Create(TaskId, OrganizationId, CreatorId, "Owned", CreatedAt, TaskPriority.Normal)
            .Cancel(CreatorId, CreatedAt.AddMinutes(1));
        var archived = completed.Archive(CreatorId, CreatedAt.AddMinutes(2));
        var trashed = cancelled.MoveToTrash(CreatorId, CreatedAt.AddMinutes(2));

        AssertConflict("INVALID_STATE_TRANSITION", completed, 2);
        AssertConflict("INVALID_STATE_TRANSITION", cancelled, 2);
        AssertConflict("OBJECT_ARCHIVED", archived, 3);
        AssertConflict("OBJECT_DELETED", trashed, 3);
    }

    private static void AssertConflict(string problemCode, TaskAggregate current, int expectedVersion)
    {
        var service = new TaskUpdateCommandService(new RecordingExecutor());
        var preparation = service.CreateCommand(
            Context(OrganizationId, EditorId),
            "update-key-07",
            """{"title":"New"}""",
            TaskId,
            expectedVersion,
            new TaskUpdateModel("New", null, OptionalInstant.Unspecified, OptionalInstant.Unspecified),
            CreateHttpResult,
            EditedAt);
        var conflict = Assert.Throws<TaskUpdateConflictException>(() => preparation.Command.Mutation(current));
        Assert.Equal(problemCode, conflict.ProblemCode);
    }

    [RequiresPostgresFact]
    public async global::System.Threading.Tasks.Task RealPostgres_PatchIsAtomicDurableAndConcurrencySafe()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(TaskCreateCommandTests.ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            throw new InvalidOperationException("The PostgreSQL test should have been skipped during discovery.");
        }

        var databaseName = $"task_patch_{Guid.NewGuid():N}";
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
            var otherSessionId = Guid.NewGuid();
            await SeedOrganizationAndUserAsync(dataSource, organizationId, actorUserId, "actor");
            await SeedOrganizationAndUserAsync(dataSource, otherOrganizationId, otherUserId, "other");
            await SeedSessionAsync(dataSource, sessionId, organizationId, actorUserId);
            await SeedSessionAsync(dataSource, otherSessionId, otherOrganizationId, otherUserId);

            var executor = new PostgresTaskWriteCommandExecutor(dataSource);
            var createService = new TaskCreateCommandService(executor);
            var updateService = new TaskUpdateCommandService(executor);
            var taskId = Guid.NewGuid();
            var context = Context(organizationId, actorUserId, sessionId);
            var createdAt = new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero);

            var createCommand = createService.CreateCommand(
                context,
                "patch-pg-create",
                """{"title":"Patch gate","priority":"high"}""",
                new TaskCreateModel("Patch gate", TaskPriority.High, true, null, null),
                aggregate => new TaskWriteHttpResult(
                    201,
                    new Dictionary<string, string> { ["ETag"] = "\"v1\"" },
                    JsonSerializer.Serialize(new { id = aggregate.Metadata.Id, title = aggregate.Title }),
                    aggregate.Metadata.Id),
                taskId,
                createdAt);
            Assert.Equal(TaskWriteCommandDisposition.Executed, (await createService.ExecuteAsync(createCommand)).Disposition);

            var updateModel = new TaskUpdateModel(
                "Patched gate",
                TaskPriority.Critical,
                OptionalInstant.Set(createdAt.AddHours(1)),
                OptionalInstant.Set(createdAt.AddHours(3)));
            var updateCommand = updateService.CreateCommand(
                context,
                "patch-pg-key-01",
                """{"title":"Patched gate","priority":"critical","startAtUtc":"2026-08-26T11:00:00Z","deadlineAt":"2026-08-26T13:00:00Z"}""",
                taskId,
                1,
                updateModel,
                CreateHttpResult,
                createdAt.AddMinutes(1));

            Assert.Equal(["title", "priority", "startAtUtc", "deadlineAt"], updateCommand.Command.ChangedFields);
            var executed = await updateService.ExecuteAsync(updateCommand.Command);
            Assert.Equal(TaskWriteCommandDisposition.Executed, executed.Disposition);
            Assert.False(executed.IsReplay);
            Assert.Equal(200, executed.HttpResult!.StatusCode);
            Assert.Contains("\"v2\"", executed.HttpResult.Headers["ETag"], StringComparison.Ordinal);
            Assert.Equal(2L, await ScalarAsync<long>(dataSource, "SELECT o.version FROM core.objects AS o WHERE o.id = $1 AND o.object_type = 'task';", taskId));
            await AssertUpdateEffectsAsync(dataSource, organizationId, taskId, expected: 1);
            await AssertEventChangedFieldsAsync(dataSource, organizationId, taskId, 2, ["title", "priority", "startAtUtc", "deadlineAt"]);

            var replayCommand = updateService.CreateCommand(
                context,
                "patch-pg-key-01",
                """{"deadlineAt":"2026-08-26T13:00:00Z","startAtUtc":"2026-08-26T11:00:00Z","priority":"critical","title":"Patched gate"}""",
                taskId,
                1,
                updateModel,
                CreateHttpResult,
                createdAt.AddMinutes(2));
            var replayed = await updateService.ExecuteAsync(replayCommand.Command);
            Assert.Equal(TaskWriteCommandDisposition.Replayed, replayed.Disposition);
            Assert.True(replayed.IsReplay);
            Assert.Equal(executed.HttpResult.BodyJson, replayed.HttpResult!.BodyJson);
            Assert.Equal(
                executed.HttpResult.Headers.OrderBy(header => header.Key, StringComparer.Ordinal),
                replayed.HttpResult.Headers.OrderBy(header => header.Key, StringComparer.Ordinal));
            await AssertUpdateEffectsAsync(dataSource, organizationId, taskId, expected: 1);

            var noOp = updateService.CreateCommand(
                context,
                "patch-pg-key-02",
                """{"title":"Patched gate","priority":"critical"}""",
                taskId,
                2,
                new TaskUpdateModel("Patched gate", TaskPriority.Critical, OptionalInstant.Unspecified, OptionalInstant.Unspecified),
                CreateHttpResult,
                createdAt.AddMinutes(3));
            Assert.Equal(["title", "priority"], noOp.Command.ChangedFields);
            var noOpExecuted = await updateService.ExecuteAsync(noOp.Command);
            Assert.Equal(TaskWriteCommandDisposition.Executed, noOpExecuted.Disposition);
            Assert.Equal(2L, await ScalarAsync<long>(dataSource, "SELECT o.version FROM core.objects AS o WHERE o.id = $1 AND o.object_type = 'task';", taskId));
            Assert.Equal(1, await CountAsync(dataSource, "SELECT count(*) FROM iam.idempotency_records WHERE idempotency_key = $1 AND state = 'completed';", "patch-pg-key-02"));
            await AssertUpdateEffectsAsync(dataSource, organizationId, taskId, expected: 1);

            var noOpReplay = await updateService.ExecuteAsync(noOp.Command);
            Assert.Equal(TaskWriteCommandDisposition.Replayed, noOpReplay.Disposition);
            Assert.Equal(noOpExecuted.HttpResult!.BodyJson, noOpReplay.HttpResult!.BodyJson);
            await AssertUpdateEffectsAsync(dataSource, organizationId, taskId, expected: 1);

            var stale = updateService.CreateCommand(
                context,
                "patch-pg-key-03",
                """{"title":"Stale"}""",
                taskId,
                1,
                new TaskUpdateModel("Stale", null, OptionalInstant.Unspecified, OptionalInstant.Unspecified),
                CreateHttpResult,
                createdAt.AddMinutes(4));
            var staleConflict = await Assert.ThrowsAsync<TaskLifecycleConcurrencyException>(
                () => updateService.ExecuteAsync(stale.Command));
            Assert.Equal(1, staleConflict.ExpectedVersion);
            Assert.Equal(2, staleConflict.ActualVersion);
            Assert.Equal(0, await CountAsync(dataSource, "SELECT count(*) FROM iam.idempotency_records WHERE idempotency_key = $1;", "patch-pg-key-03"));
            await AssertUpdateEffectsAsync(dataSource, organizationId, taskId, expected: 1);

            var reused = updateService.CreateCommand(
                context,
                "patch-pg-key-01",
                """{"title":"Different"}""",
                taskId,
                2,
                new TaskUpdateModel("Different", null, OptionalInstant.Unspecified, OptionalInstant.Unspecified),
                CreateHttpResult,
                createdAt.AddMinutes(5));
            Assert.Equal(TaskWriteCommandDisposition.IdempotencyKeyReused, (await updateService.ExecuteAsync(reused.Command)).Disposition);
            await AssertUpdateEffectsAsync(dataSource, organizationId, taskId, expected: 1);

            var foreign = updateService.CreateCommand(
                Context(otherOrganizationId, otherUserId, otherSessionId),
                "patch-pg-key-04",
                """{"title":"Foreign"}""",
                taskId,
                2,
                new TaskUpdateModel("Foreign", null, OptionalInstant.Unspecified, OptionalInstant.Unspecified),
                CreateHttpResult,
                createdAt.AddMinutes(6));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => updateService.ExecuteAsync(foreign.Command));

            var clearCommand = updateService.CreateCommand(
                context,
                "patch-pg-key-05",
                """{"startAtUtc":null,"deadlineAt":null}""",
                taskId,
                2,
                new TaskUpdateModel(null, null, OptionalInstant.Clear(), OptionalInstant.Clear()),
                CreateHttpResult,
                createdAt.AddMinutes(7));
            Assert.Equal(["startAtUtc", "deadlineAt"], clearCommand.Command.ChangedFields);
            Assert.Equal(TaskWriteCommandDisposition.Executed, (await updateService.ExecuteAsync(clearCommand.Command)).Disposition);
            Assert.Equal(3L, await ScalarAsync<long>(dataSource, "SELECT o.version FROM core.objects AS o WHERE o.id = $1 AND o.object_type = 'task';", taskId));
            Assert.True(await ScalarAsync<bool>(
                dataSource,
                "SELECT start_at_utc IS NULL AND deadline_at IS NULL FROM work.tasks WHERE id = $1;",
                taskId));
            await AssertUpdateEffectsAsync(dataSource, organizationId, taskId, expected: 2);
            await AssertEventChangedFieldsAsync(dataSource, organizationId, taskId, 3, ["startAtUtc", "deadlineAt"]);

            await SeedConflictingDomainEventAsync(dataSource, organizationId, actorUserId, taskId, 4);
            var rollback = updateService.CreateCommand(
                context,
                "patch-pg-rollback",
                """{"title":"Rollback"}""",
                taskId,
                3,
                new TaskUpdateModel("Rollback", null, OptionalInstant.Unspecified, OptionalInstant.Unspecified),
                CreateHttpResult,
                createdAt.AddMinutes(8));
            await Assert.ThrowsAsync<PostgresException>(() => updateService.ExecuteAsync(rollback.Command));
            Assert.Equal(3L, await ScalarAsync<long>(dataSource, "SELECT o.version FROM core.objects AS o WHERE o.id = $1 AND o.object_type = 'task';", taskId));
            Assert.Equal("Patched gate", await ScalarAsync<string>(dataSource, "SELECT title FROM work.tasks WHERE id = $1;", taskId));
            Assert.Equal(0, await CountAsync(dataSource, "SELECT count(*) FROM iam.idempotency_records WHERE idempotency_key = $1;", "patch-pg-rollback"));
            await AssertUpdateEffectsAsync(dataSource, organizationId, taskId, expected: 2);

            var readStore = new PostgresTaskReadStore(dataSource);
            var visible = await readStore.GetByIdAsync(organizationId, taskId);
            Assert.NotNull(visible);
            Assert.Equal("Patched gate", visible.Title);
            Assert.Equal(TaskPriority.Critical, visible.Priority);
            Assert.Null(visible.StartAtUtc);
            Assert.Null(visible.DeadlineAtUtc);
            Assert.Equal(3, visible.Version);
            Assert.Null(await readStore.GetByIdAsync(otherOrganizationId, taskId));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            Execute(adminDataSource, $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);");
        }
    }

    private static AuthenticatedRequestContext Context(Guid organizationId, Guid userId, Guid? sessionId = null) =>
        new(userId, sessionId ?? Guid.Parse("22222222-2222-2222-2222-222222222222"), organizationId, 1, 1, Guid.NewGuid().ToString("D"), "trace");

    private static TaskWriteHttpResult CreateHttpResult(TaskAggregate aggregate) =>
        new(
            200,
            new Dictionary<string, string> { ["ETag"] = $"\"v{aggregate.Metadata.Version}\"" },
            JsonSerializer.Serialize(new { id = aggregate.Metadata.Id, title = aggregate.Title, version = aggregate.Metadata.Version }),
            aggregate.Metadata.Id);

    private static TaskAggregate NewTask() =>
        TaskAggregate.Create(TaskId, OrganizationId, CreatorId, "  Inbox task  ", CreatedAt);

    private sealed class FakeAggregateStore : ITaskAggregateStore
    {
        private TaskAggregate? _task;

        public FakeAggregateStore(TaskAggregate? task)
        {
            _task = task;
        }

        public TaskAggregate? Get(Guid taskId, Guid organizationId) =>
            _task is not null && _task.Metadata.Id == taskId && _task.Metadata.OrganizationId == organizationId
                ? _task
                : null;

        public void Add(TaskAggregate task) => _task = task;

        public void Save(TaskAggregate task, int expectedVersion)
        {
            if (task.Metadata.Version != checked(expectedVersion + 1))
            {
                throw new ArgumentException("The saved aggregate version must be exactly one greater than the expected version.");
            }

            _task = task;
        }
    }

    private sealed class RecordingExecutor : ITaskWriteCommandExecutor
    {
        public global::System.Threading.Tasks.Task<TaskWriteCommandExecutionResult> ExecuteAsync(
            TaskWriteCommand command,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Recording executor does not execute.");
    }

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
        Guid taskId,
        int aggregateVersion)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO governance.domain_events (
                id, organization_id, aggregate_id, aggregate_type, aggregate_version,
                event_type, actor_user_id, correlation_id, operation_id, idempotency_key, payload)
            VALUES ($1, $2, $3, 'task', $4, 'TaskUpdated', $5, $6,
                    'seed.rollback.conflict', 'seed-key-0001', '{}'::jsonb);
            """);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(organizationId);
        command.Parameters.AddWithValue(taskId);
        command.Parameters.AddWithValue(aggregateVersion);
        command.Parameters.AddWithValue(actorUserId);
        command.Parameters.AddWithValue(Guid.NewGuid());
        await command.ExecuteNonQueryAsync();
    }

    private static async global::System.Threading.Tasks.Task AssertUpdateEffectsAsync(
        NpgsqlDataSource dataSource,
        Guid organizationId,
        Guid taskId,
        int expected)
    {
        Assert.Equal(expected, await CountAsync(dataSource, "SELECT count(*) FROM governance.audit_entries WHERE organization_id = $1 AND object_id = $2 AND action_code = 'task.update';", organizationId, taskId));
        Assert.Equal(expected, await CountAsync(dataSource, "SELECT count(*) FROM governance.domain_events WHERE organization_id = $1 AND aggregate_id = $2 AND event_type = 'TaskUpdated' AND operation_id = 'PATCH_api_v1_tasks_id' AND aggregate_version > 1;", organizationId, taskId));
        Assert.Equal(expected, await CountAsync(
            dataSource,
            """
            SELECT count(*)
            FROM governance.outbox_messages AS outbox
            JOIN governance.domain_events AS event
              ON event.organization_id = outbox.organization_id AND event.id = outbox.domain_event_id
            WHERE event.organization_id = $1 AND event.aggregate_id = $2 AND event.event_type = 'TaskUpdated';
            """,
            organizationId,
            taskId));
    }

    private static async global::System.Threading.Tasks.Task AssertEventChangedFieldsAsync(
        NpgsqlDataSource dataSource,
        Guid organizationId,
        Guid taskId,
        int aggregateVersion,
        string[] expectedFields)
    {
        var fields = (string[])await ScalarAsync<object>(
            dataSource,
            """
            SELECT changed_fields
            FROM governance.domain_events
            WHERE organization_id = $1 AND aggregate_id = $2
              AND event_type = 'TaskUpdated' AND aggregate_version = $3;
            """,
            organizationId,
            taskId,
            aggregateVersion);
        Assert.Equal(expectedFields, fields);
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

        return (T)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Expected scalar."));
    }

    private static void Execute(NpgsqlDataSource dataSource, string sql)
    {
        using var command = dataSource.CreateCommand(sql);
        command.ExecuteNonQuery();
    }
}
