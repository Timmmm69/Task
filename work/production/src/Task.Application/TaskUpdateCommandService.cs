using System.Text.Json;
using Task.Application.Security;
using Task.Domain;

namespace Task.Application;

/// <summary>
/// Presence-aware model of one Task field update. A null <see cref="Title"/> or
/// <see cref="Priority"/> leaves the field unchanged; an unspecified
/// <see cref="OptionalInstant"/> schedule bound leaves it unchanged, an explicitly
/// specified bound replaces it, and a null value clears the nullable bound.
/// </summary>
public sealed record TaskUpdateModel(
    string? Title,
    TaskPriority? Priority,
    OptionalInstant StartsAtUtc,
    OptionalInstant DeadlineAt);

/// <summary>Conflict classification produced by the update pre-check.</summary>
public sealed class TaskUpdateConflictException : Exception
{
    public TaskUpdateConflictException(string problemCode, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(problemCode);
        ProblemCode = problemCode;
    }

    public string ProblemCode { get; }
}

/// <summary>
/// Complete preparation of one Task update: the current aggregate loaded tenant-scoped
/// outside the transaction and the durable command to execute inside it.
/// </summary>
public sealed record TaskUpdatePreparation(TaskAggregate Current, TaskWriteCommand Command);

/// <summary>
/// Application service for the concurrency-safe Task update command. It pre-loads the
/// aggregate tenant-scoped to classify not-found, stale-version, archived/trashed and
/// terminal states before the command is created, computes the fields that the patch
/// will actually change and builds one <see cref="TaskWriteCommand"/> whose mutation
/// performs the single atomic <see cref="TaskAggregate.UpdateEditableFields"/> call
/// inside the executor transaction. When <see cref="TaskWriteCommand.ChangedFields"/>
/// is empty the caller short-circuits the update as a no-op without invoking the
/// executor, so no audit entry, domain event or outbox message is created.
/// </summary>
public sealed class TaskUpdateCommandService
{
    public const string OperationId = "PATCH_api_v1_tasks_id";
    public const string AuditAction = "task.update";
    public const string EventType = "TaskUpdated";

    private readonly ITaskWriteCommandExecutor _executor;
    private readonly ITaskAggregateStore _store;

