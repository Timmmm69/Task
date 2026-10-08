using Task.Desktop.ViewModels;
using Task.Desktop.Work;
using FakeClient = Task.Desktop.Tests.Work.WorkHubViewModelTests.FakeClient;

namespace Task.Desktop.Tests;

public sealed class CommandPaletteTests
{
    [Fact]
    public async System.Threading.Tasks.Task WriteCommands_OpenExistingEditors_AndDisappearOffline()
    {
        var client = new TaskScreen.TasksViewModelTests.FakeTasksApiClient();
        using var tasks = new TasksViewModel(client, ["Task.Read", "Task.Create"]);
        using var inbox = new InboxViewModel(client, ["Task.Read", "Task.Create"]);
        using var hub = new WorkHubViewModel(new FakeClient(), ["Task.Read", "Task.Create", "Search.Use"]);
        using var shell = new MainWindowViewModel(new Uri("https://task.test"), null, tasks: tasks, inbox: inbox, workHub: hub);
        var captureFocus = 0; shell.InboxCaptureRequested += () => captureFocus++;
        shell.Palette.Open(); shell.Palette.Query = "Быстрое добавление";
        shell.Palette.ExecuteSelected();
        Assert.Equal("inbox", shell.SelectedSection?.Route); Assert.Equal(1, captureFocus);
        shell.Palette.Open(); shell.Palette.Query = "Новая задача";
        shell.Palette.ExecuteSelected();
        await Eventually(() => tasks.Editor is not null);
        Assert.Equal("tasks", shell.SelectedSection?.Route);
        shell.Palette.Open(); hub.UpdateConnectivity(false);
        Assert.DoesNotContain(shell.Palette.Items, i => i.Id is "capture" or "new-task");
        Assert.Contains(shell.Palette.Items, i => i.Id == "tasks");
    }

    [Fact]
    public void Dispose_DetachesEveryObservedSource_AndClearsSelection()
    {
        var source = new TrackingSource();
        var palette = new CommandPaletteViewModel(null, () => [], source);
        Assert.Equal(1, source.Subscribers);
        palette.Open(); palette.Dispose(); palette.Dispose();
        Assert.Equal(0, source.Subscribers); Assert.Empty(palette.Items); Assert.Null(palette.Selected);
    }

