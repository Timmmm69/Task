using Task.Desktop.Projects;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;
using LinkResult = Task.Desktop.Work.DesktopWorkResult<Task.Desktop.Work.DesktopObjectLinks>;
using ContactResult = Task.Desktop.Work.DesktopWorkResult<Task.Desktop.Work.DesktopContactDetails>;
using CompanyResult = Task.Desktop.Work.DesktopWorkResult<Task.Desktop.Work.DesktopCompany>;

namespace Task.Desktop.Tests.Projects;

public sealed class ProjectContactsTests
{
    internal static readonly string[] Caps = ["Project.Read", "Project.Update", "Contact.Read", "Contact.Update", "ObjectLink.Read", "ObjectLink.Create", "ObjectLink.Delete"];
    internal static ProjectContactsViewModel Create(Client client, DesktopProjectDto project, string[]? caps = null)
    {
        var vm = new ProjectContactsViewModel(client, client, new(client, caps ?? Caps, TimeSpan.Zero));
        vm.SetContext(project, caps ?? Caps, true, true, true); return vm;
    }
    internal static async AsyncTask SelectTarget(ProjectContactsViewModel vm)
    {
        await vm.ChooseCommand.ExecuteAsync(); vm.Picker.SearchText = "Название"; await vm.Picker.CurrentOperation;
        vm.Picker.Candidate = Assert.Single(vm.Picker.Results); Assert.True(vm.Picker.ConfirmSelection());
    }

    [Fact]
    public async AsyncTask LoadsContactAndCompanyNamesWithTypes_OnlyOutgoingProjectContactLinks()
    {
        var project = ProjectFilesTests.Project(); var api = new Client();
        api.Rows.AddRange([api.Link(project.Id, api.Contact.Id), api.Link(project.Id, api.Company.Id),
            api.Link(project.Id, api.Contact.Id) with { LinkType = "related" }, api.Link(Guid.NewGuid(), project.Id)]);
        using var vm = Create(api, project); await vm.RefreshCommand.ExecuteAsync();
        Assert.Equal(["Анна", "Компания"], vm.Items.Select(row => row.Title));
        Assert.Equal(["Контакт", "Компания"], vm.Items.Select(row => row.TypeLabel));
        Assert.Equal(2, api.ContactReads); Assert.Equal(1, api.CompanyReads);
        Assert.Equal(ProjectSectionState.Loaded, vm.Section.State);
        Assert.All(vm.Items, row => Assert.DoesNotContain("secret", row.ToString()));
    }

    [Theory]
    [InlineData("Project.Read")]
    [InlineData("Contact.Read")]
    [InlineData("ObjectLink.Read")]
    public async AsyncTask MissingReadPermissionNeverLoadsLinksNamesOrPicker(string permission)
    {
        var api = new Client(); using var vm = Create(api, ProjectFilesTests.Project(), Caps.Where(code => code != permission).ToArray());
        Assert.False(await vm.RefreshCommand.ExecuteAsync()); Assert.False(await vm.ChooseCommand.ExecuteAsync());
        Assert.False(await vm.Picker.SearchCommand.ExecuteAsync());
        Assert.Equal(0, api.Reads + api.ContactReads + api.CompanyReads + api.Searches);
        Assert.Equal(ProjectSectionState.Forbidden, vm.Section.State);
    }

    [Theory]
    [InlineData("Project.Update")]
    [InlineData("Contact.Update")]
    [InlineData("ObjectLink.Create")]
    [InlineData("ObjectLink.Delete")]
    public async AsyncTask WritePermissionsAreSpecific_ReadingAndNavigationRemainAvailable(string permission)
    {
        var project = ProjectFilesTests.Project(); var api = new Client(); api.Rows.Add(api.Link(project.Id, api.Contact.Id));
        using var vm = Create(api, project, Caps.Where(code => code != permission).ToArray()); await vm.RefreshCommand.ExecuteAsync();
        Assert.Equal(permission == "ObjectLink.Delete", vm.ChooseCommand.CanExecute(null));
        Assert.Equal(permission is not ("ObjectLink.Delete" or "Project.Update"), vm.RemoveCommand.CanExecute(null));
        Assert.True(vm.NavigateCommand.CanExecute(null));
        if (permission == "Contact.Update") Assert.False(vm.Picker.CanQuery);
    }

