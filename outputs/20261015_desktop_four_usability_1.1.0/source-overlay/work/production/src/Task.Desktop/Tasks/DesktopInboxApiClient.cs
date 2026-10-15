using System.Globalization;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Task.Desktop.Security;
using Task.Domain;

namespace Task.Desktop.TaskApi;

public interface IDesktopInboxConversionClient
{
    System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> ConvertAsync(Guid id, long version, string title, CancellationToken token);
}

/// <summary>Existing InboxItem wire contract adapted to the shared desktop task editor.</summary>
public sealed class DesktopInboxApiClient : IDesktopTasksApiClient, IDesktopInboxConversionClient, IDesktopTaskWorkspaceClient
{
    private readonly DesktopTasksApiClient _tasks;
    private readonly DesktopAuthenticatedGetExecutor _executor;
    private readonly Uri _root;
    private readonly ConcurrentDictionary<Guid, byte> _convertedTasks = new();
    private readonly ConcurrentDictionary<(Guid, long), string> _conversionTitles = new();
    public DesktopInboxApiClient(HttpClient http, Uri endpoint, SessionService session, DesktopTasksApiClient tasks, DesktopConnectivityService? connectivity = null)
    {
        _tasks = tasks;
        _executor = new(http, session, connectivity);
        _root = new(endpoint.AbsoluteUri.TrimEnd('/') + "/api/v1/inbox-items");
    }
    public async System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskPage>> GetTasksAsync(string? cursor = null, CancellationToken cancellationToken = default)
    {
        var response = await _executor.GetAsync(new(_root + "?status=unprocessed&limit=200" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor))), Guid.NewGuid().ToString("D"), cancellationToken).ConfigureAwait(false);
        if (response is not AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK } ok) return PageFailure(response);
        try
        {
            var node = JsonNode.Parse(ok.Body)!;
            if (node["items"] is not JsonArray rows || rows.Count > 500) return new DesktopTasksApiResult<DesktopTaskPage>.MalformedResponse();
            var items = rows.Select(row => MapInbox(row!)).ToArray();
            var next = node["nextCursor"]?.GetValue<string>();
            if (next is { Length: > 512 } || next is not null && next == cursor) return new DesktopTasksApiResult<DesktopTaskPage>.InvalidCursor();
            return new DesktopTasksApiResult<DesktopTaskPage>.Succeeded(new(items, next, node["total"]?.GetValue<long>()));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or ArgumentException or NullReferenceException)
        { return new DesktopTasksApiResult<DesktopTaskPage>.MalformedResponse(); }
    }
    public async System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskDto>> GetTaskByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_convertedTasks.ContainsKey(id)) return await _tasks.GetTaskByIdAsync(id, cancellationToken).ConfigureAwait(false);
        var result = await _executor.GetAsync(new(_root + "/" + id.ToString("D")), Guid.NewGuid().ToString("D"), cancellationToken).ConfigureAwait(false);
        if (result is not AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK } ok) return DesktopTasksApiClient.MapTaskResult(result);
        try { return new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(MapInbox(JsonNode.Parse(ok.Body)!)); }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or ArgumentException or NullReferenceException)
        { return new DesktopTasksApiResult<DesktopTaskDto>.MalformedResponse(); }
    }
    public async System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> CreateTaskAsync(DesktopCreateTaskCommand command, CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new { itemType = "task", title = command.Title });
        var result = await _executor.SendAsync(HttpMethod.Post, _root, body, Guid.NewGuid().ToString("D"), null, command.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        if (result is not AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.Created } ok) return DesktopTasksApiClient.MapWriteResult(result, HttpStatusCode.Created);
        try
        {
            var item = MapInbox(JsonNode.Parse(ok.Body)!);
            if (ok.EntityTag != $"\"v{item.Version}\"") return new DesktopTaskWriteResult<DesktopTaskDto>.MalformedResponse();
            return new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(item, item.Version, ok.IdempotencyReplayed == "true");
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or ArgumentException or NullReferenceException)
        { return new DesktopTaskWriteResult<DesktopTaskDto>.MalformedResponse(); }
    }
    public async System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> ConvertAsync(Guid id, long version, string title, CancellationToken token)
    {
        var preparedTitle = _conversionTitles.GetOrAdd((id, version), title.Trim());
        var prepareKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"inbox-title:{id:D}:{version}:{preparedTitle}")));
        var prepared = await _executor.SendAsync(HttpMethod.Patch, new(_root + $"/{id:D}"),
            JsonSerializer.SerializeToUtf8Bytes(new { title = preparedTitle }), Guid.NewGuid().ToString("D"), $"\"v{version}\"", prepareKey, token).ConfigureAwait(false);
        if (prepared is not AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK } ready) return DesktopTasksApiClient.MapWriteResult(prepared, HttpStatusCode.OK);
        try
        {
            var inbox = MapInbox(JsonNode.Parse(ready.Body)!);
            if (inbox.Id != id || ready.EntityTag != $"\"v{inbox.Version}\"") return new DesktopTaskWriteResult<DesktopTaskDto>.MalformedResponse();
            version = inbox.Version;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or ArgumentException or NullReferenceException)
        { return new DesktopTaskWriteResult<DesktopTaskDto>.MalformedResponse(); }
        // The same logical source/version always retries the same canonical conversion.
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"inbox-convert-task:{id:D}:{version.ToString(CultureInfo.InvariantCulture)}")));
        var result = await _executor.SendAsync(HttpMethod.Post, new(_root + $"/{id:D}/convert-to-task"),
            JsonSerializer.SerializeToUtf8Bytes(new { expectedInboxVersion = version }), Guid.NewGuid().ToString("D"), $"\"v{version}\"", key, token).ConfigureAwait(false);
        var mapped = DesktopTasksApiClient.MapWriteResult(result, HttpStatusCode.Created);
        if (mapped is DesktopTaskWriteResult<DesktopTaskDto>.Succeeded success) _convertedTasks.TryAdd(success.Value.Id, 0);
        return mapped;
    }
    public System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> PatchTaskAsync(DesktopPatchTaskCommand command, CancellationToken cancellationToken = default) => _tasks.PatchTaskAsync(command, cancellationToken);
    public System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> TransitionTaskAsync(DesktopTransitionTaskCommand command, CancellationToken cancellationToken = default) => _tasks.TransitionTaskAsync(command, cancellationToken);
    public System.Threading.Tasks.Task<TaskWorkspaceResult> GetOptionsAsync(string query, CancellationToken token) => _tasks.GetOptionsAsync(query, token);
    public System.Threading.Tasks.Task<TaskWorkspaceResult> GetWorkspaceAsync(Guid id, CancellationToken token) => _tasks.GetWorkspaceAsync(id, token);
    public System.Threading.Tasks.Task<TaskWorkspaceResult> WriteWorkspaceAsync(Guid id, long version, string path, HttpMethod method, JsonObject body, string key, CancellationToken token) => _tasks.WriteWorkspaceAsync(id, version, path, method, body, key, token);

    private static DesktopTaskDto MapInbox(JsonNode node)
    {
        var id = Guid.Parse(node["id"]!.GetValue<string>()); var org = Guid.Parse(node["organizationId"]!.GetValue<string>());
        var owner = Guid.Parse(node["ownerUserId"]!.GetValue<string>()); var version = node["version"]!.GetValue<long>();
        if (id == Guid.Empty || org == Guid.Empty || owner == Guid.Empty || version < 1 || node["status"]?.GetValue<string>() != "unprocessed") throw new FormatException();
        var content = node["content"]?.GetValue<string>();
        var title = node["title"]?.GetValue<string>() ?? content ?? "Входящая запись";
        return new(id, org, version, Date(node, "createdAt"), Date(node, "updatedAt"), title, owner,
            DesktopTaskStatus.New, DesktopTaskPriority.Normal, null, null, [], [], null, new TaskCardContent { Description = content });
    }
    private static DateTimeOffset? Date(JsonNode node, string key) => node[key] is null ? null : DateTimeOffset.Parse(node[key]!.GetValue<string>(), CultureInfo.InvariantCulture);
    private static DesktopTasksApiResult<DesktopTaskPage> PageFailure(AuthenticatedGetResult result) => result switch
    {
        AuthenticatedGetResult.AuthenticationFailure => new DesktopTasksApiResult<DesktopTaskPage>.AuthenticationFailure(),
        AuthenticatedGetResult.ServerUnavailable => new DesktopTasksApiResult<DesktopTaskPage>.ServerUnavailable(),
        AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.Forbidden } => new DesktopTasksApiResult<DesktopTaskPage>.Forbidden(),
        AuthenticatedGetResult.Response { StatusCode: >= HttpStatusCode.InternalServerError } => new DesktopTasksApiResult<DesktopTaskPage>.ServerUnavailable(),
        AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.BadRequest or HttpStatusCode.Gone } => new DesktopTasksApiResult<DesktopTaskPage>.InvalidCursor(),
        _ => new DesktopTasksApiResult<DesktopTaskPage>.MalformedResponse(),
    };
}
