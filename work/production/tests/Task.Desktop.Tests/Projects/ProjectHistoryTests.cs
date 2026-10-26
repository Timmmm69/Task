using System.IO;
using Task.Desktop.Projects;
using Task.Desktop.ViewModels;
using AsyncTask = System.Threading.Tasks.Task;
using Result = Task.Desktop.Projects.DesktopProjectResult<Task.Desktop.Projects.DesktopProjectHistoryPage>;

namespace Task.Desktop.Tests.Projects;

public sealed partial class ProjectsViewModelTests
{
    private static readonly string[] HistoryCaps = ["Project.Read", "History.Read"];
    private static DesktopProjectHistoryEvent HistoryEvent() => new(Guid.NewGuid(), "project.patch", 2,
        User, DateTimeOffset.Parse("2026-10-19T12:30:00Z"), ["name", "description"]);
    private static Result HistoryPage(params DesktopProjectHistoryEvent[] rows) => new Result.Succeeded(new(rows));

    [Fact]
    public async AsyncTask History_LazySelectedPagingDeduplicatesAndRefreshes()
    {
        var client = SectionClient();
        var rows = Enumerable.Range(0, 50).Select(_ => HistoryEvent()).ToArray();
        client.HistoryHandler = (_, page, _) => AsyncTask.FromResult(page == 1 ? HistoryPage(rows) : HistoryPage(rows[0], HistoryEvent()));
        using var vm = new ProjectsViewModel(client, User, HistoryCaps);
        vm.Activate(); await vm.CurrentDetailsOperation;
        Assert.Empty(client.HistoryCalls);
        await vm.OpenHistorySectionAsync();
        Assert.Equal(50, vm.History.Count); Assert.Equal(ProjectSectionState.Loaded, vm.HistorySection.State);
        Assert.True(vm.MoreHistoryCommand.CanExecute(null));
        await vm.MoreHistoryCommand.ExecuteAsync();
        Assert.Equal(51, vm.History.Count); Assert.False(vm.MoreHistoryCommand.CanExecute(null));
        Assert.Equal(new[] { (ProjectId, 1), (ProjectId, 2) }, client.HistoryCalls);
        Assert.All(vm.History, e => { Assert.Contains("Версия 2", e.VersionText); Assert.Contains("name, description", e.FieldsText); Assert.Contains("Автор", e.AuthorText); });
        await vm.RefreshHistoryCommand.ExecuteAsync(); Assert.Equal(50, vm.History.Count);
        vm.CloseHistorySection(); Assert.Empty(vm.History);
        await vm.OpenHistorySectionAsync(); Assert.Equal(50, vm.History.Count);
    }

    [Theory]
    [InlineData("Project.Read")]
    [InlineData("History.Read")]
    public async AsyncTask History_BothCapabilitiesRequired(string capability)
    {
        var client = SectionClient(); using var vm = new ProjectsViewModel(client, User, [capability]);
        vm.Activate(); await vm.CurrentDetailsOperation; await vm.OpenHistorySectionAsync();
        Assert.Empty(client.HistoryCalls); Assert.False(vm.CanReadHistory);
        Assert.False(await vm.RefreshHistoryCommand.ExecuteAsync()); Assert.Empty(vm.History);
    }

    [Theory]
    [InlineData("switch")]
    [InlineData("return")]
    [InlineData("organization")]
    [InlineData("permission")]
    [InlineData("session")]
    [InlineData("deactivate")]
    [InlineData("dispose")]
    [InlineData("collapse")]
    [InlineData("close")]
    [InlineData("offline")]
    public async AsyncTask History_RejectsLateResponsesAndClearsContext(string transition)
    {
        var client = SectionClient();
        var gate = new System.Threading.Tasks.TaskCompletionSource<Result>(); CancellationToken token = default;
        client.HistoryHandler = (_, _, ct) => { token = ct; return gate.Task; };
        using var vm = new ProjectsViewModel(client, User, HistoryCaps); vm.Activate(); await vm.CurrentDetailsOperation;
        var old = vm.OpenHistorySectionAsync();
        client.HistoryHandler = (_, _, _) => AsyncTask.FromResult(HistoryPage());
        var original = vm.SelectedItem;
        switch (transition)
        {
            case "switch": vm.SelectedItem = new ProjectItemViewModel(Project() with { Id = Guid.NewGuid() }); break;
            case "organization": vm.SelectedItem = new ProjectItemViewModel(Project() with { OrganizationId = Guid.NewGuid() }); break;
            case "return": vm.SelectedItem = null; vm.SelectedItem = original; break;
            case "permission": vm.UpdateCapabilities(["Project.Read"]); break;
            case "session": vm.UpdateSessionState(false); break;
            case "deactivate": vm.Deactivate(); break;
            case "dispose": vm.Dispose(); break;
            case "collapse": vm.CloseHistorySection(); break;
            case "close": vm.SelectedItem = null; break;
            case "offline": vm.UpdateConnectivity(false); break;
        }
        Assert.True(token.IsCancellationRequested);
        gate.SetResult(HistoryPage(HistoryEvent())); await old;
        Assert.Empty(vm.History); Assert.NotEqual(ProjectSectionState.Loading, vm.HistorySection.State);
    }

