using System.Net;
using System.Text.Json.Nodes;
using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;
using PageResult = Task.Desktop.Work.DesktopWorkResult<System.Collections.Generic.IReadOnlyList<Task.Desktop.Work.DesktopSearchResult>>;

namespace Task.Desktop.Tests.Work;

public sealed partial class DesktopWorkApiClientTests
{
    private static string TargetBody(string type = "catalog_item") => new JsonObject
    {
        ["items"] = new JsonArray(new JsonObject
        {
            ["objectId"] = ItemId, ["objectType"] = type, ["title"] = "План", ["version"] = 1,
            ["updatedAt"] = "2026-10-17T10:00:00Z", ["lifecycleState"] = "active",
        }), ["nextCursor"] = null,
    }.ToJsonString();

    [Fact]
    public async AsyncTask TargetSearchUsesExistingAuthenticatedApiAndPreservesGlobalSearch()
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => AsyncTask.FromResult(Json(HttpStatusCode.OK, TargetBody("contact"))));
        var result = await fixture.Client.SearchTargetsAsync("  План & +  ", ["company", "contact"], "snapshot.50");
        Assert.Equal(ItemId, Assert.Single(Assert.IsType<PageResult.Succeeded>(result).Value).ObjectId);
        var request = Assert.Single(fixture.Requests); var query = Uri.UnescapeDataString(request.Uri.Query);
        Assert.Equal("/api/v1/search", request.Uri.AbsolutePath); Assert.Equal("AT_initial", request.Bearer);
        Assert.Contains("q=План & +", query); Assert.Contains("types=company,contact", query);
        Assert.Contains("limit=50", query); Assert.Contains("cursor=snapshot.50", query);
        await fixture.Client.SearchAsync("План");
        Assert.DoesNotContain("types=", fixture.Requests[1].Uri.Query); Assert.Contains("limit=100", fixture.Requests[1].Uri.Query);
    }

    [Theory]
    [InlineData("file_location")][InlineData("emptyId")][InlineData("inactive")][InlineData("badJson")]
    [InlineData("arrayRoot")][InlineData("scalarHit")][InlineData("invalidVersion")][InlineData("tooMany")]
    [InlineData("badCursor")][InlineData("repeatCursor")][InlineData("missingLifecycle")]
    public async AsyncTask TargetSearchRejectsMalformedWirePayloads(string kind)
    {
        var node = JsonNode.Parse(TargetBody())!; var row = node["items"]![0]!;
        if (kind == "file_location") row["objectType"] = "file_location";
        if (kind == "emptyId") row["objectId"] = Guid.Empty;
        if (kind == "inactive") row["lifecycleState"] = "trashed";
        if (kind == "missingLifecycle") row.AsObject().Remove("lifecycleState");
        if (kind == "invalidVersion") row["version"] = "one";
        if (kind == "tooMany") node["items"] = new JsonArray(Enumerable.Range(0, 51).Select(_ => row.DeepClone()).ToArray());
        if (kind == "scalarHit") node["items"] = new JsonArray(42);
        if (kind == "badCursor") node["nextCursor"] = new JsonObject();
        if (kind == "repeatCursor") node["nextCursor"] = "snapshot.50";
        var body = kind == "badJson" ? "{" : kind == "arrayRoot" ? "[]" : node.ToJsonString();
        await using var fixture = await Fixture.CreateAsync((_, _) => AsyncTask.FromResult(Json(HttpStatusCode.OK, body)));
        Assert.IsType<PageResult.MalformedResponse>(await fixture.Client.SearchTargetsAsync("План", ["catalog_item"], "snapshot.50"));
    }

    [Fact]
    public async AsyncTask TargetSearchRejectsInvalidRequestsWithoutTransportAndMapsForbiddenUnavailableAndExpiredCursor()
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => AsyncTask.FromResult(Json(HttpStatusCode.Forbidden, "{}")));
        Assert.IsType<PageResult.ValidationFailure>(await fixture.Client.SearchTargetsAsync("П", ["contact"], null));
        Assert.IsType<PageResult.ValidationFailure>(await fixture.Client.SearchTargetsAsync("План", ["file_location"], null));
        Assert.IsType<PageResult.ValidationFailure>(await fixture.Client.SearchTargetsAsync("План", [], null));
        Assert.Empty(fixture.Requests);
        Assert.IsType<PageResult.Forbidden>(await fixture.Client.SearchTargetsAsync("План", ["catalog_item"], null));
        await using var unavailable = await Fixture.CreateAsync((_, _) => AsyncTask.FromResult(Json(HttpStatusCode.ServiceUnavailable, "{}")));
        Assert.IsType<PageResult.ServerUnavailable>(await unavailable.Client.SearchTargetsAsync("План", ["catalog_item"], null));
        await using var unauthorized = await Fixture.CreateAsync((_, _) => AsyncTask.FromResult(Json(HttpStatusCode.Unauthorized, "{}")),
            () => Json(HttpStatusCode.Unauthorized, """{"code":"SESSION_REVOKED"}"""));
        Assert.IsType<PageResult.AuthenticationFailure>(await unauthorized.Client.SearchTargetsAsync("План", ["catalog_item"], null));
        await using var expired = await Fixture.CreateAsync((_, _) => AsyncTask.FromResult(Json(HttpStatusCode.Gone, """{"code":"SEARCH_CURSOR_EXPIRED"}""")));
        Assert.IsType<PageResult.Conflict>(await expired.Client.SearchTargetsAsync("План", ["catalog_item"], "snapshot.50"));
    }
}
