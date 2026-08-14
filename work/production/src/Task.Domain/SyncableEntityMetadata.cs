namespace Task.Domain;

/// <summary>
/// Common metadata for a server-authoritative object that can be synchronized.
/// Timestamps are stored only in UTC and every visible state transition advances the version.
/// </summary>
public sealed record SyncableEntityMetadata
{
    private SyncableEntityMetadata(
        Guid id,
        Guid organizationId,
        Guid createdBy,
        DateTimeOffset createdAtUtc,
        Guid updatedBy,
        DateTimeOffset updatedAtUtc,
        int version,
        EntityLifecycleState lifecycleState,
        EntityLifecycleState? lifecycleStateBeforeTrash,
        DateTimeOffset? deletedAtUtc,
        Guid? deletedBy,
        DateTimeOffset? archivedAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        CreatedBy = createdBy;
        CreatedAtUtc = createdAtUtc;
        UpdatedBy = updatedBy;
        UpdatedAtUtc = updatedAtUtc;
        Version = version;
        LifecycleState = lifecycleState;
        LifecycleStateBeforeTrash = lifecycleStateBeforeTrash;
        DeletedAtUtc = deletedAtUtc;
        DeletedBy = deletedBy;
        ArchivedAtUtc = archivedAtUtc;
    }

    public Guid Id { get; }

    public Guid OrganizationId { get; }

    public Guid CreatedBy { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public Guid UpdatedBy { get; }

    public DateTimeOffset UpdatedAtUtc { get; }

    public int Version { get; }

    public EntityLifecycleState LifecycleState { get; }

    public EntityLifecycleState? LifecycleStateBeforeTrash { get; }

    public DateTimeOffset? DeletedAtUtc { get; }

    public Guid? DeletedBy { get; }

    public DateTimeOffset? ArchivedAtUtc { get; }

    public static SyncableEntityMetadata Create(
        Guid id,
        Guid organizationId,
        Guid createdBy,
        DateTimeOffset createdAtUtc)
    {
        EnsureIdentifier(id, nameof(id));
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(createdBy, nameof(createdBy));
        EnsureUtc(createdAtUtc, nameof(createdAtUtc));

        return new SyncableEntityMetadata(
            id,
            organizationId,
            createdBy,
            createdAtUtc,
            createdBy,
            createdAtUtc,
            version: 1,
            EntityLifecycleState.Active,
            lifecycleStateBeforeTrash: null,
            deletedAtUtc: null,
            deletedBy: null,
            archivedAtUtc: null);
    }

    public SyncableEntityMetadata RecordVisibleChange(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        EnsureChange(actorId, occurredAtUtc);
        EnsureNotTrashed("A trashed object must be restored before it can be changed.");

        return WithChange(actorId, occurredAtUtc);
    }

    public SyncableEntityMetadata Archive(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        EnsureChange(actorId, occurredAtUtc);
        if (LifecycleState != EntityLifecycleState.Active)
        {
            throw new InvalidOperationException("Only an active object can be archived.");
        }

        return WithChange(
            actorId,
            occurredAtUtc,
            EntityLifecycleState.Archived,
            lifecycleStateBeforeTrash: null,
            deletedAtUtc: null,
            deletedBy: null,
            archivedAtUtc: occurredAtUtc);
    }

    public SyncableEntityMetadata RestoreFromArchive(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        EnsureChange(actorId, occurredAtUtc);
        if (LifecycleState != EntityLifecycleState.Archived)
        {
            throw new InvalidOperationException("Only an archived object can be restored from archive.");
        }

        return WithChange(
            actorId,
            occurredAtUtc,
            EntityLifecycleState.Active,
            lifecycleStateBeforeTrash: null,
            deletedAtUtc: null,
            deletedBy: null,
            archivedAtUtc: null);
    }

    public SyncableEntityMetadata MoveToTrash(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        EnsureChange(actorId, occurredAtUtc);
        EnsureNotTrashed("An object that is already in trash cannot be trashed again.");

        return WithChange(
            actorId,
            occurredAtUtc,
            EntityLifecycleState.Trashed,
            LifecycleState,
            occurredAtUtc,
            actorId,
            ArchivedAtUtc);
    }

    public SyncableEntityMetadata RestoreFromTrash(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        EnsureChange(actorId, occurredAtUtc);
        if (LifecycleState != EntityLifecycleState.Trashed || LifecycleStateBeforeTrash is null)
        {
            throw new InvalidOperationException("Only a trashed object with a recorded prior state can be restored.");
        }

        var restoredState = LifecycleStateBeforeTrash.Value;
        return WithChange(
            actorId,
            occurredAtUtc,
            restoredState,
            lifecycleStateBeforeTrash: null,
            deletedAtUtc: null,
            deletedBy: null,
            archivedAtUtc: restoredState == EntityLifecycleState.Archived ? ArchivedAtUtc : null);
    }

    private SyncableEntityMetadata WithChange(
        Guid actorId,
        DateTimeOffset occurredAtUtc,
        EntityLifecycleState? lifecycleState = null,
        EntityLifecycleState? lifecycleStateBeforeTrash = null,
        DateTimeOffset? deletedAtUtc = null,
        Guid? deletedBy = null,
        DateTimeOffset? archivedAtUtc = null) =>
        new(
            Id,
            OrganizationId,
            CreatedBy,
            CreatedAtUtc,
            actorId,
            occurredAtUtc,
            checked(Version + 1),
            lifecycleState ?? LifecycleState,
            lifecycleStateBeforeTrash,
            deletedAtUtc,
            deletedBy,
            archivedAtUtc);

    private void EnsureChange(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        EnsureIdentifier(actorId, nameof(actorId));
        EnsureUtc(occurredAtUtc, nameof(occurredAtUtc));
        if (occurredAtUtc < UpdatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(occurredAtUtc),
                "A change timestamp cannot be earlier than the last update timestamp.");
        }
    }

    private void EnsureNotTrashed(string message)
    {
        if (LifecycleState == EntityLifecycleState.Trashed)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void EnsureIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", parameterName);
        }
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must use the UTC offset.", parameterName);
        }
    }
}

public enum EntityLifecycleState
{
    Active = 0,
    Archived = 1,
    Trashed = 2,
}
