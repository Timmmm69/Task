namespace Task.Application;

/// <summary>
/// Asynchronous read-only port for the active task projection. This is kept
/// separate from <see cref="ITaskAggregateStore"/> so list queries never load
/// aggregates one at a time.
/// </summary>
public interface ITaskReadStore
{
    global::System.Threading.Tasks.Task<TaskReadProjection?> GetByIdAsync(
        Guid organizationId,
        Guid taskId,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<TaskReadProjection?> GetVisibleByIdAsync(
        Guid organizationId, Guid taskId, Guid actorUserId, CancellationToken cancellationToken = default) =>
        GetByIdAsync(organizationId, taskId, cancellationToken);

    global::System.Threading.Tasks.Task<TaskReadPage> GetPageAsync(
        TaskReadPageRequest request,
        CancellationToken cancellationToken = default);
}
