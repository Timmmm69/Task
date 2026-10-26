using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Task.Desktop.ViewModels;
using Task.Desktop.Views;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.Tests.Projects;

[Collection("Employee picker native WPF")]
public sealed partial class ProjectsViewModelTests
{
    [Fact]
    public async AsyncTask NativeProjectsHostReusesPickersAndPreservesUuidFallback()
    {
        if (Environment.GetEnvironmentVariable("TASK_PROJECT_PICKER_PROBE") != "1")
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
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + typeof(ProjectsViewModelTests).FullName + "." + nameof(NativeProjectsHostReusesPickersAndPreservesUuidFallback));
            start.Environment["TASK_PROJECT_PICKER_PROBE"] = "1";
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
                    var employee = Employee(login: "ivan2"); var client = PickerClient();
                    using var vm = PickerModel(client, Directory(Employee(User, "owner"), employee)); vm.Activate();
                    var view = new ProjectsView { DataContext = vm };
                    window = new Window { Content = view, Width = 1120, Height = 820, Title = "Task · Project Picker test" };
                    window.Show(); window.Activate(); PumpProject();
                    var memberView = ProjectDescendants<EmployeePickerView>(view).Single(v => v.IsVisible);
                    Assert.Same(vm.MemberPicker, memberView.DataContext);
                    var search = (TextBox)memberView.FindName("SearchBox"); search.Text = "Иван";
                    await vm.MemberPicker!.CurrentOperation; PumpProject();
                    var results = (ListBox)memberView.FindName("ResultsList");
                    results.SelectedItem = vm.MemberPicker.Results.Single(u => u.Id == employee.Id); PumpProject();
                    var confirm = ProjectDescendants<Button>(memberView).Single(b => new ButtonAutomationPeer(b).GetName() == "Подтвердить выбор сотрудника");
                    confirm.Command.Execute(null); PumpProject(); Assert.Equal(employee.Id, vm.NewMemberAccountId);
                    Assert.False(ProjectDescendants<TextBox>(view).Single(t => System.Windows.Automation.AutomationProperties.GetAutomationId(t) == "ProjectMemberUserId").IsVisible);
                    SaveProjectFrame(view, "project-member-picker.png");
                    await vm.EditProjectCommand.ExecuteAsync(); PumpProject();
                    var managerView = ProjectDescendants<EmployeePickerView>(view).Single(v => ReferenceEquals(v.DataContext, vm.Editor!.ManagerPicker));
                    Assert.True(managerView.IsVisible);
                    await Choose(vm.Editor!.ManagerPicker!, employee); PumpProject();
                    Assert.True(vm.Editor.TryBuild(out var draft)); Assert.Equal(employee.Id, draft.ManagerUserId);
                    SaveProjectFrame(view, "project-manager-picker.png");
                    window.Width = 900; PumpProject(); SaveProjectFrame(view, "project-manager-narrow.png");
                    var save = ProjectDescendants<Button>(view).Single(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == "SaveProjectButton");
                    save.BringIntoView(); PumpProject(); Assert.True(save.IsEnabled); Assert.True(save.Focus());
                    SaveProjectFrame(view, "project-manager-save.png");
                    await vm.CancelEditorCommand.ExecuteAsync(); vm.UpdateCapabilities(Capabilities); PumpProject();
                    Assert.DoesNotContain(ProjectDescendants<EmployeePickerView>(view), v => v.IsVisible);
                    Assert.True(ProjectDescendants<TextBox>(view).Single(t => System.Windows.Automation.AutomationProperties.GetAutomationId(t) == "ProjectMemberUserId").IsVisible);
                    SaveProjectFrame(view, "project-uuid-fallback.png");
                    await vm.EditProjectCommand.ExecuteAsync(); PumpProject();
                    var editorBeforeGrant = vm.Editor;
                    vm.UpdateCapabilities(Capabilities.Append("User.Read")); PumpProject();
                    Assert.Same(editorBeforeGrant, vm.Editor);
                    Assert.True(ProjectDescendants<EmployeePickerView>(view).Single(v => ReferenceEquals(v.DataContext, vm.Editor!.ManagerPicker)).IsVisible);
                    Assert.False(ProjectDescendants<TextBox>(view).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "UUID руководителя — необязательно").IsVisible);
                    SaveProjectFrame(view, "project-live-user-read-grant.png");
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

    private static void PumpProject() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> ProjectDescendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); if (child is T match) yield return match; foreach (var nested in ProjectDescendants<T>(child)) yield return nested; }
    }
    private static void SaveProjectFrame(FrameworkElement view, string name)
    {
        if (Environment.GetEnvironmentVariable("TASK_PROJECT_PICKER_EVIDENCE") is not { Length: > 0 } directory) return;
        System.IO.Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        { context.DrawRectangle((Brush)view.FindResource("Task.Brush.Surface.Base"), null, new Rect(0, 0, view.ActualWidth, view.ActualHeight)); context.DrawRectangle(new VisualBrush(view), null, new Rect(0, 0, view.ActualWidth, view.ActualHeight)); }
        bitmap.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name)); encoder.Save(file);
    }
}
