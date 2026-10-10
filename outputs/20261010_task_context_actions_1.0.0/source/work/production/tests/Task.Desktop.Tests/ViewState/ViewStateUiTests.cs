using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Task.Desktop.Infrastructure;
using Task.Desktop.Views;

namespace Task.Desktop.Tests.ViewStateTests;

public sealed class ViewStateUiTests
{
    [Fact]
    public async System.Threading.Tasks.Task NativeWpf_RestartResizeSortPermissionsAndSubscriptionLifetime()
    {
        if (Environment.GetEnvironmentVariable("TASK_VIEW_STATE_PROBE") != "1")
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, WindowStyle = ProcessWindowStyle.Hidden };
            start.ArgumentList.Add("vstest"); start.ArgumentList.Add(typeof(ViewStateUiTests).Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + GetType().FullName + "." + nameof(NativeWpf_RestartResizeSortPermissionsAndSubscriptionLifetime));
            start.Environment["TASK_VIEW_STATE_PROBE"] = "1";
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(50)); Assert.True(process.ExitCode == 0, await stdout + "\n" + await stderr); }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            return;
        }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                var root = Path.Combine(Path.GetTempPath(), "Task-view-state-ui", Guid.NewGuid().ToString("N"));
                Window? window = null;
                try
                {
                    var app = new Task.Desktop.App(); app.InitializeComponent();
                    var user = Guid.NewGuid();
                    await using (var seed = new ViewStateStore(root, "server/org", user, "device"))
                    {
                        await seed.Ready;
                        seed.SetWidth("work-links/v1/type", 180.5, 60, 500);
                        seed.SetSort("work-links/v1", new("Title", true));
                        seed.SetExpanded("projects/v1/tasks", false);
                        seed.SetExpanded("projects/v1/members", true);
                        await seed.FlushAsync();
                    }
                    await using var store = new ViewStateStore(root, "server/org", user, "device", TimeSpan.FromMilliseconds(200));
                    await store.Ready;
                    var links = new ObjectLinksView { DataContext = new { Items = new[] { new Link("Альфа"), new Link("Бета") } } };
                    ViewState.SetSurface(links, "work-links/v1");
                    var collapsed = new Expander { Header = "Связанные задачи", IsExpanded = true, Content = new TextBlock { Text = "Разрешённое содержимое" } };
                    ViewState.SetSection(collapsed, "projects/v1/tasks");
                    var hidden = new Expander { Header = "Участники", IsExpanded = false, Visibility = Visibility.Collapsed };
                    ViewState.SetSection(hidden, "projects/v1/members");
                    var panel = new StackPanel(); panel.Children.Add(links); panel.Children.Add(collapsed); panel.Children.Add(hidden);
                    window = new Window { Content = panel, Width = 900, Height = 600, Left = -20000, ShowInTaskbar = false };
                    ViewState.SetStore(window, store);
                    window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                    var grid = Descendants<DataGrid>(links).Single();
                    Assert.Equal(180.5, grid.Columns[0].Width.Value);
                    Assert.True(grid.Columns[0].Width.IsAbsolute);
                    Assert.False(collapsed.IsExpanded);
                    Assert.False(hidden.IsExpanded); Assert.False(hidden.IsVisible);
                    Assert.Equal("Бета", ((Link)grid.Items[0]).Title);
                    Assert.Equal(System.ComponentModel.ListSortDirection.Descending, grid.Columns[1].SortDirection);
                    hidden.Visibility = Visibility.Visible; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert.True(hidden.IsExpanded); // Restore only after permission/visibility allows it.
                    hidden.Visibility = Visibility.Collapsed; Assert.False(hidden.IsVisible);

                    // Native column header invocation follows the same sorting route as a click.
                    var titleHeader = Descendants<DataGridColumnHeader>(grid).Single(header => ReferenceEquals(header.Column, grid.Columns[1]));
                    Action click = () => typeof(DataGridColumnHeader).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(titleHeader, null);
                    click(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert.Equal(new ViewSort("Title", false), store.Sort("work-links/v1"));
                    Assert.Equal("Альфа", ((Link)grid.Items[0]).Title);
                    click(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert.Equal(new ViewSort("Title", true), store.Sort("work-links/v1"));
                    links.DataContext = new { Items = new[] { new Link("Гамма"), new Link("Дельта") } };
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert.Equal("Дельта", ((Link)grid.Items[0]).Title);

                    // Auto-measure and programmatic widths must not become user preferences.
                    grid.Columns[0].Width = DataGridLength.Auto;
                    grid.Columns[1].Width = new DataGridLength(1, DataGridLengthUnitType.Star);
                    window.UpdateLayout(); Assert.Equal(180.5, store.Width("work-links/v1/type", 60, 500));
                    Assert.Null(store.Width("work-links/v1/title", 100, 1600));
                    var header = Descendants<DataGridColumnHeader>(grid).Single(item => ReferenceEquals(item.Column, grid.Columns[0]));
                    var thumb = (Thumb)header.Template.FindName("PART_RightHeaderGripper", header);
                    Assert.NotNull(thumb);
                    thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                    for (int pixel = 190; pixel <= 220; pixel++) grid.Columns[0].Width = new DataGridLength(pixel);
                    thumb.RaiseEvent(new DragCompletedEventArgs(30, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
                    Assert.Equal(220, store.Width("work-links/v1/type", 60, 500));
                    Assert.Null(store.Width("work-links/v1/title", 100, 1600));
                    // A real drag also persists a Star column in logical units.
                    var titleThumb = (Thumb)titleHeader.Template.FindName("PART_RightHeaderGripper", titleHeader);
                    titleThumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                    titleThumb.RaiseEvent(new DragDeltaEventArgs(-30, 0) { RoutedEvent = Thumb.DragDeltaEvent });
                    window.UpdateLayout();
                    titleThumb.RaiseEvent(new DragCompletedEventArgs(-30, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
                    Assert.Equal(grid.Columns[1].ActualWidth, store.Width("work-links/v1/title", 100, 1600));
                    // The left gripper belongs visually to title but resizes type.
                    var left = (Thumb)titleHeader.Template.FindName("PART_LeftHeaderGripper", titleHeader);
                    left.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                    grid.Columns[0].Width = new DataGridLength(240);
                    left.RaiseEvent(new DragCompletedEventArgs(20, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
                    Assert.Equal(240, store.Width("work-links/v1/type", 60, 500));
                    // Cancelling and non-resizable columns do not overwrite preferences.
                    thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                    grid.Columns[0].Width = new DataGridLength(260);
                    thumb.RaiseEvent(new DragCompletedEventArgs(20, 0, true) { RoutedEvent = Thumb.DragCompletedEvent });
                    Assert.Equal(240, store.Width("work-links/v1/type", 60, 500));
                    grid.Columns[0].CanUserResize = false;
                    thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                    grid.Columns[0].Width = new DataGridLength(280);
                    thumb.RaiseEvent(new DragCompletedEventArgs(20, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
                    Assert.Equal(240, store.Width("work-links/v1/type", 60, 500));
                    grid.Columns[0].CanUserResize = true;
                    collapsed.IsExpanded = true;
                    await store.FlushAsync();

                    // Removing controls detaches descriptor and routed-event subscriptions.
                    panel.Children.Remove(links); panel.Children.Remove(collapsed);
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    collapsed.IsExpanded = false;
                    grid.Columns[0].Width = new DataGridLength(300);
                    click(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert.True(store.Expanded("projects/v1/tasks"));
                    Assert.Equal(240, store.Width("work-links/v1/type", 60, 500));
                    Assert.Equal(new ViewSort("Title", true), store.Sort("work-links/v1"));
                    panel.Children.Add(links); panel.Children.Add(collapsed);
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                    Assert.Equal(240, grid.Columns[0].Width.Value); Assert.True(collapsed.IsExpanded);

                    // Render the real Today view at two raster DPI values; logical UI values stay fixed.
                    var today = new TodayView(); panel.Children.Clear(); window.Content = today;
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                    var queue = (Expander)today.FindName("TodayQueueRegionExpansion");
                    var queuePeer = new ExpanderAutomationPeer(queue);
                    Assert.Equal("Несрочные и просроченные", queuePeer.GetName());
                    ((IExpandCollapseProvider)queuePeer.GetPattern(PatternInterface.ExpandCollapse)).Collapse();
                    await store.FlushAsync();
                    Assert.False(store.Expanded("today/v1/queue"));
                    window.Content = new TodayView(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                    Assert.False(((Expander)((TodayView)window.Content).FindName("TodayQueueRegionExpansion")).IsExpanded);
                    var evidence = Environment.GetEnvironmentVariable("TASK_VIEW_STATE_EVIDENCE");
                    if (!string.IsNullOrEmpty(evidence))
                    {
                        Directory.CreateDirectory(evidence);
                        foreach (var dpi in new[] { 96, 144 })
                        {
                            var target = new RenderTargetBitmap((int)(today.ActualWidth * dpi / 96), (int)(today.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
                            target.Render((Visual)window.Content);
                            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
                            using var file = File.Create(Path.Combine(evidence, "today-" + dpi + ".png")); encoder.Save(file);
                        }
                    }
                    // A real close must flush pending state before Closed, without blocking the dispatcher.
                    ViewState.Attach(window, store);
                    store.SetExpanded("today/v1/inspector", false);
                    var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    window.Closed += (_, _) => closed.SetResult();
                    window.Close(); await closed.Task.WaitAsync(TimeSpan.FromSeconds(5)); window = null;
                    await using var restart = new ViewStateStore(root, "server/org", user, "device"); await restart.Ready;
                    Assert.False(restart.Expanded("today/v1/inspector"));
                    completion.SetResult();
                }
                catch (Exception error) { completion.SetException(error); }
                finally
                {
                    window?.Close();
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                    if (Directory.Exists(root)) Directory.Delete(root, true);
                }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(40)); }
        finally
        {
            // The testhost must not unload managed WPF callbacks while STA teardown is running.
            Assert.True(await System.Threading.Tasks.Task.Run(() => thread.Join(TimeSpan.FromSeconds(10))),
                "WPF dispatcher thread did not finish teardown.");
        }
    }
    public sealed record Link(string Title) { public string TypeLabel => "Связь"; }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
