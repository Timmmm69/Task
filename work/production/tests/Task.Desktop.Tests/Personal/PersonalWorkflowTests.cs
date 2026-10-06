using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Microsoft.Data.Sqlite;
using Task.Desktop.Modes;
using Task.Desktop.Personal;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Tests.Personal;

public sealed class PersonalWorkflowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Task-personal-workflow", Guid.NewGuid().ToString("N"));
    [Fact]
    public async System.Threading.Tasks.Task ParentChoices_AreLoadedFromLocalTasksWithoutHiddenCommandFailures()
    {
        using var model = new PersonalApplicationModel(_root); var vm = model.Tasks!;
        vm.CaptureText = "Local parent"; await vm.CaptureCommand.ExecuteAsync();
        var parent = Assert.Single(vm.Items).Source;
        Exception? failure = null; vm.NewCommand.ExecutionFailed += error => failure = error;
        await vm.NewCommand.ExecuteAsync();
        Assert.Null(failure);
        Assert.Contains(vm.Editor!.Card.Tasks, choice => choice.Id == parent.Id && choice.Name == parent.Title);
        vm.Editor.Title = "Scheduled from editor";
        vm.Editor.Card.Description = "Line 1\nLine 2";
        vm.Editor.StartText = "08.10.2026 14:20";
        vm.Editor.Card.Duration = "45";
        vm.SaveCommand.ExecutionFailed += error => failure = error;
        await vm.SaveCommand.ExecuteAsync();
        Assert.Null(failure); Assert.Null(vm.Editor); Assert.Equal(2, vm.Items.Count);
    }
    [Fact]
    public async System.Threading.Tasks.Task InboxCaptureRestartReloadConversion_IsTheSamePersistentTask()
    {
        Guid id;
        using (var model = new PersonalApplicationModel(_root))
        {
            var vm = model.Tasks!; vm.SelectedSection = model.Sections.Single(s => s.Title == "Входящие");
            vm.CaptureText = "Captured"; await vm.CaptureCommand.ExecuteAsync();
            id = Assert.Single(vm.Items).Source.Id; Assert.Empty(vm.CaptureText);
        }
        using (var model = new PersonalApplicationModel(_root))
        {
            var vm = model.Tasks!; vm.SelectedSection = model.Sections.Single(s => s.Title == "Входящие"); await vm.RefreshAsync();
            vm.Selected = Assert.Single(vm.Items); Assert.Equal(id, vm.Selected.Source.Id);
            await vm.EditCommand.ExecuteAsync(); vm.Editor!.Card.Description = "Converted locally"; vm.Editor.Card.Date = "12.10.2026";
            await vm.SaveCommand.ExecuteAsync(); Assert.Null(vm.Editor); Assert.Empty(vm.Items);
        }
        using var store = new PersonalTaskStore(new(_root)); var task = store.Get(id)!;
        Assert.Equal(2, task.Version); Assert.Equal("Converted locally", task.Card!.Description); Assert.Equal(new DateOnly(2026, 10, 12), task.Card.ScheduledDate);
    }
    [Fact]
    public async System.Threading.Tasks.Task StaleEditorAndSqlFailure_KeepEditorAndUserValuesUntilSuccessfulCommit()
    {
        using var model = new PersonalApplicationModel(_root); var vm = model.Tasks!;
        vm.CaptureText = "Original"; await vm.CaptureCommand.ExecuteAsync(); vm.Selected = Assert.Single(vm.Items);
        Assert.Equal("Original", vm.Selected.ToString()); // No hidden actor or DTO metadata in WPF automation.
        await vm.EditCommand.ExecuteAsync(); vm.Editor!.Title = "My draft"; var editor = vm.Editor;
        model.Store.Patch(new(vm.Selected.Source.Id, 1, title: DesktopTaskField<string>.From("Other edit")));
        await vm.SaveCommand.ExecuteAsync(); Assert.Same(editor, vm.Editor); Assert.Equal("My draft", editor.Title); Assert.True(editor.HasConflict);
        await vm.ReloadEditorCommand.ExecuteAsync(); Assert.Equal("Other edit", editor.Title); editor.Title = "Final";
        using var sql = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = new PersonalDataPaths(_root).DatabasePath, Pooling = false }.ToString());
        sql.Open(); using var trigger = sql.CreateCommand(); trigger.CommandText = "CREATE TRIGGER reject_update BEFORE UPDATE ON tasks BEGIN SELECT RAISE(ABORT, 'test'); END;"; trigger.ExecuteNonQuery();
        await vm.SaveCommand.ExecuteAsync(); Assert.Same(editor, vm.Editor); Assert.Equal("Final", editor.Title); Assert.Contains("не сохранены", editor.StatusMessage);
        trigger.CommandText = "DROP TRIGGER reject_update;"; trigger.ExecuteNonQuery();
        await vm.SaveCommand.ExecuteAsync(); Assert.Null(vm.Editor); Assert.Equal("Final", Assert.Single(vm.Items).Title);
    }
    [Fact]
    public async System.Threading.Tasks.Task PersonalCrudChecklistInbox_ZeroCorporateHttpRequestsAndCredentialsUntouched()
    {
        Directory.CreateDirectory(_root);
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var settings = $"{{\"version\":1,\"baseUrl\":\"https://127.0.0.1:{port}/\"}}";
        var path = Path.Combine(_root, "server-settings.json"); File.WriteAllText(path, settings);
        var vault = Path.Combine(_root, "credentials.bin"); File.WriteAllBytes(vault, [9, 8, 7]);
        using (var model = new PersonalApplicationModel(_root))
        {
            var vm = model.Tasks!; await vm.NewCommand.ExecuteAsync(); vm.Editor!.Title = "No HTTP";
            vm.Editor.Card.Description = "Local"; await vm.SaveCommand.ExecuteAsync();
            vm.Selected = Assert.Single(vm.Items); await vm.EditCommand.ExecuteAsync(); vm.Editor!.Title = "Edited"; await vm.SaveCommand.ExecuteAsync();
            vm.Selected = Assert.Single(vm.Items); vm.CheckText = "Local checklist"; await vm.AddCheckCommand.ExecuteAsync();
            await vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.Completed);
            vm.CaptureText = "Inbox"; await vm.CaptureCommand.ExecuteAsync();
        }
        using (var restarted = new PersonalApplicationModel(_root)) { await restarted.Tasks!.RefreshAsync(); Assert.Equal(2, restarted.Tasks.Items.Count); }
        Assert.False(listener.Pending()); Assert.Equal(settings, File.ReadAllText(path)); Assert.Equal(new byte[] { 9, 8, 7 }, File.ReadAllBytes(vault));
        Assert.All(new[] { typeof(PersonalTaskStore), typeof(PersonalTasksClient), typeof(PersonalTasksViewModel) }, type =>
            Assert.DoesNotContain(type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic), field => field.FieldType == typeof(HttpClient) || field.FieldType.Namespace?.StartsWith("Task.Desktop.Security") == true));
    }
    [Fact]
    public async System.Threading.Tasks.Task PersonalModeGuard_CancelPreservesAndSaveCommitsBeforeSwitch()
    {
        using var model = new PersonalApplicationModel(_root); var vm = model.Tasks!;
        await vm.NewCommand.ExecuteAsync(); vm.Editor!.Title = "Save on switch"; var draft = vm.Editor;
        Assert.False(await ModeSwitchGuard.PrepareAsync(vm.InspectDrafts(), () => !vm.IsBusy, (_, _) => ModeSwitchDecision.Cancel));
        Assert.Same(draft, vm.Editor);
        Assert.True(await ModeSwitchGuard.PrepareAsync(vm.InspectDrafts(), () => !vm.IsBusy, (_, _) => ModeSwitchDecision.Save));
        Assert.Null(vm.Editor); Assert.Single(vm.Items);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
