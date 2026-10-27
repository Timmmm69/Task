using Task.Desktop.Projects;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;
using Task.Desktop.Tests.Work;
using AsyncTask = System.Threading.Tasks.Task;
using LinkResult = Task.Desktop.Work.DesktopWorkResult<Task.Desktop.Work.DesktopObjectLinks>;

namespace Task.Desktop.Tests.Projects;

public sealed partial class ProjectsViewModelTests
{
    private static string[] LinkCaps => ProjectContactsTests.Caps.Concat(ProjectFilesTests.Caps).Distinct().ToArray();
    private static ProjectsViewModel LinkHost(ProjectContactsTests.Client api, ProjectFilesTests.Client catalog) =>
        new(SectionClient(), User, LinkCaps,
            files: new(api, catalog, new(catalog, LinkCaps, TimeSpan.Zero), catalog.Resolve),
            contacts: new(api, api, new(api, LinkCaps, TimeSpan.Zero)));

    [Fact]
    public async AsyncTask ProjectLinksHost_SharesVersionAcrossFilesContactsAndDeletes_PreservesEditorAndUnrelatedRows()
    {
        var api = new ProjectContactsTests.Client(); var catalog = new ProjectFilesTests.Client();
        using var host = LinkHost(api, catalog); host.Activate(); await host.CurrentDetailsOperation;
        Assert.Equal(0, api.Reads); await host.EditProjectCommand.ExecuteAsync();
        var captured = host.Editor!.Source!.Version; host.Editor.Name = "Черновик";
        await host.OpenFilesSectionAsync(); await host.OpenContactsSectionAsync();
        Assert.Same(host.Contacts!.Section, host.ContactsSection);
        await ProjectFilesTests.SelectTarget(host.Files!); await host.Files!.AddCommand.ExecuteAsync();
        Assert.Equal(5, api.LastAdd.Item2); Assert.Single(host.Files.Items); Assert.Empty(host.Contacts.Items);
        await ProjectContactsTests.SelectTarget(host.Contacts); await host.Contacts.AddCommand.ExecuteAsync();
        Assert.Equal(6, api.LastAdd.Item2); Assert.Single(host.Files.Items); Assert.Single(host.Contacts.Items);
        Assert.Equal(7, host.SelectedItem!.Source.Version);
        await host.Files.RemoveCommand.ExecuteAsync(); Assert.Equal(7, api.LastRemove.Item2);
        Assert.Empty(host.Files.Items); Assert.Single(host.Contacts.Items); Assert.Equal(8, host.SelectedItem.Source.Version);
        await host.Contacts.RemoveCommand.ExecuteAsync(); Assert.Equal(8, api.LastRemove.Item2); Assert.Empty(api.Rows);
        Assert.Equal(9, host.SelectedItem.Source.Version); Assert.Equal(captured, host.Editor.Source.Version);
        Assert.Equal("Черновик", host.Editor.Name); Assert.Equal(0, api.CrmWrites);
    }

    [Fact]
    public async AsyncTask ProjectLinksHost_MutationsCannotRaceBetweenSections_LeaseReleasesAfterFailure()
    {
        var api = new ProjectContactsTests.Client(); var catalog = new ProjectFilesTests.Client();
        using var host = LinkHost(api, catalog); host.Activate(); await host.CurrentDetailsOperation;
        await host.OpenFilesSectionAsync(); await host.OpenContactsSectionAsync();
        await ProjectFilesTests.SelectTarget(host.Files!); await ProjectContactsTests.SelectTarget(host.Contacts!);
        var gate = new System.Threading.Tasks.TaskCompletionSource<LinkResult>(); api.Write = () => gate.Task;
        var pending = host.Contacts!.AddCommand.ExecuteAsync();
        Assert.False(host.Files!.AddCommand.CanExecute(null)); Assert.False(await host.Files.AddCommand.ExecuteAsync()); Assert.Equal(1, api.Adds);
        gate.SetResult(new LinkResult.Forbidden()); await pending;
        Assert.True(host.Files.AddCommand.CanExecute(null)); api.Write = null; await host.Files.AddCommand.ExecuteAsync();
        Assert.Equal(2, api.Adds); Assert.Single(host.Files.Items); Assert.Equal(6, host.SelectedItem!.Source.Version);
    }

    [Fact]
    public async AsyncTask ProjectLinksHost_OlderReadCannotRollBackSharedVersionOrReportFalseSuccess()
    {
        var api = new ProjectContactsTests.Client(); var catalog = new ProjectFilesTests.Client();
        using var host = LinkHost(api, catalog); host.Activate(); await host.CurrentDetailsOperation;
        await host.OpenFilesSectionAsync(); await host.OpenContactsSectionAsync();
        var gate = new System.Threading.Tasks.TaskCompletionSource<LinkResult>(); api.Read = (_, _) => gate.Task;
        var old = host.Files!.RefreshCommand.ExecuteAsync(); api.Read = null;
        await ProjectContactsTests.SelectTarget(host.Contacts!); await host.Contacts!.AddCommand.ExecuteAsync();
        gate.SetResult(new LinkResult.Succeeded(new([], 5))); await old;
        Assert.Equal(6, host.SelectedItem!.Source.Version); Assert.Contains("Версия проекта", host.Files.Message);
        Assert.False(host.Files.ChooseCommand.CanExecute(null)); Assert.Single(host.Contacts.Items);
        await host.Files.RefreshCommand.ExecuteAsync(); await ProjectFilesTests.SelectTarget(host.Files); await host.Files.AddCommand.ExecuteAsync();
        Assert.Equal(6, api.LastAdd.Item2); Assert.Equal(7, host.SelectedItem.Source.Version);
    }

