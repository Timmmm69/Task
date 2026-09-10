using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Task.Desktop.Security;

namespace Task.Desktop.Work;

public sealed record DesktopCatalogItem(Guid Id, long Version, string Name, string ItemType,
    string? Description, string? FileExtension, string LifecycleState);
public sealed record DesktopContact(Guid Id, long Version, string DisplayName, string FirstName,
    string? LastName, string? Notes, string Status, string LifecycleState);
public sealed record DesktopSearchResult(Guid ObjectId, string ObjectType, string Title,
    Guid? ParentObjectId, long Version, DateTimeOffset UpdatedAt);
public sealed record DesktopNotification(Guid Id, long Version, string NotificationType, string Title,
    string Body, string Severity, string Status, Guid? SourceObjectId, DateTimeOffset NotBefore);
public sealed record DesktopFileLocation(Guid Id, long Version, string LocationType,
    string RawPath, bool CanOpenOnDevice);

public abstract record DesktopWorkResult<T>
{
    private DesktopWorkResult() { }
    public sealed record Succeeded(T Value, long? EntityVersion = null) : DesktopWorkResult<T>;
    public sealed record AuthenticationFailure : DesktopWorkResult<T>;
    public sealed record Forbidden : DesktopWorkResult<T>;
    public sealed record NotFound : DesktopWorkResult<T>;
    public sealed record ValidationFailure(string Message) : DesktopWorkResult<T>;
    public sealed record Conflict : DesktopWorkResult<T>;
    public sealed record ServerUnavailable : DesktopWorkResult<T>;
    public sealed record MalformedResponse : DesktopWorkResult<T>;
}

public interface IDesktopWorkApiClient
{
    System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopCatalogItem>>> GetCatalogAsync(CancellationToken cancellationToken = default);
    System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopContact>>> GetContactsAsync(CancellationToken cancellationToken = default);
    System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopNotification>>> GetNotificationsAsync(CancellationToken cancellationToken = default);
    System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>> SearchAsync(string query, CancellationToken cancellationToken = default);
    System.Threading.Tasks.Task<DesktopWorkResult<DesktopCatalogItem>> CreateCatalogItemAsync(string name, string itemType, string? description, CancellationToken cancellationToken = default);
    System.Threading.Tasks.Task<DesktopWorkResult<DesktopContact>> CreateContactAsync(string firstName, string? lastName, string displayName, CancellationToken cancellationToken = default);
    System.Threading.Tasks.Task<DesktopWorkResult<bool>> AddLocationAsync(Guid itemId, long version, string rawPath, CancellationToken cancellationToken = default);
    System.Threading.Tasks.Task<DesktopWorkResult<DesktopFileLocation?>> ResolveLocationAsync(Guid itemId, CancellationToken cancellationToken = default);
    System.Threading.Tasks.Task<DesktopWorkResult<bool>> MarkNotificationReadAsync(Guid notificationId, CancellationToken cancellationToken = default);
    System.Threading.Tasks.Task<DesktopWorkResult<bool>> MarkAllNotificationsReadAsync(IReadOnlyCollection<Guid> notificationIds, CancellationToken cancellationToken = default);
}

