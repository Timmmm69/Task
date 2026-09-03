using Npgsql;
using NpgsqlTypes;
using Task.Application.ProductData;
using Task.Domain;

namespace Task.Infrastructure.Persistence;

internal sealed class PostgresProjectStore : PostgresProductObjectStore<ProjectSnapshot>, IProjectStore
{
    private static readonly string[] StatusNames = ["planning", "active", "paused", "completed"];

    public PostgresProjectStore(NpgsqlDataSource dataSource)
        : base(dataSource, "project", "projects.projects",
            new("name", NpgsqlDbType.Text), new("description", NpgsqlDbType.Text),
            new("owner_user_id", NpgsqlDbType.Uuid), new("manager_user_id", NpgsqlDbType.Uuid),
            new("status", NpgsqlDbType.Varchar), new("start_date", NpgsqlDbType.Date),
            new("planned_end_date", NpgsqlDbType.Date), new("actual_end_at", NpgsqlDbType.TimestampTz),
            new("default_time_zone", NpgsqlDbType.Text), new("color_code", NpgsqlDbType.Varchar))
    { }

    protected override ProjectSnapshot Hydrate(SyncableEntityMetadata metadata, NpgsqlDataReader reader) =>
        new(metadata, reader.GetString(12), ProductStoreSql.Text(reader, 13), reader.GetGuid(14),
            ProductStoreSql.Optional<Guid>(reader, 15),
            ProductStoreSql.ParseEnum<ProjectStatus>(reader.GetString(16), StatusNames),
            ProductStoreSql.Optional<DateOnly>(reader, 17), ProductStoreSql.Optional<DateOnly>(reader, 18),
            ProductStoreSql.Optional<DateTimeOffset>(reader, 19), ProductStoreSql.Text(reader, 20),
            ProductStoreSql.Text(reader, 21));

    protected override object?[] Values(ProjectSnapshot entity) =>
        [entity.Name, entity.Description, entity.OwnerUserId, entity.ManagerUserId,
            ProductStoreSql.EnumValue(entity.Status, StatusNames), entity.StartDate, entity.PlannedEndDate,
            entity.ActualEndAtUtc, entity.DefaultTimeZone, entity.ColorCode];

    protected override void Validate(ProjectSnapshot entity)
    {
        ProductStoreSql.Metadata(entity);
        ProductStoreSql.RequiredText(entity.Name, 300, nameof(entity.Name));
        ProductStoreSql.OptionalText(entity.Description, 20000, nameof(entity.Description));
        ProductStoreSql.Identifier(entity.OwnerUserId, nameof(entity.OwnerUserId));
        ProductStoreSql.OptionalIdentifier(entity.ManagerUserId, nameof(entity.ManagerUserId));
        _ = ProductStoreSql.EnumValue(entity.Status, StatusNames);
        if (entity.StartDate is { } start && entity.PlannedEndDate is { } end && end < start)
            throw new ArgumentException("The planned project end cannot precede its start.");
        if (entity.Status == ProjectStatus.Completed && entity.ActualEndAtUtc is null)
            throw new ArgumentException("A completed project requires its actual end timestamp.");
        ProductStoreSql.Utc(entity.ActualEndAtUtc, nameof(entity.ActualEndAtUtc));
        if (entity.DefaultTimeZone is not null)
            ProductStoreSql.RequiredText(entity.DefaultTimeZone, 64, nameof(entity.DefaultTimeZone));
        if (entity.ColorCode is not null && !System.Text.RegularExpressions.Regex.IsMatch(
            entity.ColorCode, "^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$"))
            throw new ArgumentException("Project color must be a six- or eight-digit hexadecimal color.");
    }

    protected override bool SameContent(ProjectSnapshot left, ProjectSnapshot right) =>
        left with { Metadata = right.Metadata } == right;

    protected override void BeforeWrite(NpgsqlConnection connection, NpgsqlTransaction transaction, ProjectSnapshot entity)
    {
        using var command = new NpgsqlCommand(
            """
            SELECT a.id FROM iam.user_accounts a
            JOIN core.objects o ON o.organization_id = a.organization_id AND o.id = a.id
            WHERE a.organization_id = $1 AND a.id = $2 AND a.account_status = 'active'
                AND o.lifecycle_state = 'active'
            FOR SHARE OF a, o;
            """, connection, transaction);
        ProductStoreSql.Add(command, NpgsqlDbType.Uuid, entity.Metadata.OrganizationId);
        ProductStoreSql.Add(command, NpgsqlDbType.Uuid, entity.OwnerUserId);
        if (command.ExecuteScalar() is null)
            throw new InvalidOperationException("The project owner must be an active account in the same organization.");
    }
}
