using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;
using Task.Desktop.Tests.TaskScreen;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.Tests.Projects;

public sealed partial class ProjectsViewModelTests
{
    [Theory]
    [InlineData(20, 5, false)]
    [InlineData(500, 10, false)]
    [InlineData(5000, 10, false)]
    [InlineData(5000, 10, true)]
    [InlineData(500, 0, false)]
    public async AsyncTask Baseline_CatalogWalkBlocksDetailAndPublishesOnlyAtEnd(int total, int matches, bool late)
    {
        var tasks = new TasksViewModelTests.FakeTasksApiClient();
        var pages = (total + 99) / 100;
        var entered = Enumerable.Range(0, pages).Select(_ => new System.Threading.Tasks.TaskCompletionSource()).ToArray();
        var release = Enumerable.Range(0, pages).Select(_ => new System.Threading.Tasks.TaskCompletionSource()).ToArray();
        var viewed = 0;
        for (var p = 0; p < pages; p++)
        {
            var index = p;
            tasks.EnqueuePage(async (_, ct) =>
            {
                entered[index].SetResult();
                await release[index].Task.WaitAsync(ct);
                var items = Enumerable.Range(index * 100, Math.Min(100, total - index * 100))
                    .Select(i => Task(Guid.NewGuid(), (late ? i >= total - matches : i < matches) ? ProjectId : Guid.Empty, "Задача")).ToArray();
                viewed += items.Length;
                return new DesktopTasksApiResult<DesktopTaskPage>.Succeeded(new(items, index + 1 < pages ? $"p{index + 1}" : null, total));
            });
        }
        using var vm = new ProjectsViewModel(new FakeProjectsClient { Projects = SuccessPage(Project()),
            Project = new Desktop.Projects.DesktopProjectResult<Desktop.Projects.DesktopProjectDto>.Succeeded(Project()) }, User, Capabilities, tasks);
        vm.Activate();
        for (var p = 0; p < pages; p++)
        {
            await entered[p].Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("Проект Альфа", vm.SelectedItem?.Name);
            Assert.True(vm.IsDetailLoading);
            Assert.Empty(vm.RelatedTasks);
            release[p].SetResult();
        }
        await WaitUntil(() => !vm.IsDetailLoading);
        Assert.Equal(pages, tasks.PageCallCount);
        Assert.Equal(total, viewed);
        Assert.Equal(matches, vm.RelatedTasks.Count);
    }

    [Theory]
    [InlineData("forbidden")]
    [InlineData("switch")]
    [InlineData("cancel")]
    [InlineData("error")]
    public async AsyncTask Baseline_AccessSwitchCancellationAndPageFailure(string scenario)
    {
        var tasks = new TasksViewModelTests.FakeTasksApiClient();
        var gate = new System.Threading.Tasks.TaskCompletionSource<DesktopTasksApiResult<DesktopTaskPage>>();
        CancellationToken token = default;
        tasks.EnqueuePage(new DesktopTasksApiResult<DesktopTaskPage>.Succeeded(new([Task(Guid.NewGuid(), ProjectId, "Первая")], "next", null)));
        tasks.EnqueuePage((_, ct) => { token = ct; return gate.Task; }); // Deliberately ignores cancellation.
        using var vm = new ProjectsViewModel(new FakeProjectsClient { Projects = SuccessPage(Project()),
            Project = new Desktop.Projects.DesktopProjectResult<Desktop.Projects.DesktopProjectDto>.Succeeded(Project()) }, User,
            scenario == "forbidden" ? ["Project.Read"] : Capabilities, tasks);
        vm.Activate();
        if (scenario == "forbidden") { Assert.Equal(0, tasks.PageCallCount); return; }
        Assert.Equal(2, tasks.PageCallCount);
        Assert.Empty(vm.RelatedTasks);
        if (scenario == "switch") { vm.SelectedItem = null; Assert.False(token.IsCancellationRequested); }
        if (scenario == "cancel") { vm.Deactivate(); Assert.True(token.IsCancellationRequested); }
        gate.SetResult(new DesktopTasksApiResult<DesktopTaskPage>.ServerUnavailable());
        if (scenario == "error") { await WaitUntil(() => !vm.IsDetailLoading); Assert.Empty(vm.RelatedTasks); Assert.Null(vm.Message); }
    }
}