    private sealed class TrackingSource : System.ComponentModel.INotifyPropertyChanged
    {
        public int Subscribers { get; private set; }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged
        { add => Subscribers++; remove => Subscribers--; }
    }
    [Fact]
    public void OpenRepeatedly_FocusesWithoutResettingQuery_CloseIsIdempotent()
    {
        var client = new FakeClient();
        using var hub = new WorkHubViewModel(client, ["Search.Use"]);
        using var palette = new CommandPaletteViewModel(hub, () => []);
        var focus = 0; var closed = 0;
        palette.FocusRequested += () => focus++;
        palette.Closed += () => closed++;
        palette.Open(); palette.Query = "ab"; palette.Open();
        Assert.True(palette.IsOpen);
        Assert.Equal(2, focus);
        Assert.Equal("ab", palette.Query);
        Assert.Equal(0, client.SearchCallCount);
        palette.Close(); palette.Close();
        Assert.False(palette.IsOpen);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void KeyboardSelection_ExecutesExistingCommand_AndRechecksAvailability()
    {
        var executed = ""; var allowed = true;
        PaletteCommand[] commands = [new("a", "Задачи", () => executed = "a", () => true),
            new("b", "Новая задача", () => executed = "b", () => allowed)];
        using var palette = new CommandPaletteViewModel(null, () => commands);
        palette.Open(); palette.MoveSelection(1);
        Assert.Equal("b", palette.Selected?.Id);
        Assert.Contains("2 из 2", palette.SelectionText);
        allowed = false; palette.ExecuteSelected();
        Assert.Equal("", executed);
        Assert.Single(palette.Items);
        palette.ExecuteSelected();
        Assert.Equal("a", executed);
        Assert.False(palette.IsOpen);
    }

    [Fact]
    public void ExactAndPrefixCommands_PrecedeSubstringMatches()
    {
        PaletteCommand[] commands = [new("contains", "Новая задача", () => { }, () => true),
            new("prefix", "Задача новая", () => { }, () => true), new("exact", "Задача", () => { }, () => true)];
        using var palette = new CommandPaletteViewModel(null, () => commands);
        palette.Open(); palette.Query = "задача";
        Assert.Equal(new[] { "exact", "prefix", "contains" }, palette.Items.Select(i => i.Id));
    }

    [Fact]
    public void ShellCommands_UseCapabilitiesAndRealNavigation_WithoutSearchUse()
    {
        using var hub = new WorkHubViewModel(new FakeClient(), ["Task.Read", "Contact.Read"]);
        using var shell = new MainWindowViewModel(new Uri("https://task.test"), null, workHub: hub);
        shell.Palette.Open();
        Assert.Equal(new[] { "tasks", "inbox", "contacts" }.Order(), shell.Palette.Items.Select(i => i.Id).Order());
        Assert.DoesNotContain(shell.Palette.Items, i => i.Id is "new-task" or "capture" or "catalog");
        shell.Palette.Query = "конт"; shell.Palette.ExecuteSelected();
        Assert.Equal("contacts", shell.SelectedSection?.Route);
    }

    [Fact]
    public async System.Threading.Tasks.Task Debounce_SearchesOnlyLatestValidQuery_PreservesServerRanking()
    {
        var queries = new List<string>();
        var client = new FakeClient { SearchHandler = (q, _) =>
        {
            queries.Add(q);
            return Ok([Result("project", "Second"), Result("task", "First")]);
        }};
        using var hub = new WorkHubViewModel(client, ["Search.Use"]);
        using var palette = new CommandPaletteViewModel(hub, () => []);
        palette.Open(); palette.Query = "a";
        await System.Threading.Tasks.Task.Delay(250);
        Assert.Empty(queries);
        palette.Query = "ab"; palette.Query = "abc";
        Assert.Empty(queries);
        Assert.True(palette.IsLoading);
        await Eventually(() => palette.Items.Count == 2);
        Assert.Equal(new[] { "abc" }, queries);
        Assert.Equal(new[] { "Second", "First" }, palette.Items.Select(i => i.Title));
        palette.Query = new string('x', 201);
        await System.Threading.Tasks.Task.Delay(250);
        Assert.Single(queries); Assert.Empty(palette.Items);
        Assert.Contains("200", palette.StatusText);
    }

    [Fact]
    public async System.Threading.Tasks.Task QueryChange_CancelsInFlight_RejectsLateResponseEvenIfTransportIgnoresCancellation()
    {
        var oldResponse = new System.Threading.Tasks.TaskCompletionSource<DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>>();
        CancellationToken oldToken = default;
        var client = new FakeClient { SearchHandler = (q, ct) =>
        {
            if (q == "old") { oldToken = ct; return oldResponse.Task; }
            return Ok([Result("task", "New")]);
        }};
        using var hub = new WorkHubViewModel(client, ["Search.Use"]);
        using var palette = new CommandPaletteViewModel(hub, () => []);
        palette.Open(); palette.Query = "old";
        await Eventually(() => client.SearchCallCount == 1);
        palette.Query = "new";
        Assert.True(oldToken.IsCancellationRequested);
        await Eventually(() => palette.Items.SingleOrDefault()?.Title == "New");
        oldResponse.SetResult(new DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.Succeeded([Result("task", "Old")]));
        await System.Threading.Tasks.Task.Delay(30);
        Assert.Equal("New", Assert.Single(palette.Items).Title);
        Assert.False(palette.IsLoading);
    }

    [Theory]
    [InlineData(401, "Сессия")]
    [InlineData(403, "прав")]
    [InlineData(422, "Некорректный")]
    [InlineData(503, "недоступен")]
    public async System.Threading.Tasks.Task FailedSearch_HasExplicitFeedback_AndNoProtectedResults(int status, string message)
    {
        var client = new FakeClient { SearchHandler = (_, _) => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>>(status switch
        {
            401 => new DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.AuthenticationFailure(),
            403 => new DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.Forbidden(),
            422 => new DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.ValidationFailure("Некорректный запрос"),
            _ => new DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.ServerUnavailable(),
        })};
        using var hub = new WorkHubViewModel(client, ["Search.Use", "Task.Read"]);
        using var shell = new MainWindowViewModel(new Uri("https://task.test"), null, workHub: hub);
        shell.Palette.Open(); shell.Palette.Query = "lookup";
        await Eventually(() => client.SearchCallCount > 0 && !shell.Palette.IsLoading);
        Assert.Contains(message, shell.Palette.StatusText);
        Assert.Empty(hub.SearchResults);
        Assert.DoesNotContain(shell.Palette.Items, i => i.Result is not null);
        if (status == 401) Assert.False(hub.HasCapability("Task.Read"));
        if (status == 503)
        {
            shell.Palette.Query = "Задачи";
            shell.Palette.ExecuteSelected();
            Assert.Equal("tasks", shell.SelectedSection?.Route);
            Assert.Equal(1, client.SearchCallCount);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task ScopeChange_ClearsResultsAndCancelsPendingEvenWithSameCapabilities()
    {
        var pending = new System.Threading.Tasks.TaskCompletionSource<DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>>();
        CancellationToken token = default;
        var client = new FakeClient { SearchHandler = (_, ct) => { token = ct; return pending.Task; } };
        using var hub = new WorkHubViewModel(client, ["Search.Use"]);
        using var palette = new CommandPaletteViewModel(hub, () => []);
        palette.Open(); palette.Query = "test";
        await Eventually(() => client.SearchCallCount == 1);
        hub.UpdateCapabilities(["Search.Use"]);
        Assert.True(token.IsCancellationRequested);
        pending.SetResult(new DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.Succeeded([Result("task", "Hidden")]));
        await System.Threading.Tasks.Task.Delay(30);
        Assert.Empty(palette.Items); Assert.Empty(hub.SearchResults);
    }

    [Fact]
    public async System.Threading.Tasks.Task EmptyResult_AndCloseDuringRequest_DoNotRetainResults()
    {
        var client = new FakeClient { SearchHandler = (_, _) => Ok([]) };
        using var hub = new WorkHubViewModel(client, ["Search.Use"]);
        using var palette = new CommandPaletteViewModel(hub, () => []);
        palette.Open(); palette.Query = "empty";
        await Eventually(() => client.SearchCallCount > 0 && !palette.IsLoading);
        Assert.Contains("Ничего не найдено", palette.StatusText);
        palette.Query = "cancel"; palette.Close();
        await System.Threading.Tasks.Task.Delay(250);
        Assert.Equal(1, client.SearchCallCount);
        Assert.Empty(hub.SearchResults);
    }

    [Fact]
    public async System.Threading.Tasks.Task SelectedResult_UsesExistingRouteAndParentObject_StaleEntryCannotExecute()
    {
        var parent = Guid.NewGuid();
        var result = Result("file_location", "File") with { ParentObjectId = parent };
        var client = new FakeClient { SearchHandler = (_, _) => Ok([result]) };
        using var hub = new WorkHubViewModel(client, ["Search.Use", "FileCatalog.Read"]);
        using var shell = new MainWindowViewModel(new Uri("https://task.test"), null, workHub: hub);
        var opened = Guid.Empty;
        hub.OpenObjectRequested += (_, id) => opened = id;
        shell.Palette.Open(); shell.Palette.Query = "file";
        await Eventually(() => shell.Palette.Items.Any(i => i.Result is not null));
        var entry = shell.Palette.Items.Single(i => i.Result is not null);
        shell.Palette.Selected = entry;
        shell.Palette.ExecuteSelected();
        Assert.Equal(parent, opened);
        Assert.Equal("catalog", shell.SelectedSection?.Route);
        shell.Palette.Open(); shell.Palette.Selected = entry; shell.Palette.ExecuteSelected();
        Assert.True(shell.Palette.IsOpen);
    }

    internal static DesktopSearchResult Result(string type, string title) => new(Guid.NewGuid(), type, title, null, 1, DateTimeOffset.UtcNow);
    private static System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>> Ok(IReadOnlyList<DesktopSearchResult> results) =>
        System.Threading.Tasks.Task.FromResult<DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>>(new DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.Succeeded(results));
    private static async System.Threading.Tasks.Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 150 && !condition(); i++) await System.Threading.Tasks.Task.Delay(20);
        Assert.True(condition());
    }
}
