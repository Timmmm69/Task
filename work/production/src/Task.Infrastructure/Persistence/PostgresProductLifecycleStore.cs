using Npgsql;
using NpgsqlTypes;
using Task.Application.ProductData;

namespace Task.Infrastructure.Persistence;

internal sealed class PostgresProductLifecycleStore : IProductLifecycleStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresProductLifecycleStore(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    public ArchiveEntrySnapshot? GetCurrentArchive(Guid objectId, Guid organizationId)
    {
        Validate(objectId, organizationId);
        using var command = _dataSource.CreateCommand(
            """
            SELECT id, organization_id, object_id, object_type, archived_by, archived_at, reason,
                status, restored_by, restored_at
            FROM governance.archive_entries
            WHERE organization_id = $1 AND object_id = $2 AND status = 'archived';
            """);
        ProductStoreSql.Add(command, NpgsqlDbType.Uuid, organizationId);
        ProductStoreSql.Add(command, NpgsqlDbType.Uuid, objectId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new ArchiveEntrySnapshot(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), reader.GetGuid(4),
            reader.GetFieldValue<DateTimeOffset>(5), ProductStoreSql.Text(reader, 6), ArchiveEntryStatus.Archived,
            ProductStoreSql.Optional<Guid>(reader, 8), ProductStoreSql.Optional<DateTimeOffset>(reader, 9)) : null;
    }

    public TrashEntrySnapshot? GetCurrentTrash(Guid objectId, Guid organizationId)
    {
        Validate(objectId, organizationId);
        using var command = _dataSource.CreateCommand(
            """
            SELECT id, organization_id, object_id, object_type, deleted_by, deleted_at, purge_after,
                deletion_reason, status, restored_by, restored_at, purged_at
            FROM governance.trash_entries
            WHERE organization_id = $1 AND object_id = $2 AND status IN ('retained','blocked_by_hold');
            """);
        ProductStoreSql.Add(command, NpgsqlDbType.Uuid, organizationId);
        ProductStoreSql.Add(command, NpgsqlDbType.Uuid, objectId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new TrashEntrySnapshot(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), reader.GetGuid(4),
            reader.GetFieldValue<DateTimeOffset>(5), reader.GetFieldValue<DateTimeOffset>(6),
            ProductStoreSql.Text(reader, 7), reader.GetString(8) == "retained" ? TrashEntryStatus.Retained : TrashEntryStatus.BlockedByHold,
            ProductStoreSql.Optional<Guid>(reader, 9), ProductStoreSql.Optional<DateTimeOffset>(reader, 10),
            ProductStoreSql.Optional<DateTimeOffset>(reader, 11)) : null;
    }

    private static void Validate(Guid objectId, Guid organizationId)
    {
        ProductStoreSql.Identifier(objectId, nameof(objectId));
        ProductStoreSql.Identifier(organizationId, nameof(organizationId));
    }
}
