using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using Task.Desktop.ViewModels;
using Task.Desktop.Views;

namespace Task.Desktop.Tests.Work;

public sealed class ExplorerDropViewTests
{
    [Fact]
    public async System.Threading.Tasks.Task NativeWpfFeedbackLeaveCancelAndKeyboardAlternatives()
    {
        // WPF Application can be created only once per process. Isolate this probe
        // from the other native-window tests and their application shutdowns.
        if (Environment.GetEnvironmentVariable("TASK_EXPLORER_DROP_PROBE") != "1")
        {
            var start = new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            };
            start.ArgumentList.Add("vstest"); start.ArgumentList.Add(typeof(ExplorerDropViewTests).Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + typeof(ExplorerDropViewTests).FullName + "." + nameof(NativeWpfFeedbackLeaveCancelAndKeyboardAlternatives));
            start.Environment["TASK_EXPLORER_DROP_PROBE"] = "1";
            using var process = System.Diagnostics.Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
                Assert.True(process.ExitCode == 0, await stdout + "\n" + await stderr);
            }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            return;
        }
        var completed = new System.Threading.Tasks.TaskCompletionSource<bool>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                var app = System.Windows.Application.Current ?? new System.Windows.Application();
                if (!app.Resources.Contains("Task.Shell.NavigationSurface"))
                {
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Task.Desktop;component/Resources/Theme.xaml", UriKind.Relative) });
                    app.Resources["Task.BooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
                    foreach (var name in new[] { "IconKeyToGeometryConverter", "ShellNavigationWidthConverter", "WindowWidthAtLeastConverter", "ResponsiveGridLengthConverter" })
                        app.Resources["Task." + name] = Activator.CreateInstance(typeof(WorkHubView).Assembly.GetType("Task.Desktop.Converters." + name)!);
                }
                var (client, state) = PathClientProxy.Make();
                var folder = new Task.Desktop.Work.DesktopCatalogItem(Guid.NewGuid(), 1, "Target", "virtual_folder", null, null, "active"); state.Items.Add(folder);
                using var vm = new WorkHubViewModel(client, ["FileCatalog.Read", "FileCatalog.Create", "FileLocation.Update"]);
                vm.Activate(WorkHubArea.Catalog);
                var view = new WorkHubView { DataContext = vm };
                window = new Window { Content = view, Width = 1000, Height = 700, ShowInTaskbar = false, WindowStyle = WindowStyle.ToolWindow };
                window.Show(); window.UpdateLayout();
                var area = (FrameworkElement)view.FindName("CatalogDropArea");
                var tree = FindVisual<TreeView>(view); tree.UpdateLayout();
                var container = (TreeViewItem)tree.ItemContainerGenerator.ContainerFromIndex(0);
                var data = new DataObject(); data.SetData(DataFormats.FileDrop, new[] { @"C:\data\a.txt" });
                var args = Drag(data, area); args.RoutedEvent = DragDrop.PreviewDragOverEvent;
                area.RaiseEvent(args); Assert.NotEqual(DragDropEffects.None, args.Effects);
                Assert.Single(AdornerLayer.GetAdornerLayer(area).GetAdorners(area));
                // A second over reuses the same adorner, with no layout mutation/flicker.
                var first = AdornerLayer.GetAdornerLayer(area).GetAdorners(area)[0];
                area.RaiseEvent(Drag(data, area, DragDrop.PreviewDragOverEvent));
                Assert.Same(first, AdornerLayer.GetAdornerLayer(area).GetAdorners(area)[0]);
                container.RaiseEvent(Drag(data, container, DragDrop.PreviewDragOverEvent));
                Assert.Single(AdornerLayer.GetAdornerLayer(container).GetAdorners(container));
                // Escape sends leave even while the point is inside: dispatcher cleanup must run.
                area.RaiseEvent(Drag(data, area, DragDrop.DragLeaveEvent)); Pump();
                Assert.Null(AdornerLayer.GetAdornerLayer(area).GetAdorners(area));
                Assert.Null(AdornerLayer.GetAdornerLayer(container).GetAdorners(container));
                Assert.Equal(0, state.Creates);
                // A sibling leave/over pair must not remove the overlay between targets.
                area.RaiseEvent(Drag(data, area, DragDrop.PreviewDragOverEvent));
                area.RaiseEvent(Drag(data, area, DragDrop.DragLeaveEvent));
                area.RaiseEvent(Drag(data, area, DragDrop.PreviewDragOverEvent)); Pump();
                Assert.Single(AdornerLayer.GetAdornerLayer(area).GetAdorners(area));
                vm.UpdateConnectivity(false);
                var offline = Drag(data, area, DragDrop.PreviewDragOverEvent); area.RaiseEvent(offline);
                Assert.Equal(DragDropEffects.None, offline.Effects);
                area.RaiseEvent(Drag(data, area, DragDrop.PreviewDropEvent));
                Assert.Null(AdornerLayer.GetAdornerLayer(area).GetAdorners(area)); Assert.Equal(0, state.Creates);
                vm.UpdateConnectivity(true); vm.RefreshCommand.Execute(null); Pump();
                var buttons = AllVisual<Button>(view).Where(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b).StartsWith("AddCatalog")).ToArray();
                Assert.Equal(2, buttons.Length);
                foreach (var button in buttons) { Assert.True(button.Focusable); Assert.True(button.IsTabStop); Assert.Contains("_", button.Content.ToString()); Assert.True(button.Focus()); }
                var realPath = System.IO.Path.GetTempFileName();
                try
                {
                    var realData = new DataObject(); realData.SetData(DataFormats.FileDrop, new[] { realPath });
                    var acceptedDrop = Drag(realData, area, DragDrop.PreviewDropEvent);
                    area.RaiseEvent(acceptedDrop); Assert.Equal(DragDropEffects.Link, acceptedDrop.Effects);
                    var frame = new DispatcherFrame(); var timeout = System.Diagnostics.Stopwatch.StartNew();
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
                    timer.Tick += (_, _) => { if (!vm.IsLoading || timeout.Elapsed > TimeSpan.FromSeconds(5)) frame.Continue = false; };
                    timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
                    Assert.False(vm.IsLoading); Assert.Equal(1, state.Creates); Assert.Single(state.Locations);
                    Assert.Null(AdornerLayer.GetAdornerLayer(area).GetAdorners(area));
                }
                finally { System.IO.File.Delete(realPath); }
                var inbox = new InboxView(); window.Content = inbox; window.UpdateLayout();
                var inboxDrag = Drag(data, inbox, DragDrop.PreviewDragOverEvent); inbox.RaiseEvent(inboxDrag);
                Assert.Equal(DragDropEffects.None, inboxDrag.Effects);
                inbox.RaiseEvent(Drag(data, inbox, DragDrop.DragLeaveEvent)); Pump();
                Assert.Null(AdornerLayer.GetAdornerLayer(inbox).GetAdorners(inbox));
                completed.SetResult(true);
            }
            catch (Exception e) { completed.SetException(e); }
            finally { window?.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static DragEventArgs Drag(IDataObject data, DependencyObject target, RoutedEvent? routedEvent = null)
    {
        var constructor = typeof(DragEventArgs).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single(c => c.GetParameters().Length == 5);
        var args = (DragEventArgs)constructor.Invoke([data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Copy | DragDropEffects.Link, target, new Point(20, 20)]);
        if (routedEvent is not null) args.RoutedEvent = routedEvent;
        return args;
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(() => frame.Continue = false, DispatcherPriority.ApplicationIdle); Dispatcher.PushFrame(frame);
    }
    private static T FindVisual<T>(DependencyObject root) where T : DependencyObject => AllVisual<T>(root).First();
    private static IEnumerable<T> AllVisual<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T found) yield return found;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in AllVisual<T>(System.Windows.Media.VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