public sealed class DesktopWorkApiClient : IDesktopWorkApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly DesktopAuthenticatedGetExecutor _executor;
    private readonly string _root;

    public DesktopWorkApiClient(HttpClient httpClient, Uri serverEndpoint, SessionService sessionService)
    {
        ArgumentNullException.ThrowIfNull(httpClient); ArgumentNullException.ThrowIfNull(serverEndpoint); ArgumentNullException.ThrowIfNull(sessionService);
        if (!serverEndpoint.IsAbsoluteUri) throw new ArgumentException("The server endpoint must be absolute.", nameof(serverEndpoint));
        _executor = new(httpClient, sessionService);
        _root = serverEndpoint.AbsoluteUri.TrimEnd('/') + "/api/v1/";
    }

    public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopCatalogItem>>> GetCatalogAsync(CancellationToken cancellationToken = default) =>
        GetPageAsync("catalog-items?limit=200", MapCatalog, cancellationToken);
    public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopContact>>> GetContactsAsync(CancellationToken cancellationToken = default) =>
        GetPageAsync("contacts?limit=200", MapContact, cancellationToken);
    public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopNotification>>> GetNotificationsAsync(CancellationToken cancellationToken = default) =>
        GetPageAsync("notifications?limit=200", MapNotification, cancellationToken);

    public async System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        query = query?.Trim() ?? string.Empty;
        if (query.Length is < 2 or > 200) return new DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.ValidationFailure("Введите от 2 до 200 символов.");
        return await GetPageAsync("search?q=" + System.Uri.EscapeDataString(query) + "&limit=100", MapSearch, cancellationToken).ConfigureAwait(false);
    }

    public async System.Threading.Tasks.Task<DesktopWorkResult<DesktopCatalogItem>> CreateCatalogItemAsync(string name, string itemType, string? description, CancellationToken cancellationToken = default)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 500 || itemType is not ("file_reference" or "folder_reference" or "virtual_folder"))
            return new DesktopWorkResult<DesktopCatalogItem>.ValidationFailure("Проверьте название и тип записи.");
        var body = new JsonObject { ["name"] = name, ["itemType"] = itemType, ["description"] = NullIfWhiteSpace(description), ["sortOrder"] = 0 };
        return await SendEntityAsync("catalog-items", HttpMethod.Post, body, MapCatalog, null, Key(), cancellationToken).ConfigureAwait(false);
    }

    public async System.Threading.Tasks.Task<DesktopWorkResult<DesktopContact>> CreateContactAsync(string firstName, string? lastName, string displayName, CancellationToken cancellationToken = default)
    {
        firstName = firstName?.Trim() ?? string.Empty; displayName = displayName?.Trim() ?? string.Empty;
        if (firstName.Length is < 1 or > 100 || displayName.Length is < 1 or > 300)
            return new DesktopWorkResult<DesktopContact>.ValidationFailure("Укажите имя и отображаемое имя контакта.");
        var body = new JsonObject { ["firstName"] = firstName, ["lastName"] = NullIfWhiteSpace(lastName), ["displayName"] = displayName, ["status"] = "active" };
        return await SendEntityAsync("contacts", HttpMethod.Post, body, MapContact, null, Key(), cancellationToken).ConfigureAwait(false);
    }

    public async System.Threading.Tasks.Task<DesktopWorkResult<bool>> AddLocationAsync(Guid itemId, long version, string rawPath, CancellationToken cancellationToken = default)
    {
        rawPath = rawPath?.Trim() ?? string.Empty;
        if (itemId == Guid.Empty || version < 1 || rawPath.Length is < 3 or > 4096)
            return new DesktopWorkResult<bool>.ValidationFailure("Путь или версия записи некорректны.");
        var type = rawPath.StartsWith(@"\\", StringComparison.Ordinal) ? "unc_path" : "local_path";
        var body = new JsonObject { ["locationType"] = type, ["rawPath"] = rawPath, ["priority"] = 0, ["isEnabled"] = true, ["isPrimary"] = true };
        var result = await SendAsync($"catalog-items/{itemId:D}/locations", HttpMethod.Post, body, version, Key(), cancellationToken).ConfigureAwait(false);
        return result is AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.Created }
            ? new DesktopWorkResult<bool>.Succeeded(true) : Failure<bool>(result);
    }

    public async System.Threading.Tasks.Task<DesktopWorkResult<DesktopFileLocation?>> ResolveLocationAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        if (itemId == Guid.Empty) throw new ArgumentException("Identifier must not be empty.", nameof(itemId));
        var result = await SendAsync($"catalog-items/{itemId:D}/resolve-location", HttpMethod.Post, new JsonObject(), null, Key(), cancellationToken).ConfigureAwait(false);
        if (result is not AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK } response) return Failure<DesktopFileLocation?>(result);
        try
        {
            var node = JsonNode.Parse(response.Body)?["location"];
            if (node is null) return new DesktopWorkResult<DesktopFileLocation?>.Succeeded(null);
            return MapLocation(node) is { } location
                ? new DesktopWorkResult<DesktopFileLocation?>.Succeeded(location)
                : new DesktopWorkResult<DesktopFileLocation?>.MalformedResponse();
        }
        catch (JsonException) { return new DesktopWorkResult<DesktopFileLocation?>.MalformedResponse(); }
    }

    public System.Threading.Tasks.Task<DesktopWorkResult<bool>> MarkNotificationReadAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
        SendBooleanAsync($"notifications/{notificationId:D}/read", new JsonObject(), null, null, cancellationToken);
    public System.Threading.Tasks.Task<DesktopWorkResult<bool>> MarkAllNotificationsReadAsync(IReadOnlyCollection<Guid> notificationIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notificationIds);
        if (notificationIds.Count is < 1 or > 500 || notificationIds.Any(id => id == Guid.Empty) || notificationIds.Distinct().Count() != notificationIds.Count)
            return System.Threading.Tasks.Task.FromResult<DesktopWorkResult<bool>>(new DesktopWorkResult<bool>.ValidationFailure("Выберите от 1 до 500 уникальных уведомлений."));
        var ids = new JsonArray(notificationIds.Select(id => JsonValue.Create(id)).ToArray<JsonNode?>());
        return SendBooleanAsync("notifications/read-all", new JsonObject { ["notificationIds"] = ids }, null, Key(), cancellationToken);
    }

    private async System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<T>>> GetPageAsync<T>(string path, Func<JsonNode, T?> map, CancellationToken cancellationToken) where T : class
    {
        var result = await _executor.GetAsync(Uri(path), Correlation(), cancellationToken).ConfigureAwait(false);
        if (result is not AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK } response) return Failure<IReadOnlyList<T>>(result);
        try
        {
            var items = JsonNode.Parse(response.Body)?["items"]?.AsArray();
            if (items is null || items.Count > 500) return new DesktopWorkResult<IReadOnlyList<T>>.MalformedResponse();
            var mapped = items.Select(item => item is null ? null : map(item)).ToArray();
            return mapped.Any(item => item is null)
                ? new DesktopWorkResult<IReadOnlyList<T>>.MalformedResponse()
                : new DesktopWorkResult<IReadOnlyList<T>>.Succeeded(mapped!);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { return new DesktopWorkResult<IReadOnlyList<T>>.MalformedResponse(); }
    }

    private async System.Threading.Tasks.Task<DesktopWorkResult<T>> SendEntityAsync<T>(string path, HttpMethod method, JsonObject body, Func<JsonNode, T?> map, long? version, string? key, CancellationToken cancellationToken) where T : class
    {
        var result = await SendAsync(path, method, body, version, key, cancellationToken).ConfigureAwait(false);
        if (result is not AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK or HttpStatusCode.Created } response) return Failure<T>(result);
        try { return JsonNode.Parse(response.Body) is { } node && map(node) is { } entity ? new DesktopWorkResult<T>.Succeeded(entity, ReadVersion(response.EntityTag)) : new DesktopWorkResult<T>.MalformedResponse(); }
        catch (JsonException) { return new DesktopWorkResult<T>.MalformedResponse(); }
    }

    private async System.Threading.Tasks.Task<DesktopWorkResult<bool>> SendBooleanAsync(string path, JsonObject body, long? version, string? key, CancellationToken cancellationToken)
    {
        var result = await SendAsync(path, HttpMethod.Post, body, version, key, cancellationToken).ConfigureAwait(false);
        return result is AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK or HttpStatusCode.NoContent }
            ? new DesktopWorkResult<bool>.Succeeded(true) : Failure<bool>(result);
    }

    private System.Threading.Tasks.Task<AuthenticatedGetResult> SendAsync(string path, HttpMethod method, JsonObject body, long? version, string? key, CancellationToken cancellationToken) =>
        _executor.SendAsync(method, Uri(path), JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions), Correlation(), version is null ? null : $"\"v{version}\"", key, cancellationToken);
    private Uri Uri(string path) => new(_root + path, UriKind.Absolute);
    private static string Correlation() => Guid.NewGuid().ToString("D");
    private static string Key() => Guid.NewGuid().ToString("D");
    private static JsonNode? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : JsonValue.Create(value.Trim());

    private static DesktopWorkResult<T> Failure<T>(AuthenticatedGetResult result)
    {
        if (result is AuthenticatedGetResult.AuthenticationFailure) return new DesktopWorkResult<T>.AuthenticationFailure();
        if (result is AuthenticatedGetResult.ServerUnavailable || result is AuthenticatedGetResult.Response { StatusCode: >= HttpStatusCode.InternalServerError }) return new DesktopWorkResult<T>.ServerUnavailable();
        if (result is AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.Forbidden }) return new DesktopWorkResult<T>.Forbidden();
        if (result is AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.NotFound }) return new DesktopWorkResult<T>.NotFound();
        if (result is AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity } response) return new DesktopWorkResult<T>.ValidationFailure(Problem(response.Body));
        if (result is AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed }) return new DesktopWorkResult<T>.Conflict();
        return new DesktopWorkResult<T>.MalformedResponse();
    }

    private static string Problem(string body) { try { return JsonNode.Parse(body)?["title"]?.GetValue<string>() ?? "Проверьте данные."; } catch { return "Проверьте данные."; } }
    private static long? ReadVersion(string? tag) => tag is { Length: >= 4 } && tag.StartsWith("\"v", StringComparison.Ordinal) && tag.EndsWith('"') && long.TryParse(tag.AsSpan(2, tag.Length - 3), out var value) ? value : null;
    private static string? Text(JsonNode n, string name) => n[name]?.GetValue<string>();
    private static DesktopCatalogItem? MapCatalog(JsonNode n) => Guid.TryParse(Text(n, "id"), out var id) && n["version"]?.GetValue<long>() is > 0 and var version && Text(n, "name") is { Length: > 0 } name && Text(n, "itemType") is { Length: > 0 } type
        ? new(id, version, name, type, Text(n, "description"), Text(n, "fileExtension"), Text(n, "lifecycleState") ?? "active") : null;
    private static DesktopContact? MapContact(JsonNode n) => Guid.TryParse(Text(n, "id"), out var id) && n["version"]?.GetValue<long>() is > 0 and var version && Text(n, "displayName") is { Length: > 0 } display && Text(n, "firstName") is { Length: > 0 } first
        ? new(id, version, display, first, Text(n, "lastName"), Text(n, "notes"), Text(n, "status") ?? "active", Text(n, "lifecycleState") ?? "active") : null;
    private static DesktopSearchResult? MapSearch(JsonNode n) => Guid.TryParse(Text(n, "objectId"), out var id) && Text(n, "objectType") is { Length: > 0 } type && Text(n, "title") is { Length: > 0 } title && n["version"]?.GetValue<long>() is > 0 and var version && DateTimeOffset.TryParse(Text(n, "updatedAt"), out var updated)
        ? new(id, type, title, Guid.TryParse(Text(n, "parentObjectId"), out var parent) ? parent : null, version, updated) : null;
    private static DesktopNotification? MapNotification(JsonNode n) => Guid.TryParse(Text(n, "id"), out var id) && n["version"]?.GetValue<long>() is > 0 and var version && Text(n, "notificationType") is { Length: > 0 } notificationType && Text(n, "title") is { Length: > 0 } title && Text(n, "body") is { } body && DateTimeOffset.TryParse(Text(n, "notBefore"), out var at)
        ? new(id, version, notificationType, title, body, Text(n, "severity") ?? "info", Text(n, "status") ?? "pending", Guid.TryParse(Text(n, "sourceObjectId"), out var source) ? source : null, at) : null;
    private static DesktopFileLocation? MapLocation(JsonNode n) => Guid.TryParse(Text(n, "id"), out var id) && n["version"]?.GetValue<long>() is > 0 and var version && Text(n, "locationType") is { Length: > 0 } type && Text(n, "rawPath") is { Length: > 0 } path && n["canOpenOnDevice"] is JsonValue flag && flag.TryGetValue<bool>(out var canOpen)
        ? new(id, version, type, path, canOpen) : null;
}
