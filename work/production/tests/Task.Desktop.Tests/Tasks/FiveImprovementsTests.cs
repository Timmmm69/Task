using System.IO;
using System.Windows.Input;
using Task.Desktop.Infrastructure;
using Task.Desktop.Modes;
using Task.Desktop.Personal;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Tests;

public sealed class FiveImprovementsTests
{
    [Fact]
    public async global::System.Threading.Tasks.Task CorporateCapture_DoesNotEraseNextDraft_OrSendTwice()
    {
        var path = Path.Combine(Path.GetTempPath(), "Task-five", Guid.NewGuid().ToString("N"));
        try
        {
            using var model = new PersonalApplicationModel(path);
            var created = model.Store.Create(new("first", DesktopTaskPriority.Normal));
            var release = new TaskCompletionSource<DesktopTaskWriteResult<DesktopTaskDto>>();
            var client = new TaskScreen.TasksViewModelTests.FakeTasksApiClient { CreateHandler = (_, _) => release.Task };
            client.EnqueuePage(new DesktopTasksApiResult<DesktopTaskPage>.Succeeded(new([], null, 0)));
            using var inbox = new InboxViewModel(client, ["Task.Read", "Task.Create"]);
            await inbox.ActivateAsync(); inbox.CaptureText = "first";
            var send = inbox.CaptureCommand.ExecuteAsync();
            Assert.False(await inbox.CaptureCommand.ExecuteAsync());
            inbox.CaptureText = "next draft";
            release.SetResult(new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(created, 1, false));
            await send;
            Assert.Equal(1, client.CreateCallCount); Assert.Equal("next draft", inbox.CaptureText);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
    [Fact]
    public async global::System.Threading.Tasks.Task FailedChecklistToggleAndCapture_KeepConfirmedStateAndDraft()
    {
        var path = Path.Combine(Path.GetTempPath(), "Task-five", Guid.NewGuid().ToString("N"));
        try
        {
            using var model = new PersonalApplicationModel(path);
            using var shell = new PersonalShellViewModel(model);
            shell.SelectedSection = shell.Sections.Single(s => s.Route == "tasks");
            var vm = model.Tasks!;
            await vm.NewCommand.ExecuteAsync(); vm.Editor!.Title = "check"; await vm.SaveCommand.ExecuteAsync();
            vm.Selected = Assert.Single(vm.Items); vm.CheckText = "item"; await vm.AddCheckCommand.ExecuteAsync();
            var item = Assert.Single(vm.Checklist);
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(path, "Personal", "tasks.db")};Pooling=False");
            connection.Open(); using var sql = connection.CreateCommand();
            sql.CommandText = "CREATE TRIGGER reject_write BEFORE UPDATE ON tasks BEGIN SELECT RAISE(ABORT, 'fixture'); END;"; sql.ExecuteNonQuery();
            await vm.ToggleCheckCommand.ExecuteAsync(item);
            Assert.False(Assert.Single(vm.Checklist).Completed);
            vm.CheckText = "keep checklist"; await vm.AddCheckCommand.ExecuteAsync(); Assert.Equal("keep checklist", vm.CheckText);
            sql.CommandText = "CREATE TRIGGER reject_create BEFORE INSERT ON tasks BEGIN SELECT RAISE(ABORT, 'fixture'); END;"; sql.ExecuteNonQuery();
            vm.CaptureText = "keep capture"; await vm.CaptureCommand.ExecuteAsync(); Assert.Equal("keep capture", vm.CaptureText);
            sql.CommandText = "DROP TRIGGER reject_write; DROP TRIGGER reject_create;"; sql.ExecuteNonQuery();
            vm.CheckText = ""; await vm.ToggleCheckCommand.ExecuteAsync(item);
            Assert.True(Assert.Single(vm.Checklist).Completed);
            await vm.ToggleCheckCommand.ExecuteAsync(Assert.Single(vm.Checklist));
            Assert.False(Assert.Single(vm.Checklist).Completed);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
    [Fact]
    public void DayPicker_PreservesInvalidRawInput_AndEmptyDate()
    {
        var card = new TaskCardEditor(null);
        card.Date = "31.02.2026";
        Assert.Null(card.CalendarDate);
        card.CalendarDate = null;
        Assert.Equal("31.02.2026", card.Date);
        Assert.Throws<ArgumentException>(() => card.Build(null));
        card.CalendarDate = new DateTime(2026, 10, 10);
        Assert.Equal(new DateOnly(2026, 10, 10), card.Build(null).ScheduledDate);
        card.Date = ""; Assert.Null(card.Build(null).ScheduledDate);
        card.UpdateStart(DateTimeOffset.UtcNow);
        card.SelectRelativeDay(1); Assert.Equal("", card.Date);
    }
    [Theory]
    [InlineData("2026-03-08T04:30:00Z", "08.03.2026")]
    [InlineData("2026-11-01T03:30:00Z", "01.11.2026")]
    public void Tomorrow_UsesCalendarDay_InScheduleZone_AcrossDst(string instant, string expected)
    {
        var card = new TaskCardEditor(null) { ScheduleZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time") };
        card.SelectRelativeDay(1, DateTimeOffset.Parse(instant));
        Assert.Equal(expected, card.Date);
    }
    [Fact]
    public void Shortcut_LeavesMultilineEnter_ImeAndRepeatAlone()
    {
        Assert.True(FormKeyboard.IsShortcut(Key.Enter, ModifierKeys.None, false, true));
        Assert.True(FormKeyboard.IsShortcut(Key.Enter, ModifierKeys.Control, false, false));
        Assert.False(FormKeyboard.IsShortcut(Key.Enter, ModifierKeys.None, false, false));
        Assert.False(FormKeyboard.IsShortcut(Key.ImeProcessed, ModifierKeys.Control, false, false));
        Assert.False(FormKeyboard.IsShortcut(Key.Enter, ModifierKeys.Control, true, false));
        Assert.False(FormKeyboard.IsShortcut(Key.Enter, ModifierKeys.Control | ModifierKeys.Shift, false, false));
    }
    [Fact]
    public async global::System.Threading.Tasks.Task Command_RejectsRepeat_AndContainsFailure()
    {
        var pending = new TaskCompletionSource(); int calls = 0; string draft = "keep";
        using var command = new AsyncCommand(async (_, _) => { calls++; await pending.Task; throw new IOException(); });
        bool failure = false; command.ExecutionFailed += _ => failure = true;
        var first = command.ExecuteAsync();
        Assert.False(await command.ExecuteAsync()); Assert.Equal(1, calls);
        pending.SetResult(); await first;
        Assert.True(failure); Assert.Equal("keep", draft); Assert.True(command.CanExecute(null));
    }
    [Theory]
    [InlineData(DesktopTaskStatus.Completed)]
    [InlineData(DesktopTaskStatus.Cancelled)]
    public async global::System.Threading.Tasks.Task Filter_IsViewOnly_DefaultOff_PreservesDrafts_AndLeavesInboxUnchanged(DesktopTaskStatus terminal)
    {
        var path = Path.Combine(Path.GetTempPath(), "Task-five", Guid.NewGuid().ToString("N"));
        try
        {
            using var model = new PersonalApplicationModel(path);
            using var shell = new PersonalShellViewModel(model);
            shell.SelectedSection = shell.Sections.Single(s => s.Route == "tasks");
            var vm = model.Tasks!;
            await vm.NewCommand.ExecuteAsync(); vm.Editor!.Title = "done"; await vm.SaveCommand.ExecuteAsync();
            vm.Selected = Assert.Single(vm.Items);
            await vm.TransitionCommand.ExecuteAsync(terminal);
            Assert.False(vm.HideCompleted); Assert.Single(vm.Items);
            vm.CaptureText = "capture draft"; vm.CheckText = "check draft";
            vm.HideCompleted = true;
            Assert.Empty(vm.Items); Assert.True(vm.HasHiddenTasks);
            Assert.Contains("скрыты", vm.EmptyBody); Assert.Equal("capture draft", vm.CaptureText); Assert.Equal("check draft", vm.CheckText);
            await vm.ShowCompletedCommand.ExecuteAsync(); Assert.Single(vm.Items);
            vm.CheckText = ""; vm.CaptureText = "";
            await vm.NewCommand.ExecuteAsync(); vm.Editor!.Title = "editor draft";
            vm.HideCompleted = true; Assert.Equal("editor draft", vm.Editor.Title);
            await vm.CancelCommand.ExecuteAsync();
            shell.SelectedSection = shell.Sections.Single(s => s.Route == "inbox");
            vm.CaptureText = "inbox"; await vm.CaptureCommand.ExecuteAsync();
            Assert.Single(vm.Items); Assert.False(vm.HasHiddenTasks);
            Assert.Equal(2, model.Store.List().Count);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
}
