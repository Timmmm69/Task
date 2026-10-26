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
using Task.Desktop.Views;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.Tests.Today;

[Collection("Employee picker native WPF")]
public sealed partial class TodayViewModelTests
{
    [Fact]
    public async AsyncTask QuickActions_NativeWpfButtonsConfirmationKeyboardAndReadOnly()
    {
        if (Environment.GetEnvironmentVariable("TASK_TODAY_ACTIONS_PROBE") != "1")
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("vstest"); start.ArgumentList.Add(typeof(TodayViewModelTests).Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + typeof(TodayViewModelTests).FullName + "." + nameof(QuickActions_NativeWpfButtonsConfirmationKeyboardAndReadOnly));
            start.Environment["TASK_TODAY_ACTIONS_PROBE"] = "1";
            using var child = Process.Start(start)!;
            var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(50));
            Assert.True(child.ExitCode == 0, await output + await error);
            return;
        }

        var done = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Task.Desktop;component/Resources/Theme.xaml", UriKind.Relative) });
            app.Resources.Add("Task.IconKeyToGeometryConverter", new Task.Desktop.Converters.IconKeyToGeometryConverter());
            app.Resources.Add("Task.TimelineLaneMarginConverter", new Task.Desktop.Converters.TimelineLaneMarginConverter());
            app.Resources.Add("Task.BooleanToVisibilityConverter", new BooleanToVisibilityConverter());
            app.Resources.Add("Task.WindowWidthAtLeastConverter", new Task.Desktop.Converters.WindowWidthAtLeastConverter());
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            app.Dispatcher.BeginInvoke(async () =>
            {
                Window? window = null;
                try
                {
                    var task = QuickTask(); var client = new QuickClient(task);
                    var calendar = new FakeCalendarClient(); calendar.ScheduleResults.Enqueue(Schedule([AllDayEvent(Today, "Встреча")]));
                    using var vm = QuickVm(client, calendar); await vm.ActivateAsync();
                    await vm.SelectItemCommand.ExecuteAsync(Assert.Single(vm.QueueItems));
                    var view = new TodayView { DataContext = vm };
                    window = new Window { Content = view, Width = 1120, Height = 900, Title = "Task · Сегодня · быстрые действия" };
                    window.Show(); window.Activate(); PumpToday();
                    Button Button(string id) => TodayDescendants<Button>(view).Single(b => AutomationProperties.GetAutomationId(b) == id);
                    void Invoke(Button button)
                    {
                        button.BringIntoView(); PumpToday(); Assert.True(button.IsEnabled); Assert.True(button.Focus());
                        var peer = new ButtonAutomationPeer(button);
                        Assert.False(string.IsNullOrWhiteSpace(peer.GetName()));
                        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke(); PumpToday();
                    }

                    var start = Button("TodayStartButton"); Assert.True(start.IsVisible);
                    SaveTodayFrame(view, "today-new.png"); Invoke(start);
                    Assert.Single(client.Commands); Assert.False(start.IsVisible); Assert.True(Button("TodayReviewButton").IsVisible);
                    Assert.True(Button("TodayOpenSelectedButton").IsKeyboardFocused);
                    Invoke(Button("TodayCompleteButton"));
                    var confirm = Button("TodayConfirmTransitionButton"); Assert.True(confirm.IsVisible); Assert.True(confirm.IsKeyboardFocused);
                    SaveTodayFrame(view, "today-confirm.png");
                    var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Escape)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                    confirm.RaiseEvent(escape); PumpToday(); Assert.True(escape.Handled); Assert.False(confirm.IsVisible); Assert.Single(client.Commands);
                    Assert.True(Button("TodayCompleteButton").IsKeyboardFocused);
                    window.Width = 800; window.Height = 600; PumpToday();
                    Invoke(Button("TodayReviewButton")); Assert.Equal("На проверке", vm.SelectedItem!.StatusText);
                    SaveTodayFrame(view, "today-review-compact.png");
                    vm.UpdateConnectivity(false); PumpToday(); Assert.False(Button("TodayCompleteButton").IsEnabled);
                    vm.UpdateConnectivity(true); Invoke(Button("TodayCompleteButton")); Invoke(confirm);
                    Assert.Empty(vm.QueueItems); Assert.Equal(3, client.Commands.Count);
                    Assert.False(Button("TodayStartButton").IsVisible); Assert.False(confirm.IsVisible);
                    Assert.False(vm.ShowTaskActions); Assert.True(Button("TodayOpenSelectedButton").IsVisible);
                    Assert.Contains("Завершена", TodayDescendants<TextBlock>(view).Single(t => AutomationProperties.GetAutomationId(t) == "TodayActionMessage").Text);
                    SaveTodayFrame(view, "today-completed-event.png");
                    done.SetResult();
                }
                catch (Exception error) { done.SetException(error); }
                finally { window?.Close(); app.Shutdown(); }
            });
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(40));
    }

    private static void PumpToday() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> TodayDescendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in TodayDescendants<T>(child)) yield return nested;
        }
    }
    private static void SaveTodayFrame(FrameworkElement view, string name)
    {
        if (Environment.GetEnvironmentVariable("TASK_TODAY_ACTIONS_EVIDENCE") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name)); encoder.Save(file);
    }
}
