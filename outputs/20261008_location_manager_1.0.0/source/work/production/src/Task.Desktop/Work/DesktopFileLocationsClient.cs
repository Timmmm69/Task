using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Task.Desktop.Administration;
using Task.Desktop.Security;

namespace Task.Desktop.Work;

public sealed record DesktopLocationDraft(string LocationType, string? RawPath, int Priority,
    bool IsEnabled, bool IsPrimary, Guid? NetworkResourceId = null);

public interface IDesktopFileLocationsClient
{
    System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopFileLocation>>> GetLocationsAsync(Guid itemId, CancellationToken ct = default);
    System.Threading.Tasks.Task<DesktopWorkResult<DesktopCatalogItem>> GetCatalogItemAsync(Guid itemId, CancellationToken ct = default);
    System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopNetworkResource>>> GetNetworkResourcesAsync(CancellationToken ct = default);
    System.Threading.Tasks.Task<DesktopWorkResult<bool>> CreateLocationAsync(Guid itemId, long version, DesktopLocationDraft draft, CancellationToken ct = default);
    System.Threading.Tasks.Task<DesktopWorkResult<bool>> PatchLocationAsync(Guid itemId, long version, Guid locationId, DesktopLocationDraft draft, CancellationToken ct = default);
    System.Threading.Tasks.Task<DesktopWorkResult<bool>> DeleteLocationAsync(Guid itemId, long version, Guid locationId, CancellationToken ct = default);
}

public sealed partial class DesktopWorkApiClient : IDesktopFileLocationsClient
{
    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopCatalogItem>> GetCatalogItemAsync(Guid itemId, CancellationToken ct = default) =>
        GetEntityAsync($"catalog-items/{itemId:D}", MapCatalog, ct);

    public async System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopFileLocation>>> GetLocationsAsync(Guid itemId, CancellationToken ct = default)
    {
        var result = await _executor.GetAsync(Uri($"catalog-items/{itemId:D}/locations"), Correlation(), ct).ConfigureAwait(false);
        if (result is not AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK } response)
            return LocationFailure<IReadOnlyList<DesktopFileLocation>>(result);
        try
        {
            if (JsonNode.Parse(response.Body) is not JsonArray rows)
                return new DesktopWorkResult<IReadOnlyList<DesktopFileLocation>>.MalformedResponse();
            var items = rows.Select(n => n is null ? null : MapRegisteredLocation(n)).ToArray();
            if (items.Any(n => n is null) || items.Select(n => n!.Id).Distinct().Count() != items.Length)
                return new DesktopWorkResult<IReadOnlyList<DesktopFileLocation>>.MalformedResponse();
            return new DesktopWorkResult<IReadOnlyList<DesktopFileLocation>>.Succeeded(items!, ReadVersion(response.EntityTag));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        { return new DesktopWorkResult<IReadOnlyList<DesktopFileLocation>>.MalformedResponse(); }
    }

