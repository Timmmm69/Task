using Task.Application;
using Task.Domain;
using Task.Infrastructure.Persistence;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__TaskDatabase");
var organizationText = Environment.GetEnvironmentVariable("TASK_VALIDATION_ORGANIZATION_ID");
if (string.IsNullOrWhiteSpace(connectionString) ||
    !Guid.TryParse(organizationText, out var organizationId) ||
    organizationId == Guid.Empty)
{
    Console.Error.WriteLine("TASK_CONTAINER_VALIDATION code=InvalidConfiguration");
    return 2;
}

try
{
    await using var runtime = new TaskPersistenceRuntime(connectionString);
    var store = runtime.CreateTaskStore();
    var taskId = Guid.NewGuid();
    var actorId = Guid.NewGuid();
    var occurredAt = DateTimeOffset.UtcNow;
    var original = TaskAggregate.Create(taskId, organizationId, actorId, "Container role validation", occurredAt);

    store.Add(original);
    if (store.Get(taskId, Guid.NewGuid()) is not null)
    {
        throw new InvalidOperationException("Organization boundary was not enforced.");
    }

    var loaded = store.Get(taskId, organizationId)
        ?? throw new InvalidOperationException("The task could not be loaded after Add.");
    var updated = loaded.ChangePriority(actorId, TaskPriority.High, occurredAt.AddSeconds(1));
    store.Save(updated, expectedVersion: 1);

    var roundTripped = store.Get(taskId, organizationId)
        ?? throw new InvalidOperationException("The task could not be loaded after Save.");
    if (roundTripped.Metadata.Version != 2 || roundTripped.Priority != TaskPriority.High)
    {
        throw new InvalidOperationException("The saved task state did not round-trip.");
    }

    var stale = original.Start(actorId, occurredAt.AddSeconds(2));
    try
    {
        store.Save(stale, expectedVersion: 1);
        throw new InvalidOperationException("Optimistic concurrency did not reject a stale save.");
    }
    catch (TaskLifecycleConcurrencyException exception) when (exception.ActualVersion == 2)
    {
        // Expected: the production store reported the current database version.
    }

    Console.WriteLine("TASK_CONTAINER_VALIDATION code=Passed");
    return 0;
}
catch (Exception)
{
    Console.Error.WriteLine("TASK_CONTAINER_VALIDATION code=Failed");
    return 1;
}
