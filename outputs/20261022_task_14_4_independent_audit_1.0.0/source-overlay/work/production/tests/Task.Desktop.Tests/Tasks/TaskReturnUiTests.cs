using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Tests.TaskScreen;

public sealed class TaskReturnUiTests
{
    [Fact]
    public async System.Threading.Tasks.Task NativeWpf_ReturnActionConfirmationFocusAndServerResult()
    {
        if (Environment.GetEnvironmentVariable("TASK_RETURN_UI_CHILD") != "1")
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("vstest"); start.ArgumentList.Add(GetType().Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + GetType().FullName + "." + nameof(NativeWpf_ReturnActionConfirmationFocusAndServerResult));
            start.Environment["TASK_RETURN_UI_CHILD"] = "1";
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
                try
                {
                    var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Task.Desktop;component/Resources/Theme.xaml", UriKind.Relative) });
                    app.Resources["Task.BooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
                    foreach (var name in new[] { "IconKeyToGeometryConverter", "ShellNavigationWidthConverter", "WindowWidthAtLeastConverter", "ResponsiveGridLengthConverter", "TimelineLaneMarginConverter", "CalendarLaneWidthConverter", "CalendarLaneLeftConverter" })
                        app.Resources["Task." + name] = Activator.CreateInstance(typeof(MainWindow).Assembly.GetType("Task.Desktop.Converters." + name)!);
                    var source = ActionTasksClient.Source("Результат на проверке") with { Status = DesktopTaskStatus.Review, Version = 7 };
                    var client = new TasksViewModelTests.ReturnClient(source);
                    using var tasks = new TasksViewModel(client, ["Task.Read", "Task.ChangeStatus"]);
                    using var shell = new MainWindowViewModel(new Uri("https://fixture.invalid"), null, tasks);
                    shell.SelectedSection = shell.Sections.Single(s => s.Route == "tasks"); await tasks.ActivateAsync();
                    window = new MainWindow(shell) { Left = 20, Top = 20, Width = 1100, Height = 900, ShowInTaskbar = false };
                    window.Show(); window.Activate();
                    ((Expander)window.FindName("TaskInspectorExpander")).IsExpanded = true;
                    await Drain(window);
                    var button = Find<Button>(window, "ReturnForReworkButton");
                    Assert.True(button.IsVisible); Assert.True(button.IsEnabled); Assert.True(button.IsTabStop);
                    Assert.Equal("Вернуть на доработку", AutomationProperties.GetName(button));
                    Assert.False(Find<Button>(window, "StartTaskButton").IsVisible);
                    button.BringIntoView(); Assert.True(button.Focus()); Assert.True(button.IsKeyboardFocused);
                    Assert.NotNull(button.FocusVisualStyle);
                    ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke(); await Drain(window);
                    var confirm = Find<Button>(window, "ConfirmTransitionButton");
                    Assert.Equal("Вернуть", confirm.Content);
                    Assert.False(Find<TextBox>(window, "TransitionReasonTextBox").IsVisible);
                    Assert.Same(confirm, Keyboard.FocusedElement);
                    Assert.True(confirm.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
                    Assert.Equal("Назад", Assert.IsType<Button>(Keyboard.FocusedElement).Content);
                    ((IInvokeProvider)new ButtonAutomationPeer((Button)Keyboard.FocusedElement).GetPattern(PatternInterface.Invoke)).Invoke();
                    await Drain(window);
                    Assert.Same(button, Keyboard.FocusedElement);
                    Assert.Equal(0, client.Inner.TransitionCallCount);
                    var complete = Find<Button>(window, "CompleteTaskButton");
                    Assert.True(complete.Focus());
                    ((IInvokeProvider)new ButtonAutomationPeer(complete).GetPattern(PatternInterface.Invoke)).Invoke();
                    await Drain(window);
                    Assert.Same(confirm, Keyboard.FocusedElement);
                    Assert.True(confirm.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
                    ((IInvokeProvider)new ButtonAutomationPeer((Button)Keyboard.FocusedElement).GetPattern(PatternInterface.Invoke)).Invoke();
                    await Drain(window);
                    Assert.Same(complete, Keyboard.FocusedElement);
                    Assert.True(button.Focus());
                    ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
                    await Drain(window);
                    Assert.Same(confirm, Keyboard.FocusedElement);
                    await Capture(window, "return-confirmation");
                    var release = new TaskCompletionSource<DesktopTaskWriteResult<DesktopTaskDto>>();
                    client.Inner.TransitionHandler = _ => release.Task;
                    ((IInvokeProvider)new ButtonAutomationPeer(confirm).GetPattern(PatternInterface.Invoke)).Invoke(); await Drain(window);
                    Assert.False(confirm.IsEnabled); Assert.Equal("Сохранение…", confirm.Content);
                    Assert.Equal(DesktopTaskStatus.Review, tasks.SelectedDetails!.Source.Status);
                    release.SetResult(new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(source with { Status = DesktopTaskStatus.InProgress, Version = 8 }, 8, false));
                    await Drain(window);
                    Assert.Equal(DesktopTaskStatus.InProgress, tasks.SelectedDetails!.Source.Status);
                    Assert.False(button.IsVisible);
                    Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(Find<TextBlock>(window, "TasksAnnouncement")));
                    await Capture(window, "return-success");
                    done.SetResult();
                }
                catch (Exception error) { done.SetException(error); }
                finally { window?.CloseAfterModeSwitch(); dispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(45));
        Assert.True(await System.Threading.Tasks.Task.Run(() => thread.Join(TimeSpan.FromSeconds(5))));
    }
    private static async System.Threading.Tasks.Task Drain(Window window) { await Dispatcher.Yield(DispatcherPriority.Background); window.UpdateLayout(); await System.Threading.Tasks.Task.Delay(100); }
    private static T Find<T>(DependencyObject root, string id) where T : FrameworkElement => Descendants<T>(root).Single(x => AutomationProperties.GetAutomationId(x) == id);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T match) yield return match; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
    private static async System.Threading.Tasks.Task Capture(Window window, string name)
    {
        var path = Environment.GetEnvironmentVariable("TASK_RETURN_EVIDENCE"); if (string.IsNullOrEmpty(path)) return;
        Directory.CreateDirectory(path);
        foreach (var size in new[] { (1100d, 900d), (800d, 800d), (800d, 600d), (800d, 480d) })
        {
            window.Width = size.Item1; window.Height = size.Item2; await Drain(window);
            if (name == "return-confirmation")
            {
                var confirm = Find<Button>(window, "ConfirmTransitionButton");
                confirm.Focus(); confirm.BringIntoView();
            }
            else (Keyboard.FocusedElement as FrameworkElement)?.BringIntoView();
            await Drain(window);
            var visual = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
            var bitmap = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(path, $"{name}-{size.Item1}x{size.Item2}.png")); encoder.Save(file);
            if (name == "return-confirmation")
            {
                var confirm = Find<Button>(window, "ConfirmTransitionButton");
                var bounds = confirm.TransformToAncestor(window).TransformBounds(new Rect(confirm.RenderSize));
                var inspector = (Expander)window.FindName("TaskInspectorExpander");
                var scroll = (ScrollViewer)window.FindName("TaskDetailsArea");
                File.WriteAllText(Path.Combine(path, $"layout-{size.Item1}x{size.Item2}.json"), System.Text.Json.JsonSerializer.Serialize(new
                {
                    contentHeight = ((Grid)window.FindName("TasksContentGrid")).ActualHeight,
                    inspectorHeight = inspector.ActualHeight, inspector.MaxHeight,
                    scrollHeight = scroll.ActualHeight, scroll.ViewportHeight, scroll.ExtentHeight, scroll.VerticalOffset,
                    confirmationBounds = bounds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                }));
                foreach (var point in new[] { new Point(bounds.Left + 3, bounds.Top + 3), new Point(bounds.Right - 3, bounds.Bottom - 3) })
                {
                    var hit = window.InputHitTest(point) as DependencyObject;
                    Assert.True(ReferenceEquals(hit, confirm) || hit is not null && confirm.IsAncestorOf(hit),
                        $"Confirmation is clipped at {size}: {bounds}; hit={hit?.GetType().Name ?? "null"}");
                }
            }
        }
    }
}