    [Theory]
    [InlineData("contact")]
    [InlineData("company")]
    public async AsyncTask AddUsesSameContractForBothTypes_AndWaitsForServerConfirmation(string type)
    {
        var project = ProjectFilesTests.Project(); var api = new Client { TargetType = type };
        using var vm = Create(api, project); await vm.RefreshCommand.ExecuteAsync(); await SelectTarget(vm);
        var gate = new System.Threading.Tasks.TaskCompletionSource<LinkResult>(); api.Write = () => gate.Task;
        var operation = vm.AddCommand.ExecuteAsync(); Assert.Empty(vm.Items);
        var target = type == "contact" ? api.Contact.Id : api.Company.Id;
        Assert.Equal((project.Id, 5L, target, "project_contact"), api.LastAdd);
        api.Rows.Add(api.Link(project.Id, target)); gate.SetResult(new LinkResult.Succeeded(new(api.Rows.ToArray(), 6)));
        await operation; Assert.Single(vm.Items); Assert.False(vm.Choosing);
        Assert.False(vm.AddCommand.CanExecute(null)); Assert.Contains("подтверждены сервером", vm.Message);
    }

    [Theory]
    [InlineData("contact")]
    [InlineData("company")]
    public async AsyncTask RemovalChangesOnlyTheConfirmedProjectLink(string type)
    {
        var project = ProjectFilesTests.Project(); var api = new Client();
        var row = api.Link(project.Id, type == "contact" ? api.Contact.Id : api.Company.Id); api.Rows.Add(row);
        var unrelated = api.Link(project.Id, Guid.NewGuid()) with { LinkType = "project_file" }; api.Rows.Add(unrelated);
        using var vm = Create(api, project); await vm.RefreshCommand.ExecuteAsync();
        vm.Selected = new(row, null); Assert.False(vm.RemoveCommand.CanExecute(null));
        vm.Selected = vm.Items[0]; await vm.RemoveCommand.ExecuteAsync();
        Assert.Equal((project.Id, 5L, row.Id), api.LastRemove); Assert.Empty(vm.Items);
        Assert.Equal(unrelated, Assert.Single(api.Rows)); Assert.Equal(0, api.CrmWrites);
        Assert.Equal("Анна", api.Contact.DisplayName); Assert.Equal("Компания", api.Company.Name);
    }

