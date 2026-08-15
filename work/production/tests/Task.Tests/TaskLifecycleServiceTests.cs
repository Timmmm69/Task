using Task.Application;
using Task.Domain;

namespace Task.Tests;

public sealed class TaskLifecycleServiceTests
{
    private static readonly Guid TaskId = Guid.Parse("e06a7a1e-4d3c-4f1b-9e2d-7b8c5a1f3d2a");
    private static readonly Guid OrganizationId = Guid.Parse("751fa8ce-5cc3-4d98-8574-1108080b2ff4");
    private static readonly Guid OtherOrganizationId = Guid.Parse("b2c31488-6a1e-4d5e-9c7b-21d09f3e8a51");
    private static readonly Guid CreatorId = Guid.Parse("3077f0f8-536f-4988-bd73-6f26265d0b92");
    private static readonly Guid EditorId = Guid.Parse("957c3a11-d6f2-4a2a-bb1b-0945a0f6a820");
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 15, 8, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset StartedAt = CreatedAt.AddMinutes(5);
    private static readonly DateTimeOffset CompletedAt = StartedAt.AddMinutes(5);
    private static readonly DateTimeOffset HaltedAt = CompletedAt.AddMinutes(5);
    private static readonly DateTimeOffset RestoredAt = HaltedAt.AddMinutes(5);

    [Fact]
    public void Create_CallsAddExactlyOnceAndReturnsNewTaskAtVersionOne()
    {
        var store = new FakeTaskAggregateStore();
        var service = new TaskLifecycleService(store);

        var task = service.Create(TaskId, OrganizationId, CreatorId, "  Implement login  ", CreatedAt);

        Assert.Equal(1, store.AddCalls);
        Assert.Equal(0, store.SaveCalls);
        Assert.Equal(TaskWorkStatus.New, task.WorkStatus);
        Assert.Equal(1, task.Metadata.Version);
        Assert.Equal("Implement login", task.Title);
        Assert.Equal(TaskId, task.Metadata.Id);
        Assert.Equal(OrganizationId, task.Metadata.OrganizationId);
        Assert.Equal(EntityLifecycleState.Active, task.Metadata.LifecycleState);
    }

    [Fact]
    public void Create_StoresTheAggregateInTheStore()
    {
        var store = new FakeTaskAggregateStore();
        var service = new TaskLifecycleService(store);

        service.Create(TaskId, OrganizationId, CreatorId, "Implement login", CreatedAt);

        var stored = store.Get(TaskId, OrganizationId);
        Assert.NotNull(stored);
        Assert.Equal("Implement login", stored.Title);
    }

    [Fact]
    public void Rename_UpdatesTitleAndSavesExactlyOnce()
    {
        var store = StoreWith(NewTask());
        var service = new TaskLifecycleService(store);

        var renamed = service.Rename(OrganizationId, TaskId, 1, EditorId, StartedAt, "New title");

        Assert.Equal("New title", renamed.Title);
        Assert.Equal(TaskWorkStatus.New, renamed.WorkStatus);
        Assert.Equal(1, store.SaveCalls);
        Assert.Same(renamed, store.LastSavedTask);
    }

    [Fact]
    public void Start_TransitionsToInProgressAndSavesExactlyOnce()
    {
        var store = StoreWith(NewTask());
        var service = new TaskLifecycleService(store);

        var started = service.Start(OrganizationId, TaskId, 1, EditorId, StartedAt);

        Assert.Equal(TaskWorkStatus.InProgress, started.WorkStatus);
        Assert.Equal(2, started.Metadata.Version);
        Assert.Equal(1, store.SaveCalls);
    }

    [Fact]
    public void SubmitForReview_TransitionsToReviewAndSavesExactlyOnce()
    {
        var store = StoreWith(NewTask().Start(EditorId, StartedAt));
        var service = new TaskLifecycleService(store);

        var reviewed = service.SubmitForReview(OrganizationId, TaskId, 2, EditorId, CompletedAt);

        Assert.Equal(TaskWorkStatus.Review, reviewed.WorkStatus);
        Assert.Equal(3, reviewed.Metadata.Version);
        Assert.Equal(1, store.SaveCalls);
    }

