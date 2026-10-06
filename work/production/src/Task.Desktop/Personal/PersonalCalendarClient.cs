using System.Text.Json;
using Microsoft.Data.Sqlite;
using Task.Desktop.Calendar;
using Task.Desktop.TaskApi;
using Task.Domain.Calendar;

namespace Task.Desktop.Personal;

public sealed partial class PersonalTaskStore
{
    private static readonly JsonSerializerOptions PlanningJson = new(JsonSerializerDefaults.Web);
    private DesktopCalendarEvent? Event(Guid id, SqliteTransaction? tx)
    {
        using var c = Command("SELECT payload FROM personal_events WHERE id=$id;", tx, ("$id", id.ToString("D")));
        return c.ExecuteScalar() is string json ? JsonSerializer.Deserialize<DesktopCalendarEvent>(json, PlanningJson) : null;
    }
    public DesktopCalendarEvent? GetEvent(Guid id) => Locked(() => Event(id, null));
    public IReadOnlyList<DesktopCalendarEvent> Events() => Locked(() =>
    {
        using var c = Command("SELECT payload FROM personal_events ORDER BY id;"); using var r = c.ExecuteReader();
        var events = new List<DesktopCalendarEvent>();
        while (r.Read()) events.Add(JsonSerializer.Deserialize<DesktopCalendarEvent>(r.GetString(0), PlanningJson)!);
        return (IReadOnlyList<DesktopCalendarEvent>)events;
    });
    public DesktopCalendarEvent SaveEvent(Guid? id, long? version, DesktopCalendarEventCommand draft) => Locked(() =>
    {
        var timing = CalendarEventTiming.Create(draft.EventDate, draft.IsAllDay, draft.StartAtUtc, draft.EndAtUtc, draft.TimeZoneId);
        if (string.IsNullOrWhiteSpace(draft.Title) || draft.Title.Trim().Length > 500 || draft.Description?.Length > 10000
            || draft.Status is not ("scheduled" or "cancelled") || draft.Attendees?.Count > 0)
            throw new ArgumentException("Проверьте событие. Участники компании недоступны в Personal.");
        using var tx = _database.Connection.BeginTransaction(); ValidateProjectLink(draft.ProjectId, tx);
        var old = id is { } eventId ? Event(eventId, tx) ?? throw new PersonalTaskNotFoundException() : null;
        if (old is not null && old.Version != version) throw new PersonalVersionConflictException();
        var now = _clock.GetUtcNow(); var next = (old?.Version ?? 0) + 1;
        var saved = new DesktopCalendarEvent(old?.Id ?? Guid.NewGuid(), Guid.Empty, next, old?.CreatedAtUtc ?? now, now,
            draft.ProjectId, draft.Title.Trim(), draft.Description, timing.EventDate, timing.IsAllDay, timing.StartAtUtc,
            timing.EndAtUtc, timing.TimeZoneId, draft.Status, [], $"\"v{next}\"");
        using var c = Command("""
            INSERT INTO personal_events VALUES ($id,$version,$payload,$project)
            ON CONFLICT(id) DO UPDATE SET version=$version,payload=$payload,project_id=$project;
            """, tx, ("$id", saved.Id.ToString("D")), ("$version", saved.Version),
            ("$payload", JsonSerializer.Serialize(saved, PlanningJson)), ("$project", saved.ProjectId?.ToString("D")));
        c.ExecuteNonQuery(); tx.Commit(); return saved;
    });
    public IReadOnlyList<DesktopScheduleItem> Schedule(DateTimeOffset from, DateTimeOffset to, string zoneId) => Locked(() =>
    {
        if (to <= from || (to - from).TotalDays > 370) throw new ArgumentException("Invalid calendar range.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var first = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(from, zone).DateTime);
        var last = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(to.AddTicks(-1), zone).DateTime);
        var items = new List<DesktopScheduleItem>();
        foreach (var task in List())
        {
            var card = task.Card;
            var start = task.StartAtUtc;
            var date = start is { } instant ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime) : card?.ScheduledDate;
            // A deadline is not a task start. Date-only tasks keep their calendar date.
            if (date is null) continue;
            var end = start is { } s && card?.PlannedDurationMinutes is { } minutes ? s.AddMinutes(minutes) : (DateTimeOffset?)null;
            if (start is { } at ? at >= to || (end ?? at) < from : date < first || date > last) continue;
            using var relation = Command("SELECT series_id FROM personal_occurrences WHERE task_id=$task;", null, ("$task", task.Id.ToString("D")));
            var series = relation.ExecuteScalar() is string v ? Guid.Parse(v) : (Guid?)null;
            items.Add(new(task.Id, DesktopScheduleItemType.Task, task.Title, date.Value, start, end, start is null,
                card?.ProjectId, TaskStatusCode(task.Status), (DesktopCalendarPriority)task.Priority, series, card?.Description));
        }
        foreach (var e in Events())
        {
            if (e.IsAllDay ? e.EventDate < first || e.EventDate > last : e.StartAtUtc >= to || e.EndAtUtc <= from) continue;
            items.Add(new(e.Id, DesktopScheduleItemType.CalendarEvent, e.Title, e.EventDate, e.StartAtUtc, e.EndAtUtc,
                e.IsAllDay, e.ProjectId, e.Status, null, Description: e.Description));
        }
        return (IReadOnlyList<DesktopScheduleItem>)items.OrderBy(i => i.LocalDate).ThenBy(i => i.StartAtUtc).ThenBy(i => i.ObjectId).ToArray();
    });
    internal static string TaskStatusCode(DesktopTaskStatus status) => status switch
    { DesktopTaskStatus.New => "new", DesktopTaskStatus.InProgress => "in_progress", DesktopTaskStatus.Review => "review", DesktopTaskStatus.Completed => "completed", _ => "cancelled" };
    public IReadOnlyList<DesktopScheduleConflict> Conflicts(DateTimeOffset from, DateTimeOffset to)
    {
        var timed = Schedule(from, to, "UTC").Where(i => !i.IsAllDay && i.StartAtUtc.HasValue && i.Status is not ("completed" or "cancelled")).ToArray();
        var conflicts = new List<DesktopScheduleConflict>();
        for (var i = 0; i < timed.Length; i++)
            for (var j = i + 1; j < timed.Length; j++)
            {
                var left = timed[i]; var right = timed[j];
                var overlap = CalendarOverlapPolicy.Evaluate(CalendarTimelinePlacement.Timeline(left.StartAtUtc!.Value, left.EndAtUtc),
                    CalendarTimelinePlacement.Timeline(right.StartAtUtc!.Value, right.EndAtUtc));
                if (overlap.HasOverlap) conflicts.Add(new(left.ObjectId, right.ObjectId, overlap.OverlapStartUtc!.Value,
                    overlap.OverlapEndUtc ?? to, DesktopConflictSeverity.Warning));
            }
        return conflicts;
    }
}

