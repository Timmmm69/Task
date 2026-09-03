using Npgsql;
using NpgsqlTypes;
using Task.Application.ProductData;
using Task.Domain;

namespace Task.Infrastructure.Persistence;

internal sealed class PostgresCatalogItemStore : PostgresProductObjectStore<CatalogItemSnapshot>, ICatalogItemStore
{
    private static readonly string[] TypeNames =
        ["virtual_folder", "file_reference", "folder_reference", "web_link", "text_note"];

    public PostgresCatalogItemStore(NpgsqlDataSource dataSource)
        : base(dataSource, "catalog_item", "files.catalog_items",
            new("parent_item_id", NpgsqlDbType.Uuid), new("item_type", NpgsqlDbType.Varchar),
            new("name", NpgsqlDbType.Text), new("description", NpgsqlDbType.Text),
            new("note_content", NpgsqlDbType.Text), new("web_url", NpgsqlDbType.Text),
            new("mime_type", NpgsqlDbType.Varchar), new("file_extension", NpgsqlDbType.Varchar),
            new("observed_size_bytes", NpgsqlDbType.Bigint), new("observed_modified_at", NpgsqlDbType.TimestampTz),
            new("sort_order", NpgsqlDbType.Integer), new("created_by", NpgsqlDbType.Uuid))
    { }

    protected override CatalogItemSnapshot Hydrate(SyncableEntityMetadata metadata, NpgsqlDataReader reader) =>
        new(metadata, ProductStoreSql.Optional<Guid>(reader, 12),
            ProductStoreSql.ParseEnum<CatalogItemType>(reader.GetString(13), TypeNames), reader.GetString(14),
            ProductStoreSql.Text(reader, 15), ProductStoreSql.Text(reader, 16), ProductStoreSql.Text(reader, 17),
            ProductStoreSql.Text(reader, 18), ProductStoreSql.Text(reader, 19),
            ProductStoreSql.Optional<long>(reader, 20), ProductStoreSql.Optional<DateTimeOffset>(reader, 21),
            reader.GetInt32(22));

    protected override object?[] Values(CatalogItemSnapshot entity) =>
        [entity.ParentId, ProductStoreSql.EnumValue(entity.ItemType, TypeNames), entity.Name, entity.Description,
            entity.NoteContent, entity.WebUrl, entity.MimeType, entity.FileExtension, entity.ObservedSizeBytes,
            entity.ObservedModifiedAtUtc, entity.SortOrder, entity.Metadata.CreatedBy];

    protected override void Validate(CatalogItemSnapshot entity)
    {
        ProductStoreSql.Metadata(entity);
        ProductStoreSql.OptionalIdentifier(entity.ParentId, nameof(entity.ParentId));
        if (entity.ParentId == entity.Metadata.Id) throw new ArgumentException("A catalog item cannot parent itself.");
        _ = ProductStoreSql.EnumValue(entity.ItemType, TypeNames);
        ProductStoreSql.RequiredText(entity.Name, 500, nameof(entity.Name));
        ProductStoreSql.OptionalText(entity.Description, 20000, nameof(entity.Description));
        ProductStoreSql.OptionalText(entity.NoteContent, 100000, nameof(entity.NoteContent));
        ProductStoreSql.OptionalText(entity.WebUrl, 2048, nameof(entity.WebUrl));
        ProductStoreSql.OptionalText(entity.MimeType, 200, nameof(entity.MimeType));
        ProductStoreSql.OptionalText(entity.FileExtension, 32, nameof(entity.FileExtension));
        ProductStoreSql.Utc(entity.ObservedModifiedAtUtc, nameof(entity.ObservedModifiedAtUtc));
        if (entity.ObservedSizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(entity.ObservedSizeBytes));
        var validContent = entity.ItemType switch
        {
            CatalogItemType.WebLink => entity.NoteContent is null && entity.WebUrl is not null &&
                Uri.TryCreate(entity.WebUrl, UriKind.Absolute, out _),
            CatalogItemType.TextNote => entity.NoteContent is not null && entity.WebUrl is null,
            _ => entity.NoteContent is null && entity.WebUrl is null,
        };
        if (!validContent) throw new ArgumentException("Catalog content does not match the item type.");
    }

    protected override bool SameContent(CatalogItemSnapshot left, CatalogItemSnapshot right) =>
        left with { Metadata = right.Metadata } == right;

    protected override void BeforeWrite(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CatalogItemSnapshot entity)
    {
        // Serialize hierarchy changes within a tenant so concurrent moves cannot create a cycle.
        using (var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended('task-catalog:' || $1::text, 0));", connection, transaction))
        {
            ProductStoreSql.Add(command, NpgsqlDbType.Uuid, entity.Metadata.OrganizationId);
            command.ExecuteNonQuery();
        }

        if (entity.ParentId is null) return;
        if (entity.Metadata.LifecycleState != EntityLifecycleState.Trashed)
        {
            using var parentCommand = new NpgsqlCommand(
                """
                SELECT o.lifecycle_state FROM files.catalog_items p
                JOIN core.objects o ON o.organization_id = p.organization_id AND o.id = p.id
                WHERE p.organization_id = $1 AND p.id = $2;
                """, connection, transaction);
            ProductStoreSql.Add(parentCommand, NpgsqlDbType.Uuid, entity.Metadata.OrganizationId);
            ProductStoreSql.Add(parentCommand, NpgsqlDbType.Uuid, entity.ParentId);
            if (parentCommand.ExecuteScalar() is "trashed")
                throw new InvalidOperationException("A catalog item cannot be restored or changed under a trashed parent.");
        }
        using var cycleCommand = new NpgsqlCommand(
            """
            WITH RECURSIVE ancestors AS (
                SELECT id, parent_item_id FROM files.catalog_items WHERE organization_id = $1 AND id = $2
                UNION
                SELECT p.id, p.parent_item_id FROM files.catalog_items p
                JOIN ancestors a ON a.parent_item_id = p.id WHERE p.organization_id = $1
            )
            SELECT EXISTS (SELECT 1 FROM ancestors WHERE id = $3);
            """, connection, transaction);
        ProductStoreSql.Add(cycleCommand, NpgsqlDbType.Uuid, entity.Metadata.OrganizationId);
        ProductStoreSql.Add(cycleCommand, NpgsqlDbType.Uuid, entity.ParentId);
        ProductStoreSql.Add(cycleCommand, NpgsqlDbType.Uuid, entity.Metadata.Id);
        if ((bool)cycleCommand.ExecuteScalar()!) throw new InvalidOperationException("A catalog hierarchy cannot contain cycles.");
    }
}