    [Fact]
    public void Complete_TransitionsToCompletedAndSavesExactlyOnce()
    {
        var store = StoreWith(NewTask().Start(EditorId, StartedAt));
        var service = new TaskLifecycleService(store);

        var completed = service.Complete(OrganizationId, TaskId, 2, EditorId, CompletedAt);

        Assert.Equal(TaskWorkStatus.Completed, completed.WorkStatus);
        Assert.Equal(CompletedAt, completed.CompletedAtUtc);
        Assert.Equal(EditorId, completed.CompletedBy);
        Assert.Equal(1, store.SaveCalls);
    }

    [Fact]
    public void Cancel_TransitionsToCancelledAndSavesExactlyOnce()
    {
        var store = StoreWith(NewTask().Start(EditorId, StartedAt));
        var service = new TaskLifecycleService(store);

        var cancelled = service.Cancel(OrganizationId, TaskId, 2, EditorId, CompletedAt);

        Assert.Equal(TaskWorkStatus.Cancelled, cancelled.WorkStatus);
        Assert.Null(cancelled.CompletedAtUtc);
        Assert.Null(cancelled.CompletedBy);
        Assert.Equal(1, store.SaveCalls);
    }

    [Fact]
    public void Archive_TransitionsToArchivedAndSavesExactlyOnce()
    {
        var store = StoreWith(CompletedTask());
        var service = new TaskLifecycleService(store);

        var archived = service.Archive(OrganizationId, TaskId, 3, EditorId, HaltedAt);

        Assert.Equal(EntityLifecycleState.Archived, archived.Metadata.LifecycleState);
        Assert.Equal(HaltedAt, archived.Metadata.ArchivedAtUtc);
        Assert.Equal(1, store.SaveCalls);
    }

    [Fact]
    public void RestoreFromArchive_RestoresActiveTaskAndSavesExactlyOnce()
    {
        var store = StoreWith(CompletedTask().Archive(EditorId, HaltedAt));
        var service = new TaskLifecycleService(store);

        var restored = service.RestoreFromArchive(OrganizationId, TaskId, 4, EditorId, RestoredAt);

        Assert.Equal(EntityLifecycleState.Active, restored.Metadata.LifecycleState);
        Assert.Null(restored.Metadata.ArchivedAtUtc);
        Assert.Equal(1, store.SaveCalls);
    }

    [Fact]
    public void MoveToTrash_TransitionsToTrashedAndSavesExactlyOnce()
    {
        var store = StoreWith(CompletedTask());
        var service = new TaskLifecycleService(store);

        var trashed = service.MoveToTrash(OrganizationId, TaskId, 3, EditorId, HaltedAt);

        Assert.Equal(EntityLifecycleState.Trashed, trashed.Metadata.LifecycleState);
        Assert.Equal(EntityLifecycleState.Active, trashed.Metadata.LifecycleStateBeforeTrash);
        Assert.Equal(1, store.SaveCalls);
    }

    [Fact]
    public void RestoreFromTrash_RestoresPreviousStateAndSavesExactlyOnce()
    {
        var store = StoreWith(CompletedTask().MoveToTrash(EditorId, HaltedAt));
        var service = new TaskLifecycleService(store);

        var restored = service.RestoreFromTrash(OrganizationId, TaskId, 4, EditorId, RestoredAt);

        Assert.Equal(EntityLifecycleState.Active, restored.Metadata.LifecycleState);
        Assert.Null(restored.Metadata.LifecycleStateBeforeTrash);
        Assert.Equal(TaskWorkStatus.Completed, restored.WorkStatus);
        Assert.Equal(1, store.SaveCalls);
    }

