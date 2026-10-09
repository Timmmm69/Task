using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Task.Desktop.Security;

namespace Task.Desktop.Work;

public sealed record DesktopReminderOccurrence(Guid Id, long Version, Guid ReminderId, DateTimeOffset DueAt, string Status);
public sealed record DesktopReminder(Guid Id, long Version, Guid TargetObjectId, string TargetTitle, string TriggerType,
    int? OffsetMinutes, DateTimeOffset? AbsoluteTriggerAt, DateTimeOffset NextTriggerAt, string Status, DesktopReminderOccurrence? CurrentOccurrence)
{
    public string DisplayText => $"{TargetTitle} · {NextTriggerAt.ToLocalTime():dd.MM.yyyy HH:mm} · {Status switch { "scheduled" => "Запланировано", "snoozed" => "Отложено", "due" => "Пора напомнить", "delivered" => "Доставлено", "cancelled" => "Отменено", _ => "Неактивно" }}";
}
public interface IDesktopReminderApiClient
{
    System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopReminder>>> GetRemindersAsync(CancellationToken ct = default);
    System.Threading.Tasks.Task<DesktopWorkResult<DesktopReminder>> GetReminderAsync(Guid id, CancellationToken ct = default);
    System.Threading.Tasks.Task<DesktopWorkResult<DesktopReminder>> SaveReminderAsync(Guid target, string trigger, int? offset, DateTimeOffset? absolute, DesktopReminder? existing, CancellationToken ct = default);
    System.Threading.Tasks.Task<DesktopWorkResult<bool>> CancelReminderAsync(DesktopReminder reminder, CancellationToken ct = default);
    System.Threading.Tasks.Task<DesktopWorkResult<bool>> RestoreReminderAsync(DesktopReminder reminder, CancellationToken ct = default);
    System.Threading.Tasks.Task<DesktopWorkResult<bool>> ActOnReminderAsync(Guid id, long occurrenceVersion, DateTimeOffset? until, CancellationToken ct = default);
}
public sealed partial class DesktopWorkApiClient : IDesktopReminderApiClient
{
    public async System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopReminder>>> GetRemindersAsync(CancellationToken ct = default)
    {
        var items = new List<DesktopReminder>();
        var cursors = new HashSet<string>();
        string? cursor = null;
        for (var page = 0; page < 500; page++)
        {
            var result = await _executor.GetAsync(Uri("reminders?limit=200&includeOccurrence=true" + (cursor is null ? "" : "&cursor=" + System.Uri.EscapeDataString(cursor))), Correlation(), ct).ConfigureAwait(false);
            if (result is not AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK } response) return Failure<IReadOnlyList<DesktopReminder>>(result);
            try
            {
                var node = JsonNode.Parse(response.Body);
                if (node?["items"] is not JsonArray array || array.Count > 200) return new DesktopWorkResult<IReadOnlyList<DesktopReminder>>.MalformedResponse();
                foreach (var value in array)
                { if (value is null || MapReminder(value) is not { } item) return new DesktopWorkResult<IReadOnlyList<DesktopReminder>>.MalformedResponse(); items.Add(item); }
                cursor = Text(node!, "nextCursor");
                if (string.IsNullOrEmpty(cursor)) return Bool(node!, "hasMore") == true ? new DesktopWorkResult<IReadOnlyList<DesktopReminder>>.MalformedResponse() : new DesktopWorkResult<IReadOnlyList<DesktopReminder>>.Succeeded(items);
                if (!cursors.Add(cursor)) return new DesktopWorkResult<IReadOnlyList<DesktopReminder>>.MalformedResponse();
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { return new DesktopWorkResult<IReadOnlyList<DesktopReminder>>.MalformedResponse(); }
        }
        return new DesktopWorkResult<IReadOnlyList<DesktopReminder>>.MalformedResponse();
    }
    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopReminder>> GetReminderAsync(Guid id, CancellationToken ct = default) => GetEntityAsync($"reminders/{id:D}?includeOccurrence=true", MapReminder, ct);
    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopReminder>> SaveReminderAsync(Guid target, string trigger, int? offset, DateTimeOffset? absolute, DesktopReminder? existing, CancellationToken ct = default)
    {
        if (_sessionService.CurrentSessionMetadata is not { } session) return System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopReminder>>(new DesktopWorkResult<DesktopReminder>.AuthenticationFailure());
        var body = new JsonObject { ["targetObjectId"] = target, ["recipientUserId"] = session.UserId, ["triggerType"] = trigger, ["offsetMinutes"] = offset, ["absoluteTriggerAt"] = absolute };
        return SendEntityAsync((existing is null ? "reminders" : $"reminders/{existing.Id:D}") + "?includeOccurrence=true", existing is null ? HttpMethod.Post : HttpMethod.Patch, body, MapReminder, existing?.Version, Key(), ct);
    }
    public async System.Threading.Tasks.Task<DesktopWorkResult<bool>> CancelReminderAsync(DesktopReminder reminder, CancellationToken ct = default)
    {
        var result = await SendAsync($"reminders/{reminder.Id:D}", HttpMethod.Delete, new(), reminder.Version, null, ct).ConfigureAwait(false);
        return result is AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.Accepted } ? new DesktopWorkResult<bool>.Succeeded(true) : Failure<bool>(result);
    }
    public System.Threading.Tasks.Task<DesktopWorkResult<bool>> RestoreReminderAsync(DesktopReminder reminder, CancellationToken ct = default) =>
        SendBooleanAsync($"reminders/{reminder.Id:D}/reschedule", new() { ["expectedVersion"] = reminder.Version }, reminder.Version, Key(), ct);
    public System.Threading.Tasks.Task<DesktopWorkResult<bool>> ActOnReminderAsync(Guid id, long occurrenceVersion, DateTimeOffset? until, CancellationToken ct = default)
    {
        var body = new JsonObject { ["expectedVersion"] = occurrenceVersion };
        if (until is not null) body["until"] = JsonValue.Create(until.Value);
        return SendBooleanAsync($"reminders/{id:D}/" + (until is null ? "dismiss" : "snooze"), body, occurrenceVersion, until is null ? null : Key(), ct);
    }
    private static DesktopReminder? MapReminder(JsonNode n)
    {
        if (!Guid.TryParse(Text(n, "id"), out var id) || id == Guid.Empty || n["version"]?.GetValue<long>() is not (> 0 and var version)
            || !Guid.TryParse(Text(n, "targetObjectId"), out var target) || target == Guid.Empty || Text(n, "triggerType") is not { } trigger
            || !DateTimeOffset.TryParse(Text(n, "nextTriggerAt"), out var next) || Text(n, "status") is not { } status) return null;
        DesktopReminderOccurrence? occurrence = null;
        if (n["currentOccurrence"] is { } x)
        {
            if (!Guid.TryParse(Text(x, "id"), out var xid) || xid == Guid.Empty || x["version"]?.GetValue<long>() is not (> 0 and var xv)
                || !Guid.TryParse(Text(x, "reminderId"), out var rid) || rid != id || !DateTimeOffset.TryParse(Text(x, "dueAt"), out var due) || Text(x, "status") is not { } xs) return null;
            occurrence = new(xid, xv, rid, due, xs);
        }
        return new(id, version, target, Text(n, "targetTitle") ?? "Задача или событие", trigger, Int(n, "offsetMinutes"), Date(n, "absoluteTriggerAt"), next, status, occurrence);
    }
}
