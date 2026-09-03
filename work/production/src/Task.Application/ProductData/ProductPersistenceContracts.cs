using Task.Domain;

namespace Task.Application.ProductData;

public interface IVersionedProductStore<T> where T : class, IProductEntitySnapshot
{
    T? Get(Guid entityId, Guid organizationId);

    void Add(T entity);

    void Save(T entity, int expectedVersion);
}

public interface IProjectStore : IVersionedProductStore<ProjectSnapshot>;

public interface IContactStore : IVersionedProductStore<ContactSnapshot>;

public interface ICatalogItemStore : IVersionedProductStore<CatalogItemSnapshot>;

public interface INotificationStore : IVersionedProductStore<NotificationSnapshot>;

public interface IProductSettingsStore
{
    OrganizationSettingsSnapshot? GetOrganization(Guid organizationId);

    void AddOrganization(OrganizationSettingsSnapshot settings);

    void SaveOrganization(OrganizationSettingsSnapshot settings, int expectedVersion);

    UserSettingsSnapshot? GetUser(Guid userAccountId, Guid organizationId);

    void AddUser(UserSettingsSnapshot settings);

    void SaveUser(UserSettingsSnapshot settings, int expectedVersion);

    NotificationPreferenceSnapshot? GetNotificationPreference(
        Guid userAccountId,
        Guid organizationId,
        string notificationType);

    void AddNotificationPreference(NotificationPreferenceSnapshot preference);

    void SaveNotificationPreference(NotificationPreferenceSnapshot preference, int expectedVersion);
}

public interface IProductLifecycleStore
{
    ArchiveEntrySnapshot? GetCurrentArchive(Guid objectId, Guid organizationId);

    TrashEntrySnapshot? GetCurrentTrash(Guid objectId, Guid organizationId);
}

public interface IProductEntitySnapshot
{
    SyncableEntityMetadata Metadata { get; }
}

public sealed record ProjectSnapshot(
    SyncableEntityMetadata Metadata,
    string Name,
    string? Description,
    Guid OwnerUserId,
    Guid? ManagerUserId,
    ProjectStatus Status,
    DateOnly? StartDate,
    DateOnly? PlannedEndDate,
    DateTimeOffset? ActualEndAtUtc,
    string? DefaultTimeZone,
    string? ColorCode) : IProductEntitySnapshot;

public enum ProjectStatus
{
    Planning = 0,
    Active = 1,
    Paused = 2,
    Completed = 3,
}

public sealed record ContactSnapshot(
    SyncableEntityMetadata Metadata,
    string FirstName,
    string? LastName,
    string? MiddleName,
    string DisplayName,
    string? Notes,
    ContactStatus Status) : IProductEntitySnapshot;

public enum ContactStatus
{
    Active = 0,
    Inactive = 1,
}

public sealed record CatalogItemSnapshot(
    SyncableEntityMetadata Metadata,
    Guid? ParentId,
    CatalogItemType ItemType,
    string Name,
    string? Description,
    string? NoteContent,
    string? WebUrl,
    string? MimeType,
    string? FileExtension,
    long? ObservedSizeBytes,
    DateTimeOffset? ObservedModifiedAtUtc,
    int SortOrder) : IProductEntitySnapshot;

public enum CatalogItemType
{
    VirtualFolder = 0,
    FileReference = 1,
    FolderReference = 2,
    WebLink = 3,
    TextNote = 4,
}

public sealed record NotificationSnapshot(
    SyncableEntityMetadata Metadata,
    Guid RecipientUserId,
    string NotificationType,
    Guid? SourceObjectId,
    string Title,
    string Body,
    NotificationSeverity Severity,
    NotificationStatus Status,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? DeliveredAtUtc,
    DateTimeOffset? ReadAtUtc,
    DateTimeOffset? DismissedAtUtc,
    string? DeduplicationKey,
    string ActionPayloadJson) : IProductEntitySnapshot;

public enum NotificationSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2,
}

public enum NotificationStatus
{
    Pending = 0,
    Delivered = 1,
    Read = 2,
    Dismissed = 3,
    Failed = 4,
    Expired = 5,
}

public sealed record OrganizationSettingsSnapshot(
    Guid OrganizationId,
    int TrashRetentionDays,
    int HistoryRetentionDays,
    int ChangeFeedRetentionDays,
    int RecurrenceHorizonDays,
    int RecurrenceMinInstances,
    TimeOnly DefaultWorkdayStart,
    TimeOnly DefaultWorkdayEnd,
    short FirstDayOfWeek,
    int MaxRequestBytes,
    DateTimeOffset UpdatedAtUtc,
    int Version);

public sealed record UserSettingsSnapshot(
    Guid UserAccountId,
    Guid OrganizationId,
    string Language,
    string TimeFormat,
    short FirstDayOfWeek,
    TimeOnly WorkdayStart,
    TimeOnly WorkdayEnd,
    IReadOnlyList<short> WeekendDays,
    int DefaultTaskDurationMinutes,
    int DefaultReminderOffsetMinutes,
    bool AutostartEnabled,
    bool AllowLocalPaths,
    bool ConfirmCatalogDelete,
    string MissingFileBehavior,
    string CustomPreferencesJson,
    DateTimeOffset UpdatedAtUtc,
    int Version);

public sealed record NotificationPreferenceSnapshot(
    Guid UserAccountId,
    Guid OrganizationId,
    string NotificationType,
    bool Enabled,
    bool DesktopEnabled,
    bool SoundEnabled,
    int DefaultSnoozeMinutes,
    TimeOnly? QuietHoursStart,
    TimeOnly? QuietHoursEnd,
    string? QuietHoursTimeZone,
    DateTimeOffset UpdatedAtUtc,
    int Version);

public sealed record ArchiveEntrySnapshot(
    Guid Id,
    Guid OrganizationId,
    Guid ObjectId,
    string ObjectType,
    Guid ArchivedBy,
    DateTimeOffset ArchivedAtUtc,
    string? Reason,
    ArchiveEntryStatus Status,
    Guid? RestoredBy,
    DateTimeOffset? RestoredAtUtc);

public enum ArchiveEntryStatus
{
    Archived = 0,
    Restored = 1,
}

public sealed record TrashEntrySnapshot(
    Guid Id,
    Guid OrganizationId,
    Guid ObjectId,
    string ObjectType,
    Guid DeletedBy,
    DateTimeOffset DeletedAtUtc,
    DateTimeOffset PurgeAfterUtc,
    string? DeletionReason,
    TrashEntryStatus Status,
    Guid? RestoredBy,
    DateTimeOffset? RestoredAtUtc,
    DateTimeOffset? PurgedAtUtc);

public enum TrashEntryStatus
{
    Retained = 0,
    Restored = 1,
    Purged = 2,
    BlockedByHold = 3,
}

public sealed class ProductEntityConcurrencyException : Exception
{
    public ProductEntityConcurrencyException(
        string entityType,
        Guid entityId,
        int expectedVersion,
        int? actualVersion)
        : base(actualVersion is null
            ? $"{entityType} '{entityId}' does not exist in the requested organization."
            : $"{entityType} '{entityId}' has version {actualVersion}; expected {expectedVersion}.")
    {
        EntityType = entityType;
        EntityId = entityId;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    public string EntityType { get; }

    public Guid EntityId { get; }

    public int ExpectedVersion { get; }

    public int? ActualVersion { get; }
}
