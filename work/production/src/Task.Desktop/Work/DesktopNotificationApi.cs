using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Task.Desktop.Security;

namespace Task.Desktop.Work;

public sealed record DesktopNotificationPoll(
    Guid? Revision, bool Reset, bool HistoryRequired, bool NotificationsIncluded,
    IReadOnlyList<DesktopNotification> Items, IReadOnlyList<Guid> RemovedIds,
    DesktopNotificationPreferences? Preferences, bool? AutostartEnabled);

public interface IDesktopNotificationPollClient
{
    System.Threading.Tasks.Task<DesktopWorkResult<DesktopNotificationPoll>> PollNotificationsAsync(
        Guid? revision, bool notifications, bool settings, CancellationToken cancellationToken);
}

public sealed partial class DesktopWorkApiClient : IDesktopNotificationPollClient
{
    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopNotification>> GetNotificationAsync(Guid id, CancellationToken ct = default) =>
        GetEntityAsync($"notifications/{id:D}", MapNotification, ct);

    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopNotificationPoll>> PollNotificationsAsync(
        Guid? revision, bool notifications, bool settings, CancellationToken cancellationToken)
    {
        var query = $"notifications/poll?notifications={(notifications ? "true" : "false")}&settings={(settings ? "true" : "false")}";
        if (revision is { } value) query += $"&revision={value:N}";
        return GetEntityAsync(query, MapNotificationPoll, cancellationToken);
    }

    private static DesktopNotificationPoll? MapNotificationPoll(JsonNode node)
    {
        if (Bool(node, "reset") is not { } reset || Bool(node, "historyRequired") is not { } historyRequired ||
            Bool(node, "notificationsIncluded") is not { } notificationsIncluded ||
            node["items"] is not JsonArray items || node["removedIds"] is not JsonArray removedIds)
            return null;
        var preferences = node["preferences"] is { } payload ? MapNotificationPreferences(payload) : null;
        var autostart = Bool(node, "autostartEnabled");
        if (node["preferences"] is not null && preferences is null || node["autostartEnabled"] is not null && autostart is null)
            return null;
        Guid? revision = null;
        if (notificationsIncluded && !historyRequired)
        {
            if (!Guid.TryParseExact(Text(node, "revision"), "N", out var token) || token == Guid.Empty) return null;
            revision = token;
        }
        else if (node["revision"] is not null || items.Count > 0 || removedIds.Count > 0 || !reset || historyRequired && !notificationsIncluded)
            return null;
        if (reset && removedIds.Count > 0) return null;

        var mapped = new List<DesktopNotification>(items.Count);
        var ids = new HashSet<Guid>();
        foreach (var item in items)
        {
            if (item is null || MapNotification(item) is not { } notification || !ids.Add(notification.Id)) return null;
            mapped.Add(notification);
        }
        var removed = new List<Guid>(removedIds.Count);
        foreach (var item in removedIds)
        {
            if (!Guid.TryParse(item?.ToString(), out var id) || id == Guid.Empty || !ids.Add(id)) return null;
            removed.Add(id);
        }
        return new(revision, reset, historyRequired, notificationsIncluded, mapped, removed, preferences, autostart);
    }

    private async System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopNotification>>> GetNotificationPagesAsync(CancellationToken ct)
    {
        var items = new List<DesktopNotification>();
        var cursors = new HashSet<string>();
        string? cursor = null;
        // A bounded traversal fails as a whole; never seed the journal from a partial history.
        for (var page = 0; page < 500; page++)
        {
            var path = "notifications?limit=200" + (cursor is null ? "" : "&cursor=" + System.Uri.EscapeDataString(cursor));
            var result = await _executor.GetAsync(Uri(path), Correlation(), ct).ConfigureAwait(false);
            if (result is not AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK } response) return Failure<IReadOnlyList<DesktopNotification>>(result);
            try
            {
                var node = JsonNode.Parse(response.Body);
                if (node?["items"] is not JsonArray array || array.Count > 200) return new DesktopWorkResult<IReadOnlyList<DesktopNotification>>.MalformedResponse();
                foreach (var value in array)
                {
                    if (value is null || MapNotification(value) is not { } notification) return new DesktopWorkResult<IReadOnlyList<DesktopNotification>>.MalformedResponse();
                    items.Add(notification);
                }
                cursor = Text(node!, "nextCursor");
                if (string.IsNullOrEmpty(cursor))
                {
                    if (Bool(node!, "hasMore") == true) return new DesktopWorkResult<IReadOnlyList<DesktopNotification>>.MalformedResponse();
                    return new DesktopWorkResult<IReadOnlyList<DesktopNotification>>.Succeeded(items);
                }
                if (!cursors.Add(cursor)) return new DesktopWorkResult<IReadOnlyList<DesktopNotification>>.MalformedResponse();
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { return new DesktopWorkResult<IReadOnlyList<DesktopNotification>>.MalformedResponse(); }
        }
        return new DesktopWorkResult<IReadOnlyList<DesktopNotification>>.MalformedResponse();
    }
}
