using System.IO;
using Task.Desktop.Personal;

namespace Task.Desktop.Tests.Personal;

public sealed class PersonalLifecycleBrowserTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Task-lifecycle-browser", Guid.NewGuid().ToString("N"));
    [Fact]
    public void MissingLifecycleDate_IsNotReplacedWithLastModifiedDate()
    {
        var row = new PersonalLifecycleRow(new(Guid.NewGuid(), "catalog_item", "Материалы", 1, "archived", DateTimeOffset.UtcNow, null, null, null, "personal"));
        Assert.Null(row.Date);
        Assert.Equal("Дата не указана", row.DateText);
    }
    [Fact]
    public async System.Threading.Tasks.Task FiltersSortRefreshAndRestore_UseTheSameSelectedObject()
    {
        using var store = new PersonalTaskStore(new(_root));
        for (var i = 0; i < 110; i++)
        {
            var item = store.CreateContact("Анна", null, $"Анна {i:D3}");
            store.ChangeWorkspaceLifecycle(item.Id, item.Version, "archive");
        }
        var catalog = store.CreateCatalog("Январский отчёт", "file_reference", null);
        store.ChangeWorkspaceLifecycle(catalog.Id, catalog.Version, "archive");
        using var model = new PersonalWorkspaceViewModel(store);
        model.Area = model.Areas.Single(a => a.Title == "Архив");
        await model.Hub.RefreshCommand.ExecuteAsync();
        Assert.Equal(111, model.VisibleLifecycleRows.Count);
        model.LifecycleQuery = "АННА 00"; model.LifecycleType = "Контакт";
        Assert.Equal(10, model.VisibleLifecycleRows.Count);
        model.LifecycleSelection = model.VisibleLifecycleRows[0];
        var id = model.LifecycleSelection.Id;
        model.LifecycleSort = "Название: Я–А";
        Assert.Equal(id, model.LifecycleSelection?.Id);
        await model.RefreshCommand.ExecuteAsync();
        Assert.Equal(id, model.Selected?.Id);
        Assert.Equal(id, model.LifecycleSelection?.Id);
        model.LifecycleQuery = "не найдено";
        Assert.True(model.ShowLifecycleNoResults);
        Assert.False(model.ShowLifecycleEmpty);
        Assert.Null(model.Selected);
        model.ResetLifecycleFilters();
        Assert.Equal(111, model.VisibleLifecycleRows.Count);
        Assert.False(model.HasLifecycleFilters);
        model.LifecycleSelection = model.VisibleLifecycleRows.Single(r => r.Id == id);
        await model.RestoreCommand.ExecuteAsync();
        Assert.DoesNotContain(model.VisibleLifecycleRows, r => r.Id == id);
        Assert.Equal("active", store.WorkspaceObjects().Single(r => r.Id == id).Lifecycle);
    }
    [Fact]
    public async System.Threading.Tasks.Task TrashRestoresPreviousArchiveState_AndPurgeRemovesOnlySelectedRecord()
    {
        using var store = new PersonalTaskStore(new(_root));
        var item = store.CreateCatalog("Документы", "virtual_folder", null);
        store.ChangeWorkspaceLifecycle(item.Id, item.Version, "archive");
        var archived = store.WorkspaceObjects().Single();
        store.ChangeWorkspaceLifecycle(archived.Id, archived.Version, "trash");
        using var model = new PersonalWorkspaceViewModel(store);
        model.Area = model.Areas.Single(a => a.Title == "Корзина");
        await model.Hub.RefreshCommand.ExecuteAsync();
        model.LifecycleSelection = Assert.Single(model.VisibleLifecycleRows);
        await model.RestoreCommand.ExecuteAsync();
        Assert.True(model.ShowLifecycleEmpty);
        Assert.Equal("archived", store.WorkspaceObjects().Single().Lifecycle);
        archived = store.WorkspaceObjects().Single();
        store.ChangeWorkspaceLifecycle(archived.Id, archived.Version, "trash");
        model.Refresh(); model.LifecycleSelection = Assert.Single(model.VisibleLifecycleRows);
        await model.PurgeCommand.ExecuteAsync();
        Assert.Empty(store.WorkspaceObjects());
        Assert.True(model.ShowLifecycleEmpty);
        Assert.Null(model.LifecycleSelection);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
