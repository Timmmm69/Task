using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Task.Desktop.Security;

namespace Task.Desktop.Work;

public sealed record DesktopObjectLink(Guid Id, Guid SourceObjectId, Guid TargetObjectId, string LinkType, string? RelatedTitle = null)
{
    public string Title => RelatedTitle ?? TargetObjectId.ToString("D");
    public string TypeLabel => LinkType switch { "task_file" => "Файл задачи", "project_file" => "Файл проекта", "contact_file" => "Файл контакта", "task_contact" => "Контакт задачи", _ => "Связь" };
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
        var result = await _executor.GetAsync(Uri($"objects/{source:D}/links?limit=200"), Correlation(), ct).ConfigureAwait(false);
        if (result is not AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK } response) return Failure<DesktopObjectLinks>(result);
        try
        {
            var node = JsonNode.Parse(response.Body);
            if (node?["items"] is not JsonArray items || items.Count > 200 || node["hasMore"]?.GetValue<bool>() == true || ReadVersion(response.EntityTag) is not { } version)
                return new DesktopWorkResult<DesktopObjectLinks>.MalformedResponse();
            var links = items.Select(n => new DesktopObjectLink(Guid.Parse(Text(n!, "id")!), Guid.Parse(Text(n!, "sourceObjectId")!), Guid.Parse(Text(n!, "targetObjectId")!), Text(n!, "linkType")!)).ToArray();
            return new DesktopWorkResult<DesktopObjectLinks>.Succeeded(new(links, version), version);
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or FormatException or InvalidOperationException or ArgumentException)
        { return new DesktopWorkResult<DesktopObjectLinks>.MalformedResponse(); }
    }
    public async System.Threading.Tasks.Task<DesktopWorkResult<DesktopObjectLinks>> AddLinkAsync(Guid source, long version, Guid target, string type, CancellationToken ct = default)
    {
        var result = await SendAsync($"objects/{source:D}/links", HttpMethod.Post,
            new JsonObject { ["sourceObjectId"] = source, ["targetObjectId"] = target, ["linkType"] = type }, version, Key(), ct).ConfigureAwait(false);
        return result is AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.Created } ? await GetLinksAsync(source, ct).ConfigureAwait(false) : Failure<DesktopObjectLinks>(result);
    }
    public async System.Threading.Tasks.Task<DesktopWorkResult<DesktopObjectLinks>> RemoveLinkAsync(Guid source, long version, Guid linkId, CancellationToken ct = default)
    {
        var result = await SendAsync($"objects/{source:D}/links/{linkId:D}", HttpMethod.Delete, new JsonObject(), version, Key(), ct).ConfigureAwait(false);
        return result is AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK } ? await GetLinksAsync(source, ct).ConfigureAwait(false) : Failure<DesktopObjectLinks>(result);
    }
}
