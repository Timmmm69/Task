using Task.Domain;

namespace Task.Application;

/// <summary>
/// Storage port for the Task aggregate. Implementations own persistence and
/// must enforce the optimistic concurrency guarantee of <see cref="Save"/>:
/// a saved task is expected to currently have <paramref name="expectedVersion"/>.
/// </summary>
public interface ITaskAggregateStore
{
    TaskAggregate? Get(Guid taskId, Guid organizationId);

    void Add(TaskAggregate task);

    void Save(TaskAggregate task, int expectedVersion);
}