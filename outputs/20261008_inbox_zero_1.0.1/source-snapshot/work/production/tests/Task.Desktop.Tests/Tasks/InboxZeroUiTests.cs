using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;
using Task.Desktop.Views;
using static Task.Desktop.Tests.TaskScreen.InboxViewModelTests;

namespace Task.Desktop.Tests.TaskScreen;

public sealed class InboxZeroUiTests
{
    [Fact]
    public async global::System.Threading.Tasks.Task NativeWpf_ZeroFocusKeyboardSemanticsMotionAndExternalChanges()
    {
        // WPF has one Application per process; use the existing isolated UI-probe pattern.
        if (Environment.GetEnvironmentVariable("TASK_INBOX_ZERO_PROBE") != "1")
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            start.ArgumentList.Add("vstest"); start.ArgumentList.Add(typeof(InboxZeroUiTests).Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + GetType().FullName + "." + nameof(NativeWpf_ZeroFocusKeyboardSemanticsMotionAndExternalChanges));
            start.Environment["TASK_INBOX_ZERO_PROBE"] = "1";
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
                Assert.True(process.ExitCode == 0, await stdout + "\n" + await stderr);
            }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            return;
        }
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                Window? window = null;
                try
                {
                    var app = new global::System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Task.Desktop;component/Resources/Theme.xaml", UriKind.Relative) });
                    app.Resources["Task.BooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
                    foreach (var name in new[] { "IconKeyToGeometryConverter", "WindowWidthAtLeastConverter", "ResponsiveGridLengthConverter" })
                        app.Resources["Task." + name] = Activator.CreateInstance(typeof(InboxView).Assembly.GetType("Task.Desktop.Converters." + name)!);
                    var source = CreateTask("Последняя запись");
                    var client = new FakeInboxClient { PageResult = Page(source) };
                    using var model = new InboxViewModel(client, Capabilities);
                    await model.ActivateAsync();
                    var view = new InboxView { DataContext = model, MotionEnabled = () => false };
                    window = new Window { Content = view, Width = 900, Height = 650, Left = -20000, ShowInTaskbar = false };
                    window.Show(); window.Activate(); window.UpdateLayout();
                    var list = (ListBox)view.FindName("InboxList");
                    var panel = (FrameworkElement)view.FindName("InboxZeroPanel");
                    var create = (Button)view.FindName("InboxZeroCreateButton");
                    var heading = (TextBlock)view.FindName("InboxZeroHeading");
                    var capture = (TextBox)view.FindName("InboxCaptureTextBox");
                    Assert.True(list.Focus());
                    client.PageResult = Page();
                    await view.RefreshVisibleInboxAsync(); // Same authoritative refresh used by the timer.
                    await Dispatcher.Yield(DispatcherPriority.Background); window.UpdateLayout();
                    Assert.Equal(Visibility.Visible, panel.Visibility);
                    Assert.True(create.IsKeyboardFocused);
                    Assert.False(panel.HasAnimatedProperties);
                    Assert.Equal(AutomationHeadingLevel.Level2, AutomationProperties.GetHeadingLevel(heading));
                    Assert.Equal("Входящие разобраны", new TextBlockAutomationPeer(heading).GetName());
                    var peer = new ButtonAutomationPeer(create);
                    Assert.Equal("Добавить новую запись во входящие", peer.GetName());
                    ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    Assert.True(capture.IsKeyboardFocused);
                    Assert.True(capture.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
                    Assert.NotNull(Keyboard.FocusedElement);

                    client.PageResult = Page(source);
                    await view.RefreshVisibleInboxAsync(); window.UpdateLayout();
                    Assert.Equal(Visibility.Collapsed, panel.Visibility);
                    Assert.Equal(Visibility.Visible, list.Visibility);
                    view.MotionEnabled = () => true;
                    client.PageResult = Page();
                    await view.RefreshVisibleInboxAsync();
                    Assert.True(panel.HasAnimatedProperties);
                    await global::System.Threading.Tasks.Task.Delay(180);
                    Assert.Equal(1, panel.Opacity);

                    // Conversion closes the modal only after the write; refresh then restores focus.
                    client.PageResult = Page(source);
                    await view.RefreshVisibleInboxAsync();
                    await model.ConvertCommand.ExecuteAsync(model.SelectedItem);
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    model.Conversion!.DeadlineText = DateTime.Now.AddDays(1).ToString("dd.MM.yyyy HH:mm");
                    client.PatchResult = new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(source with { DeadlineAtUtc = DateTimeOffset.UtcNow.AddDays(1) }, 2, false);
                    await model.SaveConversionCommand.ExecuteAsync();
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    Assert.True(model.ShowInboxZero);
                    Assert.True(create.IsKeyboardFocused);

                    model.UpdateConnectivity(false); window.UpdateLayout();
                    Assert.Equal(Visibility.Collapsed, panel.Visibility);
                    Assert.False(model.ShowInboxZero);
                    // A recovery response is authoritative; an old cached zero cannot recover it.
                    await view.RefreshVisibleInboxAsync(); window.UpdateLayout();
                    Assert.True(model.ShowInboxZero);
                    completed.SetResult();
                }
                catch (Exception error) { completed.SetException(error); }
                finally { window?.Close(); dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }
}
