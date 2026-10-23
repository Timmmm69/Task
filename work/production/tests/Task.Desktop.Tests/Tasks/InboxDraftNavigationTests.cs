using System.Text.Json.Nodes;
using Task.Desktop.Modes;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;
using static Task.Desktop.Tests.TaskScreen.InboxViewModelTests;

namespace Task.Desktop.Tests.TaskScreen;

public sealed class InboxDraftNavigationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async System.Threading.Tasks.Task NavigationPreservesEditorFieldsAndSourceVersion(bool throughShell)
    {
        var source = CreateTask("Source") with { Version = 7 };
        var projectId = Guid.NewGuid();
        var client = new FakeInboxClient
        {
            PageResult = Page(source),
            OptionsResult = new(new JsonObject
            {
                ["projects"] = new JsonArray(new JsonObject { ["id"] = projectId.ToString(), ["name"] = "Project" }),
            }, 0, null),
        };
        using var model = new InboxViewModel(client, Capabilities);
        using var shell = new MainWindowViewModel(null, null, inbox: model);
        shell.SelectedSection = shell.Sections.Single(section => section.Route == "inbox");
        await model.ActivateAsync();
        await model.ConvertCommand.ExecuteAsync();
        var editor = model.Conversion!;
        editor.Title = "Draft title";
        editor.Card.Description = "Draft description";
        editor.Card.Project = editor.Card.Projects.Single(project => project.Id == projectId);
        editor.Priority = editor.Priorities.Single(priority => priority.Value == DesktopTaskPriority.High);
        editor.StartText = "25.10.2026 10:00";
        editor.DeadlineText = "26.10.2026 14:30";
        editor.Card.Duration = "45";
        var revision = editor.Revision;
        if (throughShell)
        {
            shell.SelectedSection = shell.Sections.Single(section => section.Route == "tasks");
            shell.SelectedSection = shell.Sections.Single(section => section.Route == "inbox");
        }
        else
        {
            model.Deactivate();
            await model.ActivateAsync();
        }
        Assert.Same(editor, model.Conversion);
        Assert.Equal("Draft title", editor.Title);
        Assert.Equal("Draft description", editor.Card.Description);
        Assert.Equal(projectId, editor.Card.Project!.Id);
        Assert.Equal(DesktopTaskPriority.High, editor.Priority.Value);
        Assert.Equal("25.10.2026 10:00", editor.StartText);
        Assert.Equal("26.10.2026 14:30", editor.DeadlineText);
        Assert.Equal("45", editor.Card.Duration);
        Assert.Equal(revision, editor.Revision);
        Assert.Equal(7, editor.SourceVersion);
        Assert.Equal(0, client.PatchCalls);
        Assert.Null(client.LastCreate);
        // Refreshing the list must not silently rebase the user's editor.
        client.PageResult = Page(source with { Version = 8, Title = "Server title" });
        await model.RefreshCommand.ExecuteAsync();
        client.PatchResult = new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(
            source with { Version = 8, Card = new() { Description = "Draft description" } }, 8, false);
        await model.SaveConversionCommand.ExecuteAsync();
        Assert.Equal(source.Id, client.LastPatch!.Id);
        Assert.Equal(7, client.LastPatch.ExpectedVersion);
        Assert.Equal("Draft title", client.LastPatch.Title.Value);
        Assert.Equal("Draft description", JsonNode.Parse(client.LastPatch.CardPatch!)!["description"]!.GetValue<string>());
        Assert.Null(model.Conversion);
        Assert.Equal(1, client.PatchCalls);
    }

    [Theory]
    [InlineData("offline")]
    [InlineData("permissions")]
    [InlineData("forbidden")]
    public async System.Threading.Tasks.Task ReadOnlyNavigationPreservesDraftAndBlocksSave(string reason)
    {
        var client = new FakeInboxClient { PageResult = Page(CreateTask("Source")) };
        using var model = new InboxViewModel(client, Capabilities);
        await model.ActivateAsync();
        await model.ConvertCommand.ExecuteAsync();
        var editor = model.Conversion!;
        editor.Title = "Draft";
        if (reason == "offline")
        {
            model.UpdateConnectivity(false);
            client.PageResult = new DesktopTasksApiResult<DesktopTaskPage>.ServerUnavailable();
        }
        else if (reason == "permissions") model.UpdateCapabilities(["Task.Read"]);
        else client.PageResult = new DesktopTasksApiResult<DesktopTaskPage>.Forbidden();
        model.Deactivate();
        await model.ActivateAsync();
        Assert.Same(editor, model.Conversion);
        Assert.Equal("Draft", editor.Title);
        Assert.False(model.CanSaveConversion);
        Assert.False(await model.SaveConversionCommand.ExecuteAsync());
        Assert.Equal(0, client.PatchCalls);
    }

    [Fact]
    public async System.Threading.Tasks.Task AnotherConversionRequiresExplicitCancel()
    {
        var client = new FakeInboxClient { PageResult = Page(CreateTask("First"), CreateTask("Second")) };
        using var model = new InboxViewModel(client, Capabilities);
        using var shell = new MainWindowViewModel(null, null, inbox: model);
        await model.ActivateAsync();
        await model.ConvertCommand.ExecuteAsync(model.Items[0]);
        var editor = model.Conversion!;
        editor.Title = "Draft";
        model.SelectedItem = model.Items[1];
        Assert.False(model.CanConvert);
        Assert.False(await model.ConvertCommand.ExecuteAsync(model.SelectedItem));
        Assert.Same(editor, model.Conversion);
        Assert.Equal("Draft", editor.Title);
        model.Deactivate();
        await model.ActivateAsync();
        Assert.False(await model.ConvertCommand.ExecuteAsync(model.SelectedItem));
        await ApplicationModeTests.AssertCancelledAsync(shell);
        await model.CancelConversionCommand.ExecuteAsync();
        Assert.Null(model.Conversion);
        Assert.True(model.CanConvert);
        await model.ConvertCommand.ExecuteAsync(model.SelectedItem);
        Assert.NotSame(editor, model.Conversion);
        Assert.Equal(model.SelectedItem!.Id, model.Conversion!.SourceId);
    }

    [Fact]
    public async System.Threading.Tasks.Task NavigationDoesNotAbandonPendingSaveAndModeSwitchStillBlocks()
    {
        var source = CreateTask("Source");
        var pending = new TaskCompletionSource<DesktopTaskWriteResult<DesktopTaskDto>>();
        var client = new FakeInboxClient { PageResult = Page(source), PendingPatch = pending };
        using var model = new InboxViewModel(client, Capabilities);
        using var shell = new MainWindowViewModel(null, null, inbox: model);
        await model.ActivateAsync();
        await model.ConvertCommand.ExecuteAsync();
        var editor = model.Conversion!;
        editor.Card.Description = "Draft";
        var save = model.SaveConversionCommand.ExecuteAsync();
        model.Deactivate();
        await model.ActivateAsync();
        Assert.Same(editor, model.Conversion);
        Assert.True(model.IsBusy);
        Assert.False(client.PatchCancellationToken.IsCancellationRequested);
        Assert.False(await model.CancelConversionCommand.ExecuteAsync());
        Assert.False(await model.SaveConversionCommand.ExecuteAsync());
        Assert.False(await ModeSwitchGuard.PrepareAsync(ModeSwitchGuard.Inspect(shell), () => true,
            (_, _) => throw new InvalidOperationException("Pending write must block mode switch")));
        client.PageResult = Page();
        pending.SetResult(new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(source with { Version = 2 }, 2, false));
        await save;
        Assert.Null(model.Conversion);
        Assert.Equal(1, client.PatchCalls);
    }

    [Fact]
    public async System.Threading.Tasks.Task ModeSwitchCanSavePreservedDraftFromAnotherSection()
    {
        var source = CreateTask("Source");
        var client = new FakeInboxClient { PageResult = Page(source) };
        using var model = new InboxViewModel(client, Capabilities);
        using var shell = new MainWindowViewModel(null, null, inbox: model);
        shell.SelectedSection = shell.Sections.Single(section => section.Route == "inbox");
        await model.ActivateAsync();
        await model.ConvertCommand.ExecuteAsync();
        model.Conversion!.Card.Description = "Draft";
        shell.SelectedSection = shell.Sections.Single(section => section.Route == "tasks");
        client.PatchResult = new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(source with { Version = 2 }, 2, false);
        Assert.True(await ModeSwitchGuard.PrepareAsync(ModeSwitchGuard.Inspect(shell), () => true,
            (_, canSave) => { Assert.True(canSave); return ModeSwitchDecision.Save; }));
        Assert.Null(model.Conversion);
        Assert.Equal(1, client.PatchCalls);
    }

    [Fact]
    public async System.Threading.Tasks.Task SessionEndStillClearsEditorAndBlocksWrites()
    {
        var client = new FakeInboxClient { PageResult = Page(CreateTask("Source")) };
        using var model = new InboxViewModel(client, Capabilities);
        await model.ActivateAsync();
        await model.ConvertCommand.ExecuteAsync();
        model.Deactivate();
        model.UpdateSessionState(false);
        await model.ActivateAsync();
        Assert.Null(model.Conversion);
        Assert.Empty(model.Items);
        Assert.False(model.CanSaveConversion);
        Assert.False(model.CanConvert);
        Assert.Equal(InboxScreenState.SessionEnded, model.State);
    }
}
