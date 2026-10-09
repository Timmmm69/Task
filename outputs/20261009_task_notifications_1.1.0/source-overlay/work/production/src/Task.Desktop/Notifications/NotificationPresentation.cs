using Task.Desktop.Work;

namespace Task.Desktop.Notifications;

public sealed record WindowsNotification(Guid Id, string Title, string Body, DateTimeOffset EventAt,
    bool Sound, bool CanSnooze = false);
public interface IWindowsNotificationPresenter
{
    bool Submit(WindowsNotification notification);
    void Clear();
    void Reconcile(IReadOnlySet<Guid> availableUnreadIds) { }
}

public static class NotificationPresentationPolicy
{
    public static bool Allows(DesktopNotificationPreferences preferences, DateTimeOffset now)
    {
        if (!preferences.Enabled || !preferences.DesktopEnabled) return false;
        if (preferences.QuietHoursStart is null && preferences.QuietHoursEnd is null) return true;
        if (!TimeOnly.TryParse(preferences.QuietHoursStart, out var start)
            || !TimeOnly.TryParse(preferences.QuietHoursEnd, out var end)
            || preferences.QuietHoursTimeZone is null
            || !TimeZoneInfo.TryFindSystemTimeZoneById(preferences.QuietHoursTimeZone, out var zone)) return false;
        var time = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        return !(start == end || (start < end ? time >= start && time < end : time >= start || time < end));
    }
}
