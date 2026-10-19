using Task.Desktop.Administration;
using Task.Desktop.Projects;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;
using LinkResult = Task.Desktop.Work.DesktopWorkResult<Task.Desktop.Work.DesktopObjectLinks>;
using CatalogResult = Task.Desktop.Work.DesktopWorkResult<Task.Desktop.Work.DesktopCatalogItem>;
using LocationResult = Task.Desktop.Work.DesktopWorkResult<Task.Desktop.Work.DesktopFileLocation?>;

namespace Task.Desktop.Tests.Projects;

public sealed class ProjectFilesTests
{
    internal static readonly string[] Caps = ["Project.Read", "Project.Update", "FileCatalog.Read", "ObjectLink.Read", "ObjectLink.Create", "ObjectLink.Delete", "FileReference.Open"];
    internal static DesktopProjectDto Project() => new(Guid.NewGuid(), Guid.NewGuid(), 5, "Проект", null, Guid.NewGuid(), null,
        DesktopProjectStatus.Active, null, null, null, null, null, "active");
    internal static ProjectFilesViewModel Create(Client client, DesktopProjectDto project, string[]? caps = null, IFileAccessAdapter? adapter = null)
    {
        var vm = new ProjectFilesViewModel(client, client, new(client, caps ?? Caps, TimeSpan.Zero), client.Resolve, adapter);
        vm.SetContext(project, caps ?? Caps, true, true, true); return vm;
    }

    [Fact]
    public async AsyncTask LoadsOnlyOutgoingProjectFiles_ResolvesAllowedMetadata_AndConfirmsVersion()
    {
        var project = Project(); var client = new Client();
        client.Rows.AddRange([client.Link(project.Id), client.Link(project.Id) with { LinkType = "related" }, client.Link(Guid.NewGuid()) with { TargetObjectId = project.Id }]);
        using var vm = Create(client, project); long version = 0;
        vm.VersionConfirmed += (_, v) => version = v;
        await vm.RefreshCommand.ExecuteAsync();
        var row = Assert.Single(vm.Items); Assert.Equal("Договор", row.Title); Assert.Equal("Файл", row.TypeLabel);
        Assert.Equal(1, client.MetadataCalls); Assert.Equal(5, version);
        Assert.Equal(ProjectSectionState.Loaded, vm.Section.State);
    }

    [Theory]
    [InlineData("Project.Read")][InlineData("FileCatalog.Read")][InlineData("ObjectLink.Read")]
    public async AsyncTask MissingReadPermissionMakesNoRequests(string denied)
    {
        var client = new Client(); using var vm = Create(client, Project(), Caps.Where(p => p != denied).ToArray());
        Assert.False(await vm.RefreshCommand.ExecuteAsync()); Assert.Equal(0, client.ReadCalls); Assert.Equal(0, client.MetadataCalls);
        Assert.Equal(ProjectSectionState.Forbidden, vm.Section.State);
    }

