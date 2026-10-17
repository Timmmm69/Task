using System.Net;

namespace Task.ServiceHosts.Tests;

public sealed partial class ProductEndpointsTests
{
    [Fact]
    public async System.Threading.Tasks.Task LinkPickerUsesCreatePermissionWithoutSearchAndForwardsOwnReadCapabilities()
    {
        var store = new RecordingStore();
        using var server = Server(store, deniedPermission: "search.use");
        using var client = server.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/search?q=Picker")).StatusCode);
        Assert.Null(store.LastRequest);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/object-link-targets?q=Picker&types=notification,inbox_item")).StatusCode);
        Assert.Equal("ObjectLink.Create", store.LastRequest!.Route.Permission);
        Assert.Contains("Notification.ReadOwn", store.LastRequest.Permissions);
        Assert.Contains("Inbox.ReadOwn", store.LastRequest.Permissions);
        Assert.DoesNotContain("Search.Use", store.LastRequest.Permissions);
        Assert.True(store.LastRequest.RevalidatePermissions);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/objects/{ObjectId}/links")).StatusCode);
        Assert.Contains("Notification.ReadOwn", store.LastRequest.Permissions);
        Assert.Contains("Inbox.ReadOwn", store.LastRequest.Permissions);
    }
}
