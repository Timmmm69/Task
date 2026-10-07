using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed partial class DesktopWorkApiClientTests
{
    private static JsonObject CatalogJson(Guid id, string type = "file_reference", Guid? parent = null, long version = 1) => new()
    {
        ["id"] = id,
        ["version"] = version,
        ["name"] = "Item " + id,
        ["itemType"] = type,
        ["lifecycleState"] = "active",
        ["parentItemId"] = parent,
    };
    private static HttpResponseMessage CatalogPage(IEnumerable<JsonObject> items, string? cursor = null) => Json(HttpStatusCode.OK,
        new JsonObject { ["items"] = new JsonArray(items.ToArray<JsonNode?>()), ["hasMore"] = cursor is not null, ["nextCursor"] = cursor }.ToJsonString());

    [Fact]
    public async System.Threading.Tasks.Task CatalogLoadsAllPagesAndMoreThanEightLevelsWithoutDuplicates()
    {
        var roots = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToArray();
        var folders = Enumerable.Range(0, 12).Select(_ => Guid.NewGuid()).ToArray();
        await using var fixture = await Fixture.CreateAsync((request, _) =>
        {
            Assert.Equal("/api/v1/catalog/tree", request.RequestUri!.AbsolutePath);
            Assert.Contains("depth=1&limit=200", request.RequestUri.Query);
            var query = request.RequestUri.Query;
            if (!query.Contains("parentId=")) return System.Threading.Tasks.Task.FromResult(query.Contains("cursor=")
                ? CatalogPage([CatalogJson(roots[200]), CatalogJson(folders[0], "virtual_folder")])
                : CatalogPage(roots.Take(200).Select(id => CatalogJson(id)), "next+/="));
            var parent = folders.Single(id => query.Contains(id.ToString("D")));
            var index = Array.IndexOf(folders, parent);
            return System.Threading.Tasks.Task.FromResult(CatalogPage(index == folders.Length - 1 ? [] : [CatalogJson(folders[index + 1], "virtual_folder", parent)]));
        });
        var items = Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopCatalogItem>>.Succeeded>(await fixture.Client.GetCatalogAsync()).Value;
        Assert.Equal(213, items.Count); Assert.Equal(items.Count, items.Select(i => i.Id).Distinct().Count());
        Assert.Equal(roots, items.Take(201).Select(i => i.Id)); Assert.Equal(folders[10], items[^1].ParentId);
        Assert.Equal(14, fixture.Requests.Count); Assert.Contains("cursor=next%2B%2F%3D", fixture.Requests[1].Uri.Query);
    }

    [Theory]
    [InlineData("{\"items\":[],\"hasMore\":true,\"nextCursor\":null}")]
    [InlineData("{\"items\":[{\"id\":\"bad\"}],\"hasMore\":false}")]
    public async System.Threading.Tasks.Task CatalogRejectsMalformedPages(string body)
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(Json(HttpStatusCode.OK, body)));
        Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopCatalogItem>>.MalformedResponse>(await fixture.Client.GetCatalogAsync());
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateAndMoveUseRuntimeParentContractIfMatchAndNewVersion()
    {
        var parent = Guid.NewGuid(); var version = 1;
        await using var fixture = await Fixture.CreateAsync((request, _) => System.Threading.Tasks.Task.FromResult(Json(
            request.RequestUri!.AbsolutePath.EndsWith("/move", StringComparison.Ordinal) ? HttpStatusCode.OK : HttpStatusCode.Created,
            CatalogJson(ItemId, parent: parent, version: version++).ToJsonString())));
        var created = Assert.IsType<DesktopWorkResult<DesktopCatalogItem>.Succeeded>(await fixture.Client.CreateCatalogItemAsync("File", "file_reference", null, parent)).Value;
        var moved = Assert.IsType<DesktopWorkResult<DesktopCatalogItem>.Succeeded>(await fixture.Client.MoveCatalogItemAsync(created.Id, created.Version, null)).Value;
        Assert.Equal(2, moved.Version);
        Assert.Equal(parent.ToString(), JsonNode.Parse(fixture.Requests[0].Body)!["parentItemId"]!.ToString());
        Assert.Null(JsonNode.Parse(fixture.Requests[1].Body)!["parentItemId"]);
        Assert.Equal("\"v1\"", fixture.Requests[1].IfMatch); Assert.NotNull(fixture.Requests[1].IdempotencyKey);
        Assert.Equal($"/api/v1/catalog-items/{ItemId:D}/move", fixture.Requests[1].Uri.AbsolutePath);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "FORBIDDEN", "Forbidden")]
    [InlineData(HttpStatusCode.NotFound, "OBJECT_NOT_VISIBLE", "NotFound")]
    [InlineData(HttpStatusCode.PreconditionFailed, "VERSION_CONFLICT", "Conflict")]
    [InlineData(HttpStatusCode.Conflict, "CATALOG_CYCLE", "ValidationFailure")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED", "ValidationFailure")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "INTERNAL_ERROR", "ServerUnavailable")]
    public async System.Threading.Tasks.Task MoveMapsStableErrorsToControlledResults(HttpStatusCode status, string code, string kind)
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(Json(status, new JsonObject { ["code"] = code, ["title"] = "raw server title" }.ToJsonString())));
        var result = await fixture.Client.MoveCatalogItemAsync(ItemId, 1, null);
        Assert.StartsWith(kind, result.GetType().Name);
        if (result is DesktopWorkResult<DesktopCatalogItem>.ValidationFailure failure) Assert.DoesNotContain("raw server", failure.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task CorporateCreationUsesSelectionAndFailedMutationRefreshDisablesWritesUntilFreshRead()
    {
        var folder = Guid.NewGuid(); var items = new List<JsonObject> { CatalogJson(folder, "virtual_folder") };
        var readUnavailable = false;
        await using var fixture = await Fixture.CreateAsync(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                var draft = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
                Assert.Equal(folder.ToString(), draft["parentItemId"]!.ToString());
                var item = CatalogJson(ItemId, parent: folder); items.Add(item); readUnavailable = true;
                return Json(HttpStatusCode.Created, item.ToJsonString());
            }
            if (readUnavailable) return Json(HttpStatusCode.ServiceUnavailable, "{}");
            var parent = request.RequestUri!.Query.Contains("parentId=") ? folder : (Guid?)null;
            return CatalogPage(items.Where(i => i["parentItemId"]?.GetValue<Guid>() == parent).Select(i => (JsonObject)i.DeepClone()));
        });
        using var vm = new WorkHubViewModel(fixture.Client, ["FileCatalog.Read", "FileCatalog.Create", "FileCatalog.Update"]);
        vm.Activate(WorkHubArea.Catalog);
        for (var i = 0; i < 200 && vm.Catalog.Count == 0; i++) await System.Threading.Tasks.Task.Delay(10);
        Assert.True(await vm.SelectCatalogItemAsync(folder)); vm.NewItemName = "Child";
        await vm.CreateCatalogItemCommand.ExecuteAsync();
        Assert.Equal(ItemId, vm.SelectedCatalogItem!.Id); Assert.Equal(folder, vm.SelectedCatalogItem.ParentId);
        Assert.True(vm.IsOffline); Assert.Equal(2, vm.Catalog.Count);
        vm.UpdateConnectivity(true); vm.NewItemName = "Next";
        Assert.False(vm.CreateCatalogFolderCommand.CanExecute(null)); Assert.False(vm.CanMoveCatalogItem(ItemId, null));
        readUnavailable = false; await vm.RefreshCommand.ExecuteAsync();
        Assert.True(vm.CreateCatalogFolderCommand.CanExecute(null)); Assert.True(vm.CanMoveCatalogItem(ItemId, null));
        Assert.StartsWith("Обновлено", vm.LastSuccessfulRefreshText);
    }

    [Fact]
    public async System.Threading.Tasks.Task CorporateTreeMovesRecoversConflictAndPreservesCacheOnServerFailure()
    {
        var folder = Guid.NewGuid(); var parent = (Guid?)null; long version = 1; var offline = false; var conflict = true;
        await using var fixture = await Fixture.CreateAsync(async (request, ct) =>
        {
            if (offline) return Json(HttpStatusCode.ServiceUnavailable, "{}");
            if (request.Method == HttpMethod.Get)
                return request.RequestUri!.Query.Contains("parentId=")
                    ? CatalogPage(parent == folder ? [CatalogJson(ItemId, parent: folder, version: version)] : [])
                    : CatalogPage(parent is null ? [CatalogJson(folder, "virtual_folder"), CatalogJson(ItemId, version: version)] : [CatalogJson(folder, "virtual_folder")]);
            if (conflict) { conflict = false; version++; return Json(HttpStatusCode.PreconditionFailed, "{\"code\":\"VERSION_CONFLICT\"}"); }
            Assert.Equal($"\"v{version}\"", request.Headers.IfMatch.Single().ToString());
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct));
            parent = body!["parentItemId"]?.GetValue<Guid>(); version++;
            return Json(HttpStatusCode.OK, CatalogJson(ItemId, parent: parent, version: version).ToJsonString());
        });
        using var vm = new WorkHubViewModel(fixture.Client, ["FileCatalog.Read", "FileCatalog.Update", "FileCatalog.Create"]);
        vm.Activate(WorkHubArea.Catalog);
        for (var i = 0; i < 200 && vm.Catalog.Count == 0; i++) await System.Threading.Tasks.Task.Delay(10);
        Assert.True(await vm.SelectCatalogItemAsync(ItemId));
        await vm.MoveCatalogItemCommand.ExecuteAsync(new CatalogMoveRequest(ItemId, folder));
        Assert.Equal(2, vm.SelectedCatalogItem!.Version); Assert.Equal(WorkHubFeedbackKind.Warning, vm.FeedbackKind);
        await vm.MoveCatalogItemCommand.ExecuteAsync(new CatalogMoveRequest(ItemId, folder));
        Assert.Equal(3, vm.SelectedCatalogItem!.Version); Assert.Equal(folder, vm.SelectedCatalogItem.ParentId);
        await vm.MoveCatalogItemCommand.ExecuteAsync(new CatalogMoveRequest(ItemId, null));
        Assert.Equal(4, vm.SelectedCatalogItem!.Version); Assert.Null(vm.SelectedCatalogItem.ParentId);
        offline = true; await vm.RefreshCommand.ExecuteAsync();
        Assert.Equal(2, vm.CatalogRoots.Count); Assert.Equal(ItemId, vm.SelectedCatalogItem!.Id); Assert.True(vm.IsOffline);
        vm.NewItemName = "Draft"; Assert.False(vm.CreateCatalogFolderCommand.CanExecute(null)); Assert.False(vm.CanMoveCatalogItem(ItemId, folder));
        offline = false; vm.UpdateConnectivity(true); await vm.RefreshCommand.ExecuteAsync();
        Assert.True(vm.CreateCatalogFolderCommand.CanExecute(null)); Assert.True(vm.CanMoveCatalogItem(ItemId, folder));
    }
}