public sealed class PersonalCalendarClient(PersonalTaskStore store) : IDesktopCalendarApiClient
{
    internal static System.Threading.Tasks.Task<DesktopCalendarResult<T>> Execute<T>(Func<T> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); DesktopCalendarResult<T> result;
        try { result = new DesktopCalendarResult<T>.Succeeded(action()); }
        catch (PersonalVersionConflictException) { result = new DesktopCalendarResult<T>.VersionConflict(); }
        catch (PersonalTaskNotFoundException) { result = new DesktopCalendarResult<T>.NotFound(); }
        catch (Exception e) when (e is ArgumentException or FormatException or TimeZoneNotFoundException or InvalidTimeZoneException)
        { result = new DesktopCalendarResult<T>.ValidationFailure(e.Message); }
        catch (Exception e) when (e is SqliteException or System.IO.IOException or UnauthorizedAccessException)
        { result = new DesktopCalendarResult<T>.ValidationFailure("Данные не сохранены. Черновик остался в форме; проверьте свободное место и доступ к Personal DB."); }
        return System.Threading.Tasks.Task.FromResult(result);
    }
    public System.Threading.Tasks.Task<DesktopCalendarResult<DesktopSchedulePage>> GetScheduleAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, string timeZoneId, CancellationToken cancellationToken) =>
        Execute(() => new DesktopSchedulePage(store.Schedule(fromUtc, toUtc, timeZoneId), fromUtc, toUtc), cancellationToken);
    public System.Threading.Tasks.Task<DesktopCalendarResult<DesktopCalendarEvent>> GetEventAsync(Guid eventId, CancellationToken cancellationToken) =>
        Execute(() => store.GetEvent(eventId) ?? throw new PersonalTaskNotFoundException(), cancellationToken);
    public System.Threading.Tasks.Task<DesktopCalendarResult<IReadOnlyList<DesktopScheduleConflict>>> GetConflictsAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) =>
        Execute(() => store.Conflicts(fromUtc, toUtc), cancellationToken);
    public System.Threading.Tasks.Task<DesktopCalendarResult<DesktopCalendarEvent>> CreateEventAsync(DesktopCalendarEventCommand command, CancellationToken cancellationToken) =>
        Execute(() => store.SaveEvent(null, null, command), cancellationToken);
    public System.Threading.Tasks.Task<DesktopCalendarResult<DesktopCalendarEvent>> UpdateEventAsync(Guid eventId, long expectedVersion, DesktopCalendarEventCommand command, CancellationToken cancellationToken) =>
        Execute(() => store.SaveEvent(eventId, expectedVersion, command), cancellationToken);
}
