using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Task.Desktop.Tests.Projects;
using Task.Desktop.ViewModels;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.Tests.Work;

public sealed partial class DesktopWorkApiClientTests
{
    [Theory]
    [InlineData("contact")]
    [InlineData("company")]
    public async AsyncTask ProjectContactsUseExistingGetPostDeleteContractsAndStrongProjectVersion(string type)
    {
        var project = ProjectFilesTests.Project(); var picker = new ProjectContactsTests.Client { TargetType = type };
        var target = type == "contact" ? picker.Contact.Id : picker.Company.Id;
        var linked = false; var version = 5; var link = Guid.NewGuid();
        await using var fixture = await Fixture.CreateAsync(async (request, ct) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/contacts/")) return type == "contact" ? ContactResponse(target) : Json(HttpStatusCode.NotFound, "{}");
            if (path.Contains("/companies/")) return CompanyResponse(target);
            Assert.StartsWith($"/api/v1/objects/{project.Id:D}/links", path);
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal("\"v5\"", request.Headers.IfMatch.ToString());
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
                Assert.Equal("project_contact", body["linkType"]!.ToString());
                Assert.Equal(target.ToString("D"), body["targetObjectId"]!.ToString());
                Assert.Equal(project.Id.ToString("D"), body["sourceObjectId"]!.ToString());
                linked = true; version++;
                var created = Json(HttpStatusCode.Created, "{}"); created.Headers.ETag = new("\"v6\""); return created;
            }
            if (request.Method == HttpMethod.Delete)
            {
                Assert.EndsWith("/" + link.ToString("D"), path); Assert.Equal("\"v6\"", request.Headers.IfMatch.ToString());
                linked = false; version++;
                var deleted = Json(HttpStatusCode.OK, "{}"); deleted.Headers.ETag = new("\"v7\""); return deleted;
            }
            var rows = linked ? new JsonArray(new JsonObject
            { ["id"] = link, ["sourceObjectId"] = project.Id, ["targetObjectId"] = target, ["linkType"] = "project_contact" }) : new JsonArray();
            var response = Json(HttpStatusCode.OK, new JsonObject { ["items"] = rows, ["hasMore"] = false }.ToJsonString());
            response.Headers.ETag = new($"\"v{version}\""); return response;
        });
        using var vm = new ProjectContactsViewModel(fixture.Client, fixture.Client, new(picker, ProjectContactsTests.Caps, TimeSpan.Zero));
        vm.SetContext(project, ProjectContactsTests.Caps, true, true, true);
        await vm.RefreshCommand.ExecuteAsync(); await ProjectContactsTests.SelectTarget(vm); await vm.AddCommand.ExecuteAsync();
        Assert.Equal(type == "contact" ? "Анна Иванова" : "Компания", Assert.Single(vm.Items).Title);
        await vm.RemoveCommand.ExecuteAsync(); Assert.Empty(vm.Items); Assert.Equal(7, version);
        Assert.Single(fixture.Requests, r => r.Method == HttpMethod.Post); Assert.Single(fixture.Requests, r => r.Method == HttpMethod.Delete);
        Assert.DoesNotContain(fixture.Requests, r => r.Method == HttpMethod.Patch);
        Assert.All(fixture.Requests.Where(r => r.Method != HttpMethod.Get), r => Assert.Contains("/objects/", r.Uri.AbsolutePath));
    }
}
