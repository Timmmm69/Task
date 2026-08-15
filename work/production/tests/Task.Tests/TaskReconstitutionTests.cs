using Task.Domain;

namespace Task.Tests;

public sealed class TaskReconstitutionTests
{
    private static readonly Guid TaskId = Guid.Parse("4102a8c2-19dc-47c5-8b63-16a78f352719");
    private static readonly Guid OrganizationId = Guid.Parse("e10d93fd-0ad4-44b0-a1db-e0fd62884971");
    private static readonly Guid CreatorId = Guid.Parse("ad23960f-d96b-4780-aee2-822316e3c22b");
    private static readonly Guid EditorId = Guid.Parse("ad43fc14-8080-4a24-9be1-a86410d5ae88");
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset UpdatedAt = CreatedAt.AddHours(1);

    [Fact]
    public void Reconstitute_RestoresAllPersistedFieldsWithoutAdvancingVersion()
    {
        var metadata = SyncableEntityMetadata.Reconstitute(
            TaskId,
            OrganizationId,
            CreatorId,
            CreatedAt,
            EditorId,
            UpdatedAt,
            7,
            EntityLifecycleState.Trashed,
            EntityLifecycleState.Active,
            UpdatedAt,
            EditorId,
            archivedAtUtc: null);
        var schedule = TaskSchedule.Create(CreatedAt.AddMinutes(10), CreatedAt.AddMinutes(50));

        var task = TaskAggregate.Reconstitute(
            metadata,
            "  Persisted task  ",
            TaskWorkStatus.Completed,
            CreatedAt.AddMinutes(45),
            EditorId,
            TaskPriority.Critical,
            schedule);

        Assert.Equal("Persisted task", task.Title);
        Assert.Equal(7, task.Metadata.Version);
        Assert.Equal(EntityLifecycleState.Trashed, task.Metadata.LifecycleState);
        Assert.Equal(EntityLifecycleState.Active, task.Metadata.LifecycleStateBeforeTrash);
        Assert.Equal(TaskPriority.Critical, task.Priority);
        Assert.Equal(schedule, task.Schedule);
        Assert.Equal(CreatedAt.AddMinutes(45), task.CompletedAtUtc);
        Assert.Equal(EditorId, task.CompletedBy);
    }

    [Fact]
    public void MetadataReconstitute_RejectsNonUtcTimestamp()
    {
        var nonUtc = new DateTimeOffset(2026, 8, 15, 13, 0, 0, TimeSpan.FromHours(3));

        Assert.Throws<ArgumentException>(() => SyncableEntityMetadata.Reconstitute(
            TaskId,
            OrganizationId,
            CreatorId,
            nonUtc,
            EditorId,
            UpdatedAt,
            2,
            EntityLifecycleState.Active,
            null,
            null,
            null,
            null));
    }

    [Fact]
    public void MetadataReconstitute_RejectsTrashedStateWithoutPreviousState()
    {
        Assert.Throws<ArgumentException>(() => SyncableEntityMetadata.Reconstitute(
            TaskId,
            OrganizationId,
            CreatorId,
            CreatedAt,
            EditorId,
            UpdatedAt,
            2,
            EntityLifecycleState.Trashed,
            null,
            UpdatedAt,
            EditorId,
            null));
    }

    [Fact]
    public void TaskReconstitute_RejectsCompletionFieldsForNonCompletedStatus()
    {
        var metadata = SyncableEntityMetadata.Create(TaskId, OrganizationId, CreatorId, CreatedAt);

        Assert.Throws<ArgumentException>(() => TaskAggregate.Reconstitute(
            metadata,
            "Task",
            TaskWorkStatus.New,
            CreatedAt,
            CreatorId,
            TaskPriority.Normal,
            TaskSchedule.Create(null, null)));
    }

    [Fact]
    public void TaskReconstitute_RejectsCompletedStatusWithoutCompletionFields()
    {
        var metadata = SyncableEntityMetadata.Create(TaskId, OrganizationId, CreatorId, CreatedAt);

        Assert.Throws<ArgumentException>(() => TaskAggregate.Reconstitute(
            metadata,
            "Task",
            TaskWorkStatus.Completed,
            null,
            null,
            TaskPriority.Normal,
            TaskSchedule.Create(null, null)));
    }

    [Fact]
    public void TaskReconstitute_RejectsTitleLongerThanCanonicalLimit()
    {
        var metadata = SyncableEntityMetadata.Create(TaskId, OrganizationId, CreatorId, CreatedAt);

        Assert.Throws<ArgumentException>(() => TaskAggregate.Reconstitute(
            metadata,
            new string('x', 501),
            TaskWorkStatus.New,
            null,
            null,
            TaskPriority.Normal,
            TaskSchedule.Create(null, null)));
    }

    [Fact]
    public void TaskReconstitute_RejectsCompletionBeforeCreation()
    {
        var metadata = SyncableEntityMetadata.Reconstitute(
            TaskId,
            OrganizationId,
            CreatorId,
            CreatedAt,
            EditorId,
            UpdatedAt,
            2,
            EntityLifecycleState.Active,
            null,
            null,
            null,
            null);

        Assert.Throws<ArgumentOutOfRangeException>(() => TaskAggregate.Reconstitute(
            metadata,
            "Task",
            TaskWorkStatus.Completed,
            CreatedAt.AddTicks(-1),
            EditorId,
            TaskPriority.Normal,
            TaskSchedule.Create(null, null)));
    }

    [Fact]
    public void TaskReconstitute_RejectsEmptyCompletionActor()
    {
        var metadata = SyncableEntityMetadata.Reconstitute(
            TaskId,
            OrganizationId,
            CreatorId,
            CreatedAt,
            EditorId,
            UpdatedAt,
            2,
            EntityLifecycleState.Active,
            null,
            null,
            null,
            null);

        Assert.Throws<ArgumentException>(() => TaskAggregate.Reconstitute(
            metadata,
            "Task",
            TaskWorkStatus.Completed,
            UpdatedAt,
            Guid.Empty,
            TaskPriority.Normal,
            TaskSchedule.Create(null, null)));
    }

    [Fact]
    public void MetadataReconstitute_RejectsEmptyDeletionActor()
    {
        Assert.Throws<ArgumentException>(() => SyncableEntityMetadata.Reconstitute(
            TaskId,
            OrganizationId,
            CreatorId,
            CreatedAt,
            EditorId,
            UpdatedAt,
            2,
            EntityLifecycleState.Trashed,
            EntityLifecycleState.Active,
            UpdatedAt,
            Guid.Empty,
            null));
    }
}
