using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed partial class DesktopWorkApiClientTests
{
    [Fact]
    public async System.Threading.Tasks.Task LocationRequestsUseArrayContractParentEtagAndAllowedFields()
    {
        var resourceId = Guid.NewGuid();
        var responses = new Queue<HttpResponseMessage>([
            Tagged(HttpStatusCode.OK, $$"""[{"id":"{{LocationId}}","version":4,"locationType":"local_path","rawPath":"C:\\Work\\a.txt","priority":0,"isPrimary":true,"isEnabled":true,"canOpenOnDevice":true},{"id":"{{Guid.NewGuid()}}","version":1,"locationType":"unc_path","priority":1,"isPrimary":false,"isEnabled":false,"canOpenOnDevice":false,"networkResourceId":"{{resourceId}}"}]""", 7),
            Tagged(HttpStatusCode.OK, $$"""{"id":"{{ItemId}}","version":7,"name":"File","itemType":"file_reference","lifecycleState":"active"}""", 7),
            Tagged(HttpStatusCode.Created, "{\"version\":1}", 8),
            Tagged(HttpStatusCode.OK, "{\"version\":2}", 9),
            Tagged(HttpStatusCode.NoContent, "", 10),
        ]);
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(responses.Dequeue()));
        var list = Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopFileLocation>>.Succeeded>(await fixture.Client.GetLocationsAsync(ItemId));
        Assert.Equal(7, list.EntityVersion); Assert.Equal(2, list.Value.Count); Assert.Null(list.Value[1].RawPath);
        Assert.Contains("скрыт", list.Value[1].PathLabel);
        Assert.Equal(7, Assert.IsType<DesktopWorkResult<DesktopCatalogItem>.Succeeded>(await fixture.Client.GetCatalogItemAsync(ItemId)).Value.Version);
        var created = Assert.IsType<DesktopWorkResult<bool>.Succeeded>(await fixture.Client.CreateLocationAsync(ItemId, 7, new("unc_path", @"\\server\docs\a.txt", 3, true, false, resourceId)));
        Assert.Equal(8, created.EntityVersion);
        var patch = Assert.IsType<DesktopWorkResult<bool>.Succeeded>(await fixture.Client.PatchLocationAsync(ItemId, 8, LocationId, new("unc_path", null, 3, true, true)));
        Assert.Equal(9, patch.EntityVersion);
        var removed = Assert.IsType<DesktopWorkResult<bool>.Succeeded>(await fixture.Client.DeleteLocationAsync(ItemId, 9, LocationId)); Assert.Equal(10, removed.EntityVersion);
        Assert.Equal(new[] { HttpMethod.Get, HttpMethod.Get, HttpMethod.Post, HttpMethod.Patch, HttpMethod.Delete }, fixture.Requests.Select(r => r.Method));
        Assert.Equal(new[] { "\"v7\"", "\"v8\"", "\"v9\"" }, fixture.Requests.Skip(2).Select(r => r.IfMatch));
        Assert.All(fixture.Requests.Skip(2).Take(2), r => Assert.NotEmpty(r.IdempotencyKey!)); Assert.Null(fixture.Requests[4].IdempotencyKey);
        var createBody = JsonNode.Parse(fixture.Requests[2].Body)!.AsObject();
        Assert.Equal(resourceId.ToString(), createBody["networkResourceId"]!.ToString());
        Assert.False(createBody.ContainsKey("deviceId")); Assert.False(createBody.ContainsKey("ownerUserId")); Assert.False(createBody.ContainsKey("version"));
        var patchBody = JsonNode.Parse(fixture.Requests[3].Body)!.AsObject(); Assert.False(patchBody.ContainsKey("rawPath")); Assert.False(patchBody.ContainsKey("networkResourceId"));
        Assert.Equal($"/api/v1/catalog-items/{ItemId}/locations/{LocationId}", fixture.Requests[4].Uri.AbsolutePath);
    }

    [Fact]
    public async System.Threading.Tasks.Task NetworkResourcePickerLoadsAllAllowedPagesAndKeepsRedactedRootHidden()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var responses = new Queue<HttpResponseMessage>([
            Json(HttpStatusCode.OK, $$"""{"items":[{"id":"{{first}}","version":1,"name":"Docs","status":"active","lifecycleState":"active"}],"hasMore":true,"nextCursor":"next page"}"""),
            Json(HttpStatusCode.OK, $$"""{"items":[{"id":"{{second}}","version":2,"name":"Other","status":"active","lifecycleState":"active","rootUncPath":"\\\\server\\other"}],"hasMore":false,"nextCursor":null}"""),
        ]);
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(responses.Dequeue()));
        var resources = Assert.IsType<DesktopWorkResult<IReadOnlyList<Task.Desktop.Administration.DesktopNetworkResource>>.Succeeded>(await fixture.Client.GetNetworkResourcesAsync()).Value;
        Assert.Equal(2, resources.Count); Assert.Null(resources[0].RootPath); Assert.Contains("скрыт", resources[0].PathLabel);
        Assert.Contains("cursor=next%20page", fixture.Requests[1].Uri.Query);
    }

    [Theory]
    [InlineData(403, "FORBIDDEN", "forbidden")]
    [InlineData(404, "OBJECT_NOT_VISIBLE", "missing")]
    [InlineData(412, "VERSION_CONFLICT", "conflict")]
    [InlineData(422, "VALIDATION_ERROR", "validation")]
    [InlineData(503, "INTERNAL_ERROR", "offline")]
    public async System.Threading.Tasks.Task LocationErrorsMapToControlledRussianFeedback(int status, string code, string expected)
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(Json((HttpStatusCode)status, $$"""{"code":"{{code}}","title":"Internal detail"}""")));
        var result = await fixture.Client.CreateLocationAsync(ItemId, 1, new("local_path", @"C:\a.txt", 0, true, true));
        switch (expected)
        {
            case "forbidden": Assert.IsType<DesktopWorkResult<bool>.Forbidden>(result); break;
            case "missing": Assert.IsType<DesktopWorkResult<bool>.NotFound>(result); break;
            case "conflict": Assert.IsType<DesktopWorkResult<bool>.Conflict>(result); break;
            case "offline": Assert.IsType<DesktopWorkResult<bool>.ServerUnavailable>(result); break;
            default: Assert.Contains("разрешённого ресурса", Assert.IsType<DesktopWorkResult<bool>.ValidationFailure>(result).Message); break;
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task InvalidUncCannotBeSentWithoutNetworkResourceAndMalformedListIsRejected()
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(Json(HttpStatusCode.OK, "{\"items\":[]}")));
        Assert.IsType<DesktopWorkResult<bool>.ValidationFailure>(await fixture.Client.CreateLocationAsync(ItemId, 1, new("unc_path", @"\\server\docs\a.txt", 0, true, true)));
        Assert.Empty(fixture.Requests);
        Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopFileLocation>>.MalformedResponse>(await fixture.Client.GetLocationsAsync(ItemId));
    }

    [Fact]
    public async System.Threading.Tasks.Task TimeoutAndNetworkFailureAreUnavailable()
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => throw new System.Net.Http.HttpRequestException("unreachable"));
        Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopFileLocation>>.ServerUnavailable>(await fixture.Client.GetLocationsAsync(ItemId));
        await using var timeout = await Fixture.CreateAsync((_, _) => throw new TaskCanceledException("timeout"));
        Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopFileLocation>>.ServerUnavailable>(await timeout.Client.GetLocationsAsync(ItemId));
    }

    private static HttpResponseMessage Tagged(HttpStatusCode status, string body, long version)
    { var response = Json(status, body); response.Headers.ETag = new($"\"v{version}\""); return response; }

    [Fact]
    public async System.Threading.Tasks.Task WorkHubSelectionUsesLocationClientAndCapabilityRefreshClearsResourceRoots()
    {
        await using var fixture = await Fixture.CreateAsync((request, _) => System.Threading.Tasks.Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/catalog/tree" => Json(HttpStatusCode.OK, $$"""{"items":[{"id":"{{ItemId}}","version":1,"name":"File","itemType":"file_reference","lifecycleState":"active"}],"hasMore":false}"""),
            "/api/v1/network-resources" => Json(HttpStatusCode.OK, $$"""{"items":[{"id":"{{Guid.NewGuid()}}","version":1,"name":"Resource","rootUncPath":"\\\\server\\sensitive","status":"active","lifecycleState":"active"}],"hasMore":false}"""),
            _ => Tagged(HttpStatusCode.OK, "[]", 1),
        }));
        string[] caps = ["FileCatalog.Read", "FileReference.Open", "FileLocation.Update", "FileLocation.ReadSensitivePath"];
        using var vm = new Task.Desktop.ViewModels.WorkHubViewModel(fixture.Client, caps);
        vm.Activate(Task.Desktop.ViewModels.WorkHubArea.Catalog);
        for (var i = 0; i < 100 && vm.IsLoading; i++) await System.Threading.Tasks.Task.Delay(5);
        Assert.True(await vm.SelectCatalogItemAsync(ItemId));
        var locations = Assert.IsType<Task.Desktop.ViewModels.FileLocationsViewModel>(vm.Locations); await locations.SelectionLoad;
        Assert.True(locations.IsEmpty); Assert.Contains(fixture.Requests, r => r.Uri.AbsolutePath.EndsWith("/locations"));
        await locations.AddCommand.ExecuteAsync(); Assert.Equal(@"\\server\sensitive", Assert.Single(locations.NetworkResources).RootPath);
        vm.UpdateCapabilities(caps.Where(c => c != "FileLocation.ReadSensitivePath")); await locations.SelectionLoad;
        Assert.Empty(locations.NetworkResources); Assert.Null(locations.NetworkResource); Assert.Equal("", locations.RawPath); Assert.False(locations.EditorVisible);
        vm.UpdateSessionState(false); Assert.Empty(locations.Items); Assert.False(locations.AddCommand.CanExecute(null));
        vm.UpdateSessionState(true); await locations.SelectionLoad; Assert.True(locations.AddCommand.CanExecute(null));
    }
}
