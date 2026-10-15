using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed partial class DesktopWorkApiClientTests
{
    [Theory]
    [InlineData("search", 50)]
    [InlineData("search", 101)]
    [InlineData("search", 201)]
    [InlineData("contacts", 50)]
    [InlineData("contacts", 201)]
    [InlineData("archive", 201)]
    [InlineData("trash", 201)]
    public async System.Threading.Tasks.Task PagesAreLoadedOnDemandWithStableQueryAndNoDuplicates(string route, int total)
    {
        var limit = route == "search" ? 100 : 200;
        var rows = Enumerable.Range(0, total).Select(_ => Row(route)).ToArray();
        var call = 0;
        await using var fixture = await Fixture.CreateAsync((request, _) =>
        {
            Assert.Equal("/api/v1/" + route, request.RequestUri!.AbsolutePath);
            if (route == "search") Assert.Contains("q=alpha%20%26%20beta", request.RequestUri.Query);
            if (call > 0) Assert.Contains("cursor=next%2B%2F%3D", request.RequestUri.Query);
            var first = call++ == 0;
            // The next page intentionally overlaps the first by one item.
            var items = first ? rows.Take(limit) : rows.Skip(limit - 1);
            return System.Threading.Tasks.Task.FromResult(Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                items, nextCursor = first && total > limit ? "next+/=" : null
            })));
        });
        using var hub = new WorkHubViewModel(fixture.Client, ["Search.Use", "Contact.Read", "History.Read", "Trash.Read"]);
        hub.Activate(Area(route));
        await Ready(hub);
        if (route == "search") { hub.SearchQuery = "alpha & beta"; await hub.SearchCommand.ExecuteAsync(); }
        Assert.Equal(Math.Min(limit, total), Count(hub, route));
        Assert.Equal(1, call);
        if (total > limit) { Assert.True(hub.HasMoreResults); await hub.LoadMoreCommand.ExecuteAsync(); }
        Assert.Equal(total, Count(hub, route));
        Assert.False(hub.HasMoreResults);
        Assert.False(hub.LoadMoreCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("search", "repeat")]
    [InlineData("contacts", "repeat")]
    [InlineData("search", "network")]
    [InlineData("contacts", "network")]
    [InlineData("search", "forbidden")]
    [InlineData("contacts", "forbidden")]
    [InlineData("search", "cancel")]
    [InlineData("contacts", "cancel")]
    public async System.Threading.Tasks.Task NextPageFailurePreservesDataExceptWhenAccessIsRevoked(string route, string failure)
    {
        var call = 0;
        await using var fixture = await Fixture.CreateAsync((_, ct) =>
        {
            if (call++ > 0)
            {
                if (failure == "network") return System.Threading.Tasks.Task.FromResult(Json(HttpStatusCode.ServiceUnavailable, "{}"));
                if (failure == "forbidden") return System.Threading.Tasks.Task.FromResult(Json(HttpStatusCode.Forbidden, "{}"));
                if (failure == "cancel") throw new OperationCanceledException(ct);
            }
            return System.Threading.Tasks.Task.FromResult(Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { items = new[] { Row(route) }, nextCursor = "same" })));
        });
        using var hub = new WorkHubViewModel(fixture.Client, ["Search.Use", "Contact.Read"]);
        hub.Activate(Area(route)); await Ready(hub);
        if (route == "search") { hub.SearchQuery = "alpha"; await hub.SearchCommand.ExecuteAsync(); }
        if (failure == "cancel")
        {
            using var cts = new CancellationTokenSource(); cts.Cancel();
            await hub.LoadMoreCommand.ExecuteAsync(null, cts.Token);
        }
        else await hub.LoadMoreCommand.ExecuteAsync();
        Assert.Equal(failure == "forbidden" ? 0 : 1, Count(hub, route));
        if (failure is "repeat" or "forbidden") Assert.False(hub.HasMoreResults);
        Assert.False(hub.IsSearchLoading);
        Assert.False(hub.IsLoading);
        Assert.InRange(call, 1, 2);
    }

    private static WorkHubArea Area(string route) => route switch
    { "search" => WorkHubArea.Search, "contacts" => WorkHubArea.Contacts, "archive" => WorkHubArea.Archive, _ => WorkHubArea.Trash };
    private static int Count(WorkHubViewModel hub, string route) => route switch
    { "search" => hub.SearchResults.Count, "contacts" => hub.Contacts.Count, _ => hub.LifecycleItems.Count };
    private static async System.Threading.Tasks.Task Ready(WorkHubViewModel hub)
    {
        for (var i = 0; i < 100 && hub.RefreshCommand.IsExecuting; i++) await System.Threading.Tasks.Task.Delay(10);
        Assert.False(hub.RefreshCommand.IsExecuting);
    }
    private static JsonObject Row(string route)
    {
        var row = new JsonObject { ["version"] = 1 };
        if (route == "contacts")
        {
            row["id"] = Guid.NewGuid().ToString(); row["displayName"] = "Name"; row["firstName"] = "Name";
            row["status"] = "active"; row["lifecycleState"] = "active";
        }
        else
        {
            row["objectId"] = Guid.NewGuid().ToString(); row["objectType"] = "task"; row["title"] = "Title";
            row["updatedAt"] = "2026-10-15T00:00:00Z";
            row["lifecycleState"] = route == "archive" ? "archived" : "trashed";
        }
        return row;
    }
}
