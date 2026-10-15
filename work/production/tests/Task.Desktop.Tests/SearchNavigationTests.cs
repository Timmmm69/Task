using Task.Desktop.ViewModels;
using Task.Desktop.Work;
using FakeClient = Task.Desktop.Tests.Work.WorkHubViewModelTests.FakeClient;

namespace Task.Desktop.Tests;

public sealed class SearchNavigationTests
{
    [Theory]
    [InlineData("task", "tasks")]
    [InlineData("project", "projects")]
    [InlineData("calendar_event", "calendar")]
    [InlineData("catalog_item", "catalog")]
    [InlineData("file_location", "catalog")]
    [InlineData("contact", "contacts")]
    [InlineData("company", null)]
    [InlineData("employee_profile", null)]
    [InlineData("interaction", null)]
    [InlineData("comment", null)]
    [InlineData("future_type", null)]
    public async System.Threading.Tasks.Task SearchRoutesUseObjectIdentityAndExplicitFallback(string type, string? route)
    {
        var parent = Guid.NewGuid();
        var id = type is "file_location" or "catalog_item" ? FakeClient.ItemId : Guid.NewGuid();
        var hit = new DesktopSearchResult(id, type, "Object", type == "file_location" ? FakeClient.ItemId : parent, 1, DateTimeOffset.UtcNow);
        var client = new FakeClient
        {
            SearchHandler = (_, _) => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>>(
                new DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.Succeeded([hit])),
            Contacts = [new(id, 1, "Contact", "Contact", null, null, "active", "active")]
        };
        using var hub = new WorkHubViewModel(client, ["Search.Use", "Contact.Read", "FileCatalog.Read"]);
        using var shell = new MainWindowViewModel(null, null, workHub: hub);
        hub.SearchQuery = "object"; await hub.SearchCommand.ExecuteAsync();
        var requested = (Type: "", Id: Guid.Empty);
        hub.OpenObjectRequested += (t, value) => requested = (t, value);
        await hub.OpenSearchResultCommand.ExecuteAsync(hit);
        Assert.Equal((type, id), requested);
        await shell.OpenWorkObjectAsync(type, id);
        if (route is not null)
        {
            Assert.Equal(route, shell.SelectedSection?.Route);
            if (route == "contacts") Assert.Equal(id, hub.SelectedContact?.Id);
            if (route == "catalog") Assert.Equal(id, hub.SelectedCatalogItem?.Id);
        }
        else Assert.Contains("нет экрана", shell.SessionMessage);
    }

    [Theory]
    [InlineData("contact")]
    [InlineData("catalog_item")]
    public async System.Threading.Tasks.Task MissingTargetClearsPreviousSelectionAndReportsUnavailability(string type)
    {
        var contact = new DesktopContact(Guid.NewGuid(), 1, "Contact", "Contact", null, null, "active", "active");
        using var hub = new WorkHubViewModel(new FakeClient { Contacts = [contact] }, ["Contact.Read", "FileCatalog.Read"]);
        hub.Activate(type == "contact" ? WorkHubArea.Contacts : WorkHubArea.Catalog);
        await hub.OpenSearchObjectAsync(type, type == "contact" ? contact.Id : FakeClient.ItemId);
        await hub.OpenSearchObjectAsync(type, Guid.NewGuid());
        Assert.Null(hub.SelectedContact);
        Assert.Null(hub.SelectedCatalogItem);
        Assert.Contains("больше не доступен", hub.Message);
        hub.UpdateCapabilities([]);
        await hub.OpenSearchObjectAsync(type, type == "contact" ? contact.Id : FakeClient.ItemId);
        Assert.Contains("Недостаточно прав", hub.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task FileLocationWithoutParentCannotNavigateToAnUnrelatedObject()
    {
        var hit = CommandPaletteTests.Result("file_location", "File");
        using var hub = new WorkHubViewModel(new FakeClient
        {
            SearchHandler = (_, _) => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>>(new DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.Succeeded([hit]))
        }, ["Search.Use"]);
        hub.SearchQuery = "file"; await hub.SearchCommand.ExecuteAsync();
        var calls = 0; hub.OpenObjectRequested += (_, _) => calls++;
        await hub.OpenSearchResultCommand.ExecuteAsync(hit);
        Assert.Equal(0, calls);
        Assert.Contains("расположения", hub.Message);
    }
}
