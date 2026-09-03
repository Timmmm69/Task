using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Task.Application.ProductData;
using Task.Domain;

namespace Task.Infrastructure.Persistence;

internal abstract class PostgresProductObjectStore<T> : IVersionedProductStore<T>
    where T : class, IProductEntitySnapshot
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _objectType;
    private readonly string _table;
    private readonly ProductColumn[] _columns;

    protected PostgresProductObjectStore(
        NpgsqlDataSource dataSource,
        string objectType,
        string table,
        params ProductColumn[] columns)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
        _objectType = objectType;
        _table = table;
        _columns = columns;
    }

    public T? Get(Guid entityId, Guid organizationId)
    {
        ProductStoreSql.Identifier(entityId, nameof(entityId));
        ProductStoreSql.Identifier(organizationId, nameof(organizationId));
        using var connection = _dataSource.OpenConnection();
        return Read(connection, null, entityId, organizationId, false);
    }

    public void Add(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        Validate(entity);
        var metadata = entity.Metadata;
        if (metadata.Version != 1 || metadata.LifecycleState != EntityLifecycleState.Active ||
            metadata.CreatedAtUtc != metadata.UpdatedAtUtc || metadata.CreatedBy != metadata.UpdatedBy)
        {
            throw new ArgumentException("A new product object must be in its initial version-1 state.", nameof(entity));
        }

        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();
        BeforeWrite(connection, transaction, entity);
        using (var command = new NpgsqlCommand(
            """
            INSERT INTO core.objects (
                id, organization_id, object_type, version, created_at, created_by, updated_at, updated_by)
            VALUES ($1, $2, $3, 1, $4, $5, $4, $5);
            """, connection, transaction))
        {
            ProductStoreSql.Add(command, NpgsqlDbType.Uuid, metadata.Id);
            ProductStoreSql.Add(command, NpgsqlDbType.Uuid, metadata.OrganizationId);
            ProductStoreSql.Add(command, NpgsqlDbType.Varchar, _objectType);
            ProductStoreSql.Add(command, NpgsqlDbType.TimestampTz, metadata.CreatedAtUtc);
            ProductStoreSql.Add(command, NpgsqlDbType.Uuid, metadata.CreatedBy);
            command.ExecuteNonQuery();
        }

        var names = string.Join(", ", _columns.Select(column => column.Name));
        var placeholders = string.Join(", ", Enumerable.Range(1, _columns.Length + 2).Select(index => $"${index}"));
        using (var command = new NpgsqlCommand(
            $"INSERT INTO {_table} (id, organization_id, {names}) VALUES ({placeholders});",
            connection, transaction))
        {
            AddPayloadParameters(command, entity);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void Save(T entity, int expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(entity);
        Validate(entity);
        ProductStoreSql.ExpectedVersion(entity.Metadata.Version, expectedVersion);
        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();
        BeforeWrite(connection, transaction, entity);
        var previous = Read(connection, transaction, entity.Metadata.Id, entity.Metadata.OrganizationId, true);
        if (previous is null || previous.Metadata.Version != expectedVersion)
        {
            throw new ProductEntityConcurrencyException(
                _objectType, entity.Metadata.Id, expectedVersion, previous?.Metadata.Version);
        }

        ValidateMetadataChange(previous.Metadata, entity.Metadata);
        if (previous.Metadata.LifecycleState != entity.Metadata.LifecycleState && !SameContent(previous, entity))
        {
            throw new ArgumentException("A lifecycle transition must preserve the product object's content.", nameof(entity));
        }

        ValidateChange(previous, entity);
        using (var command = new NpgsqlCommand(
            """
            UPDATE core.objects SET
                version = $3, updated_at = $4, updated_by = $5, lifecycle_state = $6,
                lifecycle_state_before_trash = $7, archived_at = $8, deleted_at = $9, deleted_by = $10
            WHERE organization_id = $1 AND id = $2;
            """, connection, transaction))
        {
            var metadata = entity.Metadata;
            ProductStoreSql.Add(command, NpgsqlDbType.Uuid, metadata.OrganizationId);
            ProductStoreSql.Add(command, NpgsqlDbType.Uuid, metadata.Id);
            ProductStoreSql.Add(command, NpgsqlDbType.Bigint, (long)metadata.Version);
            ProductStoreSql.Add(command, NpgsqlDbType.TimestampTz, metadata.UpdatedAtUtc);
            ProductStoreSql.Add(command, NpgsqlDbType.Uuid, metadata.UpdatedBy);
            ProductStoreSql.Add(command, NpgsqlDbType.Varchar, ProductStoreSql.Lifecycle(metadata.LifecycleState));
            ProductStoreSql.Add(command, NpgsqlDbType.Varchar, metadata.LifecycleStateBeforeTrash is { } before
                ? ProductStoreSql.Lifecycle(before) : null);
            ProductStoreSql.Add(command, NpgsqlDbType.TimestampTz, metadata.ArchivedAtUtc);
            ProductStoreSql.Add(command, NpgsqlDbType.TimestampTz, metadata.DeletedAtUtc);
            ProductStoreSql.Add(command, NpgsqlDbType.Uuid, metadata.DeletedBy);
            command.ExecuteNonQuery();
        }

        var assignments = string.Join(", ", _columns.Select((column, index) => $"{column.Name} = ${index + 3}"));
        using (var command = new NpgsqlCommand(
            $"UPDATE {_table} SET {assignments} WHERE id = $1 AND organization_id = $2;",
            connection, transaction))
        {
            AddPayloadParameters(command, entity);
            if (command.ExecuteNonQuery() != 1)
            {
                throw new InvalidOperationException("The product object projection is missing.");
            }
        }

        transaction.Commit();
    }

    protected abstract T Hydrate(SyncableEntityMetadata metadata, NpgsqlDataReader reader);

    protected abstract object?[] Values(T entity);

    protected abstract void Validate(T entity);

    protected abstract bool SameContent(T left, T right);

    protected virtual void ValidateChange(T previous, T next) { }

    protected virtual void BeforeWrite(NpgsqlConnection connection, NpgsqlTransaction transaction, T entity) { }

    private T? Read(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid entityId,
        Guid organizationId,
        bool forUpdate)
    {
        var payloadColumns = string.Join(", ", _columns.Select(column => $"p.{column.Name}"));
        using var command = new NpgsqlCommand(
            $"""
            SELECT o.id, o.organization_id, o.created_by, o.created_at, o.updated_by, o.updated_at,
                o.version, o.lifecycle_state, o.lifecycle_state_before_trash, o.deleted_at, o.deleted_by,
                o.archived_at, {payloadColumns}
            FROM core.objects o JOIN {_table} p ON p.organization_id = o.organization_id AND p.id = o.id
            WHERE o.organization_id = $1 AND o.id = $2 AND o.object_type = $3
            {(forUpdate ? "FOR UPDATE OF o" : "")};
            """, connection, transaction);
        ProductStoreSql.Add(command, NpgsqlDbType.Uuid, organizationId);
        ProductStoreSql.Add(command, NpgsqlDbType.Uuid, entityId);
        ProductStoreSql.Add(command, NpgsqlDbType.Varchar, _objectType);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Hydrate(ProductStoreSql.ReadMetadata(reader), reader) : null;
    }

    private void AddPayloadParameters(NpgsqlCommand command, T entity)
    {
        ProductStoreSql.Add(command, NpgsqlDbType.Uuid, entity.Metadata.Id);
        ProductStoreSql.Add(command, NpgsqlDbType.Uuid, entity.Metadata.OrganizationId);
        var values = Values(entity);
        if (values.Length != _columns.Length)
        {
            throw new InvalidOperationException("The product projection mapping is incomplete.");
        }

        for (var index = 0; index < values.Length; index++)
        {
            ProductStoreSql.Add(command, _columns[index].Type, values[index]);
        }
    }

    private static void ValidateMetadataChange(SyncableEntityMetadata previous, SyncableEntityMetadata next)
    {
        SyncableEntityMetadata expected = (previous.LifecycleState, next.LifecycleState) switch
        {
            (EntityLifecycleState.Active, EntityLifecycleState.Active) =>
                previous.RecordVisibleChange(next.UpdatedBy, next.UpdatedAtUtc),
            (EntityLifecycleState.Active, EntityLifecycleState.Archived) =>
                previous.Archive(next.UpdatedBy, next.UpdatedAtUtc),
            (EntityLifecycleState.Archived, EntityLifecycleState.Active) =>
                previous.RestoreFromArchive(next.UpdatedBy, next.UpdatedAtUtc),
            (EntityLifecycleState.Active or EntityLifecycleState.Archived, EntityLifecycleState.Trashed) =>
                previous.MoveToTrash(next.UpdatedBy, next.UpdatedAtUtc),
            (EntityLifecycleState.Trashed, EntityLifecycleState.Active or EntityLifecycleState.Archived) =>
                previous.RestoreFromTrash(next.UpdatedBy, next.UpdatedAtUtc),
            _ => throw new InvalidOperationException("An archived or trashed product object is read-only until restored."),
        };
        if (expected != next)
        {
            throw new ArgumentException("The product object's immutable or lifecycle metadata was changed inconsistently.");
        }
    }
}

internal sealed record ProductColumn(string Name, NpgsqlDbType Type);

internal static class ProductStoreSql
{
    public static void Add(NpgsqlCommand command, NpgsqlDbType type, object? value) =>
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = type, Value = value ?? DBNull.Value });

    public static string? Text(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    public static T? Optional<T>(NpgsqlDataReader reader, int ordinal) where T : struct =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<T>(ordinal);

    public static SyncableEntityMetadata ReadMetadata(NpgsqlDataReader reader) =>
        SyncableEntityMetadata.Reconstitute(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetGuid(4), reader.GetFieldValue<DateTimeOffset>(5), checked((int)reader.GetInt64(6)),
            ParseLifecycle(reader.GetString(7)), Text(reader, 8) is { } before ? ParseLifecycle(before) : null,
            Optional<DateTimeOffset>(reader, 9), Optional<Guid>(reader, 10), Optional<DateTimeOffset>(reader, 11));

    public static string Lifecycle(EntityLifecycleState value) => value switch
    {
        EntityLifecycleState.Active => "active",
        EntityLifecycleState.Archived => "archived",
        EntityLifecycleState.Trashed => "trashed",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static EntityLifecycleState ParseLifecycle(string value) => value switch
    {
        "active" => EntityLifecycleState.Active,
        "archived" => EntityLifecycleState.Archived,
        "trashed" => EntityLifecycleState.Trashed,
        _ => throw new InvalidOperationException("Unknown persisted lifecycle state."),
    };

    public static string EnumValue<T>(T value, params string[] names) where T : struct, Enum
    {
        var index = Convert.ToInt32(value);
        if (!Enum.IsDefined(value) || index < 0 || index >= names.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Unknown product value.");
        }

        return names[index];
    }

    public static T ParseEnum<T>(string value, params string[] names) where T : struct, Enum
    {
        var index = Array.IndexOf(names, value);
        return index >= 0 ? (T)Enum.ToObject(typeof(T), index)
            : throw new InvalidOperationException("Unknown persisted product value.");
    }

    public static void Identifier(Guid value, string name)
    {
        if (value == Guid.Empty) throw new ArgumentException("Identifier must not be empty.", name);
    }

    public static void OptionalIdentifier(Guid? value, string name)
    {
        if (value is { } id) Identifier(id, name);
    }

    public static void RequiredText(string? value, int maximum, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum)
            throw new ArgumentException($"{name} must contain 1 to {maximum} characters.", name);
    }

    public static void OptionalText(string? value, int maximum, string name)
    {
        if (value?.Length > maximum) throw new ArgumentException($"{name} is too long.", name);
    }

    public static void Utc(DateTimeOffset? value, string name)
    {
        if (value.HasValue && value.Value.Offset != TimeSpan.Zero)
            throw new ArgumentException("Timestamp must use UTC.", name);
    }

    public static bool JsonEquivalent(string left, string right) =>
        System.Text.Json.Nodes.JsonNode.DeepEquals(
            System.Text.Json.Nodes.JsonNode.Parse(left), System.Text.Json.Nodes.JsonNode.Parse(right));

    public static void JsonObject(string value, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("JSON payload must be an object.", name);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("JSON payload is invalid.", name, exception);
        }
    }

    public static void ExpectedVersion(int actualVersion, int expectedVersion)
    {
        if (expectedVersion < 1) throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        if (actualVersion != checked(expectedVersion + 1))
            throw new ArgumentException("The saved version must be exactly one greater than the expected version.");
    }

    public static void Metadata(IProductEntitySnapshot entity) => ArgumentNullException.ThrowIfNull(entity.Metadata);
}
