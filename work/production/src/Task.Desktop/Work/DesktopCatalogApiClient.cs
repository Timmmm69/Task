using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Task.Desktop.Security;

namespace Task.Desktop.Work;

public sealed partial class DesktopWorkApiClient
{
    public async System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopCatalogItem>>> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<DesktopCatalogItem>();
        var seen = new HashSet<Guid>();
        var parents = new Queue<Guid?>();
        parents.Enqueue(null);
        while (parents.TryDequeue(out var parent))
        {
            var path = "catalog/tree?depth=1&limit=200" + (parent is null ? "" : $"&parentId={parent:D}");
            string? cursor = null;
            var cursors = new HashSet<string>(StringComparer.Ordinal);
            do
            {
                var result = await _executor.GetAsync(Uri(path + (cursor is null ? "" : "&cursor=" + System.Uri.EscapeDataString(cursor))), Correlation(), cancellationToken).ConfigureAwait(false);
                if (result is not AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK } response)
                    return CatalogFailure<IReadOnlyList<DesktopCatalogItem>>(result);
                try
                {
                    var page = JsonNode.Parse(response.Body);
                    if (page?["items"] is not JsonArray rows || rows.Count > 200)
                        return new DesktopWorkResult<IReadOnlyList<DesktopCatalogItem>>.MalformedResponse();
                    foreach (var row in rows)
                    {
                        var item = row is null ? null : MapCatalog(row);
                        if (item is null || item.Id == Guid.Empty || item.ParentId != parent || !seen.Add(item.Id))
                            return new DesktopWorkResult<IReadOnlyList<DesktopCatalogItem>>.MalformedResponse();
                        items.Add(item);
                        if (item.ItemType == "virtual_folder") parents.Enqueue(item.Id);
                    }
                    var more = page["hasMore"]?.GetValue<bool>();
                    cursor = Text(page!, "nextCursor");
                    if (more is null || more.Value && (string.IsNullOrWhiteSpace(cursor) || rows.Count == 0 || !cursors.Add(cursor)))
                        return new DesktopWorkResult<IReadOnlyList<DesktopCatalogItem>>.MalformedResponse();
                    if (!more.Value) cursor = null;
                }
                catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
                { return new DesktopWorkResult<IReadOnlyList<DesktopCatalogItem>>.MalformedResponse(); }
            } while (cursor is not null);
        }
        return new DesktopWorkResult<IReadOnlyList<DesktopCatalogItem>>.Succeeded(items);
    }

    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopCatalogItem>> MoveCatalogItemAsync(Guid id, long version, Guid? parentId, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty || version is < 1 or > int.MaxValue || parentId == id || parentId == Guid.Empty)
            return System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopCatalogItem>>(new DesktopWorkResult<DesktopCatalogItem>.ValidationFailure("Выберите допустимую папку назначения."));
        return SendEntityAsync($"catalog-items/{id:D}/move", HttpMethod.Post,
            new JsonObject { ["parentItemId"] = parentId, ["sortOrder"] = 0 }, MapCatalog, version, Key(), cancellationToken, catalog: true);
    }
    private static DesktopWorkResult<T> CatalogFailure<T>(AuthenticatedGetResult result)
    {
        if (result is AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity } response)
            return new DesktopWorkResult<T>.ValidationFailure("Проверьте название и данные записи. Родителем может быть только доступная активная виртуальная папка.");
        return Failure<T>(result);
    }

}
