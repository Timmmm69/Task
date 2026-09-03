using Npgsql;
using NpgsqlTypes;
using Task.Application.ProductData;

namespace Task.Infrastructure.Persistence;

internal sealed class PostgresProductSettingsStore : IProductSettingsStore
{
    private static readonly ProductColumn[] OrganizationColumns =
    [
        new("organization_id", NpgsqlDbType.Uuid), new("trash_retention_days", NpgsqlDbType.Integer),
        new("history_retention_days", NpgsqlDbType.Integer), new("change_feed_retention_days", NpgsqlDbType.Integer),
        new("recurrence_horizon_days", NpgsqlDbType.Integer), new("recurrence_min_instances", NpgsqlDbType.Integer),
        new("default_workday_start", NpgsqlDbType.Time), new("default_workday_end", NpgsqlDbType.Time),
        new("first_day_of_week", NpgsqlDbType.Smallint), new("max_request_bytes", NpgsqlDbType.Integer),
        new("updated_at", NpgsqlDbType.TimestampTz), new("version", NpgsqlDbType.Bigint),
    ];
    private static readonly ProductColumn[] UserColumns =
    [
        new("organization_id", NpgsqlDbType.Uuid), new("user_account_id", NpgsqlDbType.Uuid),
        new("language", NpgsqlDbType.Varchar), new("time_format", NpgsqlDbType.Varchar),
        new("first_day_of_week", NpgsqlDbType.Smallint), new("workday_start", NpgsqlDbType.Time),
        new("workday_end", NpgsqlDbType.Time), new("weekend_days", NpgsqlDbType.Array | NpgsqlDbType.Smallint),
        new("default_task_duration_minutes", NpgsqlDbType.Integer), new("default_reminder_offset_minutes", NpgsqlDbType.Integer),
        new("autostart_enabled", NpgsqlDbType.Boolean), new("allow_local_paths", NpgsqlDbType.Boolean),
        new("confirm_catalog_delete", NpgsqlDbType.Boolean), new("missing_file_behavior", NpgsqlDbType.Varchar),
        new("custom_preferences", NpgsqlDbType.Jsonb), new("updated_at", NpgsqlDbType.TimestampTz),
        new("version", NpgsqlDbType.Bigint),
    ];
    private static readonly ProductColumn[] PreferenceColumns =
    [
        new("organization_id", NpgsqlDbType.Uuid), new("user_account_id", NpgsqlDbType.Uuid),
        new("notification_type", NpgsqlDbType.Varchar), new("enabled", NpgsqlDbType.Boolean),
        new("desktop_enabled", NpgsqlDbType.Boolean), new("sound_enabled", NpgsqlDbType.Boolean),
        new("default_snooze_minutes", NpgsqlDbType.Integer), new("quiet_hours_start", NpgsqlDbType.Time),
        new("quiet_hours_end", NpgsqlDbType.Time), new("quiet_hours_time_zone", NpgsqlDbType.Text),
        new("updated_at", NpgsqlDbType.TimestampTz), new("version", NpgsqlDbType.Bigint),
    ];

    private readonly NpgsqlDataSource _dataSource;

