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
    public async AsyncTask NativeProjectFiles_SectionPickerCommandsFocusAndSafeStates()
    {
        if (Environment.GetEnvironmentVariable("TASK_PROJECT_FILES_PROBE") != "1")
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("vstest"); start.ArgumentList.Add(typeof(ProjectsViewModelTests).Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + typeof(ProjectsViewModelTests).FullName + "." + nameof(NativeProjectFiles_SectionPickerCommandsFocusAndSafeStates));
            start.Environment["TASK_PROJECT_FILES_PROBE"] = "1";
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
                    var work = new ProjectFilesTests.Client(); work.Item = work.Item with { Description = @"C:\private\description.txt" }; work.Rows.Add(work.Link(ProjectId));
                    var files = ProjectFilesTests.Create(work, Project());
                    using var vm = new ProjectsViewModel(SectionClient(), User, ProjectFilesTests.Caps, files: files);
                    vm.Activate(); await vm.CurrentDetailsOperation;
                    var view = new ProjectsView { DataContext = vm };
                    window = new Window { Content = view, Width = 1120, Height = 820, Title = "Task · Project files test" };
                    window.Show(); PumpProject();
                    var expander = ProjectDescendants<Expander>(view).Single(e => Equals(e.Header, "Файлы"));
                    Assert.True(expander.IsVisible); Assert.Equal(0, work.ReadCalls);
                    var provider = (IExpandCollapseProvider)new ExpanderAutomationPeer(expander).GetPattern(PatternInterface.ExpandCollapse);
                    provider.Expand(); await vm.CurrentFilesOperation; PumpProject();
                    expander.BringIntoView(); PumpProject(); Assert.True(expander.Focus());
                    var list = ProjectDescendants<ListBox>(expander).Single(); Assert.Single(list.Items);
                    var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                    Assert.Equal("Договор, Файл", System.Windows.Automation.AutomationProperties.GetName(container));
                    Assert.Contains(ProjectDescendants<TextBlock>(expander), block => block.Text == "Договор");
                    var open = ProjectDescendants<Button>(expander).Single(button => Equals(button.Content, "Открыть файл"));
                    Assert.True(open.IsEnabled); open.BringIntoView(); PumpProject(); Assert.True(open.Focus());
                    Assert.True(open.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next)));
                    SaveProjectFrame(view, "project-files-loaded.png");
                    await files.ChooseCommand.ExecuteAsync(); PumpProject();
                    Assert.Contains(ProjectDescendants<ObjectLinkTargetPickerView>(expander), picker => picker.IsVisible && ReferenceEquals(picker.DataContext, files.Picker));
                    ProjectDescendants<ObjectLinkTargetPickerView>(expander).Single().BringIntoView(); PumpProject();
                    SaveProjectFrame(view, "project-files-picker.png");
                    vm.UpdateCapabilities(ProjectFilesTests.Caps.Where(code => code != "FileReference.Open")); await files.RefreshCommand.ExecuteAsync(); PumpProject();
                    Assert.False(open.IsEnabled);
                    provider.Collapse(); PumpProject(); Assert.Empty(files.Items);
                    provider.Expand(); await vm.CurrentFilesOperation; PumpProject(); Assert.Single(files.Items);
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
