using System.Net;
using System.Text.Json.Nodes;
using Task.Application.ProductData;

namespace Task.ServiceHosts.Tests;

public sealed partial class ProductEndpointsTests
{
    [Theory]
    [InlineData("history.read")]
    [InlineData("project.read")]
    public async System.Threading.Tasks.Task ProjectHistory_RequiresBothCapabilitiesBeforeStore(string denied)
    {
        var store = new RecordingStore(); using var server = Server(store, deniedPermission: denied);
        using var client = server.CreateClient();
        using var response = await client.SendAsync(Request("GET", $"/api/v1/projects/{ObjectId}/history?limit=50&page=1", "{}"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); Assert.Null(store.LastRequest);
    }

    [Fact]
    public async System.Threading.Tasks.Task ProjectHistory_PreservesCanonicalResponseAndPaging()
    {
        var body = new JsonObject { ["items"] = new JsonArray(new JsonObject {
            ["id"] = Guid.NewGuid(), ["action"] = "project.patch", ["version"] = 2,
            ["actorUserId"] = User, ["occurredAt"] = "2026-10-19T12:30:00Z", ["changedFields"] = new JsonArray("name") }) };
        var store = new RecordingStore { Reply = new ProductApiResponse(body) };
        using var server = Server(store); using var client = server.CreateClient();
        using var response = await client.SendAsync(Request("GET", $"/api/v1/projects/{ObjectId}/history?limit=50&page=2", "{}"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(JsonNode.DeepEquals(body, JsonNode.Parse(await response.Content.ReadAsStringAsync())));
        Assert.Equal(ObjectId, store.LastRequest!.Id); Assert.Equal("2", store.LastRequest.Query["page"]);
        Assert.Contains("History.Read", store.LastRequest.Permissions); Assert.Contains("Project.Read", store.LastRequest.Permissions);
    }
}
