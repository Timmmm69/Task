using Task.Application;
using Task.Domain;

namespace Task.Tests;

public sealed class TaskQueryServiceTests
{
    private static readonly Guid TaskId = Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d");
    private static readonly Guid OrganizationId = Guid.Parse("d5c4b3a2-1f0e-4d9c-8b7a-6543210fedcb");
    private static readonly Guid OtherOrganizationId = Guid.Parse("9f8e7d6c-5b4a-4e3d-2c1b-0a9f8e7d6c5b");
    private static readonly Guid CreatorId = Guid.Parse("3077f0f8-536f-4988-bd73-6f26265d0b92");
    private static readonly Guid EditorId = Guid.Parse("957c3a11-d6f2-4a2a-bb1b-0945a0f6a820");
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 15, 8, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset StartedAt = CreatedAt.AddMinutes(5);
    private static readonly DateTimeOffset CompletedAt = StartedAt.AddMinutes(5);
    private static readonly DateTimeOffset HaltedAt = CompletedAt.AddMinutes(5);

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenStoreIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new TaskQueryService(null!));
    }

    [Fact]
    public void GetById_ReturnsNull_WhenTaskDoesNotExist()
    {
        var service = new TaskQueryService(new FakeTaskAggregateStore());

        var details = service.GetById(OrganizationId, TaskId);

        Assert.Null(details);
    }

    [Fact]
    public void GetById_ReturnsNull_WhenTaskBelongsToAnotherOrganization()
    {
        var store = StoreWith(NewTask());
        var service = new TaskQueryService(store);

        var details = service.GetById(OtherOrganizationId, TaskId);

        Assert.Null(details);
    }

    [Fact]
    public void GetById_PassesExactTaskIdAndOrganizationIdToStore()
    {
        var store = StoreWith(NewTask());
        var service = new TaskQueryService(store);

        service.GetById(OrganizationId, TaskId);

        var call = Assert.Single(store.GetCalls);
        Assert.Equal(TaskId, call.TaskId);
        Assert.Equal(OrganizationId, call.OrganizationId);
    }

    [Fact]
    public void GetById_DoesNotCallAddOrSave()
    {
        var store = StoreWith(NewTask());
        var service = new TaskQueryService(store);
        var addCallsBefore = store.AddCalls;
        var saveCallsBefore = store.SaveCalls;

        service.GetById(OrganizationId, TaskId);
        service.GetById(OrganizationId, Guid.NewGuid());

        Assert.Equal(addCallsBefore, store.AddCalls);
        Assert.Equal(saveCallsBefore, store.SaveCalls);
    }

    [Fact]
    public void GetById_ProjectsNewTaskFieldsExactly()
    {
        var store = StoreWith(NewTask());
        var service = new TaskQueryService(store);

        var details = service.GetById(OrganizationId, TaskId);

        Assert.NotNull(details);
        Assert.Equal(TaskId, details.Id);
        Assert.Equal(OrganizationId, details.OrganizationId);
        Assert.Equal("Original title", details.Title);
        Assert.Equal(TaskWorkStatus.New, details.WorkStatus);
        Assert.Equal(EntityLifecycleState.Active, details.LifecycleState);
        Assert.Equal(1, details.Version);
        Assert.Equal(CreatedAt, details.CreatedAtUtc);
        Assert.Equal(CreatedAt, details.UpdatedAtUtc);
        Assert.Null(details.CompletedAtUtc);
        Assert.Null(details.CompletedBy);
    }

    [Fact]
    public void GetById_ProjectsRenamedTaskFields()
    {
        var renamed = NewTask().Rename(EditorId, "Renamed title", StartedAt);
        var service = new TaskQueryService(StoreWith(renamed));

        var details = service.GetById(OrganizationId, TaskId);

        Assert.NotNull(details);
        Assert.Equal("Renamed title", details.Title);
        Assert.Equal(TaskWorkStatus.New, details.WorkStatus);
        Assert.Equal(2, details.Version);
        Assert.Equal(StartedAt, details.UpdatedAtUtc);
        Assert.Equal(CreatedAt, details.CreatedAtUtc);
    }

    [Fact]
    public void GetById_ProjectsCompletedTaskFields()
    {
        var completed = NewTask().Start(EditorId, StartedAt).Complete(EditorId, CompletedAt);
        var service = new TaskQueryService(StoreWith(completed));

        var details = service.GetById(OrganizationId, TaskId);

        Assert.NotNull(details);
        Assert.Equal(TaskWorkStatus.Completed, details.WorkStatus);
        Assert.Equal(EntityLifecycleState.Active, details.LifecycleState);
        Assert.Equal(CompletedAt, details.CompletedAtUtc);
        Assert.Equal(EditorId, details.CompletedBy);
        Assert.Equal(CompletedAt, details.UpdatedAtUtc);
        Assert.Equal(3, details.Version);
    }

    [Fact]
    public void GetById_ProjectsCancelledTaskFields()
    {
        var cancelled = NewTask().Start(EditorId, StartedAt).Cancel(EditorId, CompletedAt);
        var service = new TaskQueryService(StoreWith(cancelled));

        var details = service.GetById(OrganizationId, TaskId);

        Assert.NotNull(details);
        Assert.Equal(TaskWorkStatus.Cancelled, details.WorkStatus);
        Assert.Null(details.CompletedAtUtc);
        Assert.Null(details.CompletedBy);
    }

    [Fact]
    public void GetById_ProjectsArchivedTaskFields()
    {
        var archived = NewTask().Start(EditorId, StartedAt)
            .Complete(EditorId, CompletedAt)
            .Archive(EditorId, HaltedAt);
        var service = new TaskQueryService(StoreWith(archived));

        var details = service.GetById(OrganizationId, TaskId);

        Assert.NotNull(details);
        Assert.Equal(EntityLifecycleState.Archived, details.LifecycleState);
        Assert.Equal(TaskWorkStatus.Completed, details.WorkStatus);
        Assert.Equal(HaltedAt, details.UpdatedAtUtc);
        Assert.Equal(4, details.Version);
    }

    [Fact]
    public void GetById_ProjectsTrashedTaskFields()
    {
        var trashed = NewTask().Start(EditorId, StartedAt)
            .Complete(EditorId, CompletedAt)
            .MoveToTrash(EditorId, HaltedAt);
        var service = new TaskQueryService(StoreWith(trashed));

        var details = service.GetById(OrganizationId, TaskId);

        Assert.NotNull(details);
        Assert.Equal(EntityLifecycleState.Trashed, details.LifecycleState);
        Assert.Equal(TaskWorkStatus.Completed, details.WorkStatus);
        Assert.Equal(CompletedAt, details.CompletedAtUtc);
        Assert.Equal(EditorId, details.CompletedBy);
        Assert.Equal(HaltedAt, details.UpdatedAtUtc);
        Assert.Equal(4, details.Version);
    }

    [Fact]
    public void GetById_DoesNotMutateTheStoredAggregate()
    {
        var store = StoreWith(NewTask());
        var service = new TaskQueryService(store);

        service.GetById(OrganizationId, TaskId);

        var stored = store.Get(TaskId, OrganizationId);
        Assert.NotNull(stored);
        Assert.Equal("Original title", stored.Title);
        Assert.Equal(TaskWorkStatus.New, stored.WorkStatus);
        Assert.Equal(EntityLifecycleState.Active, stored.Metadata.LifecycleState);
        Assert.Equal(1, stored.Metadata.Version);
    }

    private static TaskAggregate NewTask() =>
        TaskAggregate.Create(TaskId, OrganizationId, CreatorId, "Original title", CreatedAt);

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

        public List<(Guid TaskId, Guid OrganizationId)> GetCalls { get; } = new();

        public TaskAggregate? Get(Guid taskId, Guid organizationId)
        {
            GetCalls.Add((taskId, organizationId));
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
            _tasks[(task.Metadata.OrganizationId, task.Metadata.Id)] = task;
        }
    }
}