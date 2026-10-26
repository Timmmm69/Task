using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed partial class WorkHubViewModelTests
{
    [Theory]
    [InlineData("403")]
    [InlineData("404")]
    [InlineData("401")]
    [InlineData("trashed")]
    public async System.Threading.Tasks.Task RefreshRemovesInaccessibleOffPageContactAndItsDraft(string failure)
    {
        var first = new DesktopContact(Guid.NewGuid(), 1, "Первый", "Первый", null, null, "active", "active");
        var selected = first with { Id = Guid.NewGuid(), DisplayName = "Закрытый контакт" };
        var revoked = false;
        var client = new FakeClient
        {
            ContactPageHandler = (cursor, _) => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<IReadOnlyList<DesktopContact>>>(
                new DesktopWorkResult<IReadOnlyList<DesktopContact>>.Succeeded(cursor is null ? [first] : [selected]) { NextCursor = cursor is null ? "next" : null }),
            ContactDetailHandler = (id, _) => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopContactDetails>>(
                revoked && id == selected.Id ? failure switch
                {
                    "403" => new DesktopWorkResult<DesktopContactDetails>.Forbidden(),
                    "404" => new DesktopWorkResult<DesktopContactDetails>.NotFound(),
                    "401" => new DesktopWorkResult<DesktopContactDetails>.AuthenticationFailure(),
                    _ => new DesktopWorkResult<DesktopContactDetails>.Succeeded(new(selected with { LifecycleState = "trashed" }, null))
                } : new DesktopWorkResult<DesktopContactDetails>.Succeeded(new(id == first.Id ? first : selected, null)))
        };
        using var hub = new WorkHubViewModel(client, ["Contact.Read", "Contact.Update"]);
        hub.Activate(WorkHubArea.Contacts); await hub.LoadMoreCommand.ExecuteAsync(); hub.SelectedContact = selected; await hub.ContactSelectionTask;
        await hub.ContactCard!.EditCommand.ExecuteAsync(); hub.ContactCard.Notes = "Секретный черновик";
        revoked = true; await hub.RefreshCommand.ExecuteAsync();
        Assert.DoesNotContain(hub.Contacts, c => c.Id == selected.Id); Assert.Null(hub.SelectedContact);
        Assert.Null(hub.ContactCard.Details); Assert.Empty(hub.ContactCard.Notes); Assert.False(hub.ContactCard.IsEditing);
        Assert.Null(hub.ContactInteractions?.Counterparty);
    }

    [Fact]
    public async System.Threading.Tasks.Task OffPageScopeRecheckCannotRestoreAnOlderSelection()
    {
        var first = new DesktopContact(Guid.NewGuid(), 1, "Первый", "Первый", null, null, "active", "active");
        var second = first with { Id = Guid.NewGuid(), DisplayName = "Второй" }; var delay = false;
        var pending = new TaskCompletionSource<DesktopWorkResult<DesktopContactDetails>>();
        var client = new FakeClient
        {
            ContactPageHandler = (cursor, _) => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<IReadOnlyList<DesktopContact>>>(
                new DesktopWorkResult<IReadOnlyList<DesktopContact>>.Succeeded(cursor is null ? [first] : [second]) { NextCursor = cursor is null ? "next" : null }),
            ContactDetailHandler = (id, _) => delay && id == second.Id ? pending.Task :
                System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopContactDetails>>(new DesktopWorkResult<DesktopContactDetails>.Succeeded(new(id == first.Id ? first : second, null)))
        };
        using var hub = new WorkHubViewModel(client, ["Contact.Read"]);
        hub.Activate(WorkHubArea.Contacts); await hub.LoadMoreCommand.ExecuteAsync(); hub.SelectedContact = second; await hub.ContactSelectionTask;
        delay = true; var refresh = hub.RefreshCommand.ExecuteAsync(); hub.SelectedContact = first; await hub.ContactSelectionTask;
        pending.SetResult(new DesktopWorkResult<DesktopContactDetails>.Succeeded(new(second, null))); await refresh;
        Assert.Equal(first.Id, hub.SelectedContact?.Id); Assert.Equal(first.Id, hub.ContactCard?.Details?.Contact.Id);
    }
}
