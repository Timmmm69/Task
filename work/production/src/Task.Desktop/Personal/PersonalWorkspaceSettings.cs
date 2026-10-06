using System.Text.Json;
using Task.Desktop.Work;

namespace Task.Desktop.Personal;

public sealed partial class PersonalTaskStore
{
    private static readonly DesktopUserSettings DefaultWorkspaceSettings = new(1, "ru-RU", "24h", 1, "09:00:00", "18:00:00", [6, 7], 60, 15, false, true, true, "show_actions");
    private static readonly DesktopNotificationPreferences DefaultWorkspaceNotifications = new(1, true, true, true, 15, null, null, null);
    private T ReadWorkspaceSettings<T>(string key, T defaults, Microsoft.Data.Sqlite.SqliteTransaction? tx = null)
    {
        using var c = Command("SELECT payload FROM personal_workspace_settings WHERE key=$key;", tx, ("$key", key));
        return c.ExecuteScalar() is string json ? JsonSerializer.Deserialize<T>(json) ?? throw new InvalidOperationException("Invalid Personal settings") : defaults;
    }
    public DesktopUserSettings WorkspaceSettings() => Locked(() => ReadWorkspaceSettings("workspace", DefaultWorkspaceSettings));
    public DesktopNotificationPreferences WorkspaceNotificationPreferences() => Locked(() => ReadWorkspaceSettings("notifications", DefaultWorkspaceNotifications));
    private void WriteWorkspaceSettings<T>(string key, long expected, T defaults, Func<T, long> version, T updated)
    {
        using var tx = _database.Connection.BeginTransaction();
        if (version(ReadWorkspaceSettings(key, defaults, tx)) != expected) throw new PersonalVersionConflictException();
        using var c = Command("INSERT INTO personal_workspace_settings VALUES($key,$version,$payload) ON CONFLICT(key) DO UPDATE SET version=$version,payload=$payload;", tx,
            ("$key", key), ("$version", expected + 1), ("$payload", JsonSerializer.Serialize(updated)));
        c.ExecuteNonQuery(); tx.Commit();
    }
    public DesktopUserSettings SaveWorkspaceSettings(DesktopUserSettings settings) => Locked(() =>
    {
        if (settings.FirstDayOfWeek is < 1 or > 7 || settings.WeekendDays.Any(d => d is < 1 or > 7) || settings.WeekendDays.Distinct().Count() != settings.WeekendDays.Count
            || !TimeOnly.TryParse(settings.WorkdayStart, out var start) || !TimeOnly.TryParse(settings.WorkdayEnd, out var end) || end <= start
            || settings.DefaultTaskDurationMinutes is < 5 or > 1440 || settings.DefaultReminderOffsetMinutes is < 0 or > 525600
            || settings.TimeFormat is not ("12h" or "24h") || settings.Language.Length is < 2 or > 16
            || settings.MissingFileBehavior is not ("show_actions" or "keep_inactive" or "prompt_relink") || settings.AutostartEnabled)
            throw new ArgumentException("Проверьте параметры Personal. Автозапуск на этом этапе недоступен.");
        var saved = settings with { Version = settings.Version + 1 };
        WriteWorkspaceSettings("workspace", settings.Version, DefaultWorkspaceSettings, s => s.Version, saved); return saved;
    });
    public DesktopNotificationPreferences SaveWorkspaceNotifications(DesktopNotificationPreferences preferences) => Locked(() =>
    {
        if (preferences.DefaultSnoozeMinutes is < 1 or > 10080 || (preferences.QuietHoursStart is null) != (preferences.QuietHoursEnd is null)
            || preferences.QuietHoursStart is not null && (!TimeOnly.TryParse(preferences.QuietHoursStart, out _) || !TimeOnly.TryParse(preferences.QuietHoursEnd, out _)
                || preferences.QuietHoursTimeZone is null || !TimeZoneInfo.TryFindSystemTimeZoneById(preferences.QuietHoursTimeZone, out _)))
            throw new ArgumentException("Проверьте параметры уведомлений.");
        var saved = preferences with { Version = preferences.Version + 1 };
        WriteWorkspaceSettings("notifications", preferences.Version, DefaultWorkspaceNotifications, s => s.Version, saved); return saved;
    });
    internal bool CanPresentPersonalNotification(Microsoft.Data.Sqlite.SqliteTransaction tx)
    {
        var preferences = ReadWorkspaceSettings("notifications", DefaultWorkspaceNotifications, tx);
        if (!preferences.Enabled || !preferences.DesktopEnabled) return false;
        if (preferences.QuietHoursStart is not { } start || preferences.QuietHoursEnd is not { } end) return true;
        var local = TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(preferences.QuietHoursTimeZone!));
        var time = TimeOnly.FromDateTime(local.DateTime); var from = TimeOnly.Parse(start); var to = TimeOnly.Parse(end);
        return !(from <= to ? time >= from && time < to : time >= from || time < to);
    }
}
