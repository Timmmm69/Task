using System.Net;
using System.Text.Json.Nodes;
using Task.Application.ProductData;

namespace Task.ServiceHosts.Tests;

public sealed partial class ProductEndpointsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async System.Threading.Tasks.Task InboxConversionUsesCanonicalBodyVersionAndReplayHeaders(bool replay)
    {
        var store = new RecordingStore { Reply = new ProductApiResponse(new JsonObject { ["id"] = ObjectId }, 201, 1, replay) };
        using var server = Server(store); using var client = server.CreateClient();
        using var request = Request("POST", $"/api/v1/inbox-items/{ObjectId}/convert-to-task", "{\"expectedInboxVersion\":1}");
        request.Headers.Add("If-Match", "\"v1\""); request.Headers.Add("Idempotency-Key", "inbox-conversion-001");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("\"v1\"", response.Headers.ETag?.Tag);
        Assert.Equal(replay ? "true" : "false", response.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal("Inbox.ManageOwn", store.LastRequest!.Route.Permission);
        Assert.Equal(1, store.LastRequest.ExpectedVersion);
        Assert.Single(store.LastRequest.Body);
        Assert.True(store.LastRequest.RevalidatePermissions);
    }
}
