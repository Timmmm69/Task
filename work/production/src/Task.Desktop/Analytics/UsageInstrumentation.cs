using System.Diagnostics;
using Task.Desktop.TaskApi;

namespace Task.Desktop.Analytics;

internal static class UsageInstrumentation
{
    public static void Record(Action<UsageEvent>? sink, UsageEvent usage)
    {
        try { sink?.Invoke(usage); }
        catch (Exception error) { Trace.TraceWarning("Task analytics event skipped ({0}).", error.GetType().Name); }
    }

    public static bool IsPlanningField(string key) => key is "startAtUtc" or "deadlineAt" or "scheduledDate"
        or "startTimeLocal" or "scheduleTimeZone" or "plannedDurationMinutes" or "deadlineDate" or "deadlineTimeZone";

    public static void RecordPatchedTask(Action<UsageEvent>? sink, DesktopTaskDto before, DesktopTaskDto after)
    {
        if (sink is null) return;
        var old = before.Card;
        var updated = after.Card;
        if (before.StartAtUtc != after.StartAtUtc || before.DeadlineAtUtc != after.DeadlineAtUtc
            || old?.ScheduledDate != updated?.ScheduledDate || old?.StartTimeLocal != updated?.StartTimeLocal
            || old?.ScheduleTimeZone != updated?.ScheduleTimeZone || old?.PlannedDurationMinutes != updated?.PlannedDurationMinutes
            || old?.DeadlineDate != updated?.DeadlineDate || old?.DeadlineTimeZone != updated?.DeadlineTimeZone)
            Record(sink, UsageEvent.TaskPlanned);
        else if (before.Title != after.Title || before.Priority != after.Priority || old?.Description != updated?.Description
            || old?.ProjectId != updated?.ProjectId || old?.ParentTaskId != updated?.ParentTaskId
            || old?.PrimaryCounterpartyObjectId != updated?.PrimaryCounterpartyObjectId)
            Record(sink, UsageEvent.TaskUpdated);
    }
}