    [Theory]
    [InlineData("403")]
    [InlineData("404")]
    [InlineData("archived")]
    [InlineData("trashed")]
    [InlineData("offline")]
    public async AsyncTask UnavailableTargetsHaveNeutralNamesAndNoNavigation(string fault)
    {
        var project = ProjectFilesTests.Project(); var api = new Client(); api.Rows.Add(api.Link(project.Id, api.Company.Id));
        api.CompanyHandler = (_, _) => AsyncTask.FromResult<CompanyResult>(fault switch
        {
            "403" => new CompanyResult.Forbidden(),
            "404" => new CompanyResult.NotFound(),
            "offline" => new CompanyResult.ServerUnavailable(),
            _ => new CompanyResult.Succeeded(api.Company with { LifecycleState = fault })
        });
        using var vm = Create(api, project); await vm.RefreshCommand.ExecuteAsync();
        Assert.Equal("Контакт или компания недоступны", Assert.Single(vm.Items).Title);
        Assert.Equal(ProjectSectionState.PartialAccess, vm.Section.State); Assert.False(vm.NavigateCommand.CanExecute(null));
        Assert.True(vm.RemoveCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("contact")]
    [InlineData("company")]
    public async AsyncTask NavigationRevalidatesExactTypeAndUsesExistingHostEvent(string type)
    {
        var project = ProjectFilesTests.Project(); var api = new Client(); var id = type == "contact" ? api.Contact.Id : api.Company.Id;
        api.Rows.Add(api.Link(project.Id, id)); using var vm = Create(api, project); await vm.RefreshCommand.ExecuteAsync();
        (string, Guid)? opened = null; vm.OpenObjectRequested += (kind, target) => opened = (kind, target);
        await vm.NavigateCommand.ExecuteAsync(); Assert.Equal((type, id), opened);
        opened = null; api.ContactHandler = (_, _) => AsyncTask.FromResult<ContactResult>(new ContactResult.Forbidden());
        api.CompanyHandler = (_, _) => AsyncTask.FromResult<CompanyResult>(new CompanyResult.Forbidden());
        var companyReads = api.CompanyReads; await vm.NavigateCommand.ExecuteAsync();
        Assert.Null(opened); Assert.Null(Assert.Single(vm.Items).Target); Assert.False(vm.NavigateCommand.CanExecute(null));
        if (type == "contact") Assert.Equal(companyReads, api.CompanyReads);
    }

    [Theory]
    [InlineData("conflict")]
    [InlineData("duplicate")]
    [InlineData("403")]
    [InlineData("404")]
    [InlineData("offline")]
    [InlineData("malformed")]
    public async AsyncTask FailedWritesNeverClaimSuccess_RequireExplicitRefresh(string fault)
    {
        var api = new Client(); using var vm = Create(api, ProjectFilesTests.Project()); await vm.RefreshCommand.ExecuteAsync(); await SelectTarget(vm);
        api.Write = () => AsyncTask.FromResult(Failure(fault)); await vm.AddCommand.ExecuteAsync();
        Assert.Empty(vm.Items); Assert.False(vm.ChooseCommand.CanExecute(null)); Assert.True(vm.RefreshCommand.CanExecute(null));
        Assert.False(vm.Section.HasConfirmedData); Assert.DoesNotContain("подтверждены сервером", vm.Message);
        if (fault == "conflict") Assert.Contains("Версия проекта", vm.Message);
        if (fault == "duplicate") Assert.Contains("существует", vm.Message);
    }

    [Theory]
    [InlineData("links")]
    [InlineData("contact")]
    [InlineData("company")]
    [InlineData("picker")]
    [InlineData("write")]
    public async AsyncTask AuthenticationFailureExpiresSessionAndScrubsNames(string stage)
    {
        var project = ProjectFilesTests.Project(); var api = new Client();
        api.Rows.Add(api.Link(project.Id, stage == "company" ? api.Company.Id : api.Contact.Id));
        using var vm = Create(api, project); var expired = false; vm.SessionExpired += () => expired = true;
        if (stage == "links") api.Read = (_, _) => AsyncTask.FromResult<LinkResult>(new LinkResult.AuthenticationFailure());
        if (stage == "contact") api.ContactHandler = (_, _) => AsyncTask.FromResult<ContactResult>(new ContactResult.AuthenticationFailure());
        if (stage == "company") api.CompanyHandler = (_, _) => AsyncTask.FromResult<CompanyResult>(new CompanyResult.AuthenticationFailure());
        await vm.RefreshCommand.ExecuteAsync();
        if (stage == "picker")
        {
            api.SearchResult = new DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.AuthenticationFailure();
            vm.Picker.SearchText = "Анна"; await vm.Picker.CurrentOperation;
        }
        if (stage == "write")
        {
            api.Rows.Clear(); await vm.RefreshCommand.ExecuteAsync(); await SelectTarget(vm);
            api.Write = () => AsyncTask.FromResult<LinkResult>(new LinkResult.AuthenticationFailure()); await vm.AddCommand.ExecuteAsync();
        }
        Assert.True(expired); Assert.Empty(vm.Items); Assert.Empty(vm.Picker.Results);
        Assert.False(vm.RefreshCommand.CanExecute(null)); Assert.False(vm.Picker.CanQuery);
    }

    [Theory]
    [InlineData("switch")]
    [InlineData("organization")]
    [InlineData("scope")]
    [InlineData("session")]
    [InlineData("offline")]
    [InlineData("collapse")]
    [InlineData("dispose")]
    public async AsyncTask LateReadIsCancelledAndCannotPublishAcrossContextChanges(string transition)
    {
        var project = ProjectFilesTests.Project(); var api = new Client(); var gate = new System.Threading.Tasks.TaskCompletionSource<LinkResult>();
        CancellationToken token = default; api.Read = (_, ct) => { token = ct; return gate.Task; };
        using var vm = Create(api, project); var operation = vm.RefreshCommand.ExecuteAsync();
        Transition(vm, project, transition); Assert.True(token.IsCancellationRequested);
        gate.SetResult(new LinkResult.Succeeded(new([api.Link(project.Id, api.Contact.Id)], 5))); await operation;
        Assert.Empty(vm.Items); Assert.Equal(0, api.ContactReads + api.CompanyReads); Assert.False(vm.Section.HasConfirmedData);
    }

    [Theory]
    [InlineData("switch")]
    [InlineData("scope")]
    [InlineData("session")]
    [InlineData("offline")]
    [InlineData("collapse")]
    public async AsyncTask LateMetadataDoesNotLeakNamesOrTriggerFallback(string transition)
    {
        var project = ProjectFilesTests.Project(); var api = new Client(); api.Rows.Add(api.Link(project.Id, api.Company.Id));
        var gate = new System.Threading.Tasks.TaskCompletionSource<ContactResult>(); api.ContactHandler = (_, _) => gate.Task;
        using var vm = Create(api, project); var operation = vm.RefreshCommand.ExecuteAsync();
        Transition(vm, project, transition); gate.SetResult(new ContactResult.NotFound()); await operation;
        Assert.Empty(vm.Items); Assert.Equal(0, api.CompanyReads);
    }

    [Fact]
    public async AsyncTask DuplicateSelectionDoesNotPost_AndInactiveProjectIsReadOnly()
    {
        var project = ProjectFilesTests.Project(); var api = new Client(); api.Rows.Add(api.Link(project.Id, api.Contact.Id));
        using var vm = Create(api, project); await vm.RefreshCommand.ExecuteAsync(); await SelectTarget(vm);
        Assert.False(await vm.AddCommand.ExecuteAsync()); Assert.Equal(0, api.Adds);
        vm.SetContext(project with { LifecycleState = "archived" }, Caps, true, true, true); await vm.RefreshCommand.ExecuteAsync();
        Assert.Single(vm.Items); Assert.False(vm.ChooseCommand.CanExecute(null)); Assert.False(vm.RemoveCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("403")]
    [InlineData("404")]
    [InlineData("archived")]
    public async AsyncTask TargetRevokedAfterPickerConfirmationNeverPosts(string fault)
    {
        var api = new Client { TargetType = "company" }; using var vm = Create(api, ProjectFilesTests.Project());
        await vm.RefreshCommand.ExecuteAsync(); await SelectTarget(vm);
        api.CompanyHandler = (_, _) => AsyncTask.FromResult<CompanyResult>(fault switch
        { "403" => new CompanyResult.Forbidden(), "404" => new CompanyResult.NotFound(), _ => new CompanyResult.Succeeded(api.Company with { LifecycleState = "archived" }) });
        await vm.AddCommand.ExecuteAsync(); Assert.Equal(0, api.Adds); Assert.Null(vm.Picker.SelectedTarget);
    }

    [Theory]
    [InlineData("403", ProjectSectionState.Forbidden)]
    [InlineData("404", ProjectSectionState.Forbidden)]
    [InlineData("offline", ProjectSectionState.ServerUnavailable)]
    [InlineData("malformed", ProjectSectionState.Error)]
    public async AsyncTask ReadFailuresAreNotReportedAsEmpty_RecoveryRequiresRefresh(string fault, ProjectSectionState expected)
    {
        var api = new Client(); api.Read = (_, _) => AsyncTask.FromResult(Failure(fault));
        var project = ProjectFilesTests.Project(); using var vm = Create(api, project);
        await vm.RefreshCommand.ExecuteAsync(); Assert.Equal(expected, vm.Section.State); Assert.False(vm.Section.HasConfirmedData);
        api.Read = null; await vm.RefreshCommand.ExecuteAsync(); Assert.Equal(ProjectSectionState.Empty, vm.Section.State);
        vm.SetContext(project, Caps, true, false, false); Assert.Equal(ProjectSectionState.Offline, vm.Section.State);
        Assert.False(await vm.RefreshCommand.ExecuteAsync()); Assert.DoesNotContain("доступны для просмотра", vm.Section.StatusText);
    }

    [Theory]
    [InlineData("switch")]
    [InlineData("scope")]
    [InlineData("session")]
    [InlineData("offline")]
    [InlineData("collapse")]
    public async AsyncTask LateMutationCannotRestoreOldNamesOrReportSuccess(string transition)
    {
        var project = ProjectFilesTests.Project(); var api = new Client(); using var vm = Create(api, project);
        await vm.RefreshCommand.ExecuteAsync(); await SelectTarget(vm);
        var gate = new System.Threading.Tasks.TaskCompletionSource<LinkResult>(); api.Write = () => gate.Task;
        var pending = vm.AddCommand.ExecuteAsync(); Transition(vm, project, transition);
        gate.SetResult(new LinkResult.Succeeded(new([api.Link(project.Id, api.Contact.Id)], 6))); await pending;
        Assert.Empty(vm.Items); Assert.Empty(vm.Picker.Results); Assert.DoesNotContain("подтверждены сервером", vm.Message);
    }

    private static void Transition(ProjectContactsViewModel vm, DesktopProjectDto project, string transition)
    {
        switch (transition)
        {
            case "switch": vm.SetContext(ProjectFilesTests.Project(), Caps, true, true, true); break;
            case "organization": vm.SetContext(project with { OrganizationId = Guid.NewGuid() }, Caps, true, true, true); break;
            case "scope": vm.Invalidate(); vm.SetContext(project, Caps, true, true, true); break;
            case "session": vm.SetContext(project, Caps, false, true, true); break;
            case "offline": vm.SetContext(project, Caps, true, false, false); break;
            case "collapse": vm.Invalidate(); break;
            case "dispose": vm.Dispose(); break;
        }
    }
    internal static LinkResult Failure(string fault) => fault switch
    {
        "conflict" => new LinkResult.Conflict(),
        "duplicate" => new LinkResult.ValidationFailure("Такая связь уже существует. Обновите связи."),
        "403" => new LinkResult.Forbidden(),
        "404" => new LinkResult.NotFound(),
        "offline" => new LinkResult.ServerUnavailable(),
        _ => new LinkResult.MalformedResponse()
    };

    internal sealed class Client : IDesktopObjectLinksClient, IDesktopCompaniesClient, IDesktopObjectLinkTargetSearchClient
    {
        public DesktopContact Contact = new(Guid.NewGuid(), 1, "Анна", "Анна", null, "secret notes", "active", "active");
        public DesktopCompany Company = new(Guid.NewGuid(), 1, "Компания", Notes: "secret company notes");
        public string TargetType = "contact";
        public long Version = 5;
        public List<DesktopObjectLink> Rows = [];
        public int Reads, ContactReads, CompanyReads, Adds, Searches, CrmWrites;
        public (Guid, long, Guid, string) LastAdd;
        public (Guid, long, Guid) LastRemove;
        public Func<Guid, CancellationToken, System.Threading.Tasks.Task<LinkResult>>? Read;
        public Func<Guid, CancellationToken, System.Threading.Tasks.Task<ContactResult>>? ContactHandler;
        public Func<Guid, CancellationToken, System.Threading.Tasks.Task<CompanyResult>>? CompanyHandler;
        public Func<System.Threading.Tasks.Task<LinkResult>>? Write;
        public DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>? SearchResult;
        public DesktopObjectLink Link(Guid project, Guid target) => new(Guid.NewGuid(), project, target, "project_contact", "untrusted title");
        public System.Threading.Tasks.Task<LinkResult> GetLinksAsync(Guid source, CancellationToken ct = default)
        { Reads++; return Read?.Invoke(source, ct) ?? AsyncTask.FromResult<LinkResult>(new LinkResult.Succeeded(new(Rows.ToArray(), Version))); }
        public System.Threading.Tasks.Task<LinkResult> AddLinkAsync(Guid source, long version, Guid target, string type, CancellationToken ct = default)
        {
            Adds++; LastAdd = (source, version, target, type);
            if (Write is not null) return Write();
            if (version != Version) return AsyncTask.FromResult<LinkResult>(new LinkResult.Conflict());
            Rows.Add(Link(source, target) with { LinkType = type }); Version++;
            return GetLinksAsync(source, ct);
        }
        public System.Threading.Tasks.Task<LinkResult> RemoveLinkAsync(Guid source, long version, Guid link, CancellationToken ct = default)
        {
            LastRemove = (source, version, link); if (version != Version) return AsyncTask.FromResult<LinkResult>(new LinkResult.Conflict());
            Rows.RemoveAll(row => row.Id == link); Version++; return GetLinksAsync(source, ct);
        }
        public System.Threading.Tasks.Task<ContactResult> GetContactAsync(Guid id, CancellationToken ct = default)
        { ContactReads++; return ContactHandler?.Invoke(id, ct) ?? AsyncTask.FromResult<ContactResult>(id == Contact.Id ? new ContactResult.Succeeded(new(Contact, null)) : new ContactResult.NotFound()); }
        public System.Threading.Tasks.Task<CompanyResult> GetCompanyAsync(Guid id, CancellationToken ct = default)
        { CompanyReads++; return CompanyHandler?.Invoke(id, ct) ?? AsyncTask.FromResult<CompanyResult>(id == Company.Id ? new CompanyResult.Succeeded(Company) : new CompanyResult.NotFound()); }
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>> SearchTargetsAsync(string query, IReadOnlyCollection<string> types, string? cursor, CancellationToken cancellationToken = default)
        { Searches++; Assert.Equal(["contact", "company"], types); return AsyncTask.FromResult(SearchResult ?? new DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.Succeeded([new(TargetType == "contact" ? Contact.Id : Company.Id, TargetType, "Название", null, 1, DateTimeOffset.UtcNow)])); }
        public System.Threading.Tasks.Task<ContactResult> UpdateContactAsync(DesktopContactDetails value, CancellationToken ct = default) { CrmWrites++; throw new NotSupportedException(); }
        public System.Threading.Tasks.Task<CompanyResult> SaveCompanyAsync(DesktopCompany value, string? key, CancellationToken ct = default) { CrmWrites++; throw new NotSupportedException(); }
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopCompany>>> GetCompaniesAsync(string query, string? cursor, CancellationToken ct = default) => throw new NotSupportedException();
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopContact>>> FindCompanyContactsAsync(string query, string? cursor, CancellationToken ct = default) => throw new NotSupportedException();
        public System.Threading.Tasks.Task<CompanyResult> LinkCompanyContactAsync(Guid id, long version, DesktopCompanyContact link, string key, CancellationToken ct = default) => throw new NotSupportedException();
        public System.Threading.Tasks.Task<CompanyResult> UnlinkCompanyContactAsync(Guid id, long version, Guid contact, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
