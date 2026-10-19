using System.Diagnostics;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using Task.Desktop.ViewModels;
using Task.Desktop.Views;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.Tests.Projects;

public sealed partial class ProjectsViewModelTests
{
    [Fact]
    public async AsyncTask NativeProjectHistory_PagingReconnectRevocationAndKeyboard()
    {
        if (Environment.GetEnvironmentVariable("TASK_PROJECT_HISTORY_PROBE") != "1")
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("vstest"); start.ArgumentList.Add(typeof(ProjectsViewModelTests).Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + typeof(ProjectsViewModelTests).FullName + "." + nameof(NativeProjectHistory_PagingReconnectRevocationAndKeyboard));
            start.Environment["TASK_PROJECT_HISTORY_PROBE"] = "1";
            using var child = Process.Start(start)!; var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
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
                    var client = SectionClient();
                    client.HistoryHandler = (_, page, _) => AsyncTask.FromResult(HistoryPage(
                        Enumerable.Range(0, page == 1 ? 50 : 1).Select(_ => HistoryEvent()).ToArray()));
                    using var vm = new ProjectsViewModel(client, User, HistoryCaps);
                    vm.Activate(); await vm.CurrentDetailsOperation;
                    var view = new ProjectsView { DataContext = vm };
                    window = new Window { Content = view, Width = 1120, Height = 820, Title = "Task · Project history test" };
                    window.Show(); PumpProject();
                    var expander = ProjectDescendants<Expander>(view).Single(e => Equals(e.Header, "История"));
                    Assert.True(expander.IsVisible); Assert.Empty(client.HistoryCalls);
                    var provider = (IExpandCollapseProvider)new ExpanderAutomationPeer(expander).GetPattern(PatternInterface.ExpandCollapse);
                    provider.Expand(); await vm.CurrentHistoryOperation; PumpProject();
                    expander.BringIntoView(); PumpProject(); Assert.True(expander.Focus());
                    var list = ProjectDescendants<ListBox>(expander).Single(); Assert.Equal(50, list.Items.Count);
                    var refresh = ProjectDescendants<Button>(expander).Single(b => Equals(b.Content, "Обновить историю"));
                    Assert.True(refresh.IsEnabled); refresh.BringIntoView(); PumpProject(); Assert.True(refresh.Focus());
                    Assert.True(refresh.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next)));
                    SaveProjectFrame(view, "project-history-loaded.png");
                    await vm.MoreHistoryCommand.ExecuteAsync(); PumpProject(); Assert.Equal(51, list.Items.Count);
                    vm.UpdateConnectivity(false); PumpProject(); Assert.False(refresh.IsEnabled); Assert.Empty(vm.History);
                    vm.UpdateConnectivity(true); PumpProject(); Assert.True(refresh.IsEnabled);
                    await vm.RefreshHistoryCommand.ExecuteAsync(); PumpProject(); Assert.Equal(50, list.Items.Count);
                    provider.Collapse(); PumpProject(); Assert.Empty(vm.History);
                    provider.Expand(); await vm.CurrentHistoryOperation; PumpProject(); Assert.Equal(50, list.Items.Count);
                    vm.UpdateCapabilities(["Project.Read"]); PumpProject(); Assert.False(expander.IsVisible); Assert.Empty(vm.History);
                    SaveProjectFrame(view, "project-history-revoked.png");
                    done.SetResult();
                }
                catch (Exception e) { done.SetException(e); }
                finally { window?.Close(); app.Shutdown(); }
            });
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(45)); thread.Join();
    }
}
