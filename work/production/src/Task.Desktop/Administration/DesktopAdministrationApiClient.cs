using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Task.Desktop.Security;
using Task.Desktop.Work;

namespace Task.Desktop.Administration;

public sealed record DesktopAdminUser(Guid Id, long Version, string DisplayName, string Login,
    string? JobTitle, string AccountStatus)
{
    public string StatusLabel => AccountStatus switch
    {
        "active" => "Активен",
        "blocked" => "Заблокирован",
        "deactivated" => "Деактивирован",
        _ => "Ожидает активации",
    };
    public string Initials => string.Concat(DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Take(2).Select(part => char.ToUpperInvariant(part[0])));
}

public sealed record DesktopAdminRole(Guid Id, string Code, string Name, bool IsSystem,
    IReadOnlyList<string> Permissions)
{
    public string KindLabel => IsSystem ? "Системная роль" : "Настраиваемая роль";
    public string PermissionSummary => Permissions.Count == 0
        ? "Разрешения не опубликованы"
        : string.Join(" · ", Permissions.Take(4));
}

public sealed record DesktopNetworkResource(Guid Id, long Version, string Name, string? RootPath,
    string? Description, string Status)
{
    public string PathLabel => string.IsNullOrWhiteSpace(RootPath)
        ? "Путь скрыт текущей областью доступа"
        : RootPath;
    public string StatusLabel => Status == "active" ? "Доступен" : "Ограничен";
}

public interface IDesktopAdministrationApiClient
{
    global::System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>> GetUsersAsync(CancellationToken cancellationToken = default);
    global::System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopAdminRole>>> GetRolesAsync(CancellationToken cancellationToken = default);
    global::System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopNetworkResource>>> GetResourcesAsync(CancellationToken cancellationToken = default);
}

public sealed class DesktopAdministrationApiClient : IDesktopAdministrationApiClient
{
    private readonly DesktopAuthenticatedGetExecutor _executor;
    private readonly string _root;

    public DesktopAdministrationApiClient(HttpClient httpClient, Uri serverEndpoint,
        SessionService sessionService, DesktopConnectivityService? connectivity = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(serverEndpoint);
        ArgumentNullException.ThrowIfNull(sessionService);
        _executor = new(httpClient, sessionService, connectivity);
        _root = serverEndpoint.AbsoluteUri.TrimEnd('/') + "/api/v1/";
    }

    public global::System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>> GetUsersAsync(CancellationToken cancellationToken = default) =>
        GetPageAsync("users?page=1&sort=id", MapUser, cancellationToken);

    public global::System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopAdminRole>>> GetRolesAsync(CancellationToken cancellationToken = default) =>
        GetPageAsync("roles?limit=200&page=1", MapRole, cancellationToken);

    public global::System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopNetworkResource>>> GetResourcesAsync(CancellationToken cancellationToken = default) =>
        GetPageAsync("network-resources?limit=200&page=1", MapResource, cancellationToken);

    private async global::System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<T>>> GetPageAsync<T>(
        string path, Func<JsonNode, T?> map, CancellationToken cancellationToken) where T : class
    {
        var result = await _executor.GetAsync(new Uri(_root + path), Guid.NewGuid().ToString("D"), cancellationToken).ConfigureAwait(false);
        if (result is not AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK } response)
            return Failure<IReadOnlyList<T>>(result);
        try
        {
            var root = JsonNode.Parse(response.Body);
            var items = root?["items"] as JsonArray ?? root as JsonArray;
            if (items is null || items.Count > 500) return new DesktopWorkResult<IReadOnlyList<T>>.MalformedResponse();
            var mapped = items.Select(item => item is null ? null : map(item)).ToArray();
            return mapped.Any(item => item is null)
                ? new DesktopWorkResult<IReadOnlyList<T>>.MalformedResponse()
                : new DesktopWorkResult<IReadOnlyList<T>>.Succeeded(mapped!);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return new DesktopWorkResult<IReadOnlyList<T>>.MalformedResponse();
        }
    }

    private static DesktopWorkResult<T> Failure<T>(AuthenticatedGetResult result) => result switch
    {
        AuthenticatedGetResult.AuthenticationFailure => new DesktopWorkResult<T>.AuthenticationFailure(),
        AuthenticatedGetResult.ServerUnavailable => new DesktopWorkResult<T>.ServerUnavailable(),
        AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.Forbidden } => new DesktopWorkResult<T>.Forbidden(),
        AuthenticatedGetResult.Response { StatusCode: >= HttpStatusCode.InternalServerError } => new DesktopWorkResult<T>.ServerUnavailable(),
        _ => new DesktopWorkResult<T>.MalformedResponse(),
    };

    private static DesktopAdminUser? MapUser(JsonNode node) =>
        Guid.TryParse(Text(node, "id"), out var id)
        && Long(node, "version") is > 0 and var version
        && Text(node, "displayName") is { Length: > 0 } name
        && Text(node, "login") is { Length: > 0 } login
        && Text(node, "accountStatus") is { Length: > 0 } status
            ? new(id, version, name, login, Text(node, "jobTitle"), status)
            : null;

    private static DesktopAdminRole? MapRole(JsonNode node) =>
        Guid.TryParse(Text(node, "id"), out var id)
        && Text(node, "code") is { Length: > 0 } code
        && (Text(node, "display_name") ?? Text(node, "displayName") ?? Text(node, "name")) is { Length: > 0 } name
            ? new(id, code, name, Bool(node, "is_system") ?? Bool(node, "isSystem") ?? false, ReadPermissions(node))
            : null;

    private static DesktopNetworkResource? MapResource(JsonNode node) =>
        Guid.TryParse(Text(node, "id"), out var id)
        && Long(node, "version") is > 0 and var version
        && Text(node, "name") is { Length: > 0 } name
            ? new(id, version, name, Text(node, "rootUncPath") ?? Text(node, "root_unc_path"),
                Text(node, "description"), Text(node, "status") ?? "active")
            : null;

    private static IReadOnlyList<string> ReadPermissions(JsonNode node)
    {
        if (node["permissions"] is not JsonArray array) return [];
        return array.Select(item => item?["code"]?.GetValue<string>())
            .Where(code => !string.IsNullOrWhiteSpace(code)).Cast<string>().ToArray();
    }

    private static string? Text(JsonNode node, string name) => node[name]?.GetValue<string>();
    private static long? Long(JsonNode node, string name) => node[name] is JsonValue value && value.TryGetValue<long>(out var result) ? result : null;
    private static bool? Bool(JsonNode node, string name) => node[name] is JsonValue value && value.TryGetValue<bool>(out var result) ? result : null;
}
