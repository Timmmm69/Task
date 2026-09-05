using System.Text.Json;
using Task.Application.Security;
using Task.Domain;

namespace Task.Application;

public sealed record TaskCreateModel(
    string Title,
    TaskPriority Priority,
    bool PrioritySpecified,
    DateTimeOffset? StartAtUtc,
    DateTimeOffset? DeadlineAtUtc, TaskCardContent? Content = null);

public sealed class TaskCreateCommandService
{
    public const string OperationId = "POST_api_v1_tasks";
    public const string AuditAction = "task.create";
    public const string EventType = "TaskCreated";

    private readonly ITaskWriteCommandExecutor _executor;

    public TaskCreateCommandService(ITaskWriteCommandExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        _executor = executor;
    }

    public TaskWriteCommand CreateCommand(
        AuthenticatedRequestContext context,
        string idempotencyKey,
        string requestJson,
        TaskCreateModel model,
        Func<TaskAggregate, TaskWriteHttpResult> createHttpResult,
        Guid? taskId = null,
        DateTimeOffset? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(createHttpResult);

        var id = taskId ?? Guid.NewGuid();
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        if (now.Offset != TimeSpan.Zero)
        {
            now = now.ToUniversalTime();
        }

        var changedFields = new List<string> { "title" };
        if (model.PrioritySpecified)
        {
            changedFields.Add("priority");
        }

        if (model.StartAtUtc is not null)
        {
            changedFields.Add("startAtUtc");
        }

        if (model.DeadlineAtUtc is not null)
        {
            changedFields.Add("deadlineAt");
        }

        if (model.Content is not null) changedFields.AddRange(TaskCardContent.Fields);

        var correlationId = Guid.TryParseExact(context.CorrelationId, "D", out var parsed)
            ? parsed
            : Guid.NewGuid();
        return new TaskWriteCommand(
            context.OrganizationId,
            context.UserAccountId,
            context.SessionId,
            OperationId,
            correlationId,
            idempotencyKey,
            TaskWriteRequestHasher.ComputeSha256(requestJson),
            id,
            expectedVersion: null,
            AuditAction,
            EventType,
            changedFields,
            BuildSafePayload(id, context.OrganizationId, context.UserAccountId, model),
            _ =>
            {
                var aggregate = TaskAggregate.Create(
                    id,
                    context.OrganizationId,
                    context.UserAccountId,
                    model.Title,
                    now,
                    model.Priority,
                    TaskSchedule.Create(model.StartAtUtc, model.DeadlineAtUtc), model.Content);
                return new TaskWriteMutationResult(aggregate, createHttpResult(aggregate));
            }) { RevalidatePermissions = true };
    }

    public global::System.Threading.Tasks.Task<TaskWriteCommandExecutionResult> ExecuteAsync(
        TaskWriteCommand command,
        CancellationToken cancellationToken = default) =>
        _executor.ExecuteAsync(command, cancellationToken);

    private static string BuildSafePayload(
        Guid taskId,
        Guid organizationId,
        Guid authorUserId,
        TaskCreateModel model)
    {
        var payload = new Dictionary<string, object?>
        {
            ["taskId"] = taskId,
            ["organizationId"] = organizationId,
            ["authorUserId"] = authorUserId,
        };
        if (model.PrioritySpecified)
        {
            payload["priority"] = model.Priority switch
            {
                TaskPriority.Low => "low",
                TaskPriority.Normal => "normal",
                TaskPriority.High => "high",
                TaskPriority.Critical => "critical",
                _ => throw new ArgumentOutOfRangeException(nameof(model)),
            };
        }

        if (model.StartAtUtc is not null)
        {
            payload["startAtUtc"] = model.StartAtUtc.Value.UtcDateTime;
        }

        if (model.DeadlineAtUtc is not null)
        {
            payload["deadlineAt"] = model.DeadlineAtUtc.Value.UtcDateTime;
        }

        return JsonSerializer.Serialize(payload);
    }
}