    [Theory]
    [InlineData("ObjectLink.Create")][InlineData("ObjectLink.Delete")][InlineData("Project.Update")]
    public async AsyncTask MissingWritePermissionDisablesOnlyApplicableWrites(string denied)
    {
        var project = Project(); var client = new Client(); client.Rows.Add(client.Link(project.Id));
        using var vm = Create(client, project, Caps.Where(p => p != denied).ToArray()); await vm.RefreshCommand.ExecuteAsync();
        Assert.Equal(denied is not ("ObjectLink.Create" or "Project.Update"), vm.ChooseCommand.CanExecute(null));
        Assert.Equal(denied is not ("ObjectLink.Delete" or "Project.Update"), vm.RemoveCommand.CanExecute(null));
        Assert.True(vm.NavigateCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("forbidden")][InlineData("notfound")][InlineData("trashed")][InlineData("offline")]
    public async AsyncTask UnavailableMetadataHasNeutralNameAndNoOpenOrNavigation(string fault)
    {
        var project = Project(); var client = new Client(); client.Rows.Add(client.Link(project.Id));
        client.Metadata = (_, _) => AsyncTask.FromResult<CatalogResult>(fault switch
        { "forbidden" => new CatalogResult.Forbidden(), "notfound" => new CatalogResult.NotFound(), "offline" => new CatalogResult.ServerUnavailable(),
            _ => new CatalogResult.Succeeded(client.Item with { LifecycleState = "trashed" }) });
        using var vm = Create(client, project); await vm.RefreshCommand.ExecuteAsync();
        Assert.Equal("Объект каталога недоступен", Assert.Single(vm.Items).Title);
        Assert.False(vm.OpenCommand.CanExecute(null)); Assert.False(vm.NavigateCommand.CanExecute(null));
        Assert.Equal(ProjectSectionState.PartialAccess, vm.Section.State);
        Assert.True(vm.RemoveCommand.CanExecute(null));
    }

    [Fact]
    public async AsyncTask EmptyIsOnlyReportedAfterSuccessfulRead()
    {
        var client = new Client(); using var vm = Create(client, Project());
        client.Read = (_, _) => AsyncTask.FromResult<LinkResult>(new LinkResult.ServerUnavailable());
        await vm.RefreshCommand.ExecuteAsync(); Assert.Equal(ProjectSectionState.ServerUnavailable, vm.Section.State);
        client.Read = null; await vm.RefreshCommand.ExecuteAsync(); Assert.Equal(ProjectSectionState.Empty, vm.Section.State);
    }

    internal static async AsyncTask SelectTarget(ProjectFilesViewModel vm)
    {
        await vm.ChooseCommand.ExecuteAsync(); vm.Picker.SearchText = "Договор"; await vm.Picker.CurrentOperation;
        vm.Picker.Candidate = Assert.Single(vm.Picker.Results); Assert.True(vm.Picker.ConfirmSelection());
    }

    [Fact]
    public async AsyncTask AddWaitsForServerReadback_UsesProjectFileAndSnapshotVersion()
    {
        var project = Project(); var client = new Client(); using var vm = Create(client, project);
        await vm.RefreshCommand.ExecuteAsync(); await SelectTarget(vm);
        var gate = new System.Threading.Tasks.TaskCompletionSource<LinkResult>();
        client.Write = () => gate.Task;
        var operation = vm.AddCommand.ExecuteAsync();
        Assert.Empty(vm.Items); Assert.Equal(1, client.AddCalls); Assert.Equal((project.Id, 5L, client.Item.Id, "project_file"), client.LastAdd);
        client.Rows.Add(client.Link(project.Id)); gate.SetResult(new LinkResult.Succeeded(new(client.Rows.ToArray(), 6)));
        await operation; Assert.Single(vm.Items); Assert.False(vm.Choosing); Assert.False(vm.AddCommand.CanExecute(null));
    }

    [Fact]
    public async AsyncTask DuplicateAndSelfTargetNeverPost()
    {
        var project = Project(); var client = new Client(); client.Rows.Add(client.Link(project.Id));
        using var vm = Create(client, project); await vm.RefreshCommand.ExecuteAsync(); await SelectTarget(vm);
        Assert.False(await vm.AddCommand.ExecuteAsync()); Assert.Equal(0, client.AddCalls);
        client.Item = client.Item with { Id = project.Id }; vm.Picker.ClearSelection();
        vm.Picker.SearchText = "Договор"; await vm.Picker.CurrentOperation;
        vm.Picker.Candidate = vm.Picker.Results.FirstOrDefault(); Assert.False(vm.Picker.ConfirmSelection());
    }

    [Fact]
    public async AsyncTask DeleteUsesConfirmedLinkIdAndNeverDeletesCatalogObject()
    {
        var project = Project(); var client = new Client(); var link = client.Link(project.Id); client.Rows.Add(link);
        using var vm = Create(client, project); await vm.RefreshCommand.ExecuteAsync();
        vm.Selected = new(link, client.Item); Assert.False(vm.RemoveCommand.CanExecute(null));
        vm.Selected = vm.Items[0]; await vm.RemoveCommand.ExecuteAsync();
        Assert.Equal((project.Id, 5L, link.Id), client.LastRemove); Assert.Empty(vm.Items);
        Assert.NotNull(client.Item); Assert.Equal(ProjectSectionState.Empty, vm.Section.State);
    }

    [Theory]
    [InlineData("conflict")][InlineData("duplicate")][InlineData("forbidden")][InlineData("notfound")][InlineData("offline")][InlineData("malformed")]
    public async AsyncTask MutationFailuresRequireRefreshAndNeverClaimSuccess(string failure)
    {
        var project = Project(); var client = new Client(); using var vm = Create(client, project);
        await vm.RefreshCommand.ExecuteAsync(); await SelectTarget(vm);
        client.Write = () => AsyncTask.FromResult<LinkResult>(failure switch
        { "conflict" => new LinkResult.Conflict(), "duplicate" => new LinkResult.ValidationFailure("Такая связь уже существует. Обновите связи."),
            "forbidden" => new LinkResult.Forbidden(), "notfound" => new LinkResult.NotFound(), "offline" => new LinkResult.ServerUnavailable(), _ => new LinkResult.MalformedResponse() });
        await vm.AddCommand.ExecuteAsync(); Assert.Empty(vm.Items); Assert.False(vm.ChooseCommand.CanExecute(null));
        Assert.False(vm.Section.HasConfirmedData); Assert.DoesNotContain("подтверждены сервером", vm.Message);
        Assert.True(vm.RefreshCommand.CanExecute(null));
        if (failure == "conflict") Assert.Contains("Версия", vm.Message);
        if (failure == "duplicate") Assert.Contains("существует", vm.Message);
    }

    [Theory]
    [InlineData("archived")][InlineData("trashed")]
    public async AsyncTask InactiveProjectAllowsReadingButDisablesAllWrites(string state)
    {
        var project = Project() with { LifecycleState = state }; var client = new Client(); client.Rows.Add(client.Link(project.Id));
        using var vm = Create(client, project); await vm.RefreshCommand.ExecuteAsync();
        Assert.Single(vm.Items); Assert.False(vm.ChooseCommand.CanExecute(null)); Assert.False(vm.RemoveCommand.CanExecute(null)); Assert.False(vm.Picker.CanQuery);
    }

    [Theory]
    [InlineData("switch")][InlineData("organization")][InlineData("scope")][InlineData("session")][InlineData("offline")][InlineData("collapse")][InlineData("dispose")]
    public async AsyncTask LateLoadNeverLeaksAcrossContextTransitions(string transition)
    {
        var project = Project(); var client = new Client(); var gate = new System.Threading.Tasks.TaskCompletionSource<LinkResult>();
        client.Read = (_, _) => gate.Task; using var vm = Create(client, project);
        var operation = vm.RefreshCommand.ExecuteAsync();
        switch (transition)
        {
            case "switch": vm.SetContext(Project(), Caps, true, true, true); break;
            case "organization": vm.SetContext(project with { OrganizationId = Guid.NewGuid() }, Caps, true, true, true); break;
            case "scope": vm.Invalidate(); vm.SetContext(project, Caps, true, true, true); break;
            case "session": vm.SetContext(project, Caps, false, true, true); break;
            case "offline": vm.SetContext(project, Caps, true, false, false); break;
            case "collapse": vm.Invalidate(); break;
            case "dispose": vm.Dispose(); break;
        }
        gate.SetResult(new LinkResult.Succeeded(new([client.Link(project.Id)], 5))); await operation;
        Assert.Empty(vm.Items); Assert.Equal(0, client.MetadataCalls); Assert.False(vm.Section.HasConfirmedData);
    }

    [Theory]
    [InlineData("none")][InlineData("hidden")][InlineData("other-device")][InlineData("disabled")][InlineData("offline")][InlineData("forbidden")]
    public async AsyncTask UnconfirmedOrUnavailablePathIsNeverPassedToWindows(string state)
    {
        var project = Project(); var client = new Client(); client.Rows.Add(client.Link(project.Id)); var files = new Files();
        client.Location = state switch
        {
            "none" => new LocationResult.Succeeded(null), "offline" => new LocationResult.ServerUnavailable(), "forbidden" => new LocationResult.Forbidden(),
            _ => new LocationResult.Succeeded(new(Guid.NewGuid(), 1, "local_path", state == "hidden" ? null : @"C:\private\file.txt", state != "other-device", IsEnabled: state != "disabled"))
        };
        using var vm = Create(client, project, adapter: files); await vm.RefreshCommand.ExecuteAsync(); await vm.OpenCommand.ExecuteAsync();
        Assert.Equal(0, files.Calls); Assert.DoesNotContain("private", vm.Message);
    }

    [Theory]
    [InlineData(FileOpenStatus.NotFound)][InlineData(FileOpenStatus.AccessDenied)][InlineData(FileOpenStatus.Opened)]
    public async AsyncTask AllowedResolutionUsesExistingFileAdapterAndItsAvailabilityResult(FileOpenStatus status)
    {
        var project = Project(); var client = new Client(); client.Rows.Add(client.Link(project.Id)); var files = new Files { Status = status };
        using var vm = Create(client, project, adapter: files); await vm.RefreshCommand.ExecuteAsync(); await vm.OpenCommand.ExecuteAsync();
        Assert.Equal(1, files.Calls); Assert.Equal("adapter-result", vm.Message);
        Assert.Equal(1, client.ResolveCalls); Assert.Equal(2, client.MetadataCalls);
    }

    [Fact]
    public async AsyncTask NoOpenPermissionDoesNotResolvePath_NavigationStillUsesCatalog()
    {
        var project = Project(); var client = new Client(); client.Rows.Add(client.Link(project.Id));
        using var vm = Create(client, project, Caps.Where(p => p != "FileReference.Open").ToArray()); await vm.RefreshCommand.ExecuteAsync();
        Assert.False(await vm.OpenCommand.ExecuteAsync()); Assert.Equal(0, client.ResolveCalls);
        Guid navigated = default; vm.OpenObjectRequested += (type, id) => { Assert.Equal("catalog_item", type); navigated = id; };
        await vm.NavigateCommand.ExecuteAsync(); Assert.Equal(client.Item.Id, navigated);
    }

    [Theory]
    [InlineData("switch")][InlineData("session")][InlineData("permission")]
    public async AsyncTask LatePathResolutionCannotLaunchAfterContextChanges(string transition)
    {
        var project = Project(); var client = new Client(); client.Rows.Add(client.Link(project.Id)); var files = new Files();
        var gate = new System.Threading.Tasks.TaskCompletionSource<LocationResult>();
        client.ResolveHandler = (_, _) => gate.Task;
        using var vm = Create(client, project, adapter: files); await vm.RefreshCommand.ExecuteAsync();
        var pending = vm.OpenCommand.ExecuteAsync();
        vm.SetContext(transition == "switch" ? Project() : project, transition == "permission" ? [] : Caps,
            transition != "session", true, true);
        gate.SetResult(client.Location); await pending; Assert.Equal(0, files.Calls);
    }

    [Fact]
    public async AsyncTask TargetRevokedBetweenListingAndOpenScrubsNameAndNeverResolves()
    {
        var project = Project(); var client = new Client(); client.Rows.Add(client.Link(project.Id));
        using var vm = Create(client, project); await vm.RefreshCommand.ExecuteAsync();
        client.Metadata = (_, _) => AsyncTask.FromResult<CatalogResult>(new CatalogResult.Forbidden());
        await vm.OpenCommand.ExecuteAsync(); Assert.Equal(0, client.ResolveCalls);
        Assert.Equal("Объект каталога недоступен", Assert.Single(vm.Items).Title);
    }

    [Theory]
    [InlineData("wrong-type")][InlineData("trashed")][InlineData("forbidden")]
    public async AsyncTask TargetRevalidatedBeforePost(string fault)
    {
        var project = Project(); var client = new Client(); using var vm = Create(client, project);
        await vm.RefreshCommand.ExecuteAsync(); await SelectTarget(vm);
        client.Metadata = (_, _) => AsyncTask.FromResult<CatalogResult>(fault == "forbidden" ? new CatalogResult.Forbidden()
            : new CatalogResult.Succeeded(client.Item with { ItemType = fault == "wrong-type" ? "contact" : "file_reference", LifecycleState = fault == "trashed" ? "trashed" : "active" }));
        await vm.AddCommand.ExecuteAsync(); Assert.Equal(0, client.AddCalls);
    }

    [Fact]
    public async AsyncTask AuthenticationFailureClearsDataAndNotifiesHost()
    {
        var project = Project(); var client = new Client(); client.Rows.Add(client.Link(project.Id));
        using var vm = Create(client, project); await vm.RefreshCommand.ExecuteAsync();
        var expired = false; vm.SessionExpired += () => expired = true;
        client.Read = (_, _) => AsyncTask.FromResult<LinkResult>(new LinkResult.AuthenticationFailure());
        await vm.RefreshCommand.ExecuteAsync(); Assert.True(expired); Assert.Empty(vm.Items);
        Assert.False(vm.RefreshCommand.CanExecute(null)); Assert.False(vm.Picker.CanQuery);
    }

    internal sealed class Files : IFileAccessAdapter
    {
        public int Calls; public FileOpenStatus Status = FileOpenStatus.Opened;
        public FileOpenResult Open(string path) { Calls++; return new(Status, "adapter-result"); }
    }
    internal sealed class Client : IDesktopObjectLinksClient, IDesktopFileLocationsClient, IDesktopObjectLinkTargetSearchClient
    {
        public DesktopCatalogItem Item = new(Guid.NewGuid(), 1, "Договор", "file_reference", null, ".txt", "active");
        public List<DesktopObjectLink> Rows = [];
        public int ReadCalls, MetadataCalls, AddCalls, ResolveCalls;
        public (Guid, long, Guid, string) LastAdd;
        public (Guid, long, Guid) LastRemove;
        public Func<Guid, CancellationToken, System.Threading.Tasks.Task<LinkResult>>? Read;
        public Func<Guid, CancellationToken, System.Threading.Tasks.Task<CatalogResult>>? Metadata;
        public Func<System.Threading.Tasks.Task<LinkResult>>? Write;
        public Func<Guid, CancellationToken, System.Threading.Tasks.Task<LocationResult>>? ResolveHandler;
        public LocationResult Location = new LocationResult.Succeeded(new(Guid.NewGuid(), 1, "local_path", @"C:\allowed\file.txt", true));
        public DesktopObjectLink Link(Guid source) => new(Guid.NewGuid(), source, Item.Id, "project_file");
        public System.Threading.Tasks.Task<LinkResult> GetLinksAsync(Guid source, CancellationToken ct = default)
        { ReadCalls++; return Read?.Invoke(source, ct) ?? AsyncTask.FromResult<LinkResult>(new LinkResult.Succeeded(new(Rows.ToArray(), 5))); }
        public System.Threading.Tasks.Task<LinkResult> AddLinkAsync(Guid source, long version, Guid target, string type, CancellationToken ct = default)
        { AddCalls++; LastAdd = (source, version, target, type); return Write?.Invoke() ?? AsyncTask.FromResult<LinkResult>(new LinkResult.Conflict()); }
        public System.Threading.Tasks.Task<LinkResult> RemoveLinkAsync(Guid source, long version, Guid linkId, CancellationToken ct = default)
        { LastRemove = (source, version, linkId); Rows.RemoveAll(row => row.Id == linkId); return AsyncTask.FromResult<LinkResult>(new LinkResult.Succeeded(new(Rows.ToArray(), 6))); }
        public System.Threading.Tasks.Task<CatalogResult> GetCatalogItemAsync(Guid id, CancellationToken ct = default)
        { MetadataCalls++; return Metadata?.Invoke(id, ct) ?? AsyncTask.FromResult<CatalogResult>(new CatalogResult.Succeeded(Item with { Id = id })); }
        public System.Threading.Tasks.Task<LocationResult> Resolve(Guid id, CancellationToken ct)
        { ResolveCalls++; return ResolveHandler?.Invoke(id, ct) ?? AsyncTask.FromResult(Location); }
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>> SearchTargetsAsync(string query, IReadOnlyCollection<string> types, string? cursor, CancellationToken cancellationToken = default)
            => AsyncTask.FromResult<DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>>(new DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.Succeeded([new(Item.Id, "catalog_item", Item.Name, null, 1, DateTimeOffset.UtcNow)]));
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopFileLocation>>> GetLocationsAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopNetworkResource>>> GetNetworkResourcesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public System.Threading.Tasks.Task<DesktopWorkResult<bool>> CreateLocationAsync(Guid id, long version, DesktopLocationDraft draft, CancellationToken ct = default) => throw new NotSupportedException();
        public System.Threading.Tasks.Task<DesktopWorkResult<bool>> PatchLocationAsync(Guid id, long version, Guid locationId, DesktopLocationDraft draft, CancellationToken ct = default) => throw new NotSupportedException();
        public System.Threading.Tasks.Task<DesktopWorkResult<bool>> DeleteLocationAsync(Guid id, long version, Guid locationId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
