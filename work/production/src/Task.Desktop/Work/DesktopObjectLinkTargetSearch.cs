using System.Text.Json.Nodes;

namespace Task.Desktop.Work;

/// <summary>Corporate, server-filtered search; never falls back to unfiltered directory reads.</summary>
public interface IDesktopObjectLinkTargetSearchClient
{
    System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>> SearchTargetsAsync(
        string query, IReadOnlyCollection<string> types, string? cursor, CancellationToken cancellationToken = default);
}

public sealed partial class DesktopWorkApiClient : IDesktopObjectLinkTargetSearchClient
{
    public async System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>> SearchTargetsAsync(
        string query, IReadOnlyCollection<string> types, string? cursor, CancellationToken cancellationToken = default)
    {
        query = query?.Trim() ?? "";
        if (query.Length is < 2 or > 200 || types is null || types.Count is < 1 or > 8
            || types.Any(type => type is not ("catalog_item" or "contact" or "company" or "task" or "project" or "calendar_event" or "interaction" or "employee_profile"))
            || cursor is not null && (string.IsNullOrWhiteSpace(cursor) || cursor.Length > 256))
            return new DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.ValidationFailure("Проверьте условия поиска.");
        var allowed = types.ToHashSet(StringComparer.Ordinal);
        var path = "search?q=" + System.Uri.EscapeDataString(query) + "&types="
            + System.Uri.EscapeDataString(string.Join(',', allowed.Order(StringComparer.Ordinal))) + "&limit=50";
        var result = await GetPageAsync(path, MapTarget, cancellationToken, cursor).ConfigureAwait(false);
        return result is DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.Succeeded page
            && (page.Value.Count > 50 || page.NextCursor is { Length: > 256 })
            ? new DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.MalformedResponse() : result;

        DesktopSearchResult? MapTarget(JsonNode node) => node is JsonObject && Text(node, "lifecycleState") == "active"
            && MapSearch(node) is { ObjectId: var id, Title.Length: <= 500 } hit && id != Guid.Empty
            && allowed.Contains(hit.ObjectType) ? hit : null;
    }
}
