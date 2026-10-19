using System.Net;
using System.Text.Json.Nodes;
using Task.Desktop.Tests.Projects;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.Tests.Work;

public sealed partial class DesktopWorkApiClientTests
{
    [Fact]
    public async AsyncTask ProjectFilesSectionConsumesAllLinkPagesAndResolvesNamesThroughCatalogGet()
    {
        var project = ProjectFilesTests.Project(); var pages = 0; var catalogReads = 0;
        await using var fixture = await Fixture.CreateAsync((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/links"))
            {
                pages++;
                if (pages == 2) Assert.Contains("cursor=next", request.RequestUri.Query);
                var rows = Enumerable.Range(0, pages == 1 ? 200 : 1).Select(_ => (JsonNode)new JsonObject
                {
                    ["id"] = Guid.NewGuid(), ["sourceObjectId"] = project.Id,
                    ["targetObjectId"] = Guid.NewGuid(), ["linkType"] = "project_file"
                }).ToArray();
                var response = Json(HttpStatusCode.OK, new JsonObject { ["items"] = new JsonArray(rows), ["hasMore"] = pages == 1, ["nextCursor"] = pages == 1 ? "next" : null }.ToJsonString());
                response.Headers.ETag = new("\"v7\""); return AsyncTask.FromResult(response);
            }
            Assert.Contains("/catalog-items/", request.RequestUri.AbsolutePath); catalogReads++;
            var id = Guid.Parse(request.RequestUri.Segments[^1]);
            return AsyncTask.FromResult(Json(HttpStatusCode.OK, $$"""{"id":"{{id}}","version":1,"name":"Catalog {{catalogReads}}","itemType":"file_reference","lifecycleState":"active"}"""));
        });
        using var vm = new ProjectFilesViewModel(fixture.Client, fixture.Client, new(fixture.Client, ProjectFilesTests.Caps), fixture.Client.ResolveLocationAsync);
        vm.SetContext(project, ProjectFilesTests.Caps, true, true, true);
        await vm.RefreshCommand.ExecuteAsync();
        Assert.Equal(2, pages); Assert.Equal(201, catalogReads); Assert.Equal(201, vm.Items.Count);
        Assert.All(vm.Items, row => Assert.StartsWith("Catalog ", row.Title));
        Assert.Equal(ProjectSectionState.Loaded, vm.Section.State);
    }
}
