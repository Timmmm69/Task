using Task.Desktop.ViewModels;
using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;
using LinkResult = Task.Desktop.Work.DesktopWorkResult<Task.Desktop.Work.DesktopObjectLinks>;
using PageResult = Task.Desktop.Work.DesktopWorkResult<System.Collections.Generic.IReadOnlyList<Task.Desktop.Work.DesktopSearchResult>>;

namespace Task.Desktop.Tests.Work;

public sealed partial class WorkHubViewModelTests
{
    internal sealed partial class FakeClient : IDesktopObjectLinksClient, IDesktopObjectLinkTargetSearchClient
    {
        internal readonly ObjectLinksIntegrationTests.Client LinkTransport = new();
        public System.Threading.Tasks.Task<LinkResult> GetLinksAsync(Guid source, CancellationToken ct = default) => LinkTransport.GetLinksAsync(source, ct);
        public System.Threading.Tasks.Task<LinkResult> AddLinkAsync(Guid source, long version, Guid target, string type, CancellationToken ct = default) => LinkTransport.AddLinkAsync(source, version, target, type, ct);
        public System.Threading.Tasks.Task<LinkResult> RemoveLinkAsync(Guid source, long version, Guid link, CancellationToken ct = default) => LinkTransport.RemoveLinkAsync(source, version, link, ct);
        public System.Threading.Tasks.Task<PageResult> SearchTargetsAsync(string query, IReadOnlyCollection<string> types, string? cursor, CancellationToken ct = default) => LinkTransport.SearchTargetsAsync(query, types, cursor, ct);
    }

    [Fact]
    public async AsyncTask LinksUseCurrentContactCatalogAndTypedSearchSource()
    {
        var contact = new DesktopContact(Guid.NewGuid(), 3, "Мария", "Мария", null, null, "active", "active");
        var client = new FakeClient { Contacts = [contact] };
        using var hub = new WorkHubViewModel(client, ObjectLinksIntegrationTests.Access.Where(p => p != "ObjectLink.Delete"));
        hub.Activate(WorkHubArea.Contacts); await Eventually(() => hub.Contacts.Count == 1);
        hub.SelectedContact = contact;
        Assert.Equal("contact", hub.Links!.SourceType); Assert.Equal("contact_file", Assert.Single(hub.Links.LinkTypes).Value);
        await ObjectLinksIntegrationTests.Select(hub.Links);
        await hub.Links.AddCommand.ExecuteAsync();
        Assert.Equal((contact.Id, 3L, client.LinkTransport.Hit.ObjectId, "contact_file"), client.LinkTransport.Added);
        Assert.False(hub.Links.RemoveCommand.CanExecute(null));
        var confirmedVersion = hub.SelectedContact!.Version;
        hub.Activate(WorkHubArea.Catalog); await Eventually(() => hub.Catalog.Count == 1);
        hub.SelectedCatalogItem = Assert.Single(hub.CatalogRoots);
        Assert.Equal("catalog_item", hub.Links.SourceType); Assert.Empty(hub.Links.LinkTypes); Assert.False(hub.Links.ShowPicker);
        await hub.Links.RefreshCommand.ExecuteAsync(); Assert.Single(hub.Links.Items);
        hub.SelectedContact = contact; Assert.Equal("catalog_item", hub.Links.SourceType);
        hub.Activate(WorkHubArea.Search);
        var project = ObjectLinkTargetPickerTests.Hit("project") with { Version = 5 };
        hub.SelectedSearchHit = new(project, project.Title, "", "");
        Assert.Equal("project", hub.Links.SourceType); Assert.Equal("project_file", Assert.Single(hub.Links.LinkTypes).Value);
        await ObjectLinksIntegrationTests.Select(hub.Links); await hub.Links.AddCommand.ExecuteAsync();
        Assert.Equal(project.ObjectId, client.LinkTransport.Added!.Value.Source); Assert.Equal(5, client.LinkTransport.Added!.Value.Version);
        Assert.Equal(client.LinkTransport.Version, hub.SelectedSearchHit.Source.Version);
        Assert.True(confirmedVersion > contact.Version);
        hub.Deactivate(); Assert.False(hub.Links.AddCommand.CanExecute(null)); Assert.Empty(hub.Links.Items);
    }

    [Fact]
    public async AsyncTask SearchRowCanExplicitlySelectCurrentLinkSourceWithoutOpeningIt()
    {
        var task = ObjectLinkTargetPickerTests.Hit("task"); var project = ObjectLinkTargetPickerTests.Hit("project");
        var client = new FakeClient { SearchHandler = (_, _) => AsyncTask.FromResult<DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>>(new DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.Succeeded([task, project])) };
        using var hub = new WorkHubViewModel(client, ObjectLinksIntegrationTests.Access);
        hub.Activate(WorkHubArea.Search); hub.SearchQuery = "План"; await hub.SearchCommand.ExecuteAsync();
        var hit = hub.SearchGroups.SelectMany(group => group.Items).Single(row => row.Source.ObjectId == project.ObjectId);
        var opened = false; hub.OpenObjectRequested += (_, _) => opened = true;
        Assert.True(await hub.SelectLinkSourceCommand.ExecuteAsync(hit));
        Assert.False(opened); Assert.Equal("project", hub.Links!.SourceType); Assert.Contains(project.Title, hub.LinksHeader);
        await ObjectLinksIntegrationTests.Select(hub.Links); await hub.Links.AddCommand.ExecuteAsync();
        Assert.Equal(project.ObjectId, client.LinkTransport.Added!.Value.Source); Assert.Equal(project.ObjectId, hub.SelectedSearchHit!.Source.ObjectId);
        Assert.False(hub.SelectLinkSourceCommand.CanExecute(hit)); // Old version is no longer a valid row.
        hub.UpdateCapabilities(ObjectLinksIntegrationTests.Access.Where(p => p != "ObjectLink.Read"));
        Assert.False(hub.SelectLinkSourceCommand.CanExecute(hub.SelectedSearchHit));
    }

