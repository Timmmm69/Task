using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Task.Desktop.Security;

namespace Task.Desktop.Calendar;

public sealed class DesktopCalendarApiClient : IDesktopCalendarApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Uri _serverEndpoint;
    private readonly DesktopAuthenticatedGetExecutor _executor;

    public DesktopCalendarApiClient(HttpClient httpClient, Uri serverEndpoint, SessionService sessionService)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(serverEndpoint);
        ArgumentNullException.ThrowIfNull(sessionService);
        if (!serverEndpoint.IsAbsoluteUri) throw new ArgumentException("Server endpoint must be absolute.", nameof(serverEndpoint));
        _serverEndpoint = serverEndpoint;
        _executor = new DesktopAuthenticatedGetExecutor(httpClient, sessionService);
    }

    public async global::System.Threading.Tasks.Task<DesktopCalendarResult<DesktopSchedulePage>> GetScheduleAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, string timeZoneId, CancellationToken cancellationToken)
    {
        ValidateRange(fromUtc, toUtc);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        var query = $"from={Escape(FormatUtc(fromUtc))}&to={Escape(FormatUtc(toUtc))}&timezone={Escape(timeZoneId)}";
        var result = await _executor.GetAsync(BuildUri($"api/v1/calendar?{query}"), NewCorrelationId(), cancellationToken).ConfigureAwait(false);
        return Map(result, response => TryReadPage(response.Body, out var page) ? page : null);
    }

    public async global::System.Threading.Tasks.Task<DesktopCalendarResult<DesktopCalendarEvent>> GetEventAsync(
        Guid eventId, CancellationToken cancellationToken)
    {
        if (eventId == Guid.Empty) throw new ArgumentException("Event id must not be empty.", nameof(eventId));
        var result = await _executor.GetAsync(BuildUri($"api/v1/calendar-events/{eventId:D}"), NewCorrelationId(), cancellationToken).ConfigureAwait(false);
        return MapEvent(result);
    }

    public async global::System.Threading.Tasks.Task<DesktopCalendarResult<IReadOnlyList<DesktopScheduleConflict>>> GetConflictsAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        ValidateRange(fromUtc, toUtc);
        var query = $"from={Escape(FormatUtc(fromUtc))}&to={Escape(FormatUtc(toUtc))}";
        var result = await _executor.GetAsync(BuildUri($"api/v1/calendar/conflicts?{query}"), NewCorrelationId(), cancellationToken).ConfigureAwait(false);
        return Map(result, response => TryReadConflicts(response.Body, out var conflicts) ? conflicts : null);
    }

    public async global::System.Threading.Tasks.Task<DesktopCalendarResult<DesktopCalendarEvent>> CreateEventAsync(
        DesktopCalendarEventCommand command, CancellationToken cancellationToken)
    {
        ValidateCommand(command);
        var body = JsonSerializer.SerializeToUtf8Bytes(ToPayload(command), JsonOptions);
        var result = await _executor.SendAsync(HttpMethod.Post, BuildUri("api/v1/calendar-events"), body,
            NewCorrelationId(), null, Guid.NewGuid().ToString("N"), cancellationToken).ConfigureAwait(false);
        return MapEvent(result, HttpStatusCode.Created);
    }

    public async global::System.Threading.Tasks.Task<DesktopCalendarResult<DesktopCalendarEvent>> UpdateEventAsync(
        Guid eventId, long expectedVersion, DesktopCalendarEventCommand command, CancellationToken cancellationToken)
    {
        if (eventId == Guid.Empty || expectedVersion < 1) throw new ArgumentException("Event identity and version are required.");
        ValidateCommand(command);
        var body = JsonSerializer.SerializeToUtf8Bytes(ToPayload(command), JsonOptions);
        var result = await _executor.SendAsync(HttpMethod.Patch, BuildUri($"api/v1/calendar-events/{eventId:D}"), body,
            NewCorrelationId(), $"\"v{expectedVersion.ToString(CultureInfo.InvariantCulture)}\"", null, cancellationToken).ConfigureAwait(false);
        return MapEvent(result);
    }

    private DesktopCalendarResult<DesktopCalendarEvent> MapEvent(AuthenticatedGetResult result, HttpStatusCode success = HttpStatusCode.OK) =>
        result switch
        {
            AuthenticatedGetResult.Response response when response.StatusCode == success
                && TryReadEvent(response.Body, response.EntityTag, out var calendarEvent) =>
                    new DesktopCalendarResult<DesktopCalendarEvent>.Succeeded(calendarEvent, response.CorrelationId),
            AuthenticatedGetResult.Response response when response.StatusCode == success =>
                new DesktopCalendarResult<DesktopCalendarEvent>.MalformedResponse(response.CorrelationId),
            _ => MapFailure<DesktopCalendarEvent>(result),
        };

    private static DesktopCalendarResult<T> Map<T>(AuthenticatedGetResult result, Func<AuthenticatedGetResult.Response, T?> reader)
        where T : class => result switch
        {
            AuthenticatedGetResult.Response response when response.StatusCode == HttpStatusCode.OK && reader(response) is { } value =>
                new DesktopCalendarResult<T>.Succeeded(value, response.CorrelationId),
            AuthenticatedGetResult.Response response when response.StatusCode == HttpStatusCode.OK =>
                new DesktopCalendarResult<T>.MalformedResponse(response.CorrelationId),
            _ => MapFailure<T>(result),
        };

    private static DesktopCalendarResult<T> MapFailure<T>(AuthenticatedGetResult result) => result switch
    {
        AuthenticatedGetResult.AuthenticationFailure => new DesktopCalendarResult<T>.AuthenticationFailure(),
        AuthenticatedGetResult.ServerUnavailable => new DesktopCalendarResult<T>.ServerUnavailable(),
        AuthenticatedGetResult.MalformedResponse => new DesktopCalendarResult<T>.MalformedResponse(),
        AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.Forbidden } r => new DesktopCalendarResult<T>.Forbidden(r.CorrelationId),
        AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.NotFound } r => new DesktopCalendarResult<T>.NotFound(r.CorrelationId),
        AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.PreconditionFailed } r => new DesktopCalendarResult<T>.VersionConflict(r.CorrelationId),
        AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity } r =>
            new DesktopCalendarResult<T>.ValidationFailure(ReadProblemTitle(r.Body), r.CorrelationId),
        AuthenticatedGetResult.Response r when (int)r.StatusCode >= 500 => new DesktopCalendarResult<T>.ServerUnavailable(r.CorrelationId),
        AuthenticatedGetResult.Response r => new DesktopCalendarResult<T>.MalformedResponse(r.CorrelationId),
        _ => new DesktopCalendarResult<T>.MalformedResponse(),
    };

    private static bool TryReadPage(string body, out DesktopSchedulePage page)
    {
        page = null!;
        try
        {
            var payload = JsonSerializer.Deserialize<SchedulePagePayload>(body, JsonOptions);
            if (payload?.Items is null || payload.Items.Count > 500 || payload.NextCursor is not null
                || !IsUtc(payload.RangeStart) || !IsUtc(payload.RangeEnd) || payload.RangeStart >= payload.RangeEnd) return false;
            var items = new List<DesktopScheduleItem>(payload.Items.Count);
            foreach (var item in payload.Items)
            {
                if (item is null || !TryMapItem(item, out var mapped)) return false;
                items.Add(mapped);
            }
            page = new(items.ToArray(), payload.RangeStart!.Value, payload.RangeEnd!.Value);
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool TryMapItem(ScheduleItemPayload p, out DesktopScheduleItem item)
    {
        item = null!;
        var type = p.ItemType switch { "task" => DesktopScheduleItemType.Task, "calendar_event" => DesktopScheduleItemType.CalendarEvent, _ => (DesktopScheduleItemType)(-1) };
        var allowedStatus = type == DesktopScheduleItemType.Task
            ? p.Status is "new" or "in_progress" or "review" or "completed" or "cancelled"
            : p.Status is "scheduled" or "cancelled";
        DesktopCalendarPriority? priority = p.Priority switch
        {
            null => null,
            "low" => DesktopCalendarPriority.Low,
            "normal" => DesktopCalendarPriority.Normal,
            "high" => DesktopCalendarPriority.High,
            "critical" => DesktopCalendarPriority.Critical,
            _ => (DesktopCalendarPriority)(-1),
        };
        if (p.ObjectId == Guid.Empty || !Enum.IsDefined(type) || string.IsNullOrWhiteSpace(p.Title) || p.Title.Length > 500
            || p.LocalDate == default || !allowedStatus || (priority.HasValue && !Enum.IsDefined(priority.Value))
            || (type == DesktopScheduleItemType.CalendarEvent && priority is not null)
            || !IsUtc(p.StartAtUtc) || !IsUtc(p.EndAtUtc)
            || (p.IsAllDay && (p.StartAtUtc.HasValue || p.EndAtUtc.HasValue))
            || (type == DesktopScheduleItemType.CalendarEvent && !p.IsAllDay && p.StartAtUtc.HasValue != p.EndAtUtc.HasValue)
            || (p.StartAtUtc.HasValue && p.EndAtUtc < p.StartAtUtc)) return false;
        item = new(p.ObjectId, type, p.Title, p.LocalDate, p.StartAtUtc, p.EndAtUtc, p.IsAllDay, p.ProjectId, p.Status!, priority, p.RecurrenceSeriesId, p.Description);
        return true;
    }

    private static bool TryReadEvent(string body, string? entityTag, out DesktopCalendarEvent calendarEvent)
    {
        calendarEvent = null!;
        try
        {
            var p = JsonSerializer.Deserialize<EventPayload>(body, JsonOptions);
            if (p is null || p.Id == Guid.Empty || p.OrganizationId == Guid.Empty || p.Version < 1
                || entityTag != $"\"v{p.Version}\"" || string.IsNullOrWhiteSpace(p.Title) || p.EventDate == default
                || string.IsNullOrWhiteSpace(p.TimeZone) || p.Status is not ("scheduled" or "cancelled")
                || !IsUtc(p.CreatedAt) || !IsUtc(p.UpdatedAt) || !IsUtc(p.StartAtUtc) || !IsUtc(p.EndAtUtc)
                || (p.IsAllDay && (p.StartAtUtc.HasValue || p.EndAtUtc.HasValue))
                || (!p.IsAllDay && (!p.StartAtUtc.HasValue || !p.EndAtUtc.HasValue || p.EndAtUtc <= p.StartAtUtc))
                || p.UserAttendees is null || p.ContactAttendees is null) return false;
            var attendees = new List<DesktopCalendarAttendee>();
            foreach (var attendee in p.UserAttendees)
                if (!TryMapAttendee(attendee, true, out var mapped)) return false; else attendees.Add(mapped);
            foreach (var attendee in p.ContactAttendees)
                if (!TryMapAttendee(attendee, false, out var mapped)) return false; else attendees.Add(mapped);
            calendarEvent = new(p.Id, p.OrganizationId, p.Version, p.CreatedAt!.Value, p.UpdatedAt!.Value,
                p.ProjectId, p.Title, p.Description, p.EventDate, p.IsAllDay, p.StartAtUtc, p.EndAtUtc,
                p.TimeZone!, p.Status!, attendees.ToArray(), entityTag);
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool TryMapAttendee(AttendeePayload p, bool isUser, out DesktopCalendarAttendee attendee)
    {
        attendee = null!;
        var id = isUser ? p.UserAccountId : p.ContactId;
        if (id is null || id == Guid.Empty || p.Role is not ("required" or "optional" or "observer")
            || p.ResponseStatus is not ("pending" or "accepted" or "declined" or "tentative") || !IsUtc(p.RespondedAt)) return false;
        attendee = new(id.Value, isUser, p.Role, p.ResponseStatus, p.RespondedAt);
        return true;
    }

    private static bool TryReadConflicts(string body, out IReadOnlyList<DesktopScheduleConflict> conflicts)
    {
        conflicts = Array.Empty<DesktopScheduleConflict>();
        try
        {
            var payload = JsonSerializer.Deserialize<List<ConflictPayload>>(body, JsonOptions);
            if (payload is null || payload.Count > 500) return false;
            var mapped = new List<DesktopScheduleConflict>(payload.Count);
            foreach (var p in payload)
            {
                var severity = p.Severity switch { "info" => DesktopConflictSeverity.Info, "warning" => DesktopConflictSeverity.Warning, "blocking" => DesktopConflictSeverity.Blocking, _ => (DesktopConflictSeverity)(-1) };
                if (p.LeftObjectId == Guid.Empty || p.RightObjectId == Guid.Empty || p.LeftObjectId == p.RightObjectId
                    || !IsUtc(p.OverlapStart) || !IsUtc(p.OverlapEnd) || p.OverlapEnd <= p.OverlapStart || !Enum.IsDefined(severity)) return false;
                mapped.Add(new(p.LeftObjectId, p.RightObjectId, p.OverlapStart!.Value, p.OverlapEnd!.Value, severity));
            }
            conflicts = mapped;
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static object ToPayload(DesktopCalendarEventCommand c)
    {
        var payload = new Dictionary<string, object?>
        {
            ["projectId"] = c.ProjectId,
            ["title"] = c.Title.Trim(),
            ["description"] = string.IsNullOrWhiteSpace(c.Description) ? null : c.Description.Trim(),
            ["eventDate"] = c.EventDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["isAllDay"] = c.IsAllDay,
            ["startAtUtc"] = c.StartAtUtc?.UtcDateTime,
            ["endAtUtc"] = c.EndAtUtc?.UtcDateTime,
            ["timeZone"] = c.TimeZoneId,
            ["status"] = c.Status,
        };
        if (c.Attendees is not null)
        {
            payload["userAttendees"] = c.Attendees.Where(a => a.IsUser).Select(a => new
            {
                userAccountId = a.Id, role = a.Role, responseStatus = a.ResponseStatus, respondedAt = a.RespondedAtUtc?.UtcDateTime,
            }).ToArray();
            payload["contactAttendees"] = c.Attendees.Where(a => !a.IsUser).Select(a => new
            {
                contactId = a.Id, role = a.Role, responseStatus = a.ResponseStatus, respondedAt = a.RespondedAtUtc?.UtcDateTime,
            }).ToArray();
        }
        return payload;
    }

    private static void ValidateCommand(DesktopCalendarEventCommand c)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (string.IsNullOrWhiteSpace(c.Title) || c.Title.Trim().Length > 500 || c.EventDate == default
            || string.IsNullOrWhiteSpace(c.TimeZoneId) || c.Status is not ("scheduled" or "cancelled")
            || (c.IsAllDay && (c.StartAtUtc.HasValue || c.EndAtUtc.HasValue))
            || (!c.IsAllDay && (!c.StartAtUtc.HasValue || !c.EndAtUtc.HasValue || c.EndAtUtc <= c.StartAtUtc))
            || (c.EndDate.HasValue && c.EndDate < c.EventDate)
            || !IsUtc(c.StartAtUtc) || !IsUtc(c.EndAtUtc)) throw new ArgumentException("Calendar event command is invalid.", nameof(c));
        if (c.Attendees is null) return;
        if (c.Attendees.Count(a => a.IsUser) > 500 || c.Attendees.Count(a => !a.IsUser) > 500
            || c.Attendees.Any(a => a.Id == Guid.Empty || a.Role is not ("required" or "optional" or "observer")
                || a.ResponseStatus is not ("pending" or "accepted" or "declined" or "tentative") || !IsUtc(a.RespondedAtUtc))
            || c.Attendees.GroupBy(a => (a.IsUser, a.Id)).Any(g => g.Count() != 1))
            throw new ArgumentException("Calendar event attendees are invalid.", nameof(c));
    }

    private static void ValidateRange(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        if (fromUtc.Offset != TimeSpan.Zero || toUtc.Offset != TimeSpan.Zero || fromUtc >= toUtc || toUtc - fromUtc > TimeSpan.FromDays(366))
            throw new ArgumentException("Calendar range must be a valid UTC half-open interval up to 366 days.");
    }

    private Uri BuildUri(string relative) => new(_serverEndpoint, relative);
    private static string Escape(string value) => Uri.EscapeDataString(value);
    private static string FormatUtc(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    private static string NewCorrelationId() => Guid.NewGuid().ToString("D");
    private static bool IsUtc(DateTimeOffset? value) => !value.HasValue || value.Value.Offset == TimeSpan.Zero;
    private static string ReadProblemTitle(string body)
    {
        try { return JsonSerializer.Deserialize<ProblemPayload>(body, JsonOptions)?.Title ?? "Проверьте данные события."; }
        catch (JsonException) { return "Проверьте данные события."; }
    }

    private sealed class SchedulePagePayload { public List<ScheduleItemPayload?>? Items { get; init; } public string? NextCursor { get; init; } public DateTimeOffset? RangeStart { get; init; } public DateTimeOffset? RangeEnd { get; init; } }
    private sealed class ScheduleItemPayload { public Guid? RecurrenceSeriesId { get; init; } public string? Description { get; init; } public Guid ObjectId { get; init; } public string? ItemType { get; init; } public string? Title { get; init; } public DateOnly LocalDate { get; init; } public DateTimeOffset? StartAtUtc { get; init; } public DateTimeOffset? EndAtUtc { get; init; } public bool IsAllDay { get; init; } public Guid? ProjectId { get; init; } public string? Status { get; init; } public string? Priority { get; init; } }
    private sealed class EventPayload { public Guid Id { get; init; } public Guid OrganizationId { get; init; } public long Version { get; init; } public DateTimeOffset? CreatedAt { get; init; } public DateTimeOffset? UpdatedAt { get; init; } public Guid? ProjectId { get; init; } public string? Title { get; init; } public string? Description { get; init; } public DateOnly EventDate { get; init; } public bool IsAllDay { get; init; } public DateTimeOffset? StartAtUtc { get; init; } public DateTimeOffset? EndAtUtc { get; init; } public string? TimeZone { get; init; } public string? Status { get; init; } public List<AttendeePayload>? UserAttendees { get; init; } public List<AttendeePayload>? ContactAttendees { get; init; } }
    private sealed class AttendeePayload { public Guid? UserAccountId { get; init; } public Guid? ContactId { get; init; } public string? Role { get; init; } public string? ResponseStatus { get; init; } public DateTimeOffset? RespondedAt { get; init; } }
    private sealed class ConflictPayload { public Guid LeftObjectId { get; init; } public Guid RightObjectId { get; init; } public DateTimeOffset? OverlapStart { get; init; } public DateTimeOffset? OverlapEnd { get; init; } public string? Severity { get; init; } }
    private sealed class ProblemPayload { [JsonPropertyName("title")] public string? Title { get; init; } }
}