    public TaskUpdateCommandService(ITaskWriteCommandExecutor executor, ITaskAggregateStore store)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(store);
        _executor = executor;
        _store = store;
    }

    public TaskUpdatePreparation CreateCommand(
        AuthenticatedRequestContext context,
        string idempotencyKey,
        string requestJson,
        Guid taskId,
        int expectedVersion,
        TaskUpdateModel model,
        Func<TaskAggregate, TaskWriteHttpResult> createHttpResult,
        DateTimeOffset? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(createHttpResult);

        var current = _store.Get(taskId, context.OrganizationId)
            ?? throw new KeyNotFoundException(
                $"Task '{taskId}' was not found in organization '{context.OrganizationId}'.");

        if (current.Metadata.Version != expectedVersion)
        {
            throw new TaskLifecycleConcurrencyException(taskId, expectedVersion, current.Metadata.Version);
        }

        EnsureUpdatable(current);

        var changedFields = ComputeChangedFields(current, model);
        var now = NormalizeNow(nowUtc ?? DateTimeOffset.UtcNow);
        var correlationId = Guid.TryParseExact(context.CorrelationId, "D", out var parsed)
            ? parsed
            : Guid.NewGuid();

        var command = new TaskWriteCommand(
            context.OrganizationId,
            context.UserAccountId,
            context.SessionId,
            OperationId,
            correlationId,
            idempotencyKey,
            TaskWriteRequestHasher.ComputeSha256(requestJson),
            taskId,
            expectedVersion,
            AuditAction,
            EventType,
            changedFields,
            BuildSafePayload(taskId, model),
            existing =>
            {
                var currentTask = existing
                    ?? throw new KeyNotFoundException("The task to update was not found.");
                var updated = currentTask.UpdateEditableFields(
                    context.UserAccountId,
                    now,
                    model.Title,
                    model.Priority,
                    model.StartsAtUtc,
                    model.DeadlineAt);
                return new TaskWriteMutationResult(updated, createHttpResult(updated));
            });

        return new TaskUpdatePreparation(current, command);
    }

    public global::System.Threading.Tasks.Task<TaskWriteCommandExecutionResult> ExecuteAsync(
        TaskWriteCommand command,
        CancellationToken cancellationToken = default) =>
        _executor.ExecuteAsync(command, cancellationToken);

    /// <summary>
    /// Classifies stored tasks that must not be updated. Trashed and archived lifecycle
    /// states and the terminal work states map to their canonical problem codes.
    /// </summary>
    private static void EnsureUpdatable(TaskAggregate task)
    {
        switch (task.Metadata.LifecycleState)
        {
            case EntityLifecycleState.Trashed:
                throw new TaskUpdateConflictException(
                    "OBJECT_DELETED",
                    "A trashed task must be restored before it can be updated.");
            case EntityLifecycleState.Archived:
                throw new TaskUpdateConflictException(
                    "OBJECT_ARCHIVED",
                    "An archived task must be restored before it can be updated.");
        }

        if (task.WorkStatus is TaskWorkStatus.Completed or TaskWorkStatus.Cancelled)
        {
            throw new TaskUpdateConflictException(
                "INVALID_STATE_TRANSITION",
                "A completed or cancelled task cannot be updated.");
        }
    }

    /// <summary>
    /// Computes the fields the patch will actually change and validates the final
    /// schedule as a whole. The executor guarantees that a matching expected version
    /// implies the identical stored state, so this list equals the mutation outcome.
    /// </summary>
    private static IReadOnlyList<string> ComputeChangedFields(TaskAggregate current, TaskUpdateModel model)
    {
        var changed = new List<string>(4);
        if (model.Title is not null && model.Title != current.Title)
        {
            changed.Add("title");
        }

        if (model.Priority is not null && model.Priority.Value != current.Priority)
        {
            changed.Add("priority");
        }

        var effectiveStart = model.StartsAtUtc.Specified ? model.StartsAtUtc.Value : current.Schedule.StartsAtUtc;
        var effectiveDeadline = model.DeadlineAt.Specified ? model.DeadlineAt.Value : current.Schedule.DeadlineUtc;
        _ = TaskSchedule.Create(effectiveStart, effectiveDeadline);

        if (model.StartsAtUtc.Specified && effectiveStart != current.Schedule.StartsAtUtc)
        {
            changed.Add("startAtUtc");
        }

        if (model.DeadlineAt.Specified && effectiveDeadline != current.Schedule.DeadlineUtc)
        {
            changed.Add("deadlineAt");
        }

        return changed;
    }

    private static DateTimeOffset NormalizeNow(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero ? value : value.ToUniversalTime();

    private static string BuildSafePayload(Guid taskId, TaskUpdateModel model)
    {
        var payload = new Dictionary<string, object?>
        {
            ["taskId"] = taskId,
        };
        if (model.Priority is not null)
        {
            payload["priority"] = model.Priority.Value switch
            {
                TaskPriority.Low => "low",
                TaskPriority.Normal => "normal",
                TaskPriority.High => "high",
                TaskPriority.Critical => "critical",
                _ => throw new ArgumentOutOfRangeException(nameof(model)),
            };
        }

        if (model.StartsAtUtc.Specified)
        {
            payload["startAtUtc"] = model.StartsAtUtc.Value?.UtcDateTime;
        }

        if (model.DeadlineAt.Specified)
        {
            payload["deadlineAt"] = model.DeadlineAt.Value?.UtcDateTime;
        }

        return JsonSerializer.Serialize(payload);
    }
}
