using System.Text.Json;
using Task.Application;
using Task.Desktop.TaskApi;

namespace Task.Desktop.Personal;

internal static class PersonalDayPlan
{
    internal static bool Includes(DesktopTaskDto task, DateOnly day, TimeZoneInfo zone)
    {
        DateOnly Local(DateTimeOffset value) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, zone).DateTime);
        return task.Card?.ScheduledDate == day || task.StartAtUtc is { } start && Local(start) == day
            || task.DeadlineAtUtc is { } deadline && Local(deadline) <= day
               && !(task.CompletedAtUtc is { } done && Local(done) < day && Local(deadline) < day);
    }
}

public sealed partial class PersonalTaskStore
{
    internal WorkdayResult? ReadDay(TaskTodayWindow day, DateTimeOffset cutoff, TimeZoneInfo zone) => Locked<WorkdayResult?>(() =>
    {
        var tasks = new List<DesktopTaskDto>();
        using (var command = Command($"""
            SELECT {Columns} FROM tasks WHERE julianday(created_at) <= julianday($cutoff)
            AND (archived_at IS NULL OR julianday(archived_at) > julianday($cutoff))
            AND (trashed_at IS NULL OR julianday(trashed_at) > julianday($cutoff))
            """, null, ("$cutoff", Instant(cutoff))))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) tasks.Add(Read(reader));

        var changed = tasks.Where(t => t.UpdatedAtUtc > cutoff).Select(t => t.Id).ToHashSet();
        var revisions = new Dictionary<Guid, DesktopTaskDto>();
        if (changed.Count > 0)
        {
            // Create and patch responses include the planning fields as saved in that revision.
            using var command = Command("""
                SELECT result FROM personal_task_commands WHERE
                    julianday(json_extract(result,'$.UpdatedAtUtc')) <= julianday($cutoff)
                ORDER BY json_extract(result,'$.Version')
                """, null, ("$cutoff", Instant(cutoff)));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var old = JsonSerializer.Deserialize<DesktopTaskDto>(reader.GetString(0));
                if (old is not null && changed.Contains(old.Id) && old.UpdatedAtUtc <= cutoff) revisions[old.Id] = old;
            }
        }

        int completed = 0, planned = 0;
        foreach (var task in tasks)
        {
            var revision = changed.Contains(task.Id) ? revisions.GetValueOrDefault(task.Id) : task;
            if (revision is null) return null;
            // Terminal transitions are immutable. Old stores also record the completion instant on the task itself.
            if (task.Status == DesktopTaskStatus.Cancelled && task.UpdatedAtUtc <= cutoff) continue;
            var finished = task.CompletedAtUtc is { } at && at <= cutoff;
            if (!PersonalDayPlan.Includes(revision with { CompletedAtUtc = finished ? task.CompletedAtUtc : null }, day.Date, zone)) continue;
            if (task.Status == DesktopTaskStatus.Completed && task.CompletedAtUtc is null) return null;
            planned++;
            if (finished) completed++;
        }
        return new(completed, planned);
    });
}
