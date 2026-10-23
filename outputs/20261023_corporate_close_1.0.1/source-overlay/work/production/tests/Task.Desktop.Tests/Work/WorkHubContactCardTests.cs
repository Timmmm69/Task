using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed partial class WorkHubViewModelTests
{
    [Fact]
    public async System.Threading.Tasks.Task CorporateCardRetainsPageSelectionAndDraftAcrossRefreshAndPagination()
    {
        var first = new DesktopContact(Guid.NewGuid(), 1, "Первый", "Первый", null, null, "active", "active");
        var second = first with { Id = Guid.NewGuid(), DisplayName = "Второй" };
        var client = new FakeClient { ContactPageHandler = (cursor, _) => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<IReadOnlyList<DesktopContact>>>(
            new DesktopWorkResult<IReadOnlyList<DesktopContact>>.Succeeded(cursor is null ? [first] : [second]) { NextCursor = cursor is null ? "next" : null }),
            ContactDetailHandler = (id, _) => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopContactDetails>>(
                new DesktopWorkResult<DesktopContactDetails>.Succeeded(new(id == first.Id ? first : second, "Отчество"))) };
        using var hub = new WorkHubViewModel(client, ["Contact.Read", "Contact.Update", "Contact.Create"]);
        hub.Activate(WorkHubArea.Contacts); await hub.LoadMoreCommand.ExecuteAsync();
        Assert.Equal(2, hub.Contacts.Count); hub.SelectedContact = second; await hub.ContactSelectionTask;
        var card = Assert.IsType<ContactCardViewModel>(hub.ContactCard);
        await card.EditCommand.ExecuteAsync(); card.Notes = "Черновик";
        await hub.RefreshCommand.ExecuteAsync();
        Assert.Equal(second.Id, hub.SelectedContact?.Id); Assert.Equal("Черновик", card.Notes);
        Assert.True(hub.HasMoreResults); await hub.LoadMoreCommand.ExecuteAsync(); Assert.Equal(2, hub.Contacts.Count);
        await card.SaveCommand.ExecuteAsync(); Assert.Equal("Черновик", hub.SelectedContact?.Notes);
        Assert.Equal(2, hub.SelectedContact?.Version);
        hub.NewContactFirstName = "Новый"; hub.NewContactDisplayName = "Новый контакт";
        await hub.CreateContactCommand.ExecuteAsync(); Assert.Equal(3, hub.Contacts.Count);
        hub.UpdateCapabilities([]); Assert.Null(card.Details); Assert.Empty(hub.Contacts); Assert.False(card.EditCommand.CanExecute(null));
    }

    [Fact]
    public async System.Threading.Tasks.Task PersonalContactsKeepExistingCreationAndNeverLoadCorporateDetails()
    {
        var reads = 0;
        var client = new FakeClient { ContactDetailHandler = (_, _) => { reads++; throw new InvalidOperationException(); } };
        using var hub = new WorkHubViewModel(client, ["Contact.Read", "Contact.Create", "Contact.Update"], personal: true);
        hub.Activate(WorkHubArea.Contacts); Assert.Null(hub.ContactCard);
        hub.NewContactFirstName = "Личный"; hub.NewContactDisplayName = "Личный контакт";
        await hub.CreateContactCommand.ExecuteAsync(); hub.SelectedContact = Assert.Single(hub.Contacts);
        Assert.Equal(0, reads);
    }

    [Theory]
    [InlineData(true)][InlineData(false)]
    public async System.Threading.Tasks.Task SessionOrScopeChangeIgnoresLateContactDetails(bool sessionLoss)
    {
        var contact = new DesktopContact(Guid.NewGuid(), 1, "Private", "Private", null, null, "active", "active");
        var pending = new TaskCompletionSource<DesktopWorkResult<DesktopContactDetails>>();
        var client = new FakeClient { Contacts = [contact], ContactDetailHandler = (_, _) => pending.Task };
        using var hub = new WorkHubViewModel(client, ["Contact.Read", "Contact.Update"]);
        hub.Activate(WorkHubArea.Contacts); hub.SelectedContact = contact; var load = hub.ContactSelectionTask;
        if (sessionLoss) hub.UpdateSessionState(false); else hub.UpdateCapabilities(["Contact.Read", "Contact.Update"]);
        pending.SetResult(new DesktopWorkResult<DesktopContactDetails>.Succeeded(new(contact, "private"))); await load;
        if (sessionLoss) { Assert.Null(hub.SelectedContact); Assert.Empty(hub.Contacts); }
        else Assert.Equal(contact.Id, hub.SelectedContact?.Id);
        Assert.Null(hub.ContactCard!.Details);
    }

    [Fact]
    public async System.Threading.Tasks.Task CancelledModeSwitchPreservesCorporateCardDraft()
    {
        var contact = new DesktopContact(Guid.NewGuid(), 1, "Анна", "Анна", null, null, "active", "active");
        var client = new FakeClient { Contacts = [contact], ContactDetailHandler = (_, _) =>
            System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopContactDetails>>(new DesktopWorkResult<DesktopContactDetails>.Succeeded(new(contact, null))) };
        using var hub = new WorkHubViewModel(client, ["Contact.Read", "Contact.Update"]);
        hub.Activate(WorkHubArea.Contacts); hub.SelectedContact = contact; await hub.ContactSelectionTask;
        await hub.ContactCard!.EditCommand.ExecuteAsync(); hub.ContactCard.Notes = "Не терять";
        using var shell = new MainWindowViewModel(null, null, workHub: hub);
        await ApplicationModeTests.AssertCancelledAsync(shell);
        Assert.True(hub.ContactCard.IsEditing); Assert.Equal("Не терять", hub.ContactCard.Notes);
    }

    internal sealed partial class FakeClient : IDesktopContactDetailsClient
    {
        public Func<Guid, CancellationToken, System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>>>? ContactDetailHandler { get; init; }
        public DesktopWorkResult<DesktopContactDetails>? ContactWriteResult { get; init; }
        public int ContactDetailCalls { get; private set; }
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> GetContactAsync(Guid id, CancellationToken cancellationToken = default)
        {
            ContactDetailCalls++;
            return ContactDetailHandler?.Invoke(id, cancellationToken) ?? System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopContactDetails>>(
                Contacts.FirstOrDefault(c => c.Id == id) is { } contact ? new DesktopWorkResult<DesktopContactDetails>.Succeeded(new(contact, null)) : new DesktopWorkResult<DesktopContactDetails>.NotFound());
        }
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> UpdateContactAsync(DesktopContactDetails details, CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopContactDetails>>(ContactWriteResult ?? new DesktopWorkResult<DesktopContactDetails>.Succeeded(details with { Contact = details.Contact with { Version = details.Contact.Version + 1 } }));
    }
}
