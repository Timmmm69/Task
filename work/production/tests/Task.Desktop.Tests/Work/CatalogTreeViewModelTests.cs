using System.IO;
using Task.Desktop.Personal;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed class CatalogTreeViewModelTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TaskCatalogTreeTests", Guid.NewGuid().ToString("N"));
    private PersonalTaskStore Store() => new(new PersonalDataPaths(_directory));
    private static WorkHubViewModel Hub(IDesktopWorkApiClient client, string[]? capabilities = null, bool personal = true)
    {
        var vm = new WorkHubViewModel(client, capabilities ?? ["FileCatalog.Read", "FileCatalog.Create", "FileCatalog.Update", "FileLocation.Update", "FileReference.Open"], personal: personal);
        vm.Activate(WorkHubArea.Catalog);
        return vm;
    }

    [Fact]
    public void EmptyCatalogHasNoSelectionAndCanCreateInRoot()
    {
        using var store = Store(); using var vm = Hub(new PersonalWorkClient(store));
        Assert.Empty(vm.CatalogRoots); Assert.Null(vm.SelectedCatalogItem); Assert.True(vm.IsCatalogEmpty);
        vm.NewItemName = "Folder"; Assert.True(vm.CreateCatalogFolderCommand.CanExecute(null));
    }

    [Fact]
    public async System.Threading.Tasks.Task BuildsMultipleLevelsAndRevealsSelectedParentChain()
    {
        using var store = Store();
        var root = store.CreateCatalog("Root", "virtual_folder", null);
        var child = store.CreateCatalog("Nested", "virtual_folder", null, root.Id);
        var file = store.CreateCatalog("File", "file_reference", null, child.Id);
        using var vm = Hub(new PersonalWorkClient(store));
        var node = Assert.Single(vm.CatalogRoots);
        Assert.Equal(root.Id, node.Id); Assert.Equal(child.Id, Assert.Single(node.Children).Id);
        Assert.Equal(file.Id, Assert.Single(node.Children[0].Children).Id);
        Assert.True(await vm.SelectCatalogItemAsync(file.Id));
        Assert.True(node.IsExpanded); Assert.True(node.Children[0].IsExpanded); Assert.True(vm.SelectedCatalogItem!.IsSelected);
    }

    [Fact]
    public async System.Threading.Tasks.Task CreatesFoldersInRootAndSelectedFolderAndFilesBesideSelectedFile()
    {
        using var store = Store(); using var vm = Hub(new PersonalWorkClient(store));
        vm.NewItemName = "Root"; await vm.CreateCatalogFolderCommand.ExecuteAsync();
        var root = vm.SelectedCatalogItem!; Assert.Null(root.ParentId);
        vm.NewItemName = "Nested"; await vm.CreateCatalogFolderCommand.ExecuteAsync();
        var child = vm.SelectedCatalogItem!; Assert.Equal(root.Id, child.ParentId);
        vm.NewItemName = "File"; await vm.CreateCatalogItemCommand.ExecuteAsync();
        var file = vm.SelectedCatalogItem!; Assert.Equal(child.Id, file.ParentId); Assert.Equal("file_reference", file.ItemType);
        vm.NewItemName = "Sibling"; vm.NewItemType = "folder_reference"; await vm.CreateCatalogItemCommand.ExecuteAsync();
        Assert.Equal(child.Id, vm.SelectedCatalogItem!.ParentId);
        Assert.Equal(2, vm.CatalogRoots[0].Children[0].Children.Count);
        Assert.True(vm.CatalogRoots[0].IsExpanded); Assert.True(vm.CatalogRoots[0].Children[0].IsExpanded);
        await vm.SelectCatalogRootCommand.ExecuteAsync(); vm.NewItemName = "Other";
        await vm.CreateCatalogFolderCommand.ExecuteAsync(); Assert.Equal(2, vm.CatalogRoots.Count);
    }

    [Theory]
    [InlineData("file_reference")]
    [InlineData("virtual_folder")]
    public async System.Threading.Tasks.Task MovesBetweenFoldersAndBackToRootUsingFreshVersions(string type)
    {
        using var store = Store();
        var a = store.CreateCatalog("A", "virtual_folder", null); var b = store.CreateCatalog("B", "virtual_folder", null);
        var item = store.CreateCatalog("Item", type, null, a.Id);
        using var vm = Hub(new PersonalWorkClient(store)); await vm.SelectCatalogItemAsync(item.Id);
        vm.MoveDestination = vm.CatalogMoveDestinations.Single(d => d.Id == b.Id);
        await vm.MoveCatalogItemCommand.ExecuteAsync();
        Assert.Equal(b.Id, vm.SelectedCatalogItem!.ParentId); Assert.Equal(2, vm.SelectedCatalogItem.Version);
        Assert.Empty(vm.CatalogRoots.Single(n => n.Id == a.Id).Children);
        Assert.Equal(item.Id, Assert.Single(vm.CatalogRoots.Single(n => n.Id == b.Id).Children).Id);
        await vm.MoveCatalogItemCommand.ExecuteAsync(new CatalogMoveRequest(item.Id, null));
        Assert.Null(vm.SelectedCatalogItem!.ParentId); Assert.Equal(3, vm.SelectedCatalogItem.Version);
        Assert.Equal(3, vm.CatalogRoots.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task RejectsSelfDescendantAndRegularItemParentsInUiAndStore()
    {
        using var store = Store(); var root = store.CreateCatalog("Root", "virtual_folder", null);
        var child = store.CreateCatalog("Child", "virtual_folder", null, root.Id);
        var file = store.CreateCatalog("File", "file_reference", null);
        using var vm = Hub(new PersonalWorkClient(store)); await vm.SelectCatalogItemAsync(root.Id);
        foreach (var parent in new[] { root.Id, child.Id, file.Id })
        {
            Assert.False(vm.CanMoveCatalogItem(root.Id, parent));
            Assert.False(await vm.MoveCatalogItemCommand.ExecuteAsync(new CatalogMoveRequest(root.Id, parent)));
            Assert.Throws<ArgumentException>(() => store.MoveCatalog(root.Id, 1, parent));
        }
        Assert.DoesNotContain(vm.CatalogMoveDestinations, d => d.Id == child.Id || d.Id == root.Id || d.Id == file.Id);
    }

    [Fact]
    public async System.Threading.Tasks.Task ConflictRefreshesVersionAndKeepsWorkingForRetry()
    {
        using var store = Store(); var a = store.CreateCatalog("A", "virtual_folder", null); var b = store.CreateCatalog("B", "virtual_folder", null);
        var file = store.CreateCatalog("File", "file_reference", null, a.Id);
        using var vm = Hub(new PersonalWorkClient(store)); await vm.SelectCatalogItemAsync(file.Id);
        store.MoveCatalog(file.Id, 1, b.Id);
        await vm.MoveCatalogItemCommand.ExecuteAsync(new CatalogMoveRequest(file.Id, null));
        Assert.Equal(WorkHubFeedbackKind.Warning, vm.FeedbackKind); Assert.Equal(b.Id, vm.SelectedCatalogItem!.ParentId); Assert.Equal(2, vm.SelectedCatalogItem.Version);
        await vm.MoveCatalogItemCommand.ExecuteAsync(new CatalogMoveRequest(file.Id, null));
        Assert.Null(vm.SelectedCatalogItem!.ParentId); Assert.Equal(3, vm.SelectedCatalogItem.Version);
    }

    [Fact]
    public async System.Threading.Tasks.Task LocationMutationAlsoRefreshesItemVersionBeforeMove()
    {
        using var store = Store(); var folder = store.CreateCatalog("Folder", "virtual_folder", null);
        var file = store.CreateCatalog("File", "file_reference", null);
        using var vm = Hub(new PersonalWorkClient(store)); await vm.SelectCatalogItemAsync(file.Id);
        vm.NewItemPath = @"C:\Work\document.docx"; await vm.AddLocationCommand.ExecuteAsync();
        Assert.Equal(2, vm.SelectedCatalogItem!.Version);
        await vm.MoveCatalogItemCommand.ExecuteAsync(new CatalogMoveRequest(file.Id, folder.Id));
        Assert.Equal(3, vm.SelectedCatalogItem!.Version); Assert.Equal(folder.Id, vm.SelectedCatalogItem.ParentId);
    }

    [Fact]
    public async System.Threading.Tasks.Task RefreshPreservesExpansionAndSelectionAndFallsBackToParent()
    {
        using var store = Store(); var folder = store.CreateCatalog("Folder", "virtual_folder", null);
        var file = store.CreateCatalog("File", "file_reference", null, folder.Id);
        using var vm = Hub(new PersonalWorkClient(store)); await vm.SelectCatalogItemAsync(file.Id);
        await vm.RefreshCommand.ExecuteAsync(); Assert.Equal(file.Id, vm.SelectedCatalogItem!.Id); Assert.True(vm.CatalogRoots[0].IsExpanded);
        store.ChangeWorkspaceLifecycle(file.Id, 1, "trash"); await vm.RefreshCommand.ExecuteAsync();
        Assert.Equal(folder.Id, vm.SelectedCatalogItem!.Id); Assert.Empty(vm.SelectedCatalogItem.Children);
    }

    [Fact]
    public async System.Threading.Tasks.Task MissingPermissionsAndUnavailableServerDisableWritesWithoutClearingTree()
    {
        using var store = Store(); var folder = store.CreateCatalog("Folder", "virtual_folder", null);
        var file = store.CreateCatalog("File", "file_reference", null);
        using var vm = Hub(new PersonalWorkClient(store), ["FileCatalog.Read"], false);
        await vm.SelectCatalogItemAsync(file.Id); vm.NewItemName = "Draft";
        Assert.False(vm.CreateCatalogItemCommand.CanExecute(null)); Assert.False(vm.CreateCatalogFolderCommand.CanExecute(null)); Assert.False(vm.CanMoveCatalogItem(file.Id, folder.Id));
        vm.UpdateCapabilities(["FileCatalog.Read", "FileCatalog.Create", "FileCatalog.Update"]);
        Assert.True(vm.CanMoveCatalogItem(file.Id, folder.Id));
        vm.UpdateConnectivity(false);
        Assert.False(vm.CreateCatalogItemCommand.CanExecute(null)); Assert.False(vm.CreateCatalogFolderCommand.CanExecute(null)); Assert.False(vm.CanMoveCatalogItem(file.Id, folder.Id));
        Assert.Equal(2, vm.CatalogRoots.Count); Assert.Equal(file.Id, vm.SelectedCatalogItem!.Id);
        vm.UpdateConnectivity(true); await vm.RefreshCommand.ExecuteAsync();
        Assert.True(vm.CreateCatalogFolderCommand.CanExecute(null)); Assert.True(vm.CanMoveCatalogItem(file.Id, folder.Id));
    }

    [Fact]
    public async System.Threading.Tasks.Task PersonalHierarchyAndMovedRootPersistAcrossReopen()
    {
        Guid rootId, nestedId, fileId;
        using (var store = Store())
        {
            var root = store.CreateCatalog("Root", "virtual_folder", null); rootId = root.Id;
            var nested = store.CreateCatalog("Nested", "virtual_folder", null, root.Id); nestedId = nested.Id;
            var file = store.CreateCatalog("File", "file_reference", null, nested.Id); fileId = file.Id;
            store.MoveCatalog(file.Id, 1, root.Id); store.MoveCatalog(nested.Id, 1, null);
        }
        using var reopened = Store(); using var vm = Hub(new PersonalWorkClient(reopened));
        Assert.Equal(2, vm.CatalogRoots.Count); Assert.Null(vm.Catalog.Single(i => i.Id == nestedId).ParentId);
        Assert.Equal(rootId, vm.Catalog.Single(i => i.Id == fileId).ParentId);
        Assert.True(await vm.SelectCatalogItemAsync(fileId)); Assert.Equal(2, vm.SelectedCatalogItem!.Version);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