    public PostgresProductSettingsStore(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    public OrganizationSettingsSnapshot? GetOrganization(Guid organizationId)
    {
        ProductStoreSql.Identifier(organizationId, nameof(organizationId));
        using var command = ReadCommand("core.organization_settings", OrganizationColumns, organizationId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new OrganizationSettingsSnapshot(
            reader.GetGuid(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4),
            reader.GetInt32(5), reader.GetFieldValue<TimeOnly>(6), reader.GetFieldValue<TimeOnly>(7),
            reader.GetInt16(8), reader.GetInt32(9), reader.GetFieldValue<DateTimeOffset>(10), checked((int)reader.GetInt64(11))) : null;
    }

    public void AddOrganization(OrganizationSettingsSnapshot settings) => WriteOrganization(settings, null);

    public void SaveOrganization(OrganizationSettingsSnapshot settings, int expectedVersion) => WriteOrganization(settings, expectedVersion);

    public UserSettingsSnapshot? GetUser(Guid userAccountId, Guid organizationId)
    {
        ProductStoreSql.Identifier(userAccountId, nameof(userAccountId));
        ProductStoreSql.Identifier(organizationId, nameof(organizationId));
        using var command = ReadCommand("org.user_settings", UserColumns, organizationId, userAccountId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new UserSettingsSnapshot(
            reader.GetGuid(1), reader.GetGuid(0), reader.GetString(2), reader.GetString(3), reader.GetInt16(4),
            reader.GetFieldValue<TimeOnly>(5), reader.GetFieldValue<TimeOnly>(6),
            Array.AsReadOnly(reader.GetFieldValue<short[]>(7)), reader.GetInt32(8), reader.GetInt32(9),
            reader.GetBoolean(10), reader.GetBoolean(11), reader.GetBoolean(12), reader.GetString(13),
            reader.GetString(14), reader.GetFieldValue<DateTimeOffset>(15), checked((int)reader.GetInt64(16))) : null;
    }

    public void AddUser(UserSettingsSnapshot settings) => WriteUser(settings, null);

    public void SaveUser(UserSettingsSnapshot settings, int expectedVersion) => WriteUser(settings, expectedVersion);

    public NotificationPreferenceSnapshot? GetNotificationPreference(
        Guid userAccountId, Guid organizationId, string notificationType)
    {
        ProductStoreSql.Identifier(userAccountId, nameof(userAccountId));
        ProductStoreSql.Identifier(organizationId, nameof(organizationId));
        ProductStoreSql.RequiredText(notificationType, 40, nameof(notificationType));
        using var command = ReadCommand("notify.notification_preferences", PreferenceColumns,
            organizationId, userAccountId, notificationType);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new NotificationPreferenceSnapshot(
            reader.GetGuid(1), reader.GetGuid(0), reader.GetString(2), reader.GetBoolean(3), reader.GetBoolean(4),
            reader.GetBoolean(5), reader.GetInt32(6), ProductStoreSql.Optional<TimeOnly>(reader, 7),
            ProductStoreSql.Optional<TimeOnly>(reader, 8), ProductStoreSql.Text(reader, 9),
            reader.GetFieldValue<DateTimeOffset>(10), checked((int)reader.GetInt64(11))) : null;
    }

    public void AddNotificationPreference(NotificationPreferenceSnapshot preference) => WritePreference(preference, null);

    public void SaveNotificationPreference(NotificationPreferenceSnapshot preference, int expectedVersion) =>
        WritePreference(preference, expectedVersion);

    private void WriteOrganization(OrganizationSettingsSnapshot settings, int? expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ProductStoreSql.Identifier(settings.OrganizationId, nameof(settings.OrganizationId));
        Range(settings.TrashRetentionDays, 1, 3650, nameof(settings.TrashRetentionDays));
        Range(settings.HistoryRetentionDays, 90, 36500, nameof(settings.HistoryRetentionDays));
        Range(settings.ChangeFeedRetentionDays, 7, 3650, nameof(settings.ChangeFeedRetentionDays));
        Range(settings.RecurrenceHorizonDays, 7, 730, nameof(settings.RecurrenceHorizonDays));
        Range(settings.RecurrenceMinInstances, 1, 500, nameof(settings.RecurrenceMinInstances));
        Range(settings.FirstDayOfWeek, 1, 7, nameof(settings.FirstDayOfWeek));
        Range(settings.MaxRequestBytes, 65536, 10485760, nameof(settings.MaxRequestBytes));
        if (settings.DefaultWorkdayEnd <= settings.DefaultWorkdayStart) throw new ArgumentException("Workday end must follow start.");
        ProductStoreSql.Utc(settings.UpdatedAtUtc, nameof(settings.UpdatedAtUtc));
        Write("core.organization_settings", "organization_settings", settings.OrganizationId, OrganizationColumns,
            [settings.OrganizationId, settings.TrashRetentionDays, settings.HistoryRetentionDays,
                settings.ChangeFeedRetentionDays, settings.RecurrenceHorizonDays, settings.RecurrenceMinInstances,
                settings.DefaultWorkdayStart, settings.DefaultWorkdayEnd, settings.FirstDayOfWeek,
                settings.MaxRequestBytes, settings.UpdatedAtUtc, (long)settings.Version],
            1, settings.Version, settings.UpdatedAtUtc, expectedVersion);
    }

    private void WriteUser(UserSettingsSnapshot settings, int? expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ProductStoreSql.Identifier(settings.OrganizationId, nameof(settings.OrganizationId));
        ProductStoreSql.Identifier(settings.UserAccountId, nameof(settings.UserAccountId));
        ProductStoreSql.RequiredText(settings.Language, 16, nameof(settings.Language));
        if (settings.Language.Length < 2) throw new ArgumentException("Language must contain at least two characters.");
        if (settings.TimeFormat is not ("12h" or "24h")) throw new ArgumentException("Unknown time format.");
        Range(settings.FirstDayOfWeek, 1, 7, nameof(settings.FirstDayOfWeek));
        if (settings.WorkdayEnd <= settings.WorkdayStart) throw new ArgumentException("Workday end must follow start.");
        ArgumentNullException.ThrowIfNull(settings.WeekendDays);
        var weekend = settings.WeekendDays.ToArray();
        if (weekend.Length is < 1 or > 6 || weekend.Distinct().Count() != weekend.Length || weekend.Any(day => day is < 1 or > 7))
            throw new ArgumentException("Weekend days must contain one to six distinct days numbered 1 through 7.");
        Range(settings.DefaultTaskDurationMinutes, 5, 1440, nameof(settings.DefaultTaskDurationMinutes));
        Range(settings.DefaultReminderOffsetMinutes, 0, 525600, nameof(settings.DefaultReminderOffsetMinutes));
        if (settings.MissingFileBehavior is not ("show_actions" or "keep_inactive" or "prompt_relink"))
            throw new ArgumentException("Unknown missing-file behavior.");
        ProductStoreSql.JsonObject(settings.CustomPreferencesJson, nameof(settings.CustomPreferencesJson));
        ProductStoreSql.Utc(settings.UpdatedAtUtc, nameof(settings.UpdatedAtUtc));
        Write("org.user_settings", "user_settings", settings.UserAccountId, UserColumns,
            [settings.OrganizationId, settings.UserAccountId, settings.Language, settings.TimeFormat,
                settings.FirstDayOfWeek, settings.WorkdayStart, settings.WorkdayEnd, weekend,
                settings.DefaultTaskDurationMinutes, settings.DefaultReminderOffsetMinutes, settings.AutostartEnabled,
                settings.AllowLocalPaths, settings.ConfirmCatalogDelete, settings.MissingFileBehavior,
                settings.CustomPreferencesJson, settings.UpdatedAtUtc, (long)settings.Version],
            2, settings.Version, settings.UpdatedAtUtc, expectedVersion);
    }

    private void WritePreference(NotificationPreferenceSnapshot preference, int? expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(preference);
        ProductStoreSql.Identifier(preference.OrganizationId, nameof(preference.OrganizationId));
        ProductStoreSql.Identifier(preference.UserAccountId, nameof(preference.UserAccountId));
        ProductStoreSql.RequiredText(preference.NotificationType, 40, nameof(preference.NotificationType));
        Range(preference.DefaultSnoozeMinutes, 1, 10080, nameof(preference.DefaultSnoozeMinutes));
        var hasQuietHours = preference.QuietHoursStart is not null && preference.QuietHoursEnd is not null &&
            preference.QuietHoursTimeZone is not null;
        var noQuietHours = preference.QuietHoursStart is null && preference.QuietHoursEnd is null &&
            preference.QuietHoursTimeZone is null;
        if (!hasQuietHours && !noQuietHours) throw new ArgumentException("Quiet hours require both times and a timezone.");
        if (preference.QuietHoursTimeZone is not null)
            ProductStoreSql.RequiredText(preference.QuietHoursTimeZone, 64, nameof(preference.QuietHoursTimeZone));
        ProductStoreSql.Utc(preference.UpdatedAtUtc, nameof(preference.UpdatedAtUtc));
        Write("notify.notification_preferences", "notification_preference", preference.UserAccountId, PreferenceColumns,
            [preference.OrganizationId, preference.UserAccountId, preference.NotificationType, preference.Enabled,
                preference.DesktopEnabled, preference.SoundEnabled, preference.DefaultSnoozeMinutes,
                preference.QuietHoursStart, preference.QuietHoursEnd, preference.QuietHoursTimeZone,
                preference.UpdatedAtUtc, (long)preference.Version],
            3, preference.Version, preference.UpdatedAtUtc, expectedVersion);
    }

    private NpgsqlCommand ReadCommand(string table, ProductColumn[] columns, params object[] keys)
    {
        var select = string.Join(", ", columns.Select(column => column.Name));
        var predicate = Predicate(columns, keys.Length);
        var command = _dataSource.CreateCommand($"SELECT {select} FROM {table} WHERE {predicate};");
        for (var index = 0; index < keys.Length; index++) ProductStoreSql.Add(command, columns[index].Type, keys[index]);
        return command;
    }

    private void Write(
        string table, string entityType, Guid entityId, ProductColumn[] columns, object?[] values,
        int keyCount, int version, DateTimeOffset updatedAtUtc, int? expectedVersion)
    {
        if (expectedVersion is { } expected) ProductStoreSql.ExpectedVersion(version, expected);
        else if (version != 1) throw new ArgumentException("New settings must start at version 1.");
        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();
        if (expectedVersion is { } currentVersion)
        {
            using var check = new NpgsqlCommand(
                $"SELECT version, updated_at FROM {table} WHERE {Predicate(columns, keyCount)} FOR UPDATE;",
                connection, transaction);
            for (var index = 0; index < keyCount; index++) ProductStoreSql.Add(check, columns[index].Type, values[index]);
            using var reader = check.ExecuteReader();
            if (!reader.Read()) throw new ProductEntityConcurrencyException(entityType, entityId, currentVersion, null);
            var actualVersion = checked((int)reader.GetInt64(0));
            if (actualVersion != currentVersion)
                throw new ProductEntityConcurrencyException(entityType, entityId, currentVersion, actualVersion);
            if (updatedAtUtc < reader.GetFieldValue<DateTimeOffset>(1))
                throw new ArgumentException("Settings update timestamp cannot precede the previous update.");
        }

        var sql = expectedVersion is null
            ? $"INSERT INTO {table} ({string.Join(", ", columns.Select(column => column.Name))}) VALUES ({string.Join(", ", Enumerable.Range(1, columns.Length).Select(index => $"${index}"))});"
            : $"UPDATE {table} SET {string.Join(", ", columns.Skip(keyCount).Select((column, index) => $"{column.Name} = ${index + keyCount + 1}"))} WHERE {Predicate(columns, keyCount)};";
        using var command = new NpgsqlCommand(sql, connection, transaction);
        for (var index = 0; index < columns.Length; index++) ProductStoreSql.Add(command, columns[index].Type, values[index]);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("The settings projection is missing.");
        transaction.Commit();
    }

    private static string Predicate(ProductColumn[] columns, int count) =>
        string.Join(" AND ", columns.Take(count).Select((column, index) => $"{column.Name} = ${index + 1}"));

    private static void Range(int value, int minimum, int maximum, string name)
    {
        if (value < minimum || value > maximum) throw new ArgumentOutOfRangeException(name);
    }
}
