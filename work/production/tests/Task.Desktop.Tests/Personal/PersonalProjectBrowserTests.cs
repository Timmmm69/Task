using System.IO;
using Task.Desktop.Personal;
using Task.Desktop.Projects;
using Task.Desktop.TaskApi;
using Task.Domain;

namespace Task.Desktop.Tests.Personal;

public sealed class PersonalProjectBrowserTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Task-project-browser", Guid.NewGuid().ToString("N"));

    [Fact]
    public void FiltersCombineWithoutChangingCanonicalProjectSnapshot()
    {
        using var store = new PersonalTaskStore(new(_root));
        store.SaveProject(new(Guid.Empty, 0, "Офис Б", null, DesktopProjectStatus.Active, null, new(2026, 12, 1)));
        store.SaveProject(new(Guid.Empty, 0, "Офис А", null, DesktopProjectStatus.Active, null, null));
        store.SaveProject(new(Guid.Empty, 0, "Офис В", null, DesktopProjectStatus.Paused, null, null));
        var archived = store.SaveProject(new(Guid.Empty, 0, "Архив", null, DesktopProjectStatus.Completed, null, null, DateTimeOffset.UtcNow));
        store.SaveProject(archived with { Lifecycle = "archived" });
        using var vm = new PersonalPlanningViewModel(store);
        vm.ProjectSearch = "ОФИС"; vm.ProjectStatusFilter = 2; vm.ProjectSort = 3;
        Assert.Equal(3, vm.Projects.Count);
        Assert.Equal(new[] { "Офис Б", "Офис А" }, vm.VisibleProjects.Select(p => p.Name));
        vm.ProjectSort = 1;
        Assert.Equal(new[] { "Офис Б", "Офис А" }, vm.VisibleProjects.Select(p => p.Name));
        vm.ProjectSearch = "Нет совпадений";
        Assert.True(vm.ShowProjectEmpty); Assert.Equal("Ничего не найдено", vm.ProjectEmptyTitle);
        vm.ResetProjectFilters();
        Assert.Equal(3, vm.VisibleProjects.Count); Assert.False(vm.HasProjectFilters);
    }

    [Fact]
    public async System.Threading.Tasks.Task EditorRetainsSourceAndReminderDraftThroughFilteringAndRefresh()
    {
        using var store = new PersonalTaskStore(new(_root));
        var first = store.SaveProject(new(Guid.Empty, 0, "Первый", null, DesktopProjectStatus.Active, null, null));
        var second = store.SaveProject(new(Guid.Empty, 0, "Второй", null, DesktopProjectStatus.Active, null, null));
        using var vm = new PersonalPlanningViewModel(store);
        vm.SelectedProject = first;
        vm.AbsoluteTime = "03.11.2026 12:00"; vm.OffsetMinutes = "45";
        await vm.EditProjectCommand.ExecuteAsync();
        vm.ProjectName = "Мой черновик"; vm.ProjectDescription = "Подробное описание";
        vm.ProjectSearch = "Нет совпадений"; vm.ProjectStatusFilter = 3; vm.Refresh(); vm.SelectedProject = second;
        Assert.True(vm.IsProjectEditing); Assert.False(vm.CanSelectProject);
        Assert.Equal(first.Id, vm.SelectedProject!.Id); Assert.Equal("Мой черновик", vm.ProjectName);
        Assert.Equal("Подробное описание", vm.ProjectDescription);
        Assert.Equal("03.11.2026 12:00", vm.AbsoluteTime); Assert.Equal("45", vm.OffsetMinutes);
        vm.ResetProjectFilters(); await vm.SaveProjectCommand.ExecuteAsync();
        Assert.False(vm.IsProjectEditing);
        Assert.Equal("Мой черновик", store.Projects().Single(p => p.Id == first.Id).Name);
        Assert.Equal("Второй", store.Projects().Single(p => p.Id == second.Id).Name);
    }

    [Fact]
    public async System.Threading.Tasks.Task RelatedTasksUseActualProjectLinksAndOpenTheRequestedTask()
    {
        using var store = new PersonalTaskStore(new(_root));
        var project = store.SaveProject(new(Guid.Empty, 0, "Проект", null, DesktopProjectStatus.Active, null, null));
        var linked = store.Create(new DesktopCreateTaskCommand("Связанная", DesktopTaskPriority.Normal, card: new TaskCardContent { ProjectId = project.Id }));
        store.Transition(new(linked.Id, linked.Version, DesktopTaskStatus.Completed));
        store.Create(new DesktopCreateTaskCommand("Отдельная", DesktopTaskPriority.High));
        using var vm = new PersonalPlanningViewModel(store);
        Assert.Single(vm.RelatedProjectTasks); Assert.Contains("выполнено: 1", vm.ProjectTaskSummary);
        (string?, Guid?) opened = default;
        vm.OpenObjectRequested += (kind, id) => opened = (kind, id);
        await vm.OpenProjectTaskCommand.ExecuteAsync(vm.RelatedProjectTasks[0]);
        Assert.Equal(("task", (Guid?)linked.Id), opened);
        vm.ProjectSearch = "Нет совпадений";
        Assert.Empty(vm.RelatedProjectTasks); Assert.False(vm.HasProjectSelection);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
