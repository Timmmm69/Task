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
    OptionalInstant DeadlineAt, string? CardPatch = null);

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

/// <summary>Deferred update command ready for idempotency-first transactional execution.</summary>
public sealed record TaskUpdatePreparation(TaskWriteCommand Command);

/// <summary>
/// Application service for the concurrency-safe Task update command. The command defers
/// tenant-scoped loading, version/state validation and actual changed-field calculation
/// until after durable idempotency acquisition inside the executor transaction. This keeps
/// exact replay and key-reuse classification ahead of stale-version checks. A no-op is
/// completed durably by the executor without aggregate, audit, event or outbox effects.
/// </summary>
public sealed class TaskUpdateCommandService
{
    public const string OperationId = "PATCH_api_v1_tasks_id";
    public const string AuditAction = "task.update";
    public const string EventType = "TaskUpdated";

    private readonly ITaskWriteCommandExecutor _executor;

    public TaskUpdateCommandService(ITaskWriteCommandExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        _executor = executor;
    }

    public TaskUpdatePreparation CreateCommand(
        AuthenticatedRequestContext context,
        string idempotencyKey,
        string requestJson,
        Guid taskId,
        long expectedVersion,
        TaskUpdateModel model,
        Func<TaskAggregate, TaskWriteHttpResult> createHttpResult,
        DateTimeOffset? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(createHttpResult);

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
            ComputeRequestHash(taskId, expectedVersion, requestJson),
            taskId,
            expectedVersion,
            AuditAction,
            EventType,
            ComputeRequestedFields(model),
            BuildSafePayload(taskId, model),
            existing =>
            {
                var currentTask = existing
                    ?? throw new KeyNotFoundException("The task to update was not found.");
                EnsureUpdatable(currentTask);
                var changedFields = ComputeChangedFields(currentTask, model);
                var updated = currentTask.UpdateEditableFields(
                    context.UserAccountId,
                    now,
                    model.Title,
                    model.Priority,
                    model.StartsAtUtc,
                    model.DeadlineAt, currentTask.Content.Apply(model.CardPatch));
                return new TaskWriteMutationResult(updated, createHttpResult(updated), changedFields);
            }) { RevalidatePermissions = true };

        return new TaskUpdatePreparation(command);
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

