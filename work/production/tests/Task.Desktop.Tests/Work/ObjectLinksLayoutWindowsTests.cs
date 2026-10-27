using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Task.Desktop.ViewModels;
using Task.Desktop.Views;
using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.Tests.Work;

[Collection("Employee picker native WPF")]
public sealed class ObjectLinksLayoutWindowsTests
{
    [Fact]
    public async AsyncTask WorkHubKeepsLinkActionsReachableAndReleasesCollapsedSpace()
    {
        if (Environment.GetEnvironmentVariable("TASK_LINK_LAYOUT_PROBE") != "1")
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("vstest");
            start.ArgumentList.Add(typeof(ObjectLinksLayoutWindowsTests).Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + typeof(ObjectLinksLayoutWindowsTests).FullName + "." + nameof(WorkHubKeepsLinkActionsReachableAndReleasesCollapsedSpace));
            start.Environment["TASK_LINK_LAYOUT_PROBE"] = "1";
            using var child = Process.Start(start)!;
            var output = child.StandardOutput.ReadToEndAsync();
            var error = child.StandardError.ReadToEndAsync();
            try
            {
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
                Assert.True(child.ExitCode == 0, await output + await error);
            }
            finally
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
            }
            return;
        }

        var completion = new System.Threading.Tasks.TaskCompletionSource(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources["Task.BooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Task.Desktop;component/Resources/Theme.xaml", UriKind.Relative) });
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            app.Dispatcher.BeginInvoke(async () =>
            {
                Window? window = null;
                try
                {
                    var contact = new DesktopContact(Guid.NewGuid(), 3, "Мария", "Мария", null, null, "active", "active");
                    var client = new WorkHubViewModelTests.FakeClient { Contacts = [contact] };
                    using var model = new WorkHubViewModel(client, ObjectLinksIntegrationTests.Access);
                    model.Activate(WorkHubArea.Contacts);
                    for (var i = 0; i < 100 && model.Contacts.Count == 0; i++) await AsyncTask.Delay(10);
                    Assert.Single(model.Contacts);
                    model.SelectedContact = contact;
                    var hub = new WorkHubView { DataContext = model };
                    var root = (Grid)hub.Content;
                    var expander = (Expander)hub.FindName("LinksExpander");
                    expander.IsExpanded = true;
                    window = new Window { Content = hub, Width = 1100, Height = 730, Left = -20000, ShowInTaskbar = false };
                    window.Show();
                    Pump();
                    var view = Descendants<ObjectLinksView>(hub).Single();
                    var picker = Descendants<ObjectLinkTargetPickerView>(view).Single();
                    var scroll = (ScrollViewer)view.FindName("LinksScrollViewer");
                    var add = Descendants<Button>(view).Single(button => AutomationProperties.GetAutomationId(button) == "AddObjectLink");
                    var search = (TextBox)picker.FindName("SearchBox");
                    await ObjectLinksIntegrationTests.Select(model.Links!);
                    Pump();
                    Assert.True(add.IsEnabled);

                    foreach (var (width, height) in new[] { (1100d, 730d), (820d, 730d), (780d, 730d), (520d, 730d), (820d, 480d) })
                    {
                        window.Width = width;
                        window.Height = height;
                        Pump();
                        Assert.Same(picker, Descendants<ObjectLinkTargetPickerView>(view).Single());
                        Assert.NotNull(model.Links!.TargetPicker!.SelectedTarget);
                        search.BringIntoView();
                        Pump();
                        AssertInsideViewport(search, scroll);
                        add.BringIntoView();
                        Pump();
                        AssertInsideViewport(add, scroll);
                        Assert.True(root.RowDefinitions[2].ActualHeight > 0);
                        Assert.True(view.ActualHeight < hub.ActualHeight);
                    }

                    var expandedContentHeight = root.RowDefinitions[2].ActualHeight;
                    expander.IsExpanded = false;
                    Pump();
                    Assert.True(root.RowDefinitions[2].ActualHeight > expandedContentHeight);
                    expander.IsExpanded = true;
                    Pump();
                    model.UpdateCapabilities(ObjectLinksIntegrationTests.Access.Where(capability => capability != "ObjectLink.Read"));
                    Pump();
                    Assert.False(expander.IsVisible);
                    Assert.Equal(0, root.RowDefinitions[3].ActualHeight);
                    completion.SetResult();
                }
                catch (Exception error)
                {
                    completion.SetException(error);
                }
                finally
                {
                    window?.Close();
                    app.Shutdown();
                }
            });
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(35));
    }

    private static void AssertInsideViewport(FrameworkElement element, ScrollViewer scroll)
    {
        var bounds = element.TransformToAncestor(scroll).TransformBounds(new Rect(element.RenderSize));
        Assert.True(bounds.Top >= -1 && bounds.Bottom <= scroll.ActualHeight + 1, $"Outside vertical viewport: {bounds}; height={scroll.ActualHeight}");
        Assert.True(bounds.Left >= -1 && bounds.Right <= scroll.ActualWidth + 1, $"Outside horizontal viewport: {bounds}; width={scroll.ActualWidth}");
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
