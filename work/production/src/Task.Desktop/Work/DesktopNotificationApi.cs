using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Task.Desktop.Security;

namespace Task.Desktop.Work;

public sealed partial class DesktopWorkApiClient
{
    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopNotification>> GetNotificationAsync(Guid id, CancellationToken ct = default) =>
        GetEntityAsync($"notifications/{id:D}", MapNotification, ct);

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
