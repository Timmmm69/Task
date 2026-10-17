using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Task.Desktop.Security;

namespace Task.Desktop.Work;

public sealed record DesktopObjectLink(Guid Id, Guid SourceObjectId, Guid TargetObjectId, string LinkType, string? RelatedTitle = null)
{
    public string Title => RelatedTitle ?? TargetObjectId.ToString("D");
    public string TypeLabel => LinkType switch { "task_file" => "Файл задачи", "project_file" => "Файл проекта", "contact_file" => "Файл контакта", "task_contact" => "Контакт задачи", "project_contact" => "Контакт проекта", "task_project" => "Проект задачи", "related" => "Связанный объект", _ => "Связь" };
}
public sealed record DesktopObjectLinks(IReadOnlyList<DesktopObjectLink> Items, long SourceVersion);
public interface IDesktopObjectLinksClient
{
    System.Threading.Tasks.Task<DesktopWorkResult<DesktopObjectLinks>> GetLinksAsync(Guid source, CancellationToken ct = default);
    System.Threading.Tasks.Task<DesktopWorkResult<DesktopObjectLinks>> AddLinkAsync(Guid source, long version, Guid target, string type, CancellationToken ct = default);
    System.Threading.Tasks.Task<DesktopWorkResult<DesktopObjectLinks>> RemoveLinkAsync(Guid source, long version, Guid linkId, CancellationToken ct = default);
}

public sealed partial class DesktopWorkApiClient : IDesktopObjectLinksClient
{
    public async System.Threading.Tasks.Task<DesktopWorkResult<DesktopObjectLinks>> GetLinksAsync(Guid source, CancellationToken ct = default)
    {
        var links = new List<DesktopObjectLink>();
        var ids = new HashSet<Guid>();
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        long? sourceVersion = null;
        do
        {
            ct.ThrowIfCancellationRequested();
            var path = $"objects/{source:D}/links?limit=200" + (cursor is null ? "" : "&cursor=" + System.Uri.EscapeDataString(cursor));
            var result = await _executor.GetAsync(Uri(path), Correlation(), ct).ConfigureAwait(false);
            if (result is AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.Gone })
                return new DesktopWorkResult<DesktopObjectLinks>.Conflict();
            if (result is not AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK } response) return LinkFailure(result);
            try
            {
                var node = JsonNode.Parse(response.Body);
                if (node is not JsonObject || node["items"] is not JsonArray items || items.Count > 200
                    || ReadVersion(response.EntityTag) is not { } version || version < 1)
                    return new DesktopWorkResult<DesktopObjectLinks>.MalformedResponse();
                if (sourceVersion is { } expected && version != expected)
                    return new DesktopWorkResult<DesktopObjectLinks>.Conflict();
                sourceVersion = version;
                var more = node["hasMore"]?.GetValue<bool>() ?? false;
                cursor = Text(node, "nextCursor");
                if (more != (cursor is not null) || cursor is not null && (string.IsNullOrWhiteSpace(cursor)
                    || cursor.Length > 2048 || !cursors.Add(cursor)) || more && items.Count == 0)
                    return new DesktopWorkResult<DesktopObjectLinks>.MalformedResponse();
                foreach (var item in items)
                {
                    if (item is not JsonObject || !Guid.TryParse(Text(item, "id"), out var id) || id == Guid.Empty || !ids.Add(id)
                        || !Guid.TryParse(Text(item, "sourceObjectId"), out var from) || from == Guid.Empty
                        || !Guid.TryParse(Text(item, "targetObjectId"), out var target) || target == Guid.Empty
                        || from != source && target != source || Text(item, "linkType") is not { Length: > 0 } type)
                        return new DesktopWorkResult<DesktopObjectLinks>.MalformedResponse();
                    links.Add(new(id, from, target, type));
                }
            }
            catch (Exception e) when (e is System.Text.Json.JsonException or FormatException or InvalidOperationException or ArgumentException)
            { return new DesktopWorkResult<DesktopObjectLinks>.MalformedResponse(); }
        }
        while (cursor is not null);
        return new DesktopWorkResult<DesktopObjectLinks>.Succeeded(new(links, sourceVersion!.Value), sourceVersion);
    }
    public async System.Threading.Tasks.Task<DesktopWorkResult<DesktopObjectLinks>> AddLinkAsync(Guid source, long version, Guid target, string type, CancellationToken ct = default)
    {
        var result = await SendAsync($"objects/{source:D}/links", HttpMethod.Post,
            new JsonObject { ["sourceObjectId"] = source, ["targetObjectId"] = target, ["linkType"] = type }, version, Key(), ct).ConfigureAwait(false);
        return result is AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.Created } ? await GetLinksAsync(source, ct).ConfigureAwait(false) : LinkFailure(result);
    }
    public async System.Threading.Tasks.Task<DesktopWorkResult<DesktopObjectLinks>> RemoveLinkAsync(Guid source, long version, Guid linkId, CancellationToken ct = default)
    {
        var result = await SendAsync($"objects/{source:D}/links/{linkId:D}", HttpMethod.Delete, new JsonObject(), version, Key(), ct).ConfigureAwait(false);
        return result is AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK } ? await GetLinksAsync(source, ct).ConfigureAwait(false) : LinkFailure(result);
    }
    private static DesktopWorkResult<DesktopObjectLinks> LinkFailure(AuthenticatedGetResult result)
    {
        if (result is AuthenticatedGetResult.Response response)
        {
            var code = ErrorCode(response.Body);
            if (response.StatusCode == HttpStatusCode.PreconditionFailed || code == "VERSION_CONFLICT")
                return new DesktopWorkResult<DesktopObjectLinks>.Conflict();
            if (response.StatusCode == HttpStatusCode.Conflict && code == "CONFLICT")
                return new DesktopWorkResult<DesktopObjectLinks>.ValidationFailure(ObjectLinkMessages.Duplicate);
            if (code == "INVALID_STATE_TRANSITION")
                return new DesktopWorkResult<DesktopObjectLinks>.ValidationFailure(ObjectLinkMessages.InvalidState);
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
                return new DesktopWorkResult<DesktopObjectLinks>.ValidationFailure(ObjectLinkMessages.InvalidTarget);
        }
        return Failure<DesktopObjectLinks>(result);
    }

}


internal static class ObjectLinkMessages
{
    internal const string Duplicate = "Такая связь уже существует. Обновите связи.";
    internal const string InvalidState = "Состояние объекта не допускает изменение связей. Обновите объект.";
    internal const string InvalidTarget = "Объект или тип связи недопустим. Повторите выбор активного объекта и обновите связи.";
}