        if (model.CardPatch is not null && current.Content.Apply(model.CardPatch).ToJson() != current.Content.ToJson())
        {
            using var patch = JsonDocument.Parse(model.CardPatch);
            using var before = JsonDocument.Parse(current.Content.ToJson());
            using var after = JsonDocument.Parse(current.Content.Apply(model.CardPatch).ToJson());
            changed.AddRange(patch.RootElement.EnumerateObject().Where(p =>
                before.RootElement.GetProperty(p.Name).GetRawText() != after.RootElement.GetProperty(p.Name).GetRawText()).Select(p => p.Name));
        }
        return changed;
    }

    private static IReadOnlyList<string> ComputeRequestedFields(TaskUpdateModel model)
    {
        var requested = new List<string>(4);
        if (model.Title is not null)
        {
            requested.Add("title");
        }

        if (model.Priority is not null)
        {
            requested.Add("priority");
        }

        if (model.StartsAtUtc.Specified)
        {
            requested.Add("startAtUtc");
        }

        if (model.DeadlineAt.Specified)
        {
            requested.Add("deadlineAt");
        }

        if (model.CardPatch is not null)
        {
            using var patch = JsonDocument.Parse(model.CardPatch);
            requested.AddRange(patch.RootElement.EnumerateObject().Select(p => p.Name));
        }
        return requested;
    }

    private static byte[] ComputeRequestHash(Guid taskId, long expectedVersion, string requestJson)
    {
        using var request = JsonDocument.Parse(requestJson);
        var envelope = JsonSerializer.Serialize(new
        {
            taskId,
            expectedVersion,
            patch = request.RootElement,
        });
        return TaskWriteRequestHasher.ComputeSha256(envelope);
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

public sealed class TaskStatusTransitionConflictException : Exception
{
    public TaskStatusTransitionConflictException(string problemCode, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(problemCode);
        ProblemCode = problemCode;
    }

    public string ProblemCode { get; }
}

/// <summary>Builds an idempotent, optimistic-concurrency-safe Task status transition.</summary>
public sealed class TaskStatusTransitionCommandService
{
    public const string OperationId = "POST_api_v1_tasks_id_transition";
    public const string AuditAction = "task.change_status";
    public const string EventType = "TaskStatusChanged";

    private readonly ITaskWriteCommandExecutor _executor;

    public TaskStatusTransitionCommandService(ITaskWriteCommandExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        _executor = executor;
    }

    public TaskWriteCommand CreateCommand(
        AuthenticatedRequestContext context,
        string idempotencyKey,
        string requestJson,
        Guid taskId,
        long expectedVersion,
        TaskWorkStatus targetStatus,
        Func<TaskAggregate, TaskWriteHttpResult> createHttpResult,
        DateTimeOffset? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(createHttpResult);
        var now = (nowUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var correlationId = Guid.TryParseExact(context.CorrelationId, "D", out var parsed)
            ? parsed
            : Guid.NewGuid();
        var target = ToContractValue(targetStatus);
        var requestHash = ComputeTransitionRequestHash(taskId, expectedVersion, requestJson);

        return new TaskWriteCommand(
            context.OrganizationId,
            context.UserAccountId,
            context.SessionId,
            OperationId,
            correlationId,
            idempotencyKey,
            requestHash,
            taskId,
            expectedVersion,
            AuditAction,
            EventType,
            targetStatus == TaskWorkStatus.Completed ? ["status", "completedAt", "completedBy"] : ["status"],
            JsonSerializer.Serialize(new { taskId, targetStatus = target }),
            current =>
            {
                var existing = current ?? throw new KeyNotFoundException("The task to transition was not found.");
                EnsureActive(existing);
                var updated = ApplyTransition(existing, targetStatus, context.UserAccountId, now);
                var payload = JsonSerializer.Serialize(new
                {
                    taskId,
                    fromStatus = ToContractValue(existing.WorkStatus),
                    targetStatus = target,
                    aggregateVersion = updated.Metadata.Version,
                    correlationId,
                    actorId = context.UserAccountId,
                });
                return new TaskWriteMutationResult(updated, createHttpResult(updated), SafePayloadJson: payload);
            }) { RevalidatePermissions = true };
    }

    public global::System.Threading.Tasks.Task<TaskWriteCommandExecutionResult> ExecuteAsync(
        TaskWriteCommand command,
        CancellationToken cancellationToken = default) =>
        _executor.ExecuteAsync(command, cancellationToken);

    private static TaskAggregate ApplyTransition(
        TaskAggregate task,
        TaskWorkStatus target,
        Guid actorId,
        DateTimeOffset now) => (task.WorkStatus, target) switch
        {
            (TaskWorkStatus.New, TaskWorkStatus.InProgress) => task.Start(actorId, now),
            (TaskWorkStatus.InProgress, TaskWorkStatus.Review) => task.SubmitForReview(actorId, now),
            (TaskWorkStatus.New or TaskWorkStatus.InProgress or TaskWorkStatus.Review, TaskWorkStatus.Completed) =>
                task.Complete(actorId, now),
            (TaskWorkStatus.New or TaskWorkStatus.InProgress or TaskWorkStatus.Review, TaskWorkStatus.Cancelled) =>
                task.Cancel(actorId, now),
            _ => throw new TaskStatusTransitionConflictException(
                "INVALID_STATE_TRANSITION",
                "The requested task status transition is not allowed."),
        };

    private static void EnsureActive(TaskAggregate task)
    {
        if (task.Metadata.LifecycleState == EntityLifecycleState.Archived)
        {
            throw new TaskStatusTransitionConflictException(
                "OBJECT_ARCHIVED",
                "An archived task must be restored before its status can change.");
        }

        if (task.Metadata.LifecycleState == EntityLifecycleState.Trashed)
        {
            throw new TaskStatusTransitionConflictException(
                "OBJECT_DELETED",
                "A trashed task must be restored before its status can change.");
        }
    }

    private static string ToContractValue(TaskWorkStatus status) => status switch
    {
        TaskWorkStatus.New => "new",
        TaskWorkStatus.InProgress => "in_progress",
        TaskWorkStatus.Review => "review",
        TaskWorkStatus.Completed => "completed",
        TaskWorkStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static byte[] ComputeTransitionRequestHash(Guid taskId, long expectedVersion, string requestJson)
    {
        using var request = JsonDocument.Parse(requestJson);
        return TaskWriteRequestHasher.ComputeSha256(JsonSerializer.Serialize(new
        {
            taskId,
            expectedVersion,
            transition = request.RootElement,
        }));
    }
}
