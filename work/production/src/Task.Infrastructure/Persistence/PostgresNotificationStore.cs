using Npgsql;
using NpgsqlTypes;
using Task.Application.ProductData;
using Task.Domain;

namespace Task.Infrastructure.Persistence;

internal sealed class PostgresNotificationStore : PostgresProductObjectStore<NotificationSnapshot>, INotificationStore
{
    private static readonly string[] SeverityNames = ["info", "warning", "critical"];
    private static readonly string[] StatusNames = ["pending", "delivered", "read", "dismissed", "failed", "expired"];

    public PostgresNotificationStore(NpgsqlDataSource dataSource)
        : base(dataSource, "notification", "notify.notifications",
            new("recipient_user_id", NpgsqlDbType.Uuid), new("notification_type", NpgsqlDbType.Varchar),
            new("source_object_id", NpgsqlDbType.Uuid), new("title", NpgsqlDbType.Text),
            new("body", NpgsqlDbType.Text), new("severity", NpgsqlDbType.Varchar),
            new("status", NpgsqlDbType.Varchar), new("not_before", NpgsqlDbType.TimestampTz),
            new("expires_at", NpgsqlDbType.TimestampTz), new("delivered_at", NpgsqlDbType.TimestampTz),
            new("read_at", NpgsqlDbType.TimestampTz), new("dismissed_at", NpgsqlDbType.TimestampTz),
            new("deduplication_key", NpgsqlDbType.Varchar), new("action_payload", NpgsqlDbType.Jsonb))
    { }

    protected override NotificationSnapshot Hydrate(SyncableEntityMetadata metadata, NpgsqlDataReader reader) =>
        new(metadata, reader.GetGuid(12), reader.GetString(13), ProductStoreSql.Optional<Guid>(reader, 14),
            reader.GetString(15), reader.GetString(16),
            ProductStoreSql.ParseEnum<NotificationSeverity>(reader.GetString(17), SeverityNames),
            ProductStoreSql.ParseEnum<NotificationStatus>(reader.GetString(18), StatusNames),
            reader.GetFieldValue<DateTimeOffset>(19), ProductStoreSql.Optional<DateTimeOffset>(reader, 20),
            ProductStoreSql.Optional<DateTimeOffset>(reader, 21), ProductStoreSql.Optional<DateTimeOffset>(reader, 22),
            ProductStoreSql.Optional<DateTimeOffset>(reader, 23), ProductStoreSql.Text(reader, 24), reader.GetString(25));

    protected override object?[] Values(NotificationSnapshot entity) =>
        [entity.RecipientUserId, entity.NotificationType, entity.SourceObjectId, entity.Title, entity.Body,
            ProductStoreSql.EnumValue(entity.Severity, SeverityNames), ProductStoreSql.EnumValue(entity.Status, StatusNames),
            entity.NotBeforeUtc, entity.ExpiresAtUtc, entity.DeliveredAtUtc, entity.ReadAtUtc, entity.DismissedAtUtc,
            entity.DeduplicationKey, entity.ActionPayloadJson];

