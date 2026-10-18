using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed partial class WorkHubViewModelTests
{
    [Fact]
    public async System.Threading.Tasks.Task ContactNavigationUsesDirectGetAndIgnoresAnOlderNavigation()
    {
        var target = new DesktopContact(Guid.NewGuid(), 1, "Target", "Target", null, null, "active", "active");
        var pending = new TaskCompletionSource<DesktopWorkResult<DesktopContactDetails>>();
        var delayed = false;
        var client = new FakeClient
        {
            ContactDetailHandler = (id, _) => delayed ? pending.Task : System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopContactDetails>>(
                id == target.Id ? new DesktopWorkResult<DesktopContactDetails>.Succeeded(new(target, null)) : new DesktopWorkResult<DesktopContactDetails>.NotFound())

        };
        using var hub = new WorkHubViewModel(client, ["Contact.Read"]);
        hub.Activate(WorkHubArea.Contacts);
        var listCalls = client.ContactCallCount;
        await hub.OpenSearchObjectAsync("contact", target.Id);
        Assert.Equal(target.Id, hub.SelectedContact?.Id);
        Assert.Equal(listCalls, client.ContactCallCount);
        delayed = true;
        var older = hub.OpenSearchObjectAsync("contact", target.Id);
        delayed = false;
        await hub.OpenSearchObjectAsync("contact", Guid.NewGuid());
        pending.SetResult(new DesktopWorkResult<DesktopContactDetails>.Succeeded(new(target, null)));
        await older;
        Assert.Null(hub.SelectedContact);
        Assert.Contains("больше не доступен", hub.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task FailedContactRecheckDoesNotRetainSelectedObject()
    {
        var target = new DesktopContact(Guid.NewGuid(), 1, "Target", "Target", null, null, "active", "active");
        var forbidden = false;
        var client = new FakeClient { ContactDetailHandler = (_, _) => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopContactDetails>>(
            forbidden ? new DesktopWorkResult<DesktopContactDetails>.Forbidden() : new DesktopWorkResult<DesktopContactDetails>.Succeeded(new(target, null))) };
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
