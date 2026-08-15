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

    public static SyncableEntityMetadata Reconstitute(
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
        EnsureIdentifier(id, nameof(id));
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(createdBy, nameof(createdBy));
        EnsureIdentifier(updatedBy, nameof(updatedBy));
        EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        EnsureUtc(updatedAtUtc, nameof(updatedAtUtc));
        EnsureOptionalUtc(deletedAtUtc, nameof(deletedAtUtc));
        EnsureOptionalUtc(archivedAtUtc, nameof(archivedAtUtc));

        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Version must be positive.");
        }

        if (!Enum.IsDefined(lifecycleState))
        {
            throw new ArgumentOutOfRangeException(nameof(lifecycleState), "Unknown lifecycle state.");
        }

        if (lifecycleStateBeforeTrash is not null && !Enum.IsDefined(lifecycleStateBeforeTrash.Value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifecycleStateBeforeTrash),
                "Unknown lifecycle state before trash.");
        }

        if (updatedAtUtc < createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(updatedAtUtc), "Updated timestamp cannot precede creation.");
        }

        EnsureLifecycleTimestampRange(deletedAtUtc, createdAtUtc, updatedAtUtc, nameof(deletedAtUtc));
        EnsureLifecycleTimestampRange(archivedAtUtc, createdAtUtc, updatedAtUtc, nameof(archivedAtUtc));
        if (deletedBy is not null)
        {
            EnsureIdentifier(deletedBy.Value, nameof(deletedBy));
        }

        EnsureLifecycleConsistency(
            lifecycleState,
            lifecycleStateBeforeTrash,
            deletedAtUtc,
            deletedBy,
            archivedAtUtc);

        return new SyncableEntityMetadata(
            id,
            organizationId,
            createdBy,
            createdAtUtc,
            updatedBy,
            updatedAtUtc,
            version,
            lifecycleState,
            lifecycleStateBeforeTrash,
            deletedAtUtc,
            deletedBy,
            archivedAtUtc);
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

    private static void EnsureOptionalUtc(DateTimeOffset? value, string parameterName)
    {
        if (value.HasValue)
        {
            EnsureUtc(value.Value, parameterName);
        }
    }

    private static void EnsureLifecycleTimestampRange(
        DateTimeOffset? value,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        string parameterName)
    {
        if (value is not null && (value.Value < createdAtUtc || value.Value > updatedAtUtc))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Lifecycle timestamp must be between creation and last update.");
        }
    }

    private static void EnsureLifecycleConsistency(
        EntityLifecycleState lifecycleState,
        EntityLifecycleState? lifecycleStateBeforeTrash,
        DateTimeOffset? deletedAtUtc,
        Guid? deletedBy,
        DateTimeOffset? archivedAtUtc)
    {
        var hasDeletion = deletedAtUtc is not null && deletedBy is not null;
        var hasPartialDeletion = deletedAtUtc is null != (deletedBy is null);
        if (hasPartialDeletion)
        {
            throw new ArgumentException("Deletion timestamp and actor must be either both present or both absent.");
        }

        switch (lifecycleState)
        {
            case EntityLifecycleState.Active when
                lifecycleStateBeforeTrash is not null || hasDeletion || archivedAtUtc is not null:
                throw new ArgumentException("Active lifecycle metadata contains archive or trash fields.");
            case EntityLifecycleState.Archived when
                lifecycleStateBeforeTrash is not null || hasDeletion || archivedAtUtc is null:
                throw new ArgumentException("Archived lifecycle metadata is inconsistent.");
            case EntityLifecycleState.Trashed:
                if (lifecycleStateBeforeTrash is not (EntityLifecycleState.Active or EntityLifecycleState.Archived) ||
                    !hasDeletion ||
                    (lifecycleStateBeforeTrash == EntityLifecycleState.Archived) != (archivedAtUtc is not null))
                {
                    throw new ArgumentException("Trashed lifecycle metadata is inconsistent.");
                }

                break;
        }
    }
}

public enum EntityLifecycleState
{
    Active = 0,
    Archived = 1,
    Trashed = 2,
}
