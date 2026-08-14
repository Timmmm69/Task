using Task.Domain;

namespace Task.Tests;

public sealed class SyncableEntityMetadataTests
{
    private static readonly Guid EntityId = Guid.Parse("4e1c1cb1-cd04-4a4e-b0b4-c02d2532e784");
    private static readonly Guid OrganizationId = Guid.Parse("4a5a1eaf-9256-4385-a2a0-7f8d1bb87a9b");
    private static readonly Guid CreatorId = Guid.Parse("e01f6a74-c28f-4573-a2db-b01f1bb5c43d");
    private static readonly Guid EditorId = Guid.Parse("1888811b-b81c-4d55-b86c-bbf3f04102be");
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_SetsImmutableIdentityAuditFieldsAndInitialVersion()
    {
        var metadata = SyncableEntityMetadata.Create(EntityId, OrganizationId, CreatorId, CreatedAt);

        Assert.Equal(EntityId, metadata.Id);
        Assert.Equal(OrganizationId, metadata.OrganizationId);
        Assert.Equal(CreatorId, metadata.CreatedBy);
        Assert.Equal(CreatedAt, metadata.CreatedAtUtc);
        Assert.Equal(CreatorId, metadata.UpdatedBy);
        Assert.Equal(CreatedAt, metadata.UpdatedAtUtc);
        Assert.Equal(1, metadata.Version);
        Assert.Equal(EntityLifecycleState.Active, metadata.LifecycleState);
    }

    [Fact]
    public void Create_RejectsEmptyIdentifiersAndNonUtcTimestamp()
    {
        Assert.Throws<ArgumentException>(() =>
            SyncableEntityMetadata.Create(Guid.Empty, OrganizationId, CreatorId, CreatedAt));
        Assert.Throws<ArgumentException>(() =>
            SyncableEntityMetadata.Create(EntityId, OrganizationId, CreatorId,
                new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.FromHours(3))));
    }

    [Fact]
    public void ArchiveAndRestore_AdvanceVersionAndMaintainAuditTrail()
    {
        var created = SyncableEntityMetadata.Create(EntityId, OrganizationId, CreatorId, CreatedAt);
        var archivedAt = CreatedAt.AddMinutes(5);
        var archived = created.Archive(EditorId, archivedAt);
        var restored = archived.RestoreFromArchive(CreatorId, archivedAt.AddMinutes(5));

        Assert.Equal(EntityLifecycleState.Archived, archived.LifecycleState);
        Assert.Equal(archivedAt, archived.ArchivedAtUtc);
        Assert.Equal(2, archived.Version);
        Assert.Equal(EntityLifecycleState.Active, restored.LifecycleState);
        Assert.Null(restored.ArchivedAtUtc);
        Assert.Equal(CreatorId, restored.UpdatedBy);
        Assert.Equal(3, restored.Version);
    }

    [Fact]
    public void TrashAndRestore_PreservesPriorLifecycleStateAndClearsDeletionMetadata()
    {
        var archived = SyncableEntityMetadata.Create(EntityId, OrganizationId, CreatorId, CreatedAt)
            .Archive(EditorId, CreatedAt.AddMinutes(1));
        var trashedAt = CreatedAt.AddMinutes(2);
        var trashed = archived.MoveToTrash(EditorId, trashedAt);
        var restored = trashed.RestoreFromTrash(CreatorId, trashedAt.AddMinutes(1));

        Assert.Equal(EntityLifecycleState.Trashed, trashed.LifecycleState);
        Assert.Equal(EntityLifecycleState.Archived, trashed.LifecycleStateBeforeTrash);
        Assert.Equal(trashedAt, trashed.DeletedAtUtc);
        Assert.Equal(EditorId, trashed.DeletedBy);
        Assert.Equal(EntityLifecycleState.Archived, restored.LifecycleState);
        Assert.Null(restored.LifecycleStateBeforeTrash);
        Assert.Null(restored.DeletedAtUtc);
        Assert.Null(restored.DeletedBy);
        Assert.Equal(4, restored.Version);
    }

    [Fact]
    public void InvalidTransitionsAndBackdatedChanges_AreRejected()
    {
        var created = SyncableEntityMetadata.Create(EntityId, OrganizationId, CreatorId, CreatedAt);
        var trashed = created.MoveToTrash(EditorId, CreatedAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => created.RestoreFromArchive(EditorId, CreatedAt.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => trashed.RecordVisibleChange(EditorId, CreatedAt.AddMinutes(2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => created.RecordVisibleChange(EditorId, CreatedAt.AddMinutes(-1)));
    }
}
