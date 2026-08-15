using Task.Domain;

namespace Task.Application;

/// <summary>
/// Application service for the task lifecycle. Every mutating operation loads
/// the aggregate through <see cref="ITaskAggregateStore.Get"/>, verifies the
/// caller-provided expected version against the stored one, delegates the
/// transition to the aggregate and persists the result with the original
/// expected version so the store can atomically confirm the concurrency guard.
/// Lifecycle rules are not duplicated here and are never bypassed.
/// </summary>
public sealed class TaskLifecycleService
{
    private readonly ITaskAggregateStore _store;

    public TaskLifecycleService(ITaskAggregateStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public TaskAggregate Create(
        Guid taskId,
        Guid organizationId,
        Guid creatorId,
        string title,
        DateTimeOffset createdAtUtc)
    {
        var task = TaskAggregate.Create(taskId, organizationId, creatorId, title, createdAtUtc);
        _store.Add(task);

        return task;
    }

    public TaskAggregate Rename(
        Guid organizationId,
        Guid taskId,
        int expectedVersion,
        Guid actorId,
        DateTimeOffset occurredAtUtc,
        string title) =>
        Execute(organizationId, taskId, expectedVersion, (task) => task.Rename(actorId, title, occurredAtUtc));

    public TaskAggregate Start(
        Guid organizationId,
        Guid taskId,
        int expectedVersion,
        Guid actorId,
        DateTimeOffset occurredAtUtc) =>
        Execute(organizationId, taskId, expectedVersion, (task) => task.Start(actorId, occurredAtUtc));

    public TaskAggregate SubmitForReview(
        Guid organizationId,
        Guid taskId,
        int expectedVersion,
        Guid actorId,
        DateTimeOffset occurredAtUtc) =>
        Execute(organizationId, taskId, expectedVersion, (task) => task.SubmitForReview(actorId, occurredAtUtc));

    public TaskAggregate Complete(
        Guid organizationId,
        Guid taskId,
        int expectedVersion,
        Guid actorId,
        DateTimeOffset occurredAtUtc) =>
        Execute(organizationId, taskId, expectedVersion, (task) => task.Complete(actorId, occurredAtUtc));

    public TaskAggregate Cancel(
        Guid organizationId,
        Guid taskId,
        int expectedVersion,
        Guid actorId,
        DateTimeOffset occurredAtUtc) =>
        Execute(organizationId, taskId, expectedVersion, (task) => task.Cancel(actorId, occurredAtUtc));

    public TaskAggregate Archive(
        Guid organizationId,
        Guid taskId,
        int expectedVersion,
        Guid actorId,
        DateTimeOffset occurredAtUtc) =>
        Execute(organizationId, taskId, expectedVersion, (task) => task.Archive(actorId, occurredAtUtc));

    public TaskAggregate RestoreFromArchive(
        Guid organizationId,
        Guid taskId,
        int expectedVersion,
        Guid actorId,
        DateTimeOffset occurredAtUtc) =>
        Execute(organizationId, taskId, expectedVersion, (task) => task.RestoreFromArchive(actorId, occurredAtUtc));

    public TaskAggregate MoveToTrash(
        Guid organizationId,
        Guid taskId,
        int expectedVersion,
        Guid actorId,
        DateTimeOffset occurredAtUtc) =>
        Execute(organizationId, taskId, expectedVersion, (task) => task.MoveToTrash(actorId, occurredAtUtc));

    public TaskAggregate RestoreFromTrash(
        Guid organizationId,
        Guid taskId,
        int expectedVersion,
        Guid actorId,
        DateTimeOffset occurredAtUtc) =>
        Execute(organizationId, taskId, expectedVersion, (task) => task.RestoreFromTrash(actorId, occurredAtUtc));

    private TaskAggregate Execute(
        Guid organizationId,
        Guid taskId,
        int expectedVersion,
        Func<TaskAggregate, TaskAggregate> transition)
    {
        var task = _store.Get(taskId, organizationId)
            ?? throw new KeyNotFoundException(
                $"Task '{taskId}' was not found in organization '{organizationId}'.");

        if (task.Metadata.Version != expectedVersion)
        {
            throw new TaskLifecycleConcurrencyException(taskId, expectedVersion, task.Metadata.Version);
        }

        var updatedTask = transition(task);
        _store.Save(updatedTask, expectedVersion);

        return updatedTask;
    }
}