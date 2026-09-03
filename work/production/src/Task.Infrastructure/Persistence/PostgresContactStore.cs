using Npgsql;
using NpgsqlTypes;
using Task.Application.ProductData;
using Task.Domain;

namespace Task.Infrastructure.Persistence;

internal sealed class PostgresContactStore : PostgresProductObjectStore<ContactSnapshot>, IContactStore
{
    private static readonly string[] StatusNames = ["active", "inactive"];

    public PostgresContactStore(NpgsqlDataSource dataSource)
        : base(dataSource, "contact", "crm.contacts",
            new("first_name", NpgsqlDbType.Text), new("last_name", NpgsqlDbType.Text),
            new("middle_name", NpgsqlDbType.Text), new("display_name", NpgsqlDbType.Text),
            new("notes", NpgsqlDbType.Text), new("status", NpgsqlDbType.Varchar))
    { }

    protected override ContactSnapshot Hydrate(SyncableEntityMetadata metadata, NpgsqlDataReader reader) =>
        new(metadata, reader.GetString(12), ProductStoreSql.Text(reader, 13), ProductStoreSql.Text(reader, 14),
            reader.GetString(15), ProductStoreSql.Text(reader, 16),
            ProductStoreSql.ParseEnum<ContactStatus>(reader.GetString(17), StatusNames));

    protected override object?[] Values(ContactSnapshot entity) =>
        [entity.FirstName, entity.LastName, entity.MiddleName, entity.DisplayName, entity.Notes,
            ProductStoreSql.EnumValue(entity.Status, StatusNames)];

    protected override void Validate(ContactSnapshot entity)
    {
        ProductStoreSql.Metadata(entity);
        ProductStoreSql.RequiredText(entity.FirstName, 100, nameof(entity.FirstName));
        ProductStoreSql.OptionalText(entity.LastName, 100, nameof(entity.LastName));
        ProductStoreSql.OptionalText(entity.MiddleName, 100, nameof(entity.MiddleName));
        ProductStoreSql.RequiredText(entity.DisplayName, 300, nameof(entity.DisplayName));
        ProductStoreSql.OptionalText(entity.Notes, 20000, nameof(entity.Notes));
        _ = ProductStoreSql.EnumValue(entity.Status, StatusNames);
    }

    protected override bool SameContent(ContactSnapshot left, ContactSnapshot right) =>
        left with { Metadata = right.Metadata } == right;
}
