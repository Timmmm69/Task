using System.Diagnostics;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using Task.Desktop.ViewModels;
using Task.Desktop.Views;
using Task.Desktop.Tests.TaskScreen;
using AsyncTask = System.Threading.Tasks.Task;
using PageResult = Task.Desktop.TaskApi.DesktopTasksApiResult<Task.Desktop.TaskApi.DesktopTaskPage>;

namespace Task.Desktop.Tests.Projects;

public sealed partial class ProjectsViewModelTests
{
    [Fact]
    public async AsyncTask NativeRelatedTasks_ExplicitLoadProgressCollapseResumeAndUnloadedCancellation()
    {
        if (Environment.GetEnvironmentVariable("TASK_RELATED_TASKS_PROBE") != "1")
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("vstest"); start.ArgumentList.Add(typeof(ProjectsViewModelTests).Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + typeof(ProjectsViewModelTests).FullName + "." + nameof(NativeRelatedTasks_ExplicitLoadProgressCollapseResumeAndUnloadedCancellation));
            start.Environment["TASK_RELATED_TASKS_PROBE"] = "1";
            using var child = Process.Start(start)!;
            var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
            try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(50)); Assert.True(child.ExitCode == 0, await output + await error); }
            finally { if (!child.HasExited) child.Kill(entireProcessTree: true); }
            return;
        }
        var done = new System.Threading.Tasks.TaskCompletionSource(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Task.Desktop;component/Resources/Theme.xaml", UriKind.Relative) });
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            app.Dispatcher.BeginInvoke(async () =>
            {
                Window? window = null;
                try
                {
                    var tasks = new TasksViewModelTests.FakeTasksApiClient();
                    using var vm = RelatedModel(tasks);
                    var view = new ProjectsView { DataContext = vm };
                    window = new Window { Content = view, Width = 1120, Height = 820, Title = "Task · Related tasks test" };
                    window.Show(); PumpProject();
                    foreach (var expander in ProjectDescendants<Expander>(view).Where(expander => expander.IsVisible).ToArray())
                    {
                        expander.BringIntoView(); PumpProject();
                        Assert.True(expander.Focus());
                        var peer = new ExpanderAutomationPeer(expander);
                        var provider = (IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse);
                        provider.Collapse(); PumpProject(); Assert.False(expander.IsExpanded);
                        provider.Expand(); PumpProject(); Assert.True(expander.IsExpanded);
                        Assert.True(expander.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next)));
                    }
                    var section = ProjectDescendants<Expander>(view).Single(e => Equals(e.Header, "Связанные задачи"));
                    section.IsExpanded = true; PumpProject();
                    Assert.Equal(0, tasks.PageCallCount); // Expanded/restored state never starts a catalog walk.
                    var button = ProjectDescendants<Button>(section).Single();
                    var status = ProjectDescendants<TextBlock>(section).Single(t => System.Windows.Automation.AutomationProperties.GetAutomationId(t) == "ProjectRelatedTasksStatus");
                    button.BringIntoView(); PumpProject();
                    Assert.True(button.Focus()); Assert.True(button.IsEnabled);
                    tasks.EnqueuePage(RelatedPage("next", Task(Guid.NewGuid(), ProjectId, "Связанная задача")));
                    ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
                    PumpProject();
                    Assert.Equal(ProjectTasksState.Partial, vm.RelatedTasksState);
                    Assert.Contains("пока 1", status.Text);
                    Assert.Equal("Продолжить поиск", button.Content);
                    SaveProjectFrame(view, "related-tasks-partial.png");
                    var gate = new System.Threading.Tasks.TaskCompletionSource<PageResult>();
                    CancellationToken token = default;
                    tasks.EnqueuePage((_, ct) => { token = ct; return gate.Task; });
                    var pending = vm.LoadRelatedTasksCommand.ExecuteAsync(); PumpProject();
                    Assert.False(button.IsEnabled); Assert.Contains("Поиск", status.Text);
                    section.IsExpanded = false;
                    Assert.True(token.IsCancellationRequested);
                    gate.SetResult(RelatedPage()); await pending;
                    section.IsExpanded = true; PumpProject();
                    Assert.Single(vm.RelatedTasks); Assert.Contains("остановлен", status.Text);
                    tasks.EnqueuePage(RelatedPage());
                    await vm.LoadRelatedTasksCommand.ExecuteAsync(); PumpProject();
                    Assert.Equal(ProjectTasksState.Complete, vm.RelatedTasksState);
                    Assert.False(button.IsEnabled);
                    SaveProjectFrame(view, "related-tasks-complete.png");
                    var selected = vm.SelectedItem; vm.SelectedItem = null; vm.SelectedItem = selected;
                    var unloadGate = new System.Threading.Tasks.TaskCompletionSource<PageResult>();
                    tasks.EnqueuePage((_, ct) => { token = ct; return unloadGate.Task; });
                    var unloading = vm.LoadRelatedTasksCommand.ExecuteAsync();
                    window.Content = null; PumpProject(); Assert.True(token.IsCancellationRequested);
                    unloadGate.SetResult(RelatedPage()); await unloading;
                    Assert.False(vm.LoadRelatedTasksCommand.IsExecuting);
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
}