    [Theory]
    [InlineData("switch")]
    [InlineData("session")]
    [InlineData("scope")]
    [InlineData("offline")]
    [InlineData("deactivate")]
    [InlineData("close")]
    public async AsyncTask ProjectLinksHost_CancelsBothSectionsOnContextChanges(string transition)
    {
        var api = new ProjectContactsTests.Client(); using var host = LinkHost(api, new());
        host.Activate(); await host.CurrentDetailsOperation;
        var gate = new System.Threading.Tasks.TaskCompletionSource<LinkResult>(); api.Read = (_, _) => gate.Task;
        var files = host.OpenFilesSectionAsync(); var contacts = host.OpenContactsSectionAsync();
        switch (transition)
        {
            case "switch": host.SelectedItem = new(Project() with { Id = Guid.NewGuid() }); break;
            case "session": host.UpdateSessionState(false); break;
            case "scope": host.UpdateCapabilities(LinkCaps); break;
            case "offline": host.UpdateConnectivity(false); break;
            case "deactivate": host.Deactivate(); break;
            case "close": host.SelectedItem = null; break;
        }
        gate.SetResult(new LinkResult.Succeeded(new([api.Link(ProjectId, api.Contact.Id)], 5))); await files; await contacts;
        Assert.Empty(host.Files!.Items); Assert.Empty(host.Contacts!.Items); Assert.Equal(0, api.ContactReads + api.CompanyReads);
    }

    [Fact]
    public async AsyncTask ProjectLinksHost_ForbiddenProjectNeverLoadsCrm_AndReopeningReloadsCollapsedSection()
    {
        var client = SectionClient(); client.ProjectHandler = (_, _) => AsyncTask.FromResult<DesktopProjectResult<DesktopProjectDto>>(new DesktopProjectResult<DesktopProjectDto>.Forbidden());
        var api = new ProjectContactsTests.Client(); using var blocked = new ProjectsViewModel(client, User, LinkCaps, contacts: new(api, api, new(api, LinkCaps)));
        blocked.Activate(); await blocked.CurrentDetailsOperation; await blocked.OpenContactsSectionAsync(); Assert.Equal(0, api.Reads);
        using var host = LinkHost(api, new()); host.Activate(); await host.CurrentDetailsOperation;
        await host.OpenContactsSectionAsync(); Assert.Equal(ProjectSectionState.Empty, host.ContactsSection.State);
        host.CloseContactsSection(); api.Rows.Add(api.Link(ProjectId, api.Contact.Id));
        await host.OpenContactsSectionAsync(); Assert.Single(host.Contacts!.Items); Assert.Equal(2, api.Reads);
        host.UpdateCapabilities(LinkCaps.Where(code => code != "Contact.Read"));
        Assert.Empty(host.Contacts.Items); Assert.Empty(host.Contacts.Picker.Results); Assert.False(host.Contacts.RefreshCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("contact")]
    [InlineData("company")]
    public async AsyncTask ProjectLinksHost_NavigationUsesExistingShellAndCrmCards(string type)
    {
        var api = new ProjectContactsTests.Client(); api.Rows.Add(api.Link(ProjectId, type == "contact" ? api.Contact.Id : api.Company.Id));
        using var host = LinkHost(api, new());
        var crm = new CompanyTestClient(api.Company); crm.Contacts.Add(api.Contact.Id, api.Contact);
        using var hub = new WorkHubViewModel(new WorkHubViewModelTests.FakeClient { CompanyApi = crm, ContactDetailHandler = api.GetContactAsync }, LinkCaps);
        using var shell = new MainWindowViewModel(null, null, projects: host, workHub: hub);
        await shell.OpenWorkObjectAsync("project", ProjectId); await host.CurrentDetailsOperation; await host.OpenContactsSectionAsync();
        var card = hub.ContactCard; var companies = hub.Companies;
        await host.Contacts!.NavigateCommand.ExecuteAsync();
        Assert.Equal("contacts", shell.SelectedSection!.Route);
        Assert.Same(card, hub.ContactCard); Assert.Same(companies, hub.Companies);
        if (type == "contact") { Assert.False(hub.ShowCompanies); Assert.Equal(api.Contact.Id, hub.ContactCard?.Details?.Contact.Id); }
        else { Assert.True(hub.ShowCompanies); Assert.Equal(api.Company.Id, hub.Companies?.Details?.Id); }
    }
}