    protected override void Validate(NotificationSnapshot entity)
    {
        ProductStoreSql.Metadata(entity);
        ProductStoreSql.Identifier(entity.RecipientUserId, nameof(entity.RecipientUserId));
        ProductStoreSql.OptionalIdentifier(entity.SourceObjectId, nameof(entity.SourceObjectId));
        ProductStoreSql.RequiredText(entity.NotificationType, 40, nameof(entity.NotificationType));
        ProductStoreSql.RequiredText(entity.Title, 500, nameof(entity.Title));
        ProductStoreSql.RequiredText(entity.Body, 10000, nameof(entity.Body));
        ProductStoreSql.OptionalText(entity.DeduplicationKey, 200, nameof(entity.DeduplicationKey));
        _ = ProductStoreSql.EnumValue(entity.Severity, SeverityNames);
        _ = ProductStoreSql.EnumValue(entity.Status, StatusNames);
        ProductStoreSql.Utc(entity.NotBeforeUtc, nameof(entity.NotBeforeUtc));
        ProductStoreSql.Utc(entity.ExpiresAtUtc, nameof(entity.ExpiresAtUtc));
        ProductStoreSql.Utc(entity.DeliveredAtUtc, nameof(entity.DeliveredAtUtc));
        ProductStoreSql.Utc(entity.ReadAtUtc, nameof(entity.ReadAtUtc));
        ProductStoreSql.Utc(entity.DismissedAtUtc, nameof(entity.DismissedAtUtc));
        ProductStoreSql.JsonObject(entity.ActionPayloadJson, nameof(entity.ActionPayloadJson));
        if (entity.ExpiresAtUtc <= entity.NotBeforeUtc) throw new ArgumentException("Notification expiry must follow not-before.");
        if ((entity.Status is NotificationStatus.Delivered or NotificationStatus.Read && entity.DeliveredAtUtc is null) ||
            (entity.Status == NotificationStatus.Read && entity.ReadAtUtc is null) ||
            (entity.Status == NotificationStatus.Dismissed && entity.DismissedAtUtc is null))
            throw new ArgumentException("Notification status timestamps are incomplete.");
        if (entity.Metadata.Version == 1 && (entity.Status != NotificationStatus.Pending ||
            entity.DeliveredAtUtc is not null || entity.ReadAtUtc is not null || entity.DismissedAtUtc is not null))
            throw new ArgumentException("A new notification must be pending without delivery/read/dismiss timestamps.");
        foreach (var timestamp in new[] { entity.DeliveredAtUtc, entity.ReadAtUtc, entity.DismissedAtUtc })
            if (timestamp is { } value && (value < entity.Metadata.CreatedAtUtc || value > entity.Metadata.UpdatedAtUtc))
                throw new ArgumentException("Notification status timestamps must be within the object's lifetime.");
        if (entity.ReadAtUtc < entity.DeliveredAtUtc || entity.DismissedAtUtc < entity.ReadAtUtc)
            throw new ArgumentException("Notification status timestamps must be chronological.");
    }

    protected override bool SameContent(NotificationSnapshot left, NotificationSnapshot right) =>
        left with { Metadata = right.Metadata, ActionPayloadJson = right.ActionPayloadJson } == right &&
        ProductStoreSql.JsonEquivalent(left.ActionPayloadJson, right.ActionPayloadJson);

    protected override void ValidateChange(NotificationSnapshot previous, NotificationSnapshot next)
    {
        var content = previous with
        {
            Metadata = next.Metadata,
            Status = next.Status,
            DeliveredAtUtc = next.DeliveredAtUtc,
            ReadAtUtc = next.ReadAtUtc,
            DismissedAtUtc = next.DismissedAtUtc,
            ActionPayloadJson = next.ActionPayloadJson,
        };
        if (content != next || !ProductStoreSql.JsonEquivalent(previous.ActionPayloadJson, next.ActionPayloadJson))
            throw new InvalidOperationException("Notification content is immutable after creation.");
        if ((previous.DeliveredAtUtc is not null && previous.DeliveredAtUtc != next.DeliveredAtUtc) ||
            (previous.ReadAtUtc is not null && previous.ReadAtUtc != next.ReadAtUtc) ||
            (previous.DismissedAtUtc is not null && previous.DismissedAtUtc != next.DismissedAtUtc))
            throw new InvalidOperationException("Notification status timestamps cannot be cleared or rewritten.");
        if (previous.Status is NotificationStatus.Read or NotificationStatus.Dismissed or NotificationStatus.Expired &&
            next.Status != previous.Status && !(previous.Status == NotificationStatus.Read && next.Status == NotificationStatus.Dismissed))
            throw new InvalidOperationException("A terminal notification state cannot be reversed.");
    }
}
