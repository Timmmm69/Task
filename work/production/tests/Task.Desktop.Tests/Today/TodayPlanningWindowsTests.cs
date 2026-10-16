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
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.Tests.Today;

public sealed partial class TodayViewModelTests
{
    [Fact]
    public async AsyncTask Planning_NativeWpfFormKeyboardCancelSaveReadOnlyAndCompactScrolling()
    {
        if (Environment.GetEnvironmentVariable("TASK_TODAY_PLANNING_PROBE") != "1")
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("vstest"); start.ArgumentList.Add(typeof(TodayViewModelTests).Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + typeof(TodayViewModelTests).FullName + "." + nameof(Planning_NativeWpfFormKeyboardCancelSaveReadOnlyAndCompactScrolling));
            start.Environment["TASK_TODAY_PLANNING_PROBE"] = "1";
            using var child = Process.Start(start)!;
            var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(50));
            Assert.True(child.ExitCode == 0, await output + await error); return;
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
                    var client = new PlanningClient(PlanningTask()); using var vm = PlanningVm(client); await vm.ActivateAsync();
                    var view = new TodayView { DataContext = vm };
                    window = new Window { Content = view, Width = 1120, Height = 900, Title = "Task · Сегодня · планирование" };
                    window.Show(); window.Activate(); PumpToday();
                    T Find<T>(string id) where T : DependencyObject => TodayDescendants<T>(view).Single(c => AutomationProperties.GetAutomationId(c) == id);
                    void Invoke(string id)
                    {
                        var button = Find<Button>(id); button.BringIntoView(); PumpToday();
                        Assert.True(button.IsEnabled); Assert.True(button.Focus());
                        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke(); PumpToday();
                    }
                    var date = Find<DatePicker>("TodayPlanningDate"); var time = Find<TextBox>("TodayPlanningTime");
                    var duration = Find<TextBox>("TodayPlanningDuration");
                    Assert.Equal("60", duration.Text); Assert.Null(date.SelectedDate);
                    date.SelectedDate = Today.ToDateTime(TimeOnly.MinValue); PumpToday();
                    time.BringIntoView(); PumpToday(); Assert.True(time.Focus());
                    var peer = new TextBoxAutomationPeer(time); Assert.Contains("ЧЧ:ММ", peer.GetName());
                    ((IValueProvider)peer.GetPattern(PatternInterface.Value)).SetValue("09:30"); PumpToday();
                    Assert.Equal("09:30", vm.Planning!.Time); Assert.Empty(client.Patches);
                    var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Escape)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                    time.RaiseEvent(escape); PumpToday(); Assert.True(escape.Handled);
                    Assert.Null(date.SelectedDate); Assert.Empty(time.Text); Assert.Empty(client.Patches);
                    date.SelectedDate = Today.ToDateTime(TimeOnly.MinValue); time.Text = "09:30"; duration.Text = "90"; PumpToday();
                    Invoke("TodaySavePlanning"); Assert.Single(client.Patches); Assert.Single(vm.TimelineItems);
                    Assert.Equal("09:30–11:00", vm.SelectedItem!.TimeText); Assert.Equal("90", duration.Text);
                    SaveTodayFrame(view, "today-planning-saved.png");
                    date.Text = "31.02.2026"; date.Focus();
                    duration.Focus(); PumpToday();
                    // DatePicker rejects an impossible date and restores its confirmed selection.
                    Assert.Equal(Today.ToDateTime(TimeOnly.MinValue), date.SelectedDate);
                    Assert.Equal(date.SelectedDate, vm.Planning!.Date); Assert.Single(client.Patches);
                    date.Text = "31.08.2026"; date.Focus(); duration.Focus(); PumpToday();
                    Assert.Null(vm.Planning.Error);
                    duration.Text = "10081"; PumpToday(); Assert.False(Find<Button>("TodaySavePlanning").IsEnabled);
                    Invoke("TodayCancelPlanning"); Assert.Equal("90", duration.Text);
                    vm.UpdateCapabilities(["Task.Read"]); PumpToday(); Assert.False(time.IsEnabled);
                    Assert.Equal("09:30", time.Text); Assert.False(Find<Button>("TodaySavePlanning").IsEnabled);
                    vm.UpdateCapabilities(["Task.Read", "Task.Update", "Task.ChangeStatus"]); vm.UpdateConnectivity(false); PumpToday();
                    Assert.False(time.IsEnabled); vm.UpdateConnectivity(true); PumpToday(); Assert.True(time.IsEnabled);
                    window.Width = 800; window.Height = 600; PumpToday();
                    duration.BringIntoView(); PumpToday(); Assert.True(duration.Focus());
                    duration.Text = "45"; PumpToday(); Invoke("TodaySavePlanning");
                    Assert.Equal(2, client.Patches.Count); Assert.Single(vm.TimelineItems);
                    Assert.Contains(TodayDescendants<ScrollViewer>(view), s => s.ScrollableHeight > 0);
                    SaveTodayFrame(view, "today-planning-compact.png");
                    await vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.InProgress); PumpToday();
                    Assert.Equal(client.Patches[1].ExpectedVersion + 1, Assert.Single(client.Transitions).ExpectedVersion);
                    done.SetResult();
                }
                catch (Exception error) { done.SetException(error); }
                finally { window?.Close(); app.Shutdown(); }
            });
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); await done.Task.WaitAsync(TimeSpan.FromSeconds(40));
    }
}