    [Fact]
    public void Operation_OnTaskOfAnotherOrganization_ThrowsKeyNotFoundExceptionAndDoesNotSave()
    {
        var store = StoreWith(NewTask());
        var service = new TaskLifecycleService(store);

        Assert.Throws<KeyNotFoundException>(() =>
            service.Rename(OtherOrganizationId, TaskId, 1, EditorId, StartedAt, "Hijacked title"));

        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public void Operation_OnMissingTask_ThrowsKeyNotFoundExceptionAndDoesNotSave()
    {
        var store = new FakeTaskAggregateStore();
        var service = new TaskLifecycleService(store);

        Assert.Throws<KeyNotFoundException>(() =>
            service.Start(OrganizationId, TaskId, 1, EditorId, StartedAt));

        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public void StaleExpectedVersion_ThrowsConcurrencyExceptionWithFieldsAndDoesNotSave()
    {
        var store = StoreWith(NewTask().Start(EditorId, StartedAt));
        var service = new TaskLifecycleService(store);

        var exception = Assert.Throws<TaskLifecycleConcurrencyException>(() =>
            service.Complete(OrganizationId, TaskId, 1, EditorId, CompletedAt));

        Assert.Equal(TaskId, exception.TaskId);
        Assert.Equal(1, exception.ExpectedVersion);
        Assert.Equal(2, exception.ActualVersion);
        Assert.IsAssignableFrom<InvalidOperationException>(exception);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public void StaleExpectedVersion_MessageExplainsTheConflict()
    {
        var store = StoreWith(NewTask().Start(EditorId, StartedAt));
        var service = new TaskLifecycleService(store);

        var exception = Assert.Throws<TaskLifecycleConcurrencyException>(() =>
            service.Complete(OrganizationId, TaskId, 1, EditorId, CompletedAt));

        Assert.Contains("expected version 1", exception.Message);
        Assert.Contains("actual version is 2", exception.Message);
        Assert.Contains(TaskId.ToString(), exception.Message);
    }

    [Fact]
    public void InvalidTransition_DomainException_DoesNotCallSave()
    {
        var store = StoreWith(NewTask().Start(EditorId, StartedAt));
        var service = new TaskLifecycleService(store);

        Assert.Throws<InvalidOperationException>(() =>
            service.Start(OrganizationId, TaskId, 2, EditorId, CompletedAt));

        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public void Operation_OnArchivedTask_DomainException_DoesNotCallSave()
    {
        var store = StoreWith(CompletedTask().Archive(EditorId, HaltedAt));
        var service = new TaskLifecycleService(store);

        Assert.Throws<InvalidOperationException>(() =>
            service.Rename(OrganizationId, TaskId, 4, EditorId, RestoredAt, "Forbidden rename"));

        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public void Save_ReceivesOriginalExpectedVersion_AndResultHasIncrementedVersion()
    {
        var store = StoreWith(NewTask());
        var service = new TaskLifecycleService(store);

        service.Start(OrganizationId, TaskId, 1, EditorId, StartedAt);

        Assert.Equal(1, store.LastSavedExpectedVersion);
        Assert.NotNull(store.LastSavedTask);
        Assert.Equal(2, store.LastSavedTask.Metadata.Version);
    }

    [Fact]
    public void Save_PersistsTheUpdatedAggregate()
    {
        var store = StoreWith(NewTask());
        var service = new TaskLifecycleService(store);

        service.Start(OrganizationId, TaskId, 1, EditorId, StartedAt);

        var stored = store.Get(TaskId, OrganizationId);
        Assert.NotNull(stored);
        Assert.Equal(TaskWorkStatus.InProgress, stored.WorkStatus);
        Assert.Equal(2, stored.Metadata.Version);
        Assert.Equal(EditorId, stored.Metadata.UpdatedBy);
    }

    private static TaskAggregate NewTask() =>
        TaskAggregate.Create(TaskId, OrganizationId, CreatorId, "Original title", CreatedAt);

    private static TaskAggregate CompletedTask() =>
        NewTask().Start(EditorId, StartedAt).Complete(EditorId, CompletedAt);

    private static FakeTaskAggregateStore StoreWith(TaskAggregate task)
    {
        var store = new FakeTaskAggregateStore();
        store.Add(task);
        return store;
    }

    private sealed class FakeTaskAggregateStore : ITaskAggregateStore
    {
        private readonly Dictionary<(Guid OrganizationId, Guid TaskId), TaskAggregate> _tasks = new();

        public int AddCalls { get; private set; }

        public int SaveCalls { get; private set; }

        public TaskAggregate? LastSavedTask { get; private set; }

        public int? LastSavedExpectedVersion { get; private set; }

        public TaskAggregate? Get(Guid taskId, Guid organizationId)
        {
            _tasks.TryGetValue((organizationId, taskId), out var task);
            return task;
        }

        public void Add(TaskAggregate task)
        {
            AddCalls++;
            _tasks[(task.Metadata.OrganizationId, task.Metadata.Id)] = task;
        }

        public void Save(TaskAggregate task, int expectedVersion)
        {
            SaveCalls++;
            LastSavedTask = task;
            LastSavedExpectedVersion = expectedVersion;
            _tasks[(task.Metadata.OrganizationId, task.Metadata.Id)] = task;
        }
    }
}