    [Theory]
    [InlineData("empty", ProjectSectionState.Empty)]
    [InlineData("forbidden", ProjectSectionState.Forbidden)]
    [InlineData("notfound", ProjectSectionState.Forbidden)]
    [InlineData("unavailable", ProjectSectionState.ServerUnavailable)]
    [InlineData("malformed", ProjectSectionState.Error)]
    [InlineData("exception", ProjectSectionState.Error)]
    public async AsyncTask History_SafeIndependentStatesAndRecovery(string mode, ProjectSectionState state)
    {
        var client = SectionClient();
        client.HistoryHandler = (_, _, _) => mode == "exception" ? AsyncTask.FromException<Result>(new IOException("secret path"))
            : AsyncTask.FromResult<Result>(mode switch
            {
                "empty" => HistoryPage(),
                "forbidden" => new Result.Forbidden(),
                "notfound" => new Result.NotFound(),
                "unavailable" => new Result.ServerUnavailable(),
                _ => new Result.MalformedResponse()
            });
        using var vm = new ProjectsViewModel(client, User, HistoryCaps); vm.Activate(); await vm.CurrentDetailsOperation;
        await vm.OpenHistorySectionAsync();
        Assert.Equal(state, vm.HistorySection.State); Assert.Empty(vm.History);
        Assert.DoesNotContain("secret", vm.HistorySection.StatusText);
        Assert.Equal(ProjectSectionState.Loaded, vm.OverviewSection.State); Assert.NotNull(vm.SelectedItem);
        client.HistoryHandler = (_, _, _) => AsyncTask.FromResult(HistoryPage(HistoryEvent()));
        await vm.RefreshHistoryCommand.ExecuteAsync(); Assert.Single(vm.History);
        vm.UpdateConnectivity(false); Assert.Empty(vm.History); Assert.False(vm.RefreshHistoryCommand.CanExecute(null));
        vm.UpdateConnectivity(true); await vm.RefreshHistoryCommand.ExecuteAsync(); Assert.Single(vm.History);
        vm.UpdateSessionState(false); Assert.Empty(vm.History); Assert.False(vm.CanReadHistory);
    }
    [Fact]
    public async AsyncTask History_NamesRequireUserReadAndForbiddenDirectoryScrubsEarlierPages()
    {
        var client = SectionClient(); var employee = Employee(User);
        var directory = Directory(employee);
        client.HistoryHandler = (_, page, _) => AsyncTask.FromResult(HistoryPage(
            Enumerable.Range(0, page == 1 ? 50 : 1).Select(_ => HistoryEvent()).ToArray()));
        using var vm = new ProjectsViewModel(client, User, HistoryCaps, directory: directory);
        vm.Activate(); await vm.CurrentDetailsOperation; await vm.OpenHistorySectionAsync();
        Assert.Equal(0, directory.GetCalls); Assert.All(vm.History, e => Assert.Contains("недоступен", e.AuthorText));
        vm.UpdateCapabilities(HistoryCaps.Append("User.Read")); await vm.RefreshHistoryCommand.ExecuteAsync();
        Assert.All(vm.History, e => Assert.Equal(employee.DisplayName, e.AuthorText));
        directory.Get = (_, _) => AsyncTask.FromResult<Task.Desktop.Work.DesktopWorkResult<Task.Desktop.Administration.DesktopAdminUser>>(
            new Task.Desktop.Work.DesktopWorkResult<Task.Desktop.Administration.DesktopAdminUser>.Forbidden());
        await vm.MoreHistoryCommand.ExecuteAsync();
        Assert.All(vm.History, e => Assert.Contains("недоступен", e.AuthorText));
        vm.UpdateCapabilities(HistoryCaps); Assert.Empty(vm.History);
    }

    [Fact]
    public async AsyncTask History_FailedNextPageRetriesSamePageWithoutLosingConfirmedEvents()
    {
        var client = SectionClient(); var failed = true;
        client.HistoryHandler = (_, page, _) => AsyncTask.FromResult(page == 1
            ? HistoryPage(Enumerable.Range(0, 50).Select(_ => HistoryEvent()).ToArray())
            : failed ? new Result.ServerUnavailable() : HistoryPage(HistoryEvent()));
        using var vm = new ProjectsViewModel(client, User, HistoryCaps); vm.Activate(); await vm.CurrentDetailsOperation;
        await vm.OpenHistorySectionAsync(); await vm.MoreHistoryCommand.ExecuteAsync();
        Assert.Equal(50, vm.History.Count); Assert.Equal(ProjectSectionState.ServerUnavailable, vm.HistorySection.State);
        failed = false; await vm.MoreHistoryCommand.ExecuteAsync(); Assert.Equal(51, vm.History.Count);
        Assert.Equal(new[] { 1, 2, 2 }, client.HistoryCalls.Select(c => c.Page));
        int notifications = 0; vm.RefreshHistoryCommand.CanExecuteChanged += (_, _) => notifications++;
        vm.UpdateConnectivity(false); var before = notifications;
        vm.UpdateConnectivity(true); Assert.True(notifications > before); Assert.True(vm.RefreshHistoryCommand.CanExecute(null));
    }

}
