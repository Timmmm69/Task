using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Task.Desktop.Infrastructure;
using Task.Desktop.ViewModels;
using Task.Desktop.TaskApi;
using static Task.Desktop.Tests.TaskScreen.InboxViewModelTests;

namespace Task.Desktop.Tests;

public sealed class CorporateCloseUiTests
{
    [Fact]
    public async System.Threading.Tasks.Task NativeWpf_CloseCancelTrayExitTransitionsAndPreferenceFlush()
    {
        if (Environment.GetEnvironmentVariable("TASK_CORPORATE_CLOSE_CHILD") != "1")
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("vstest"); start.ArgumentList.Add(GetType().Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + GetType().FullName + "." + nameof(NativeWpf_CloseCancelTrayExitTransitionsAndPreferenceFlush));
            start.Environment["TASK_CORPORATE_CLOSE_CHILD"] = "1";
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(55)); Assert.True(process.ExitCode == 0, await stdout + await stderr); }
            finally { if (!process.HasExited) process.Kill(true); }
            return;
        }
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                MainWindow? window = null;
                var root = Path.Combine(Path.GetTempPath(), "Task-corporate-close", Guid.NewGuid().ToString("N"));
                try
                {
                    var app = new App(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    var source = CreateTask("Запись");
                    var pending = new TaskCompletionSource<DesktopTaskWriteResult<DesktopTaskDto>>();
                    var client = new FakeInboxClient { PageResult = Page(source), PendingPatch = pending };
                    using var inbox = new InboxViewModel(client, Capabilities);
                    using var shell = new MainWindowViewModel(null, null, inbox: inbox);
                    await inbox.ActivateAsync(); await inbox.ConvertCommand.ExecuteAsync();
                    var editor = inbox.Conversion!; editor.Card.Description = "Не потерять"; inbox.CaptureText = "Новая запись";
                    var section = shell.SelectedSection;
                    window = new MainWindow(shell) { ShowInTaskbar = false };
                    var closed = false; var confirmations = 0; var blocks = 0; var accept = false;
                    var closeCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    window.Closed += (_, _) => { closed = true; closeCompletion.TrySetResult(); };
                    window.ConfirmCloseOverride = names => { confirmations++; Assert.Equal(2, names.Count); return accept; };
                    window.OperationPendingOverride = () => blocks++;
                    window.TrayAvailableOverride = () => true;
                    window.Show();
                    window.Close(); // X hides the window; no confirmation and no disposal.
                    Assert.False(window.IsVisible); Assert.False(closed); Assert.Equal(0, confirmations);
                    Assert.Same(editor, inbox.Conversion);
                    window.Show();
                    window.RequestExit(); // Tray exit can be cancelled.
                    Assert.False(closed); Assert.True(window.IsVisible); Assert.Equal(1, confirmations);
                    Assert.Same(section, shell.SelectedSection); Assert.Equal("Не потерять", editor.Card.Description);
                    inbox.UpdateConnectivity(false); inbox.UpdateCapabilities(["Task.Read"]);
                    window.RequestExit(); Assert.False(closed); Assert.Equal(2, confirmations);
                    Assert.Same(editor, inbox.Conversion); Assert.Equal("Новая запись", inbox.CaptureText);
                    inbox.UpdateConnectivity(true); inbox.UpdateCapabilities(Capabilities);
                    await inbox.RefreshCommand.ExecuteAsync();
                    Assert.True(inbox.CanSaveConversion);
                    var save = inbox.SaveConversionCommand.ExecuteAsync();
                    accept = true; window.RequestExit();
                    Assert.False(closed); Assert.Equal(1, blocks); Assert.Equal(2, confirmations);
                    Assert.Same(editor, inbox.Conversion); Assert.False(client.PatchCancellationToken.IsCancellationRequested);
                    pending.SetResult(new DesktopTaskWriteResult<DesktopTaskDto>.VersionConflict()); await save;
                    Assert.True(editor.HasConflict);
                    accept = false; window.RequestExit(); Assert.False(closed); Assert.True(editor.HasConflict);
                    Assert.Equal("Не потерять", editor.Card.Description);
                    var writes = client.PatchCalls;
                    // The actual async preference flush must close without asking twice.
                    var store = new ViewStateStore(root, "server/org", Guid.NewGuid(), "device"); await store.Ready;
                    ViewState.Attach(window, store);
                    window.ConfirmCloseOverride = names => { confirmations++; return true; };
                    var before = confirmations; window.RequestExit();
                    await closeCompletion.Task.WaitAsync(TimeSpan.FromSeconds(15));
                    Assert.True(closed); Assert.Equal(before + 1, confirmations); Assert.Equal(writes, client.PatchCalls); Assert.Null(client.LastCreate);
                    window = null;
                    // A later Closing subscriber can cancel the final preference-flush close.
                    window = new MainWindow(shell) { ShowInTaskbar = false };
                    var cancelledCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var cancelOnce = true; var repeatedPrompts = 0; var finallyClosed = false;
                    window.ConfirmCloseOverride = _ => { repeatedPrompts++; return true; };
                    var cancelStore = new ViewStateStore(root + "-cancelled", "server/org", Guid.NewGuid(), "device"); await cancelStore.Ready;
                    ViewState.Attach(window, cancelStore);
                    window.Closing += (_, args) =>
                    {
                        if (ViewState.IsCompletingClose(window!) && cancelOnce)
                        { cancelOnce = false; args.Cancel = true; cancelledCompletion.TrySetResult(); }
                    };
                    window.Closed += (_, _) => finallyClosed = true;
                    window.Show(); window.RequestExit();
                    await cancelledCompletion.Task.WaitAsync(TimeSpan.FromSeconds(15));
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    Assert.False(finallyClosed); Assert.True(window.IsEnabled);
                    window.RequestExit(); Assert.True(finallyClosed); Assert.Equal(2, repeatedPrompts); window = null;
                    // The security and mode transition paths bypass user cancellation.
                    foreach (var authentication in new[] { false, true })
                    {
                        window = new MainWindow(shell) { ShowInTaskbar = false };
                        window.ConfirmCloseOverride = _ => throw new Exception("No second prompt on technical close");
                        window.TrayAvailableOverride = () => true;
                        var technicalClosed = false; window.Closed += (_, _) => technicalClosed = true; window.Show();
                        if (authentication) window.CloseForAuthenticationTransition(); else window.CloseAfterModeSwitch();
                        Assert.True(technicalClosed); window = null;
                    }
                    // Authentication may change while a modal confirmation pumps messages.
                    foreach (var authentication in new[] { false, true })
                    {
                        window = new MainWindow(shell) { ShowInTaskbar = false };
                        var reentrantClosed = false; window.Closed += (_, _) => reentrantClosed = true;
                        window.ConfirmCloseOverride = _ =>
                        {
                            window.RequestExit(); // Repeated tray exit must not recurse either.
                            if (authentication) window.CloseForAuthenticationTransition(); else window.CloseAfterModeSwitch();
                            return false;
                        };
                        window.Show(); window.RequestExit();
                        Assert.True(reentrantClosed); window = null;
                    }
                    // Cancelling tray exit must not unlock a mode-switch-disabled owner.
                    window = new MainWindow(shell) { ShowInTaskbar = false };
                    window.ConfirmCloseOverride = _ => false;
                    window.Show(); window.IsEnabled = false; window.RequestExit();
                    Assert.False(window.IsEnabled);
                    window.CloseAfterModeSwitch(); window = null;
                    using var clean = new MainWindowViewModel();
                    window = new MainWindow(clean) { ShowInTaskbar = false };
                    window.ConfirmCloseOverride = _ => throw new Exception("Clean exit must not prompt");
                    var cleanClosed = false; window.Closed += (_, _) => cleanClosed = true; window.Show(); window.Close();
                    Assert.True(cleanClosed); window = null;
                    done.SetResult();
                }
                catch (Exception error) { done.SetException(error); }
                finally { window?.CloseForAuthenticationTransition(); dispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(45));
        Assert.True(await System.Threading.Tasks.Task.Run(() => thread.Join(TimeSpan.FromSeconds(5))));
    }
}
