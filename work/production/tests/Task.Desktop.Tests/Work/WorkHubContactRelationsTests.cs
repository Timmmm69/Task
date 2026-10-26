using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed partial class WorkHubViewModelTests
{
    [Fact]
    public async System.Threading.Tasks.Task ListRefreshDoesNotRebaseAnOpenChildDraftToANewParentVersion()
    {
        var contact = new DesktopContact(Guid.NewGuid(), 1, "Анна", "Анна", null, null, "active", "active"); var reads = 0;
        var client = new FakeClient
        {
            Contacts = [contact],
            ContactDetailHandler = (_, _) =>
            System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopContactDetails>>(new DesktopWorkResult<DesktopContactDetails>.Succeeded(new(contact with { Version = ++reads }, null)))
        };
        using var hub = new WorkHubViewModel(client, ["Contact.Read", "Contact.Update"]);
        hub.Activate(WorkHubArea.Contacts); hub.SelectedContact = contact; await hub.ContactSelectionTask;
        await hub.ContactCard!.AddChannelCommand.ExecuteAsync(); hub.ContactCard.ChannelDraft!.Value = "keep draft";
        var before = reads; await hub.RefreshCommand.ExecuteAsync();
        Assert.Equal(before, reads); Assert.Equal("keep draft", hub.ContactCard.ChannelDraft.Value);
        Assert.Equal(before, hub.ContactCard.Details!.Contact.Version); Assert.Equal(contact.Id, hub.SelectedContact!.Id);
    }

    [Fact]
    public async System.Threading.Tasks.Task ChildMutationPreservesSelectedContactAndUpdatesItsVersion()
    {
        var contact = new DesktopContact(Guid.NewGuid(), 1, "Анна", "Анна", null, null, "active", "active");
        var details = new DesktopContactDetails(contact, null);
        var client = new FakeClient
        {
            Contacts = [contact],
            RelationState = details,
            ContactDetailHandler = (_, _) => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopContactDetails>>(new DesktopWorkResult<DesktopContactDetails>.Succeeded(details))
        };
        using var hub = new WorkHubViewModel(client, ["Contact.Read", "Contact.Update"]);
        hub.Activate(WorkHubArea.Contacts); hub.SelectedContact = contact; await hub.ContactSelectionTask;
        await hub.ContactCard!.AddChannelCommand.ExecuteAsync(); hub.ContactCard.ChannelDraft!.Value = "value";
        await hub.ContactCard.SaveRelationCommand.ExecuteAsync();
        Assert.Equal(contact.Id, hub.SelectedContact!.Id); Assert.Equal(2, hub.SelectedContact.Version);
        Assert.Single(hub.ContactCard.Channels); Assert.False(hub.ContactCard.HasRelationDraft);
    }

    internal sealed partial class FakeClient : IDesktopContactRelationsClient
    {
        public DesktopContactDetails? RelationState { get; set; }
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> SaveChannelAsync(Guid id, long version, DesktopContactChannel c, CancellationToken ct = default) =>
            ConfirmRelation(version, d => d with { Channels = d.Channels.Where(x => x.Id != c.Id).Append(c with { Id = c.Id == Guid.Empty ? Guid.NewGuid() : c.Id }).ToArray() });
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> DeleteChannelAsync(Guid id, long version, Guid child, CancellationToken ct = default) =>
            ConfirmRelation(version, d => d with { Channels = d.Channels.Where(x => x.Id != child).ToArray() });
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> AddAddressAsync(Guid id, long version, DesktopContactAddress a, CancellationToken ct = default) =>
            ConfirmRelation(version, d => d with { Addresses = d.Addresses.Append(a with { Id = Guid.NewGuid() }).ToArray() });
        private System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> ConfirmRelation(long version, Func<DesktopContactDetails, DesktopContactDetails> change)
        {
            var state = RelationState ?? throw new InvalidOperationException("Missing fixture details.");
            RelationState = change(state) with { Contact = state.Contact with { Version = version + 1 } };
            return System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopContactDetails>>(new DesktopWorkResult<DesktopContactDetails>.Succeeded(RelationState));
        }
    }
}