    [Theory]
    [InlineData("ObjectLink.Create")][InlineData("Contact.Update")]
    public async AsyncTask LinksEnforceCreateAndObjectWritesIndependentlyFromDelete(string missing)
    {
        var contact = new DesktopContact(Guid.NewGuid(), 3, "Мария", "Мария", null, null, "active", "active");
        using var hub = new WorkHubViewModel(new FakeClient { Contacts = [contact] }, ObjectLinksIntegrationTests.Access.Where(p => p != missing));
        hub.Activate(WorkHubArea.Contacts); await Eventually(() => hub.Contacts.Count == 1); hub.SelectedContact = contact;
        Assert.False(hub.Links!.AddCommand.CanExecute(null));
        hub.UpdateCapabilities(ObjectLinksIntegrationTests.Access.Where(p => p != "ObjectLink.Delete"));
        await ObjectLinksIntegrationTests.Select(hub.Links); Assert.True(hub.Links.AddCommand.CanExecute(null));
        hub.UpdateConnectivity(false); Assert.False(hub.Links.TargetPicker!.CanQuery); Assert.Null(hub.Links.TargetPicker.SelectedTarget);
        hub.UpdateConnectivity(true); await ObjectLinksIntegrationTests.Select(hub.Links);
        hub.UpdateSessionState(false); Assert.False(hub.Links.AddCommand.CanExecute(null)); Assert.Empty(hub.Links.Items);
    }
    [Theory]
    [InlineData("calendar_event", "Task.Read")][InlineData("calendar_event", "Calendar.Read")]
    [InlineData("interaction", "Contact.Read")][InlineData("catalog_item", "FileCatalog.Read")]
    public async AsyncTask UnsupportedCreationSourcesRetainPermissionCorrectReadOnlyLinks(string type, string permission)
    {
        using var hub = new WorkHubViewModel(new FakeClient(), ["Search.Use", "ObjectLink.Read", permission]);
        hub.Activate(WorkHubArea.Search); var hit = ObjectLinkTargetPickerTests.Hit(type); hub.SelectedSearchHit = new(hit, hit.Title, "", "");
        Assert.True(hub.Links!.RefreshCommand.CanExecute(null)); Assert.Empty(hub.Links.LinkTypes); Assert.False(hub.Links.AddCommand.CanExecute(null));
        Assert.True(await hub.Links.RefreshCommand.ExecuteAsync());
    }

    [Fact]
    public async AsyncTask TaskLinkDoesNotInventGlobalTargetUpdateOrDeleteRequirement()
    {
        using var hub = new WorkHubViewModel(new FakeClient(), ["Task.Read", "Search.Use", "ObjectLink.Read", "ObjectLink.Create", "FileCatalog.Read"]);
        hub.Activate(WorkHubArea.Search); hub.SearchQuery = "alpha"; await hub.SearchCommand.ExecuteAsync();
        Assert.Equal("task", hub.Links!.SourceType);
        await ObjectLinksIntegrationTests.Select(hub.Links); Assert.True(hub.Links.AddCommand.CanExecute(null));
        await hub.Links.AddCommand.ExecuteAsync(); Assert.Single(hub.Links.Items); Assert.False(hub.Links.RemoveCommand.CanExecute(null));
        // Effective target update scope is certified by POST, not by a global capability approximation.
    }

    [Fact]
    public async AsyncTask LinksCanBeGrantedReadAfterConstructionAndRevokedWithoutLeakingRows()
    {
        var contact = new DesktopContact(Guid.NewGuid(), 3, "Мария", "Мария", null, null, "active", "active");
        using var hub = new WorkHubViewModel(new FakeClient { Contacts = [contact] }, ["Contact.Read"]);
        hub.Activate(WorkHubArea.Contacts); await Eventually(() => hub.Contacts.Count == 1); hub.SelectedContact = contact;
        Assert.False(hub.HasLinks); Assert.False(hub.Links!.RefreshCommand.CanExecute(null));
        hub.UpdateCapabilities(ObjectLinksIntegrationTests.Access); Assert.True(hub.HasLinks);
        await ObjectLinksIntegrationTests.Select(hub.Links); await hub.Links.AddCommand.ExecuteAsync(); Assert.Single(hub.Links.Items);
        hub.UpdateCapabilities(ObjectLinksIntegrationTests.Access.Where(p => p != "ObjectLink.Read"));
        Assert.Empty(hub.Links.Items); Assert.Null(hub.Links.TargetPicker!.SelectedTarget); Assert.False(hub.Links.RefreshCommand.CanExecute(null));
    }
}
