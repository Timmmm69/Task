using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Task.Desktop.Converters;
using Task.Desktop.Modes;
using Task.Desktop.Personal;
using Task.Desktop.Views;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.Tests.Personal;

public sealed class PersonalWorkspaceLayoutWindowsTests
{
    [Fact]
    public async AsyncTask PersonalWorkspacesLoadAndAdaptWithoutOverlappingPanes()
    {
        const string probe = "TASK_PERSONAL_LAYOUT_PROBE";
        if (Environment.GetEnvironmentVariable(probe) != "1")
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add("vstest");
            start.ArgumentList.Add(GetType().Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + GetType().FullName + "." + nameof(PersonalWorkspacesLoadAndAdaptWithoutOverlappingPanes));
            start.Environment[probe] = "1";
            using var child = Process.Start(start)!;
            var output = child.StandardOutput.ReadToEndAsync();
            var error = child.StandardError.ReadToEndAsync();
            try
            {
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90));
                Assert.True(child.ExitCode == 0, await output + await error);
            }
            finally
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
            }
            return;
        }

        var done = new TaskCompletionSource(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Task.Desktop;component/Resources/Theme.xaml", UriKind.Relative) });
            app.Resources["Task.BooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
            app.Resources["Task.WindowWidthAtLeastConverter"] = new WindowWidthAtLeastConverter();
            app.Resources["Task.IconKeyToGeometryConverter"] = new IconKeyToGeometryConverter();
            app.Resources["Task.ResponsiveGridLengthConverter"] = new ResponsiveGridLengthConverter();
            app.Resources["Task.TimelineLaneMarginConverter"] = new TimelineLaneMarginConverter();
            app.Resources["Task.CalendarLaneWidthConverter"] = new CalendarLaneWidthConverter();
            app.Resources["Task.CalendarLaneLeftConverter"] = new CalendarLaneLeftConverter();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            app.Dispatcher.BeginInvoke(async () =>
            {
                Window? window = null;
                var root = Path.Combine(Path.GetTempPath(), "Task-personal-layout", Guid.NewGuid().ToString("N"));
                try
                {
                    using var model = new PersonalApplicationModel(root);
                    using var shell = new PersonalShellViewModel(model);
                    shell.SelectedSection = shell.Sections.Single(s => s.Route == "tasks");
                    await model.Tasks!.RefreshAsync();
                    window = new Window { DataContext = shell, Width = 1200, Height = 900 };
                    window.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("/Task.Desktop;component/Resources/Personal.Controls.xaml", UriKind.Relative),
                    });
                    window.Show();
                    UserControl[] views =
                    [
                        new PersonalTasksView { DataContext = model.Tasks },
                        new PersonalProjectsView { DataContext = model.Planning },
                        new PersonalRemindersView { DataContext = model.Planning },
                    ];
                    foreach (var view in views)
                    {
                        window.Content = view;
                        foreach (var width in new[] { 1200, 800 })
                        {
                            window.Width = width;
                            Pump();
                            var grid = (Grid)view.FindName("SplitWorkspace");
                            var panes = grid.Children.OfType<Border>().ToArray();
                            Assert.Equal(2, panes.Length);
                            Assert.All(panes, p => Assert.True(p.ActualWidth > 250 && p.ActualHeight > 100));
                            var first = panes[0].TranslatePoint(new Point(), grid);
                            var second = panes[1].TranslatePoint(new Point(), grid);
                            if (width == 1200)
                            {
                                Assert.True(second.X >= first.X + panes[0].ActualWidth - 1);
                                Assert.Equal(first.Y, second.Y, 1);
                            }
                            else
                            {
                                Assert.True(second.Y >= first.Y + panes[0].ActualHeight - 1);
                                Assert.Equal(first.X, second.X, 1);
                            }
                        }
                    }
                    var projects = views[1];
                    window.Content = projects;
                    await model.Planning!.NewProjectCommand.ExecuteAsync();
                    Pump();
                    var projectGrid = (Grid)projects.FindName("SplitWorkspace");
                    Assert.Equal(2, Grid.GetRowSpan(projectGrid.Children.OfType<Border>().Last()));
                    await model.Planning.CancelProjectCommand.ExecuteAsync();
                    Pump();
                    Assert.Equal(1, Grid.GetRowSpan(projectGrid.Children.OfType<Border>().Last()));
                    // Shared controls must also load with the personal resource scope and every route.
                    var workspace = new PersonalWorkspaceView { DataContext = model.Workspace };
                    foreach (var area in model.Workspace!.Areas)
                    {
                        model.Workspace.Area = area;
                        window.Content = workspace;
                        foreach (var width in new[] { 1200, 800 })
                        {
                            window.Width = width;
                            Pump();
                            Assert.True(workspace.ActualWidth > 700);
                            Assert.True(workspace.ActualHeight > 400);
                            Assert.Contains(Descendants<WorkHubView>(workspace), hub => hub.IsVisible);
                        }
                    }
                    window.Content = new CalendarView { DataContext = model.Calendar };
                    foreach (var width in new[] { 1200, 800 })
                    {
                        window.Width = width;
                        Pump();
                        Assert.True(((CalendarView)window.Content).ActualWidth > 700);
                    }
                    done.SetResult();
                }
                catch (Exception error)
                {
                    done.SetException(error);
                }
                finally
                {
                    window?.Close();
                    app.Shutdown();
                    if (Directory.Exists(root)) Directory.Delete(root, true);
                }
            });
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(75));
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}
