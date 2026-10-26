using Task.Desktop.Modes;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed partial class WorkHubViewModelTests
{
    [Fact]
    public async System.Threading.Tasks.Task PersonalContactSearchKeepsLocalAdapterAndNeverRequestsCorporateDetails()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "task-crm-personal-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new Task.Desktop.Personal.PersonalTaskStore(new(path));
            var client = new Task.Desktop.Personal.PersonalWorkClient(store);
            var contact = Assert.IsType<DesktopWorkResult<DesktopContact>.Succeeded>(await client.CreateContactAsync("Личный", null, "Личный контакт")).Value;
            using var hub = new WorkHubViewModel(client, ["Contact.Read"], personal: true);
            hub.Activate(WorkHubArea.Contacts); await hub.OpenSearchObjectAsync("contact", contact.Id);
            Assert.Equal(contact.Id, hub.SelectedContact?.Id); Assert.Null(hub.ContactCard); Assert.Null(hub.ContactInteractions);
        }
        finally { if (System.IO.Directory.Exists(path)) System.IO.Directory.Delete(path, true); }
    }

    [Fact]
    public async System.Threading.Tasks.Task NewerInteractionNavigationWinsEvenForSameCounterparty()
    {
        var api = new InteractionsViewModelTests.Client(); var old = api.Record(); var newer = api.Record() with { Subject = "Новая навигация" }; api.Values = [old, newer];
        var contact = new DesktopContact(api.Target.Id, 1, "Анна", "Анна", null, null, "active", "active");
        var pending = new TaskCompletionSource<DesktopWorkResult<DesktopContactDetails>>(); var reads = 0;
        var fake = new FakeClient
        {
            InteractionApi = api,
            ContactDetailHandler = (_, _) => ++reads == 1 ? pending.Task :
            System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopContactDetails>>(new DesktopWorkResult<DesktopContactDetails>.Succeeded(new(contact, null)))
        };
        using var hub = new WorkHubViewModel(fake, ["Contact.Read"]); hub.Activate(WorkHubArea.Contacts);
        var older = hub.OpenSearchObjectAsync("interaction", old.Id);
        await hub.OpenSearchObjectAsync("interaction", newer.Id);
        Assert.Equal(newer.Id, hub.ContactInteractions?.Details?.Id);
        pending.SetResult(new DesktopWorkResult<DesktopContactDetails>.Succeeded(new(contact, null))); await older;
        Assert.Equal(newer.Id, hub.ContactInteractions?.Details?.Id);
    }

    [Fact]
    public async System.Threading.Tasks.Task ContactAndCompanyScopeLifecyclePermissionsAndModeGuardIncludeInteractions()
    {
        var api = new InteractionsViewModelTests.Client(); var contact = new DesktopContact(api.Target.Id, 1, "Анна", "Анна", null, null, "active", "active");
        var fake = new FakeClient { InteractionApi = api, Contacts = [contact], ContactDetailHandler = (_, _) => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopContactDetails>>(new DesktopWorkResult<DesktopContactDetails>.Succeeded(new(contact, null))) };
        using var hub = new WorkHubViewModel(fake, ["Contact.Read", "Contact.Update", "Interaction.Create", "Interaction.Update"]);
        hub.Activate(WorkHubArea.Contacts); hub.SelectedContact = contact; await hub.ContactSelectionTask;
        Assert.Equal(contact.Id, hub.ContactInteractions?.Counterparty?.Id); Assert.Null(hub.CompanyInteractions?.Counterparty);
        await hub.ContactInteractions!.NewCommand.ExecuteAsync(); hub.ContactInteractions.Subject = "Не терять";
        using var shell = new MainWindowViewModel(null, null, workHub: hub); await ApplicationModeTests.AssertCancelledAsync(shell);
        Assert.True(hub.ContactInteractions.IsEditing); Assert.Equal("Не терять", hub.ContactInteractions.Subject);
        hub.UpdateConnectivity(false); Assert.False(hub.ContactInteractions.SaveCommand.CanExecute(null)); Assert.Equal(0, api.Writes);
        hub.UpdateConnectivity(true); hub.UpdateCapabilities(["Contact.Read"]);
        Assert.Null(hub.ContactInteractions.Counterparty); Assert.False(hub.ContactInteractions.NewCommand.CanExecute(null));
        hub.UpdateSessionState(false); Assert.Null(hub.ContactInteractions.Details);
        using var personal = new WorkHubViewModel(fake, ["Contact.Read"], personal: true); Assert.Null(personal.ContactInteractions); Assert.Null(personal.CompanyInteractions);
    }

    [Theory]
    [InlineData("channel")]
    [InlineData("address")]
    [InlineData("delete")]
    [InlineData("company-create")]
    [InlineData("company-edit")]
    public async System.Threading.Tasks.Task ModeSwitchGuardPreservesAllCrmDraftKinds(string draft)
    {
        var contact = new DesktopContact(Guid.NewGuid(), 1, "Анна", "Анна", null, null, "active", "active");
        var channel = new DesktopContactChannel(Guid.NewGuid(), "phone", "123", null, false);
        var details = new DesktopContactDetails(contact, null) { Channels = [channel] };
        var company = new DesktopCompany(Guid.NewGuid(), 1, "Компания");
        var fake = new FakeClient { Contacts = [contact], CompanyApi = new CompanyTestClient(company), ContactDetailHandler = (_, _) => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopContactDetails>>(new DesktopWorkResult<DesktopContactDetails>.Succeeded(details)) };
        using var hub = new WorkHubViewModel(fake, ["Contact.Read", "Contact.Update", "Contact.Create"]);
        hub.Activate(WorkHubArea.Contacts); hub.SelectedContact = contact; await hub.ContactSelectionTask;
        if (draft == "channel") await hub.ContactCard!.AddChannelCommand.ExecuteAsync();
        if (draft == "address") await hub.ContactCard!.AddAddressCommand.ExecuteAsync();
        if (draft == "delete") await hub.ContactCard!.RemoveChannelCommand.ExecuteAsync(channel);
        if (draft.StartsWith("company"))
        {
            await hub.ShowCompaniesCommand.ExecuteAsync(); await hub.Companies!.OpenAsync(company.Id);
            await (draft == "company-create" ? hub.Companies.NewCommand : hub.Companies.EditCommand).ExecuteAsync();
        }
        using var shell = new MainWindowViewModel(null, null, workHub: hub); await ApplicationModeTests.AssertCancelledAsync(shell);
        var pending = ModeSwitchGuard.Inspect(shell).Where(e => e.HasState()).ToArray(); Assert.NotEmpty(pending);
        if (draft == "delete") Assert.Null(Assert.Single(pending).Save);
    }

    internal sealed partial class FakeClient : IDesktopInteractionsClient
    {
        public InteractionsViewModelTests.Client InteractionApi { get; init; } = new();
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopInteraction>>> GetInteractionsAsync(string? cursor, CancellationToken ct = default) => InteractionApi.GetInteractionsAsync(cursor, ct);
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopInteraction>> GetInteractionAsync(Guid id, CancellationToken ct = default) => InteractionApi.GetInteractionAsync(id, ct);
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopInteraction>> SaveInteractionAsync(DesktopInteraction value, string? key, CancellationToken ct = default) => InteractionApi.SaveInteractionAsync(value, key, ct);
        public System.Threading.Tasks.Task<DesktopWorkResult<InteractionCounterparty>> GetInteractionCounterpartyAsync(Guid id, string type, CancellationToken ct = default) => InteractionApi.GetInteractionCounterpartyAsync(id, type, ct);
    }
}