    public async System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopNetworkResource>>> GetNetworkResourcesAsync(CancellationToken ct = default)
    {
        var items = new List<DesktopNetworkResource>();
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            var path = "network-resources?limit=200&status=active" + (cursor is null ? "" : "&cursor=" + System.Uri.EscapeDataString(cursor));
            var result = await _executor.GetAsync(Uri(path), Correlation(), ct).ConfigureAwait(false);
            if (result is not AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK } response)
                return LocationFailure<IReadOnlyList<DesktopNetworkResource>>(result);
            try
            {
                var page = JsonNode.Parse(response.Body);
                if (page?["items"] is not JsonArray rows || Bool(page, "hasMore") is not { } more)
                    return new DesktopWorkResult<IReadOnlyList<DesktopNetworkResource>>.MalformedResponse();
                foreach (var n in rows)
                {
                    if (n is null || !Guid.TryParse(Text(n, "id"), out var id) || id == Guid.Empty ||
                        Text(n, "name") is not { Length: > 0 } name || n["version"]?.GetValue<long>() is not (> 0 and var version))
                        return new DesktopWorkResult<IReadOnlyList<DesktopNetworkResource>>.MalformedResponse();
                    if (Text(n, "status") == "active" && Text(n, "lifecycleState") == "active")
                        items.Add(new(id, version, name, Text(n, "rootUncPath"), Text(n, "description"), "active"));
                }
                cursor = more ? Text(page, "nextCursor") : null;
                if (more && (string.IsNullOrWhiteSpace(cursor) || rows.Count == 0 || !cursors.Add(cursor)))
                    return new DesktopWorkResult<IReadOnlyList<DesktopNetworkResource>>.MalformedResponse();
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
            { return new DesktopWorkResult<IReadOnlyList<DesktopNetworkResource>>.MalformedResponse(); }
        } while (cursor is not null);
        return new DesktopWorkResult<IReadOnlyList<DesktopNetworkResource>>.Succeeded(items);
    }

    public System.Threading.Tasks.Task<DesktopWorkResult<bool>> CreateLocationAsync(Guid itemId, long version, DesktopLocationDraft draft, CancellationToken ct = default) =>
        ChangeLocationAsync(itemId, version, null, draft, ct);
    public System.Threading.Tasks.Task<DesktopWorkResult<bool>> PatchLocationAsync(Guid itemId, long version, Guid locationId, DesktopLocationDraft draft, CancellationToken ct = default) =>
        ChangeLocationAsync(itemId, version, locationId, draft, ct);

    private async System.Threading.Tasks.Task<DesktopWorkResult<bool>> ChangeLocationAsync(Guid itemId, long version, Guid? locationId, DesktopLocationDraft draft, CancellationToken ct)
    {
        if (itemId == Guid.Empty || version is < 1 or > int.MaxValue || locationId == Guid.Empty ||
            draft.LocationType is not ("local_path" or "mapped_drive" or "unc_path") ||
            draft.RawPath is { Length: > 4096 } || locationId is null && string.IsNullOrWhiteSpace(draft.RawPath) ||
            (draft.RawPath is not null && draft.LocationType == "unc_path" && (draft.NetworkResourceId is null || draft.NetworkResourceId == Guid.Empty)))
            return new DesktopWorkResult<bool>.ValidationFailure("Укажите путь и разрешённый сетевой ресурс для сетевого расположения.");
        var body = new JsonObject { ["priority"] = draft.Priority, ["isEnabled"] = draft.IsEnabled, ["isPrimary"] = draft.IsPrimary };
        // A hidden path stays absent on PATCH. Never reconstruct it or send a placeholder.
        if (draft.RawPath is not null)
        {
            body["locationType"] = draft.LocationType;
            body["rawPath"] = draft.RawPath.Trim();
            body["networkResourceId"] = draft.LocationType == "unc_path" ? draft.NetworkResourceId : null;
        }
        var path = $"catalog-items/{itemId:D}/locations" + (locationId is { } id ? $"/{id:D}" : "");
        var result = await SendAsync(path, locationId is null ? HttpMethod.Post : HttpMethod.Patch, body, version, Key(), ct).ConfigureAwait(false);
        return LocationMutationResult(result);
    }

    public async System.Threading.Tasks.Task<DesktopWorkResult<bool>> DeleteLocationAsync(Guid itemId, long version, Guid locationId, CancellationToken ct = default)
    {
        if (itemId == Guid.Empty || locationId == Guid.Empty || version is < 1 or > int.MaxValue)
            return new DesktopWorkResult<bool>.ValidationFailure("Выберите расположение для удаления.");
        return LocationMutationResult(await SendAsync($"catalog-items/{itemId:D}/locations/{locationId:D}", HttpMethod.Delete, new JsonObject(), version, null, ct).ConfigureAwait(false));
    }

    private static DesktopWorkResult<bool> LocationMutationResult(AuthenticatedGetResult result) =>
        result is AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK or HttpStatusCode.Created or HttpStatusCode.NoContent } response
            ? new DesktopWorkResult<bool>.Succeeded(true, ReadVersion(response.EntityTag)) : LocationFailure<bool>(result);

    private static DesktopFileLocation? MapRegisteredLocation(JsonNode n) =>
        Guid.TryParse(Text(n, "id"), out var id) && id != Guid.Empty && n["version"]?.GetValue<long>() is > 0 and var version &&
        Text(n, "locationType") is "local_path" or "mapped_drive" or "unc_path" &&
        Bool(n, "isPrimary") is { } primary && Bool(n, "isEnabled") is { } enabled &&
        Bool(n, "canOpenOnDevice") is { } canOpen && Int(n, "priority") is { } priority
            ? new(id, version, Text(n, "locationType")!, Text(n, "rawPath"), canOpen, primary, enabled, priority,
                n["networkResourceId"] is null ? null : Guid.Parse(Text(n, "networkResourceId")!),
                n["deviceId"] is null ? null : Guid.Parse(Text(n, "deviceId")!)) : null;

    private static DesktopWorkResult<T> LocationFailure<T>(AuthenticatedGetResult result)
    {
        if (result is AuthenticatedGetResult.Response response)
        {
            var code = ErrorCode(response.Body);
            if (code == "VERSION_CONFLICT") return new DesktopWorkResult<T>.Conflict();
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
                return new DesktopWorkResult<T>.ValidationFailure("Проверьте абсолютный путь, тип и приоритет. Сетевой путь должен находиться внутри выбранного разрешённого ресурса; ресурс должен быть доступен и включён.");
            if (response.StatusCode == HttpStatusCode.Forbidden)
                return new DesktopWorkResult<T>.Forbidden();
        }
        return Failure<T>(result);
    }
}
