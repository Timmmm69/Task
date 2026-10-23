using Task.Desktop.Modes;
using Task.Desktop.ViewModels;
using Task.Desktop.TaskApi;
using Task.Desktop.Tests.TaskScreen;
using Task.Desktop.Tests.Work;
using static Task.Desktop.Tests.TaskScreen.InboxViewModelTests;

namespace Task.Desktop.Tests;

public sealed class CorporateCloseGuardTests
{
    [Fact]
    public void CleanExit_DoesNotPrompt()
    {
        using var shell = new MainWindowViewModel();
        Assert.True(Check(shell, _ => throw new Exception("Unexpected confirmation")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async System.Threading.Tasks.Task InactiveSections_AllDraftsArePreservedAndNeverSaved(bool confirm)
    {
        var client = new FakeInboxClient { PageResult = Page(CreateTask("Входящая")) };
        using var inbox = new InboxViewModel(client, Capabilities);
        using var tasks = new TasksViewModel(client, Capabilities);
        using var hub = new WorkHubViewModel(new WorkHubViewModelTests.FakeClient(), []);
        using var shell = new MainWindowViewModel(null, null, tasks, inbox: inbox, workHub: hub);
        await tasks.ActivateAsync(); await tasks.NewTaskCommand.ExecuteAsync();
        tasks.Editor!.Title = "Задача";
        await inbox.ActivateAsync(); await inbox.ConvertCommand.ExecuteAsync();
        var conversion = inbox.Conversion!; conversion.Title = "Преобразование";
        inbox.CaptureText = "Новая входящая"; hub.NewContactFirstName = "Контакт";
        var section = shell.SelectedSection;
        for (var attempt = 0; attempt < 2; attempt++)
            Assert.Equal(confirm, Check(shell, names =>
            {
                Assert.Contains("Задача", names); Assert.Contains("Преобразование входящей задачи", names);
                Assert.Contains("Новая входящая задача", names); Assert.Contains("Контакт", names);
                return confirm;
            }));
        Assert.Same(section, shell.SelectedSection); Assert.Same(conversion, inbox.Conversion);
        Assert.Equal("Преобразование", conversion.Title); Assert.Equal("Задача", tasks.Editor.Title);
        Assert.Equal("Новая входящая", inbox.CaptureText); Assert.Equal("Контакт", hub.NewContactFirstName);
        Assert.Equal(0, client.PatchCalls); Assert.Null(client.LastCreate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async System.Threading.Tasks.Task PendingAndUnknownWrite_CannotBeDiscardedEvenAfterApproval(bool success)
    {
        var source = CreateTask("Запись");
        var pending = new TaskCompletionSource<DesktopTaskWriteResult<DesktopTaskDto>>();
        var client = new FakeInboxClient { PageResult = Page(source), PendingPatch = pending };
        using var inbox = new InboxViewModel(client, Capabilities);
        using var shell = new MainWindowViewModel(null, null, inbox: inbox);
        await inbox.ActivateAsync(); await inbox.ConvertCommand.ExecuteAsync();
        var editor = inbox.Conversion!; editor.Card.Description = "Черновик";
        var save = inbox.SaveConversionCommand.ExecuteAsync();
        var command = client.LastPatch; var blocks = 0;
        bool Attempt() => CorporateCloseGuard.CanClose(() => ModeSwitchGuard.Inspect(shell), true,
            _ => throw new Exception("Must not offer discard"), () => blocks++);
        Assert.False(Attempt()); Assert.Equal(1, blocks);
        Assert.Same(editor, inbox.Conversion); Assert.False(client.PatchCancellationToken.IsCancellationRequested);
        pending.SetResult(success ? new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(source with { Version = 2 }, 2, false)
            : new DesktopTaskWriteResult<DesktopTaskDto>.ServerUnavailable());
        await save;
        if (success) Assert.True(Attempt());
        else
        {
            inbox.UpdateConnectivity(false); inbox.UpdateCapabilities(["Task.Read"]);
            Assert.False(Attempt()); Assert.Same(editor, inbox.Conversion);
            Assert.Same(command, client.LastPatch); Assert.Equal("Черновик", editor.Card.Description);
        }
        Assert.Equal(1, client.PatchCalls);
    }

    [Fact]
    public async System.Threading.Tasks.Task AnotherCaptureSuccessCannotResolvePreviousUnknownCapture()
    {
        var client = new FakeInboxClient { PageResult = Page() };
        using var inbox = new InboxViewModel(client, Capabilities);
        using var shell = new MainWindowViewModel(null, null, inbox: inbox);
        await inbox.ActivateAsync(); inbox.CaptureText = "Первый запрос";
        Assert.True(await inbox.CaptureCommand.ExecuteAsync()); Assert.True(inbox.HasUnconfirmedMutation);
        await inbox.RefreshCommand.ExecuteAsync();
        var source = CreateTask("Второй запрос");
        client.CreateResult = new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(source, source.Version, false);
        inbox.CaptureText = "Второй запрос";
        Assert.True(await inbox.CaptureCommand.ExecuteAsync()); Assert.Empty(inbox.CaptureText);
        Assert.True(inbox.HasUnconfirmedMutation);
        Assert.False(CorporateCloseGuard.CanClose(() => ModeSwitchGuard.Inspect(shell), true,
            _ => throw new Exception("The original request is still unknown"), () => { }));
    }

    [Fact]
    public void OperationStartedInsideConfirmation_IsRechecked()
    {
        var busy = false; var blocked = false;
        ModeSwitchEditor[] editors = [new("Комментарий", () => true, () => busy, null)];
        Assert.False(CorporateCloseGuard.CanClose(() => editors, false, _ => { busy = true; return true; }, () => blocked = true));
        Assert.True(blocked);
    }

    [Fact]
    public async System.Threading.Tasks.Task DefinitiveForbiddenTaskReply_DoesNotBlockExplicitDiscard()
    {
        var client = new TasksViewModelTests.FakeTasksApiClient { CreateResult = new DesktopTaskWriteResult<DesktopTaskDto>.Forbidden() };
        client.EnqueuePage(Page());
        using var tasks = new TasksViewModel(client, ["Task.Read", "Task.Create"]);
        using var shell = new MainWindowViewModel(null, null, tasks);
        await tasks.ActivateAsync(); await tasks.NewTaskCommand.ExecuteAsync(); tasks.Editor!.Title = "Черновик";
        await tasks.SaveEditorCommand.ExecuteAsync();
        var prompts = 0;
        Assert.True(Check(shell, _ => { prompts++; return true; }));
        Assert.Equal(1, prompts); Assert.Equal("Черновик", tasks.Editor.Title);
        Assert.Equal(1, client.CreateCallCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task MalformedTaskReply_PreservesOriginalCommandUntilDefinitiveRetry()
    {
        var source = CreateTask("Задача");
        var client = new TasksViewModelTests.FakeTasksApiClient { CreateResult = new DesktopTaskWriteResult<DesktopTaskDto>.MalformedResponse() };
        client.EnqueuePage(Page());
        using var tasks = new TasksViewModel(client, ["Task.Read", "Task.Create"]);
        using var shell = new MainWindowViewModel(null, null, tasks);
        await tasks.ActivateAsync(); await tasks.NewTaskCommand.ExecuteAsync(); tasks.Editor!.Title = "Задача";
        await tasks.SaveEditorCommand.ExecuteAsync();
        Assert.False(CorporateCloseGuard.CanClose(() => ModeSwitchGuard.Inspect(shell), true,
            _ => throw new Exception("Unknown task result"), () => { }));
        Assert.Equal(1, client.CreateCallCount);
        client.CreateResult = new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(source, source.Version, false);
        await tasks.SaveEditorCommand.ExecuteAsync();
        Assert.Equal(2, client.CreateCallCount); Assert.Same(client.CreateCommands[0], client.CreateCommands[1]);
        Assert.True(Check(shell, _ => throw new Exception("Successfully saved editor must be closed")));
    }

    [Fact]
    public async System.Threading.Tasks.Task CommentChecklistAndLinkDrafts_AreInspectedTogether()
    {
        var source = CreateTask("Задача");
        var client = new WorkspaceClient { DetailResult = new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(source) };
        client.EnqueuePage(Page(source));
        using var tasks = new TasksViewModel(client, ["Task.Read", "Task.Update", "Comment.Create"]);
        using var shell = new MainWindowViewModel(null, null, tasks);
        await tasks.ActivateAsync(); await tasks.OpenByIdAsync(source.Id);
        var workspace = Assert.IsType<TaskWorkspaceViewModel>(tasks.Workspace);
        workspace.Comment = "Комментарий"; workspace.CheckText = "Пункт";
        workspace.File = new TaskChoice(Guid.NewGuid(), "Файл");
        Assert.False(Check(shell, names =>
        {
            Assert.Contains("Комментарий задачи", names); Assert.Contains("Чек-лист задачи", names);
            Assert.Contains("Связи задачи", names); return false;
        }));
        Assert.Same(workspace, tasks.Workspace); Assert.Equal("Комментарий", workspace.Comment);
        Assert.Equal("Пункт", workspace.CheckText); Assert.NotNull(workspace.File); Assert.Equal(0, client.Writes);
        client.FailWrite = true;
        await workspace.AddCommentCommand.ExecuteAsync();
        var blocked = false;
        Assert.False(CorporateCloseGuard.CanClose(() => ModeSwitchGuard.Inspect(shell), true,
            _ => throw new Exception("Unconfirmed comment must not be discarded"), () => blocked = true));
        Assert.True(blocked); Assert.Equal("Комментарий", workspace.Comment); Assert.Equal(1, client.Writes);
    }

    private sealed class WorkspaceClient : TasksViewModelTests.FakeTasksApiClient, IDesktopTaskWorkspaceClient
    {
        public int Writes { get; private set; }
        public bool FailWrite { get; set; }
        public System.Threading.Tasks.Task<TaskWorkspaceResult> GetOptionsAsync(string query, CancellationToken token) =>
            System.Threading.Tasks.Task.FromResult(new TaskWorkspaceResult(new System.Text.Json.Nodes.JsonObject(), 1, null));
        public System.Threading.Tasks.Task<TaskWorkspaceResult> GetWorkspaceAsync(Guid id, CancellationToken token) => GetOptionsAsync("", token);
        public System.Threading.Tasks.Task<TaskWorkspaceResult> WriteWorkspaceAsync(Guid id, long version, string path,
            System.Net.Http.HttpMethod method, System.Text.Json.Nodes.JsonObject body, string key, CancellationToken token)
        {
            Writes++;
            return FailWrite ? System.Threading.Tasks.Task.FromResult(new TaskWorkspaceResult(null, version, "Нет ответа сервера"))
                : GetOptionsAsync("", token);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async System.Threading.Tasks.Task UnconfirmedContactWrite_BlocksCloseAfterCommandCompletion(bool malformed)
    {
        var contact = new Task.Desktop.Work.DesktopContact(Guid.NewGuid(), 1, "Анна", "Анна", null, null, "active", "active");
        var client = new WorkHubViewModelTests.FakeClient
        {
            Contacts = [contact],
            ContactWriteResult = malformed
                ? new Task.Desktop.Work.DesktopWorkResult<Task.Desktop.Work.DesktopContactDetails>.MalformedResponse()
                : new Task.Desktop.Work.DesktopWorkResult<Task.Desktop.Work.DesktopContactDetails>.ServerUnavailable()
        };
        using var hub = new WorkHubViewModel(client, ["Contact.Read", "Contact.Update"]);
        hub.Activate(WorkHubArea.Contacts); hub.SelectedContact = contact; await hub.ContactSelectionTask;
        var card = hub.ContactCard!; await card.EditCommand.ExecuteAsync(); card.Notes = "Черновик";
        await card.SaveCommand.ExecuteAsync(); Assert.False(card.IsSaving);
        using var shell = new MainWindowViewModel(null, null, workHub: hub);
        var blocked = false;
        Assert.False(CorporateCloseGuard.CanClose(() => ModeSwitchGuard.Inspect(shell), true,
            _ => throw new Exception("Unconfirmed write cannot be discarded"), () => blocked = true));
        Assert.True(blocked); Assert.True(card.IsEditing); Assert.Equal("Черновик", card.Notes);
        Assert.False(await ModeSwitchGuard.PrepareAsync(ModeSwitchGuard.Inspect(shell), () => true,
            (_, _) => throw new Exception("Mode switch must not bypass unresolved operation")));
    }

    internal static bool Check(MainWindowViewModel shell, Func<IReadOnlyList<string>, bool> confirm) =>
        CorporateCloseGuard.CanClose(() => ModeSwitchGuard.Inspect(shell), false, confirm,
            () => throw new Exception("Unexpected pending operation"));
}
