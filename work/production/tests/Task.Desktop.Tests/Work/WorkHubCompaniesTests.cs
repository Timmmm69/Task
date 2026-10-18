using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed partial class WorkHubViewModelTests
{
    [Fact]
    public async System.Threading.Tasks.Task CompanySearchUsesExistingShellRouteAndContactCardThenReturnsToSelectedCompany()
    {
        var contact = CompanyTestClient.Contact("Анна");
        var company = new DesktopCompany(Guid.NewGuid(), 2, "Компания") { Contacts = [new(contact.Id)] };
        var api = new CompanyTestClient(company); api.Contacts.Add(contact.Id, contact);
        var client = new FakeClient { CompanyApi = api, Contacts = [contact], ContactDetailHandler = api.GetContactAsync };
        using var hub = new WorkHubViewModel(client, ["Contact.Read", "Contact.Create", "Contact.Update", "Search.Use"]);
        using var shell = new MainWindowViewModel(null, null, workHub: hub);
        await shell.OpenWorkObjectAsync("company", company.Id);
        Assert.Equal("contacts", shell.SelectedSection!.Route); Assert.True(hub.ShowCompanies); Assert.Equal(company.Id, hub.Companies?.Details?.Id);
        var card = hub.ContactCard;
        await hub.Companies!.OpenContactCommand.ExecuteAsync(Assert.Single(hub.Companies.RelatedContacts));
        Assert.False(hub.ShowCompanies); Assert.Same(card, hub.ContactCard); Assert.Equal(contact.Id, hub.ContactCard?.Details?.Contact.Id);
        await hub.ShowCompaniesCommand.ExecuteAsync(); Assert.True(hub.ShowCompanies); Assert.Equal(company.Id, hub.Companies.SelectedCompany?.Id);
        Assert.Equal(company.Id, hub.Companies.Details?.Id);
        hub.UpdateConnectivity(false); Assert.False(hub.Companies.EditCommand.CanExecute(null));
        hub.UpdateCapabilities(["Contact.Read", "Contact.Update"]); Assert.Null(hub.Companies.Details); Assert.Empty(hub.Companies.RelatedContacts);
    }

    [Fact]
    public async System.Threading.Tasks.Task PersonalModeHasNoCompanyWorkspaceAndSessionLossClearsCorporateCompanies()
    {
        var company = new DesktopCompany(Guid.NewGuid(), 1, "Компания"); var api = new CompanyTestClient(company);
        using var personal = new WorkHubViewModel(new FakeClient { CompanyApi = api }, ["Contact.Read", "Contact.Create"], personal: true);
        personal.Activate(WorkHubArea.Contacts); Assert.Null(personal.Companies); Assert.False(personal.ShowCompaniesCommand.CanExecute(null));
        using var corporate = new WorkHubViewModel(new FakeClient { CompanyApi = api }, ["Contact.Read", "Contact.Update"]);
        corporate.Activate(WorkHubArea.Contacts); await corporate.ShowCompaniesCommand.ExecuteAsync(); await corporate.Companies!.OpenAsync(company.Id);
        corporate.UpdateSessionState(false); Assert.Null(corporate.Companies.Details); Assert.Empty(corporate.Companies.Items);
    }

    internal sealed partial class FakeClient : IDesktopCompaniesClient
    {
        public CompanyTestClient CompanyApi { get; init; } = new();
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopCompany>>> GetCompaniesAsync(string query, string? cursor, CancellationToken ct = default) => CompanyApi.GetCompaniesAsync(query, cursor, ct);
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopCompany>> GetCompanyAsync(Guid id, CancellationToken ct = default) => CompanyApi.GetCompanyAsync(id, ct);
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopCompany>> SaveCompanyAsync(DesktopCompany company, string? key, CancellationToken ct = default) => CompanyApi.SaveCompanyAsync(company, key, ct);
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopContact>>> FindCompanyContactsAsync(string query, string? cursor, CancellationToken ct = default) => CompanyApi.FindCompanyContactsAsync(query, cursor, ct);
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopCompany>> LinkCompanyContactAsync(Guid id, long version, DesktopCompanyContact link, string key, CancellationToken ct = default) => CompanyApi.LinkCompanyContactAsync(id, version, link, key, ct);
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopCompany>> UnlinkCompanyContactAsync(Guid id, long version, Guid contactId, CancellationToken ct = default) => CompanyApi.UnlinkCompanyContactAsync(id, version, contactId, ct);
    }
}
