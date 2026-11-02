using System.IO;
using Task.Desktop.Personal;
using Task.Desktop.Work;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Tests.Personal;

public sealed class PersonalCatalogBrowserTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Task-catalog-browser", Guid.NewGuid().ToString("N"));
    private readonly PersonalTaskStore _store;
    public PersonalCatalogBrowserTests() => _store = new(new(_root));
    private async System.Threading.Tasks.Task<PersonalWorkspaceViewModel> Open()
    {
        var vm = new PersonalWorkspaceViewModel(_store, new WindowsFileAccessAdapter(_ => { }));
        vm.Hub.Activate(WorkHubArea.Catalog); await vm.Hub.RefreshCommand.ExecuteAsync();
        return vm;
    }
    [Fact]
    public async System.Threading.Tasks.Task LargeCatalog_FiltersNameAndType_SortsAndResets()
    {
        for (var i = 0; i < 305; i++) _store.CreateCatalog($"Документ {i:D3}.txt", i % 5 == 0 ? "folder_reference" : "file_reference", null);
        using var vm = await Open();
        Assert.Equal(305, vm.VisibleCatalogRows.Count);
        vm.CatalogQuery = "  ДОКУМЕНТ 00  "; Assert.Equal(10, vm.VisibleCatalogRows.Count);
        vm.CatalogTypeFilter = "Папка на диске"; Assert.Equal(2, vm.VisibleCatalogRows.Count);
        vm.CatalogSort = "Название: Я–А"; Assert.Equal("Документ 005.txt", vm.VisibleCatalogRows[0].Name);
        vm.CatalogQuery = "Нет такого документа"; Assert.True(vm.ShowCatalogEmpty); Assert.Equal("Ничего не найдено", vm.CatalogEmptyTitle);
        vm.ResetCatalogFilters(); Assert.Equal(305, vm.VisibleCatalogRows.Count); Assert.Equal("Документ 000.txt", vm.VisibleCatalogRows[0].Name);
    }
    [Fact]
    public async System.Threading.Tasks.Task Hierarchy_UsesExistingParents_AndSearchCanIncludeDescendants()
    {
        var folder = _store.CreateCatalog("Проект", "virtual_folder", null);
        var child = _store.CreateCatalog("Материалы", "virtual_folder", null, folder.Id);
        _store.CreateCatalog("План.txt", "file_reference", null, child.Id);
        using var vm = await Open(); Assert.Single(vm.VisibleCatalogRows);
        vm.BrowseCatalogFolder(folder.Id); Assert.Equal(child.Id, Assert.Single(vm.VisibleCatalogRows).Id);
        vm.BrowseCatalogFolder(child.Id); Assert.Equal("Каталог / Проект / Материалы", vm.CatalogLocation);
        Assert.Equal("План.txt", Assert.Single(vm.VisibleCatalogRows).Name);
        Assert.Equal("Каталог / Проект / Материалы", vm.VisibleCatalogRows[0].ParentLocation);
        vm.GoCatalogUp(); Assert.Equal(child.Id, Assert.Single(vm.VisibleCatalogRows).Id);
        vm.BrowseCatalogFolder(null); vm.CatalogQuery = "План"; Assert.Empty(vm.VisibleCatalogRows);
        vm.CatalogEverywhere = true; Assert.Single(vm.VisibleCatalogRows);
    }
    [Fact]
    public async System.Threading.Tasks.Task PathDraftsAndSelection_SurviveFiltersRefreshAndOtherSelections()
    {
        var first = _store.CreateCatalog("Один", "file_reference", null);
        var second = _store.CreateCatalog("Два", "file_reference", null);
        using var vm = await Open();
        vm.CatalogSelection = vm.VisibleCatalogRows.Single(r => r.Id == first.Id);
        vm.CatalogPathDraft = @"C:\Drafts\one.txt"; vm.Hub.NewItemName = "Несохранённая новая запись";
        vm.CatalogQuery = "ничего"; vm.CatalogSelection = null;
        Assert.Equal(first.Id, vm.CatalogSelection!.Id); Assert.Equal(@"C:\Drafts\one.txt", vm.CatalogPathDraft);
        vm.ResetCatalogFilters(); vm.CatalogSelection = vm.VisibleCatalogRows.Single(r => r.Id == second.Id);
        vm.CatalogPathDraft = @"C:\Drafts\two.txt";
        await vm.Hub.RefreshCommand.ExecuteAsync();
        Assert.Equal(second.Id, vm.CatalogSelection!.Id); Assert.Equal(second.Id, vm.Selected!.Id);
        vm.CatalogSelection = vm.VisibleCatalogRows.Single(r => r.Id == first.Id);
        Assert.Equal(@"C:\Drafts\one.txt", vm.CatalogPathDraft); Assert.True(vm.HasCatalogPathDrafts);
        Assert.Equal("Несохранённая новая запись", vm.Hub.NewItemName);
        await vm.SaveCatalogPathCommand.ExecuteAsync();
        Assert.Equal(@"C:\Drafts\one.txt", _store.PersonalLocation(first.Id)!.RawPath);
        Assert.Null(_store.PersonalLocation(second.Id));
        Assert.Equal(first.Id, vm.CatalogSelection!.Id);
    }
    [Fact]
    public async System.Threading.Tasks.Task UnavailableFile_UsesExistingOpenFeedback_AndInvalidPathKeepsDraft()
    {
        var item = _store.CreateCatalog("Недоступный", "file_reference", null);
        _store.AddPersonalLocation(item.Id, item.Version, @"Z:\Task-Catalog-Missing-830156\missing.txt");
        using var vm = await Open(); vm.CatalogSelection = Assert.Single(vm.VisibleCatalogRows);
        await vm.Hub.OpenFileCommand.ExecuteAsync(); Assert.Contains("недоступны", vm.Hub.Message);
        vm.CatalogPathDraft = "invalid-relative-path";
        await vm.SaveCatalogPathCommand.ExecuteAsync();
        Assert.Equal("invalid-relative-path", vm.CatalogPathDraft); Assert.True(vm.HasCatalogPathDrafts);
        Assert.Equal(@"Z:\Task-Catalog-Missing-830156\missing.txt", _store.PersonalLocation(item.Id)!.RawPath);
    }
    [Fact]
    public async System.Threading.Tasks.Task LifecycleAndMove_ApplyToTheSelectedCatalogRecord()
    {
        var folder = _store.CreateCatalog("Папка", "virtual_folder", null);
        var file = _store.CreateCatalog("Файл", "file_reference", null);
        using var vm = await Open(); vm.CatalogSelection = vm.VisibleCatalogRows.Single(r => r.Id == file.Id);
        vm.Hub.MoveDestination = vm.Hub.CatalogMoveDestinations.Single(d => d.Id == folder.Id);
        await vm.Hub.MoveCatalogItemCommand.ExecuteAsync();
        Assert.Equal(folder.Id, _store.Catalog().Single(r => r.Id == file.Id).ParentId);
        Assert.Equal(file.Id, vm.Selected!.Id);
        await vm.ArchiveCommand.ExecuteAsync();
        Assert.DoesNotContain(_store.Catalog(), r => r.Id == file.Id);
        Assert.Contains(_store.WorkspaceLifecycle("archived"), r => r.ObjectId == file.Id);
    }
    public void Dispose() { _store.Dispose(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
