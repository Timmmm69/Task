using System.IO;
using Task.Desktop.Personal;
using Task.Desktop.Projects;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;
using Task.Domain;
using FakeClient = Task.Desktop.Tests.Work.WorkHubViewModelTests.FakeClient;

namespace Task.Desktop.Tests.Personal;

public sealed class PersonalSearchBrowserTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Task-search-browser-tests", Guid.NewGuid().ToString("N"));
    [Fact]
    public async System.Threading.Tasks.Task ExistingSearchFields_CyrillicCaseAndLifecycleArePreserved()
    {
        var paths = new PersonalDataPaths(_root);
        using var store = new PersonalTaskStore(paths);
        var task = store.Create(new DesktopCreateTaskCommand("Проверка названия", DesktopTaskPriority.Normal,
            card: new TaskCardContent { Description = "Кириллица в описании 7391" }));
        var project = store.SaveProject(new(Guid.Empty, 0, "Новый проект", "Бюджет7391", DesktopProjectStatus.Planning, null, null));
        var person = store.CreateContact("Евгения7391", "Петрова7391", "Женя");
        var file = store.CreateCatalog("Документ", "file_reference", "Описание7391");
        store.AddPersonalLocation(file.Id, file.Version, Path.Combine(_root, "material7391.pdf"));
        using (var db = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = paths.DatabasePath, Pooling = false }.ToString()))
        {
            db.Open(); using var c = db.CreateCommand();
            c.CommandText = "UPDATE personal_contacts SET notes='Смета7391' WHERE id=$id";
            c.Parameters.AddWithValue("$id", person.Id.ToString("D")); c.ExecuteNonQuery();
        }
        using var hub = new WorkHubViewModel(new PersonalWorkClient(store), ["Search.Use"], personal: true);
        using var page = new PersonalSearchViewModel(hub, store);
        foreach (var (query, id) in new[] { ("НАЗВАНИЯ", task.Id), ("кириллица", task.Id), ("бюджет7391", project.Id),
                     ("евгения7391", person.Id), ("петрова7391", person.Id), ("смета7391", person.Id),
                     ("описание7391", file.Id), ("material7391", file.Id) })
        {
            page.Query = query; await page.SubmitCommand.ExecuteAsync();
            Assert.Equal(id, Assert.Single(page.Results).Source.ObjectId);
        }
        store.ChangeWorkspaceLifecycle(task.Id, task.Version, "archive");
        page.Query = "названия"; await page.SubmitCommand.ExecuteAsync(); Assert.True(page.ShowEmpty);
    }
    [Fact]
    public async System.Threading.Tasks.Task HundredsOfMatches_FilterAfterFullSearch_AndClearInvalidatesSelection()
    {
        using var store = new PersonalTaskStore(new(_root));
        for (var i = 0; i < 160; i++) store.Create(new DesktopCreateTaskCommand($"Альфа {i:000}", DesktopTaskPriority.Normal,
            card: new TaskCardContent { Description = "Общий поисковый контекст" }));
        var contact = store.CreateContact("Контекст", null, "Яна Общий");
        store.SaveProject(new(Guid.Empty, 0, "Якорь Общий", "Проект с описанием", DesktopProjectStatus.Active, null, null));
        store.CreateCatalog("Ящик Общий", "virtual_folder", null);
        using var hub = new WorkHubViewModel(new PersonalWorkClient(store), ["Search.Use"], personal: true);
        using var page = new PersonalSearchViewModel(hub, store);
        Assert.True(page.ShowPrompt); Assert.False(page.ShowEmpty);
        page.Query = "ОБЩИЙ";
        await page.SubmitCommand.ExecuteAsync();
        Assert.Equal(163, page.Results.Count);
        Assert.False(page.ShowPrompt); Assert.False(page.ShowEmpty);
        Assert.Contains(page.Results, row => row.Source.ObjectType == "task" && row.Context.Contains("контекст"));
        hub.SearchTypeFilter = "Контакты";
        Assert.Equal(contact.Id, Assert.Single(page.Results).Source.ObjectId);
        page.Selected = page.Results[0];
        var requested = Guid.Empty; hub.OpenObjectRequested += (_, id) => requested = id;
        Assert.Equal(Guid.Empty, requested); // Selecting a row never opens it.
        await page.OpenCommand.ExecuteAsync(); Assert.Equal(contact.Id, requested);
        await page.ResetCommand.ExecuteAsync(); Assert.Equal(163, page.Results.Count);
        page.Query = "Неттакогосовпадения"; await page.SubmitCommand.ExecuteAsync(); Assert.True(page.ShowEmpty);
        await page.ClearCommand.ExecuteAsync(); Assert.Equal("", page.Query);
        Assert.True(page.ShowPrompt); Assert.False(page.ShowEmpty); Assert.Empty(page.Results); Assert.Null(page.Selected);
        page.Query = "я"; Assert.False(page.SubmitCommand.CanExecute(null));
        page.Query = new string('я', 201); Assert.False(page.SubmitCommand.CanExecute(null));
    }
    [Theory]
    [InlineData("task")]
    [InlineData("project")]
    [InlineData("contact")]
    [InlineData("catalog_item")]
    public async System.Threading.Tasks.Task MissingOrArchivedResult_RemainsOnSearchAndExplainsRecovery(string type)
    {
        using var store = new PersonalTaskStore(new(_root));
        switch (type)
        {
            case "task": store.Create(new DesktopCreateTaskCommand("Общий", DesktopTaskPriority.Normal)); break;
            case "project": store.SaveProject(new(Guid.Empty, 0, "Общий", null, DesktopProjectStatus.Planning, null, null)); break;
            case "contact": store.CreateContact("Общий", null, "Общий"); break;
            default: store.CreateCatalog("Общий", "file_reference", null); break;
        }
        using var hub = new WorkHubViewModel(new PersonalWorkClient(store), ["Search.Use"], personal: true);
        using var page = new PersonalSearchViewModel(hub, store);
        page.Query = "общ"; await page.SubmitCommand.ExecuteAsync(); page.Selected = Assert.Single(page.Results);
        var requested = 0; hub.OpenObjectRequested += (_, _) => requested++;
        await page.OpenCommand.ExecuteAsync(); Assert.Equal(1, requested);
        store.ChangeWorkspaceLifecycle(page.Selected.Source.ObjectId, page.Selected.Source.Version, "trash");
        await page.OpenCommand.ExecuteAsync(); Assert.Equal(1, requested);
        Assert.True(page.HasError); Assert.Contains("недоступна", page.ErrorText);
        await page.SubmitCommand.ExecuteAsync(); Assert.True(page.ShowEmpty); Assert.False(page.HasError);
    }
    [Fact]
    public async System.Threading.Tasks.Task LateResponseAfterQueryEdit_CannotRepopulatePage_AndErrorsCanRetry()
    {
        using var store = new PersonalTaskStore(new(_root));
        var response = new System.Threading.Tasks.TaskCompletionSource<DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>>();
        var fail = false;
        using var hub = new WorkHubViewModel(new FakeClient
        {
            SearchHandler = (q, _) => q == "old" ? response.Task :
            System.Threading.Tasks.Task.FromResult<DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>>(fail
                ? new DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.ValidationFailure("Ошибка чтения")
                : new DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.Succeeded([]))
        }, ["Search.Use"], personal: true);
        using var page = new PersonalSearchViewModel(hub, store);
        page.Query = "old"; var pending = page.SubmitCommand.ExecuteAsync(); Assert.True(page.IsLoading);
        page.Query = "new";
        response.SetResult(new DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.Succeeded([new(Guid.NewGuid(), "task", "Old", null, 1, DateTimeOffset.UtcNow)]));
        await pending; Assert.Empty(page.Results); Assert.True(page.ShowPrompt);
        fail = true; await page.SubmitCommand.ExecuteAsync(); Assert.True(page.HasError); Assert.False(page.ShowEmpty);
        fail = false; await page.SubmitCommand.ExecuteAsync(); Assert.False(page.HasError); Assert.True(page.ShowEmpty);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
