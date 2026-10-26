using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed partial class WorkHubViewModelTests
{
    [Fact]
    public async System.Threading.Tasks.Task AreaSwitchDuringContinuationLoadsNewAreaAndRejectsCancelledPage()
    {
        var pending = new TaskCompletionSource<DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>>();
        var contact = new DesktopContact(Guid.NewGuid(), 1, "Contact", "Contact", null, null, "active", "active");
        var client = new FakeClient { Contacts = [contact], SearchPageHandler = (_, cursor, _) => cursor is null ? SearchPage("item", "next") : pending.Task };
        using var hub = new WorkHubViewModel(client, ["Search.Use", "Contact.Read"]);
        hub.Activate(WorkHubArea.Search); hub.SearchQuery = "item";
        await hub.SearchCommand.ExecuteAsync();
        var more = hub.LoadMoreCommand.ExecuteAsync();
        hub.Activate(WorkHubArea.Contacts);
        Assert.Equal(contact.Id, Assert.Single(hub.Contacts).Id);
        pending.SetResult(new DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.Succeeded([]));
        await more;
        Assert.Equal(contact.Id, Assert.Single(hub.Contacts).Id);
        Assert.Equal("item", Assert.Single(hub.SearchResults).Title);
    }
    [Fact]
    public async System.Threading.Tasks.Task QueryChangeIgnoresOldContinuationEvenWhenTransportIgnoresCancellation()
    {
        var pending = new TaskCompletionSource<DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>>();
        var client = new FakeClient
        {
            SearchPageHandler = (query, cursor, _) => cursor is not null ? pending.Task
                : SearchPage(query, query == "old" ? "next" : null)
        };
        using var hub = new WorkHubViewModel(client, ["Search.Use"]);
        hub.Activate(WorkHubArea.Search);
        hub.SearchQuery = "old"; await hub.SearchCommand.ExecuteAsync();
        var more = hub.LoadMoreCommand.ExecuteAsync();
        hub.SearchQuery = "new"; await hub.SearchCommand.ExecuteAsync();
        pending.SetResult(new DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.Succeeded([
            new(Guid.NewGuid(), "task", "obsolete", null, 1, DateTimeOffset.UtcNow)]));
        await more;
        Assert.Equal("new", Assert.Single(hub.SearchResults).Title);
        Assert.False(hub.HasMoreResults);
    }

    [Fact]
    public async System.Threading.Tasks.Task CursorCycleStopsAndCapabilityChangeCancelsContinuation()
    {
        var calls = 0;
        var client = new FakeClient { SearchPageHandler = (_, _, _) => SearchPage("item", calls++ % 2 == 0 ? "a" : "b") };
        using var hub = new WorkHubViewModel(client, ["Search.Use"]);
        hub.Activate(WorkHubArea.Search); hub.SearchQuery = "item";
        await hub.SearchCommand.ExecuteAsync();
        await hub.LoadMoreCommand.ExecuteAsync();
        await hub.LoadMoreCommand.ExecuteAsync();
        Assert.False(hub.HasMoreResults);
        Assert.Equal(3, calls);
        Assert.Contains("повторил", hub.SearchMessage);
        hub.UpdateCapabilities([]);
        Assert.Empty(hub.SearchResults);
        Assert.False(hub.LoadMoreCommand.CanExecute(null));
    }

    private static System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>> SearchPage(string title, string? cursor) =>
        System.Threading.Tasks.Task.FromResult<DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>>(
            new DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.Succeeded([
                new(FakeClient.TaskId, "task", title, null, 1, DateTimeOffset.UtcNow)])
            { NextCursor = cursor });

    internal sealed partial class FakeClient
    {
        public Func<string, string?, CancellationToken, System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>>>? SearchPageHandler { get; init; }
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>> SearchAsync(string query, string? cursor, CancellationToken ct) =>
            SearchPageHandler?.Invoke(query, cursor, ct) ?? SearchAsync(query, ct);
    }
}
