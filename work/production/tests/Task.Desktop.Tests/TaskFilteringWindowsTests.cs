using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Tests;

[Collection("CompletionWpfUi")]
public sealed class TaskFilteringWindowsTests
{
    [Fact]
    public async System.Threading.Tasks.Task Native_FourFiltersKeyboardPaginationAndSmallWindow()
    {
        if (Environment.GetEnvironmentVariable("TASK_FILTER_UI_CHILD") != "1")
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("vstest"); start.ArgumentList.Add(GetType().Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + GetType().FullName + "." + nameof(Native_FourFiltersKeyboardPaginationAndSmallWindow));
            start.Environment["TASK_FILTER_UI_CHILD"] = "1";
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60)); Assert.True(process.ExitCode == 0, await stdout + await stderr); }
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
                    var now = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
                    var first = new DesktopTaskDto(Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.NewGuid(), 1, now,
                        now, "Проверить фильтры", Guid.NewGuid(), DesktopTaskStatus.InProgress,
                        DesktopTaskPriority.High, null, null, [], [], null);
                    var peer = first with { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), Title = "Акт проверки" };
                    var second = first with { Id = Guid.NewGuid(), Priority = DesktopTaskPriority.Critical };
                    var client = new TaskScreen.TasksViewModelTests.FakeTasksApiClient { DetailResult = new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(first) };
                    client.EnqueuePage(new DesktopTasksApiResult<DesktopTaskPage>.Succeeded(new([first, peer], "next", null)));
                    client.EnqueuePage(new DesktopTasksApiResult<DesktopTaskPage>.Succeeded(new([second], "remaining", null)));
                    using var tasks = new TasksViewModel(client, ["Task.Read"]);
                    using var shell = new MainWindowViewModel(new Uri("https://fixture.invalid"), null, tasks);
                    shell.SelectedSection = shell.Sections.Single(section => section.Route == "tasks");
                    await tasks.ActivateAsync();
                    window = new MainWindow(shell) { ShowInTaskbar = false, ShowActivated = false };
                    window.Show(); await Drain(window);
                    var status = Find<ComboBox>(window, "TasksStatusFilter");
                    var project = Find<ComboBox>(window, "TasksProjectFilter");
                    var priority = Find<ComboBox>(window, "TasksPriorityFilter");
                    var deadline = Find<ComboBox>(window, "TasksDeadlineFilter");
                    var sort = Find<ComboBox>(window, "TasksSortOrder");
                    Assert.All(new[] { status, project, priority, deadline, sort }, combo => Assert.False(string.IsNullOrEmpty(AutomationProperties.GetName(combo))));
                    Assert.Equal(8, sort.Items.Count);
                    var list = Find<ListBox>(window, "TasksList");
                    var selected = tasks.SelectedItem;
                    var reads = client.DetailCallCount;
                    status.SelectedValue = DesktopTaskStatus.InProgress;
                    priority.SelectedValue = DesktopTaskPriority.High;
                    deadline.SelectedValue = TaskDeadlineFilter.NoDeadline;
                    await Drain(window);
                    Assert.Same(selected, tasks.SelectedItem);
                    Assert.Same(selected, list.SelectedItem);
                    Assert.Equal(reads, client.DetailCallCount);
                    var details = tasks.SelectedDetails;
                    foreach (var option in tasks.SortOrders)
                    {
                        sort.SelectedValue = option.Value; await Drain(window);
                        Assert.Same(selected, tasks.SelectedItem); Assert.Same(selected, list.SelectedItem);
                        Assert.Same(details, tasks.SelectedDetails);
                    }
                    Assert.Equal(reads, client.DetailCallCount);
                    // WPF must preserve the selected ID even when project options are rebuilt.
                    project.SelectedItem = tasks.ProjectFilters.Single(option => !option.IsAll);
                    priority.SelectedValue = DesktopTaskPriority.Critical;
                    await Drain(window);
                    Assert.True(tasks.IsFilteredEmpty);
                    var more = Find<Button>(window, "TasksLoadMoreButton");
                    Assert.True(more.IsVisible && more.IsEnabled);
                    await tasks.LoadMoreCommand.ExecuteAsync(); await Drain(window);
                    Assert.Equal(second.Id, Assert.Single(tasks.Items).Id);
                    Assert.False(tasks.SelectedProjectFilter.IsAll);
                    await tasks.ResetFiltersCommand.ExecuteAsync(); await Drain(window);
                    Assert.Null(status.SelectedValue); Assert.Null(priority.SelectedValue);
                    Assert.Equal(TaskDeadlineFilter.All, deadline.SelectedValue);
                    Assert.True(((TaskProjectFilter)project.SelectedItem).IsAll);
                    Assert.Equal(TaskSortOrder.TitleDescending, sort.SelectedValue);
                    status.SelectedValue = DesktopTaskStatus.InProgress; // Keep reset enabled for the Tab traversal.
                    // Logical viewports for 1920x1080 at 125/150/200%, plus the minimum window.
                    // The physical desktop DPI is recorded separately; these are layout equivalents.
                    foreach (var size in new[] { (800d, 480d), (1200d, 900d), (1536d, 864d), (1280d, 720d), (960d, 540d) })
                    {
                        window.Width = size.Item1; window.Height = size.Item2; await Drain(window);
                        foreach (var combo in new[] { status, project, priority, deadline, sort })
                        {
                            var origin = combo.TranslatePoint(new Point(), window);
                            Assert.True(combo.IsVisible && combo.ActualHeight > 0);
                            Assert.InRange(origin.X + combo.ActualWidth, 0, window.ActualWidth);
                            Assert.InRange(origin.Y + combo.ActualHeight, 0, window.ActualHeight);
                        }
                        Assert.True(list.ActualHeight >= 40, $"List viewport is only {list.ActualHeight} DIP; " + string.Join("; ", new[] { status, project, priority, deadline, sort }.Select(combo => $"{AutomationProperties.GetAutomationId(combo)}={combo.TranslatePoint(new Point(), window)} {combo.ActualWidth}x{combo.ActualHeight}")));
                        Assert.True(more.IsVisible && more.IsEnabled);
                        Assert.InRange(more.TranslatePoint(new Point(0, more.ActualHeight), window).Y, 0, window.ActualHeight);
                        Assert.True(status.Focus());
                        foreach (var next in new Control[] { project, priority, deadline, sort, Find<Button>(window, "TasksResetFilters") })
                        {
                            Assert.True(((UIElement)Keyboard.FocusedElement).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
                            Assert.Same(next, Keyboard.FocusedElement);
                        }
                        var evidence = Environment.GetEnvironmentVariable("TASK_FILTER_EVIDENCE");
                        if (!string.IsNullOrEmpty(evidence))
                        {
                            Directory.CreateDirectory(evidence);
                            var visual = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
                            var bitmap = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                            bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            using var file = File.Create(Path.Combine(evidence, $"filters-{size.Item1}.png")); encoder.Save(file);
                        }
                    }
                    Assert.Equal(2, client.PageCallCount);
                    // Measure the complete synchronous filter/sort/projection on this WPF dispatcher.
                    var bulk = Enumerable.Range(1, 10000).Select(i => first with
                    {
                        Id = Guid.Parse($"00000000-0000-0000-0001-{i:000000000000}"),
                        Title = $"Задача {10000 - i:00000}", Priority = (DesktopTaskPriority)(i % 4),
                        UpdatedAtUtc = now.AddMinutes(-i), DeadlineAtUtc = i % 5 == 0 ? null : now.AddMinutes(i),
                    }).ToArray();
                    client.EnqueuePage(new DesktopTasksApiResult<DesktopTaskPage>.Succeeded(new(bulk, null, null)));
                    await tasks.RefreshAsync(); await tasks.ResetFiltersCommand.ExecuteAsync(); await Drain(window);
                    var timings = new List<string>();
                    foreach (var option in tasks.SortOrders)
                    {
                        var watch = Stopwatch.StartNew(); tasks.SelectedSortOrder = option.Value; watch.Stop();
                        timings.Add($"{option.Value}: {watch.Elapsed.TotalMilliseconds:F2} ms");
                        Assert.Equal(10000, tasks.Items.Count);
                        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"10k local rows took {watch.Elapsed}");
                        await Drain(window);
                        Assert.True(list.ItemContainerGenerator.ContainerFromIndex(9999) is null, "List must remain virtualized");
                    }
                    var output = Environment.GetEnvironmentVariable("TASK_FILTER_EVIDENCE");
                    if (!string.IsNullOrEmpty(output))
                        File.WriteAllLines(Path.Combine(output, "sorting-performance.txt"),
                            new[] { $"WPF dispatcher, 10000 rows; physical DPI scale: {VisualTreeHelper.GetDpi(window).DpiScaleX}; OS: {Environment.OSVersion}; .NET: {Environment.Version}",
                                "Times measure synchronous filter/sort/projection and binding notification; rendering is drained after each sample. One sample per order, no percentile claim." }.Concat(timings));
                    done.SetResult();
                }
                catch (Exception error) { done.SetException(error); }
                finally { window?.CloseAfterModeSwitch(); dispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        try { await done.Task.WaitAsync(TimeSpan.FromSeconds(45)); }
        finally { Assert.True(await System.Threading.Tasks.Task.Run(() => thread.Join(TimeSpan.FromSeconds(5)))); }
    }

    private static async System.Threading.Tasks.Task Drain(Window window)
    { await Dispatcher.Yield(DispatcherPriority.Background); window.UpdateLayout(); await System.Threading.Tasks.Task.Delay(50); }

    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject
    {
        if (root is T match && (AutomationProperties.GetAutomationId(root) == id || root is FrameworkElement element && element.Name == id)) return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            try { return Find<T>(VisualTreeHelper.GetChild(root, index), id); }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException(id);
    }
}
