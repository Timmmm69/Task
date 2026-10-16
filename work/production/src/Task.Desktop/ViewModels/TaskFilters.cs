using Task.Desktop.TaskApi;

namespace Task.Desktop.ViewModels;

public sealed record TaskFilterOption<T>(T Value, string Name);

public sealed record TaskProjectFilter(Guid? ProjectId, string Name, bool IsAll = false)
{
    public static TaskProjectFilter AllProjects { get; } = new(null, "Все проекты", true);
}

public enum TaskDeadlineFilter { All, Overdue, Today, NextSevenDays, NoDeadline }

internal static class TaskDeadlineFiltering
{
    internal static DateOnly? CalendarDate(DesktopTaskDto task, TimeZoneInfo userZone) =>
        task.DeadlineAtUtc is { } instant
            ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, userZone).DateTime)
            : task.Card?.DeadlineDate;

    public static bool Matches(DesktopTaskDto task, TaskDeadlineFilter filter, DateTimeOffset now, TimeZoneInfo userZone)
    {
        if (filter == TaskDeadlineFilter.All) return true;
        if (filter == TaskDeadlineFilter.Overdue)
            return task.Status is not DesktopTaskStatus.Completed and not DesktopTaskStatus.Cancelled
                && Task.Domain.TaskRules.IsOverdue(task.DeadlineAtUtc, task.Card, now);

        // The domain gives the exact instant precedence. A date-only deadline is already
        // a calendar date and must not be shifted through UTC or the machine's offset.
        var day = CalendarDate(task, userZone);
        if (filter == TaskDeadlineFilter.NoDeadline) return day is null;
        if (day is null) return false;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, userZone).DateTime);
        return filter switch
        {
            TaskDeadlineFilter.Today => day == today,
            // Seven calendar dates, including today and the sixth following date.
            TaskDeadlineFilter.NextSevenDays => day >= today && day.Value.DayNumber - today.DayNumber <= 6,
            _ => false,
        };
    }
}
