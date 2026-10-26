using System.Net;
using System.Text.Json.Nodes;
using Task.Application.ProductData;

namespace Task.ServiceHosts.Tests;

public sealed partial class ProductEndpointsTests
{
    [Theory]
    [InlineData("POST", "channels", "channel-add", 201)]
    [InlineData("PATCH", "channels/child", "channel-patch", 200)]
    [InlineData("DELETE", "channels/child", "channel-remove", 204)]
    [InlineData("POST", "addresses", "address-add", 201)]
    public async System.Threading.Tasks.Task ContactChildRoutesForwardParentCasAndReturnParentEtag(string method, string path, string operation, int status)
    {
        var child = Guid.NewGuid(); var store = new RecordingStore { Reply = new(status == 204 ? null : new JsonObject { ["id"] = child }, status, 8) };
        using var server = Server(store); using var client = server.CreateClient();
        using var request = Request(method, $"/api/v1/contacts/{ObjectId}/{path.Replace("child", child.ToString())}", method == "DELETE" ? "{}" : path == "addresses" ? "{\"addressType\":\"work\",\"formattedAddress\":\"Минск\"}" : "{\"channelType\":\"phone\",\"value\":\"test\"}");
        request.Headers.TryAddWithoutValidation("If-Match", "\"v7\"");
        if (method != "DELETE") request.Headers.Add("Idempotency-Key", "contact-child-001");
        var response = await client.SendAsync(request);
        Assert.Equal(status, (int)response.StatusCode); Assert.Equal("\"v8\"", response.Headers.ETag?.Tag);
        Assert.Equal(7, store.LastRequest!.ExpectedVersion); Assert.Equal(ObjectId, store.LastRequest.Id);
        Assert.Equal(path.Contains('/') ? child : (Guid?)null, store.LastRequest.ChildId);
        Assert.Equal(operation, store.LastRequest.Route.Operation); Assert.Equal("Contact.Update", store.LastRequest.Route.Permission);
        Assert.Equal(method == "DELETE" ? null : "contact-child-001", store.LastRequest.IdempotencyKey);
    }

    [Theory]
    [InlineData("POST", "channels", true, 428)]
    [InlineData("PATCH", "channels/child", true, 428)]
    [InlineData("DELETE", "channels/child", true, 428)]
    [InlineData("POST", "addresses", true, 428)]
    [InlineData("POST", "channels", false, 400)]
    [InlineData("PATCH", "channels/child", false, 400)]
    [InlineData("POST", "addresses", false, 400)]
    public async System.Threading.Tasks.Task ContactChildRoutesRejectMissingRequiredHeaders(string method, string path, bool missingVersion, int expected)
    {
        var store = new RecordingStore(); using var server = Server(store); using var client = server.CreateClient();
        using var request = Request(method, $"/api/v1/contacts/{ObjectId}/{path.Replace("child", Guid.NewGuid().ToString())}", method == "DELETE" ? "{}" : "{\"value\":\"test\"}");
        if (!missingVersion) request.Headers.TryAddWithoutValidation("If-Match", "\"v7\"");
        else if (method != "DELETE") request.Headers.Add("Idempotency-Key", "contact-child-001");
        Assert.Equal(expected, (int)(await client.SendAsync(request)).StatusCode); Assert.Null(store.LastRequest);
    }

    [Theory]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async System.Threading.Tasks.Task AddressUpdateAndDeleteHaveNoRoute(string method)
    {
        var store = new RecordingStore(); using var server = Server(store); using var client = server.CreateClient();
        using var request = Request(method, $"/api/v1/contacts/{ObjectId}/addresses/{Guid.NewGuid()}", "{}");
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(request)).StatusCode); Assert.Null(store.LastRequest);
    }
}
