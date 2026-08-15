using Task.Domain;

namespace Task.Application;

/// <summary>
/// Read-only application service for querying tasks. Never mutates the
/// aggregate: it only loads the task through <see cref="ITaskAggregateStore.Get"/>
/// and projects it into <see cref="TaskDetails"/> without any lifecycle transitions.
/// </summary>
public sealed class TaskQueryService
{
    private readonly ITaskAggregateStore _store;

    public TaskQueryService(ITaskAggregateStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public TaskDetails? GetById(Guid organizationId, Guid taskId)
    {
        var task = _store.Get(taskId, organizationId);
        if (task is null)
        {
            return null;
        }

        return new TaskDetails(
            task.Metadata.Id,
            task.Metadata.OrganizationId,
            task.Title,
            task.WorkStatus,
            task.Metadata.LifecycleState,
            task.Metadata.Version,
            task.Metadata.CreatedAtUtc,
            task.Metadata.UpdatedAtUtc,
            task.CompletedAtUtc,
            task.CompletedBy);
    }
}