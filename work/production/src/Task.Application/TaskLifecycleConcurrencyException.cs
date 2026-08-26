namespace Task.Application;

/// <summary>
/// Signals an optimistic concurrency conflict: the task was modified by
/// another writer between the version the caller expected and the version
/// actually stored.
/// </summary>
public sealed class TaskLifecycleConcurrencyException : InvalidOperationException
{
    public TaskLifecycleConcurrencyException(Guid taskId, long expectedVersion, int actualVersion)
        : base(
            $"Optimistic concurrency conflict for task '{taskId}': " +
            $"expected version {expectedVersion} but actual version is {actualVersion}.")
    {
        TaskId = taskId;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    public Guid TaskId { get; }

    public long ExpectedVersion { get; }

    public int ActualVersion { get; }
}
