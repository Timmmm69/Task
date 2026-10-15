using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed partial class WorkHubViewModelTests
{
    [Fact]
    public async System.Threading.Tasks.Task ContactNavigationFindsTargetAfterFirstPageAndIgnoresAnOlderNavigation()
    {
        var target = new DesktopContact(Guid.NewGuid(), 1, "Target", "Target", null, null, "active", "active");
        var pending = new TaskCompletionSource<DesktopWorkResult<IReadOnlyList<DesktopContact>>>();
        var delayed = false;
        var client = new FakeClient
        {
            ContactPageHandler = (cursor, _) => delayed ? pending.Task : System.Threading.Tasks.Task.FromResult<DesktopWorkResult<IReadOnlyList<DesktopContact>>>(
                new DesktopWorkResult<IReadOnlyList<DesktopContact>>.Succeeded(cursor is null ? [] : [target]) { NextCursor = cursor is null ? "next" : null })
        };
        using var hub = new WorkHubViewModel(client, ["Contact.Read"]);
        hub.Activate(WorkHubArea.Contacts);
        await hub.OpenSearchObjectAsync("contact", target.Id);
        Assert.Equal(target.Id, hub.SelectedContact?.Id);
        delayed = true;
        var older = hub.OpenSearchObjectAsync("contact", target.Id);
        delayed = false;
        await hub.OpenSearchObjectAsync("contact", Guid.NewGuid());
        pending.SetResult(new DesktopWorkResult<IReadOnlyList<DesktopContact>>.Succeeded([target]));
        await older;
        Assert.Null(hub.SelectedContact);
        Assert.Contains("больше не доступен", hub.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task FailedContactRecheckDoesNotRetainSelectedObject()
    {
        var target = new DesktopContact(Guid.NewGuid(), 1, "Target", "Target", null, null, "active", "active");
        var forbidden = false;
        var client = new FakeClient { ContactPageHandler = (_, _) => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<IReadOnlyList<DesktopContact>>>(
            forbidden ? new DesktopWorkResult<IReadOnlyList<DesktopContact>>.Forbidden() : new DesktopWorkResult<IReadOnlyList<DesktopContact>>.Succeeded([target])) };
        using var hub = new WorkHubViewModel(client, ["Contact.Read"]);
        hub.Activate(WorkHubArea.Contacts);
        await hub.OpenSearchObjectAsync("contact", target.Id);
        forbidden = true;
        await hub.OpenSearchObjectAsync("contact", target.Id);
        Assert.Null(hub.SelectedContact);
        Assert.Empty(hub.Contacts);
        Assert.Contains("Недостаточно прав", hub.Message);
    }

    internal sealed partial class FakeClient
    {
        public Func<string?, CancellationToken, System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopContact>>>>? ContactPageHandler { get; init; }
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopContact>>> GetContactsAsync(string? cursor, CancellationToken ct) =>
            ContactPageHandler?.Invoke(cursor, ct) ?? GetContactsAsync(ct);
    }
}
