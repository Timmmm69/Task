using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Task.Desktop.Personal;

namespace Task.Desktop.Tests.Projects;

public sealed class ProjectConfirmationUiTests
{
    [Fact]
    public async System.Threading.Tasks.Task NativeWpf_ConfirmationCancelEscapeCloseAndAutomation()
    {
        if (Environment.GetEnvironmentVariable("TASK_PROJECT_CONFIRM_CHILD") != "1")
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("vstest"); start.ArgumentList.Add(GetType().Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + GetType().FullName + "." + nameof(NativeWpf_ConfirmationCancelEscapeCloseAndAutomation));
            start.Environment["TASK_PROJECT_CONFIRM_CHILD"] = "1";
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
            dispatcher.BeginInvoke(() =>
            {
                Window? owner = null;
                try
                {
                    var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Task.Desktop;component/Resources/Theme.xaml", UriKind.Relative) });
                    owner = new Window { Width = 400, Height = 200, ShowInTaskbar = false }; owner.Show();
                    foreach (var action in new[] { "Архивировать", "Исключить" })
                        foreach (var operation in new[] { "cancel", "escape", "close", "confirm" })
                        {
                            Exception? callbackError = null; var timedOut = false;
                            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                            timer.Tick += (_, _) => { timedOut = true; foreach (Window dialog in owner.OwnedWindows) dialog.Close(); timer.Stop(); };
                            timer.Start();
                            dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
                            {
                                Window? dialog = null;
                                try
                                {
                                    dialog = owner.OwnedWindows.Cast<Window>().Single();
                                    Assert.Equal(action + "?", dialog.Title);
                                    var body = Assert.IsType<StackPanel>(dialog.Content);
                                    var buttons = body.Children.OfType<StackPanel>().Single().Children.OfType<Button>().ToArray();
                                    var cancel = buttons.Single(button => button.IsCancel);
                                    var confirm = buttons.Single(button => !button.IsCancel);
                                    Assert.True(cancel.IsDefault); Assert.False(confirm.IsDefault); Assert.Same(cancel, Keyboard.FocusedElement);
                                    Assert.Equal("Отмена", new ButtonAutomationPeer(cancel).GetName());
                                    Assert.Equal(action, new ButtonAutomationPeer(confirm).GetName());
                                    if (operation == "close") dialog.Close();
                                    else if (operation == "escape")
                                    {
                                        var handle = new WindowInteropHelper(dialog).Handle;
                                        Assert.True(PostMessage(handle, 0x0100, 0x1B, 0));
                                        Assert.True(PostMessage(handle, 0x0101, 0x1B, 0));
                                    }
                                    else ((IInvokeProvider)new ButtonAutomationPeer(operation == "confirm" ? confirm : cancel).GetPattern(PatternInterface.Invoke)).Invoke();
                                }
                                catch (Exception error) { callbackError = error; dialog?.Close(); }
                            });
                            var result = PersonalConfirmationDialog.Confirm(owner, action + "?", "Проект „Альфа“ · участник 123", action);
                            timer.Stop(); Assert.Null(callbackError); Assert.False(timedOut); Assert.Equal(operation == "confirm", result);
                        }
                    done.SetResult();
                }
                catch (Exception error) { done.SetException(error); }
                finally { owner?.Close(); dispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(45));
        Assert.True(await System.Threading.Tasks.Task.Run(() => thread.Join(TimeSpan.FromSeconds(5))));
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr handle, uint message, nuint wParam, nint lParam);
}
