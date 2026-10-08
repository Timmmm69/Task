using System.IO;
using Task.Desktop.Modes;
using Task.Desktop.Personal;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Tests.Personal;

public sealed class PersonalShellViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Task-personal-shell", Guid.NewGuid().ToString("N"));
    [Fact]
    public async System.Threading.Tasks.Task Palette_UsesExistingPersonalSearchAndNavigation()
    {
        using var model = new PersonalApplicationModel(_root);
        using var shell = new PersonalShellViewModel(model);
        shell.Palette.Open(); shell.Palette.Query = "Каталог файлов"; shell.Palette.ExecuteSelected();
        Assert.Equal("catalog", shell.SelectedSection.Route);
        shell.Palette.Open(); shell.Palette.Query = "unmatched search";
        await System.Threading.Tasks.Task.Delay(300);
        Assert.Empty(model.Workspace!.Hub.SearchResults);
        Assert.Contains("Ничего не найдено", shell.Palette.StatusText);
        shell.Palette.Close();
    }
    [Fact]
    public void WorkspaceRoutes_ActivateExistingLocalAreas()
    {
        using var model = new PersonalApplicationModel(_root);
        using var shell = new PersonalShellViewModel(model);
        foreach (var area in model.Workspace!.Areas)
        {
            shell.SelectedSection = shell.Sections.Single(s => s.Title == area.Title);
            Assert.True(shell.IsWorkspace);
            Assert.False(shell.IsTasks);
            Assert.Equal(area.Area, model.Workspace.Hub.Area);
        }
        Assert.Equal(12, shell.Sections.Count);
        Assert.DoesNotContain(shell.Sections, s => s.Route == "administration");
    }
    [Fact]
    public async global::System.Threading.Tasks.Task TaskDraft_BlocksNavigationAndKeepsValuesUntilExplicitCancel()
    {
        using var model = new PersonalApplicationModel(_root);
        using var shell = new PersonalShellViewModel(model);
        shell.SelectedSection = shell.Sections.Single(s => s.Route == "tasks");
        await model.Tasks!.NewCommand.ExecuteAsync();
        model.Tasks.Editor!.Title = "Не потерять черновик";
        shell.SelectedSection = shell.Sections.Single(s => s.Route == "settings");
        Assert.Equal("tasks", shell.SelectedSection.Route);
        Assert.Equal("Не потерять черновик", model.Tasks.Editor.Title);
        Assert.False(shell.CanNavigate);
        await model.Tasks.CancelCommand.ExecuteAsync();
        Assert.True(shell.CanNavigate);
        shell.SelectedSection = shell.Sections.Single(s => s.Route == "settings");
        Assert.Equal(WorkHubArea.Settings, model.Workspace!.Hub.Area);
    }
    [Fact]
    public async global::System.Threading.Tasks.Task CaptureDraft_IsPreservedAndTaskSectionsFilterIndependently()
    {
        using var model = new PersonalApplicationModel(_root);
        using var shell = new PersonalShellViewModel(model);
        shell.SelectedSection = shell.Sections.Single(s => s.Route == "inbox");
        model.Tasks!.CaptureText = "Входящая задача";
        shell.SelectedSection = shell.Sections.Single(s => s.Route == "projects");
        Assert.Equal("inbox", shell.SelectedSection.Route);
        await model.Tasks.CaptureCommand.ExecuteAsync();
        Assert.True(shell.CanNavigate);
        Assert.Single(model.Tasks.Items);
        shell.SelectedSection = shell.Sections.Single(s => s.Route == "today");
        Assert.Empty(model.Tasks.Items);
        Assert.True(model.Tasks.ShowEmpty);
        Assert.Equal("На сегодня задач нет", model.Tasks.EmptyTitle);
        shell.SelectedSection = shell.Sections.Single(s => s.Route == "tasks");
        Assert.Single(model.Tasks.Items);
    }
    [Fact]
    public async global::System.Threading.Tasks.Task PersonalContactSearch_UsesRussianFilterWithoutChangingCorporateContract()
    {
        using (var store = new PersonalTaskStore(new(_root))) store.CreateContact("Анна", "Иванова", "Анна Иванова");
        using var model = new PersonalApplicationModel(_root);
        using var shell = new PersonalShellViewModel(model);
        shell.SelectedSection = shell.Sections.Single(s => s.Route == "search");
        var hub = model.Workspace!.Hub;
        hub.SearchQuery = "Анна";
        await hub.SearchCommand.ExecuteAsync();
        hub.SearchTypeFilter = "Контакты";
        Assert.Single(hub.SearchGroups);
        Assert.Equal("Контакты", hub.SearchGroups[0].Title);
        Assert.Single(hub.OverlaySearchHits);
        Assert.DoesNotContain("CRM", hub.SearchTypeFilters);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    [Fact]
    public async global::System.Threading.Tasks.Task SearchTask_OpensTaskSectionAndSelectsTheActualObject()
    {
        Guid id;
        using (var store = new PersonalTaskStore(new(_root))) id = store.Create(new("Найти конкретную задачу", Task.Desktop.TaskApi.DesktopTaskPriority.Normal)).Id;
        using var model = new PersonalApplicationModel(_root);
        using var shell = new PersonalShellViewModel(model);
        await model.Tasks!.RefreshAsync();
        shell.SelectedSection = shell.Sections.Single(s => s.Route == "search");
        var hub = model.Workspace!.Hub;
        hub.SearchQuery = "конкретную";
        await hub.SearchCommand.ExecuteAsync();
        await hub.OpenSearchResultCommand.ExecuteAsync(Assert.Single(hub.OverlaySearchHits));
        Assert.Equal("tasks", shell.SelectedSection.Route);
        Assert.Equal(id, model.Tasks.Selected?.Source.Id);
    }
    [Fact]
    public async global::System.Threading.Tasks.Task SearchNewTask_WaitsForFreshListBeforeSelectingHit()
    {
        using var model = new PersonalApplicationModel(_root);
        using var shell = new PersonalShellViewModel(model);
        await model.Tasks!.RefreshAsync();
        var id = model.Store.Create(new("Новая задача для поиска", Task.Desktop.TaskApi.DesktopTaskPriority.Normal)).Id;
        shell.SelectedSection = shell.Sections.Single(s => s.Route == "search");
        var hub = model.Workspace!.Hub;
        hub.SearchQuery = "для поиска";
        await hub.SearchCommand.ExecuteAsync();
        await hub.OpenSearchResultCommand.ExecuteAsync(Assert.Single(hub.OverlaySearchHits));
        await model.Tasks.RefreshAsync();
        Assert.Equal("tasks", shell.SelectedSection.Route);
        Assert.Equal(id, model.Tasks.Selected?.Source.Id);
    }
}
