using System.Globalization;
using Task.Desktop.TaskApi;

namespace Task.Desktop.ViewModels;

public enum TaskSortOrder
{
    UpdatedNewest, UpdatedOldest, DeadlineNearest, DeadlineLatest,
    PriorityHighest, PriorityLowest, TitleAscending, TitleDescending,
}

internal static class TaskSorting
{
    // Task's current UI language is Russian. Do not let the OS language change title order.
    private static readonly StringComparer TitleComparer = StringComparer.Create(CultureInfo.GetCultureInfo("ru-RU"), ignoreCase: true);

    public static IEnumerable<DesktopTaskDto> Apply(IEnumerable<DesktopTaskDto> tasks, TaskSortOrder order, TimeZoneInfo userZone)
    {
        IOrderedEnumerable<DesktopTaskDto> sorted = order switch
        {
            TaskSortOrder.DeadlineNearest => tasks.OrderBy(task => Deadline(task, userZone) is null)
                .ThenBy(task => Deadline(task, userZone)),
            TaskSortOrder.DeadlineLatest => tasks.OrderBy(task => Deadline(task, userZone) is null)
                .ThenByDescending(task => Deadline(task, userZone)),
            TaskSortOrder.PriorityHighest => tasks.OrderBy(task => PriorityRank(task.Priority)),
            TaskSortOrder.PriorityLowest => tasks.OrderByDescending(task => PriorityRank(task.Priority)),
            TaskSortOrder.TitleAscending => tasks.OrderBy(task => task.Title ?? string.Empty, TitleComparer),
            TaskSortOrder.TitleDescending => tasks.OrderByDescending(task => task.Title ?? string.Empty, TitleComparer),
            TaskSortOrder.UpdatedOldest => tasks.OrderBy(task => task.UpdatedAtUtc is null).ThenBy(task => task.UpdatedAtUtc),
            _ => tasks.OrderBy(task => task.UpdatedAtUtc is null).ThenByDescending(task => task.UpdatedAtUtc),
        };
        return sorted.ThenBy(task => task.Id);
    }

    private static int PriorityRank(DesktopTaskPriority priority) => priority switch
    {
        DesktopTaskPriority.Critical => 0,
        DesktopTaskPriority.High => 1,
        DesktopTaskPriority.Normal => 2,
        DesktopTaskPriority.Low => 3,
        _ => 4,
    };

    private static (int Day, long Instant)? Deadline(DesktopTaskDto task, TimeZoneInfo userZone)
    {
        var day = TaskDeadlineFiltering.CalendarDate(task, userZone);
        // Compare calendar dates first, using the same semantics as filtering. Within a day,
        // exact instants keep UTC order (including a DST fold); a date-only deadline is due
        // through that entire day and follows timed deadlines. Never convert DateOnly to UTC.
        return day is null ? null : (day.Value.DayNumber, task.DeadlineAtUtc?.UtcTicks ?? long.MaxValue);
    }
}
