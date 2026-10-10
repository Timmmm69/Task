using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Task.Desktop.Infrastructure;
using Task.Desktop.Modes;
using Task.Desktop.Personal;
using Task.Desktop.Projects;
using Task.Desktop.ViewModels;
using Task.Desktop.Views;

namespace Task.Desktop.Tests.TaskScreen;

public sealed class TaskContextActionsUiTests
{
    [Fact]
    public async System.Threading.Tasks.Task NativeWindows_MouseKeyboardFocusRelationsAndSmallWindow()
    {
        if (Environment.GetEnvironmentVariable("TASK_CONTEXT_UI_CHILD") != "1")
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("vstest"); start.ArgumentList.Add(GetType().Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + GetType().FullName + "." + nameof(NativeWindows_MouseKeyboardFocusRelationsAndSmallWindow));
            start.Environment["TASK_CONTEXT_UI_CHILD"] = "1";
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(75)); Assert.True(process.ExitCode == 0, await stdout + await stderr); }
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
                var root = Path.Combine(Path.GetTempPath(), "Task-action-ui", Guid.NewGuid().ToString("N"));
                var errors = new BindingErrors(); Window? company = null; Window? personal = null;
                try
                {
                    var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Task.Desktop;component/Resources/Theme.xaml", UriKind.Relative) });
                    app.Resources["Task.BooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
                    foreach (var name in new[] { "IconKeyToGeometryConverter", "ShellNavigationWidthConverter", "WindowWidthAtLeastConverter", "ResponsiveGridLengthConverter", "TimelineLaneMarginConverter", "CalendarLaneWidthConverter", "CalendarLaneLeftConverter" })
                        app.Resources["Task." + name] = Activator.CreateInstance(typeof(MainWindow).Assembly.GetType("Task.Desktop.Converters." + name)!);
                    PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
                    PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
                    var first = ActionTasksClient.Source("Первая задача"); var second = ActionTasksClient.Source("Вторая задача");
                    var client = new ActionTasksClient(first, second); var projectClient = new ActionProjectsClient();
                    using var tasks = new TasksViewModel(client, ["Task.Read", "Task.Create", "Task.Update"]);
                    using var projects = new ProjectsViewModel(projectClient, projectClient.Project.OwnerUserId, ["Project.Read"]);
                    using var shell = new MainWindowViewModel(new Uri("https://fixture.invalid"), null, tasks, projects: projects);
                    shell.SelectedSection = shell.Sections.Single(s => s.Route == "tasks"); await tasks.ActivateAsync();
                    company = new MainWindow(shell) { Left = 20, Top = 20, ShowInTaskbar = false }; company.Show(); Assert.True(company.Activate()); await Drain(company);
                    var list = Find<ListBox>(company, "TasksList");
                    await TaskContextActionsTests.Until(() => tasks.Workspace?.IsBusy != true);
                    var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(tasks.Items.Single(x => x.Id == second.Id));
                    var text = Descendants<TextBlock>(row).First(x => x.Text == second.Title);
                    Assert.Same(row.DataContext, TaskRowAction.ResolveRow(list, text));
                    Assert.Null(TaskRowAction.ResolveRow(list, list));
                    foreach (var scroll in Descendants<ScrollBar>(list)) Assert.Null(TaskRowAction.ResolveRow(list, scroll));
                    // Interactive descendants in a real row must never resolve as the row action.
                    var holder = new StackPanel(); var nestedButton = new Button { Content = "Вложенная кнопка" }; var check = new CheckBox { Content = "Флажок" };
                    holder.Children.Add(nestedButton); holder.Children.Add(check); var nestedRow = new ListBoxItem { Content = holder, DataContext = second };
                    var nestedList = new ListBox(); nestedList.Items.Add(nestedRow); var testWindow = new Window { Content = nestedList, Width = 300, Height = 200, ShowInTaskbar = false };
                    testWindow.Show(); testWindow.UpdateLayout(); Assert.Null(TaskRowAction.ResolveRow(nestedList, nestedButton)); Assert.Null(TaskRowAction.ResolveRow(nestedList, check)); testWindow.Close();
                    Assert.True(company.Activate()); await DoubleClick(text);
                    await TaskContextActionsTests.Until(() => tasks.Editor is not null); await Drain(company);
                    Assert.Equal(second.Id, tasks.Editor!.SourceId); Assert.True(Find<TextBox>(company, "TaskTitleTextBox").IsKeyboardFocused);
                    await tasks.DiscardEditorCommand.ExecuteAsync(); await Drain(company); Assert.True(list.IsKeyboardFocused);
                    await DoubleClickAt(list, new Point(30, list.ActualHeight - 15)); await Drain(company); Assert.Null(tasks.Editor);
                    // Read-only fallback expands the actual inspector.
                    tasks.UpdateCapabilities(["Task.Read"]); await TaskContextActionsTests.Until(() => tasks.Workspace?.IsBusy != true);
                    await DoubleClick(text); await Drain(company); Assert.Null(tasks.Editor); Assert.True(((Expander)company.FindName("TaskInspectorExpander")).IsExpanded);
                    tasks.UpdateCapabilities(["Task.Read", "Task.Create", "Task.Update"]);
                    shell.SelectedSection = shell.Sections.Single(s => s.Route == "projects");
                    await TaskContextActionsTests.Until(() => projects.State == ProjectsScreenState.Loaded && !projects.IsDetailLoading); await Drain(company);
                    var projectAction = Find<Button>(company, "ProjectAddTask"); Assert.True(projectAction.IsEnabled); Assert.True(projectAction.Focus());
                    Assert.Equal("Добавить задачу", projectAction.Content); await Capture(company, "corporate-project");
                    Assert.True(projectAction.Focus()); keybd_event(13, 0, 0, UIntPtr.Zero); keybd_event(13, 0, 2, UIntPtr.Zero);
                    await TaskContextActionsTests.Until(() => tasks.Editor is not null); await Drain(company);
                    Assert.Equal(projectClient.Project.Id, tasks.Editor!.Card.Project!.Id);
                    var projectChoice = Find<ComboBox>(company, "TaskProjectComboBox");
                    tasks.Editor.Card.SetOptions(new System.Text.Json.Nodes.JsonObject()); await Drain(company);
                    Assert.Equal(projectClient.Project.Id, tasks.Editor.Card.Project!.Id); Assert.NotNull(projectChoice.SelectedItem);
                    await Capture(company, "corporate-project-draft"); await tasks.DiscardEditorCommand.ExecuteAsync();
                    await TaskContextActionsTests.Until(() => tasks.Workspace?.IsBusy != true);
                    ((Expander)company.FindName("TaskInspectorExpander")).IsExpanded = true; await Drain(company);
                    var childAction = Descendants<Button>(company).Single(x => x.IsVisible && AutomationProperties.GetAutomationId(x) is "AddSubtaskButton" or "CompactAddSubtaskButton"); childAction.BringIntoView(); Assert.True(childAction.Focus()); await Capture(company, "corporate-task-card");
                    keybd_event(32, 0, 0, UIntPtr.Zero); keybd_event(32, 0, 2, UIntPtr.Zero); await TaskContextActionsTests.Until(() => tasks.Editor is not null); await Drain(company);
                    Assert.Equal(tasks.SelectedDetails!.Id, tasks.Editor!.Card.Parent!.Id); await Capture(company, "corporate-subtask-draft");
                    await tasks.DiscardEditorCommand.ExecuteAsync(); await Drain(company); ((MainWindow)company).CloseAfterModeSwitch(); company = null;

                    using var model = new PersonalApplicationModel(root);
                    var localProject = model.Store.SaveProject(new(Guid.Empty, 0, "Личный проект", null, DesktopProjectStatus.Active, null, null));
                    model.Planning!.Refresh(); model.Planning.SelectedProject = model.Planning.Projects.Single();
                    personal = new PersonalWindow(model) { Left = 20, Top = 20, ShowInTaskbar = false }; personal.Show(); Assert.True(personal.Activate()); await Drain(personal);
                    var localShell = (PersonalShellViewModel)personal.DataContext;
                    localShell.SelectedSection = localShell.Sections.Single(s => s.Route == "projects"); await Drain(personal);
                    Assert.True(Find<Button>(personal, "PersonalProjectAddTask").IsEnabled); await Capture(personal, "personal-project");
                    Assert.True(Find<Button>(personal, "PersonalProjectAddTask").Focus()); keybd_event(32, 0, 0, UIntPtr.Zero); keybd_event(32, 0, 2, UIntPtr.Zero);
                    await TaskContextActionsTests.Until(() => model.Tasks!.Editor is not null); await Drain(personal);
                    var local = model.Tasks!; Assert.Equal(localProject.Id, local.Editor!.Card.Project!.Id);
                    Assert.True(Find<TextBox>(personal, "PersonalTaskTitle").IsKeyboardFocused);
                    local.Editor.Title = "Личная задача"; await Capture(personal, "personal-project-draft"); await local.SaveCommand.ExecuteAsync(); await local.OpenSavedTaskCommand.ExecuteAsync(); await Drain(personal);
                    local.DetailsExpanded = true; await Drain(personal); var localChildAction = Find<Button>(personal, "PersonalAddSubtask"); localChildAction.BringIntoView(); Assert.True(localChildAction.Focus()); await Capture(personal, "personal-task-card");
                    keybd_event(32, 0, 0, UIntPtr.Zero); keybd_event(32, 0, 2, UIntPtr.Zero); await TaskContextActionsTests.Until(() => local.Editor is not null); await Drain(personal); Assert.Equal("Личная задача", local.Editor!.Card.Parent!.Name);
                    await Capture(personal, "personal-subtask-draft");
                    var planning = Descendants<Expander>(personal).Single(x => x.Header?.ToString() == "Планирование и связи"); planning.IsExpanded = true; await Drain(personal);
                    var parentCombo = Find<ComboBox>(personal, "PersonalParentTask"); parentCombo.BringIntoView(); await Drain(personal);
                    parentCombo.SelectedIndex = 0; local.Editor.Card.SetOptions(new()); await Drain(personal); Assert.Null(local.Editor.Card.Parent!.Id);
                    await local.CancelCommand.ExecuteAsync(); await Drain(personal);
                    var localList = Find<ListBox>(personal, "PersonalTasksList"); Assert.True(localList.IsKeyboardFocused);
                    var localRow = (ListBoxItem)localList.ItemContainerGenerator.ContainerFromIndex(0);
                    await DoubleClick(Descendants<TextBlock>(localRow).First(x => x.Text == "Личная задача")); await Drain(personal);
                    Assert.NotNull(local.Editor); await local.CancelCommand.ExecuteAsync(); await Drain(personal);
                    Assert.True(localList.Focus()); Assert.True(localList.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next))); Assert.NotNull(Keyboard.FocusedElement);
                    Assert.Empty(errors.Messages);
                    done.SetResult();
                }
                catch (Exception error) { done.SetException(error); }
                finally
                {
                    if (company is MainWindow companyWindow) companyWindow.CloseAfterModeSwitch(); if (personal is PersonalWindow personalWindow) personalWindow.CloseForContextTransition(); PresentationTraceSources.DataBindingSource.Listeners.Remove(errors);
                    if (Directory.Exists(root)) Directory.Delete(root, true); dispatcher.InvokeShutdown();
                }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        try { await done.Task.WaitAsync(TimeSpan.FromSeconds(60)); }
        finally { Assert.True(await System.Threading.Tasks.Task.Run(() => thread.Join(TimeSpan.FromSeconds(10))), "WPF dispatcher thread did not finish teardown."); }
    }
    private static async System.Threading.Tasks.Task Drain(Window window) { await Dispatcher.Yield(DispatcherPriority.Background); window.UpdateLayout(); await System.Threading.Tasks.Task.Delay(80); }
    private static async System.Threading.Tasks.Task DoubleClick(FrameworkElement target) => await DoubleClickAt(target, new Point(Math.Min(25, target.ActualWidth / 2), target.ActualHeight / 2));
    private static async System.Threading.Tasks.Task DoubleClickAt(FrameworkElement target, Point local)
    {
        var point = target.PointToScreen(local); Assert.True(SetCursorPos((int)point.X, (int)point.Y));
        mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
        await System.Threading.Tasks.Task.Delay(70);
        mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
        await System.Threading.Tasks.Task.Delay(100);
    }
    private static async System.Threading.Tasks.Task Capture(Window window, string name)
    {
        var path = Environment.GetEnvironmentVariable("TASK_CONTEXT_EVIDENCE"); if (string.IsNullOrEmpty(path)) return;
        Directory.CreateDirectory(path);
        foreach (var size in new[] { (1200d, 900d, "normal"), (800d, 480d, "minimum") })
        {
            window.Width = size.Item1; window.Height = size.Item2; await Drain(window);
            (Keyboard.FocusedElement as FrameworkElement)?.BringIntoView(); await Drain(window);
            var visual = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
            var bitmap = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(path, name + "-" + size.Item3 + ".png")); encoder.Save(file);
        }
        window.Width = 1200; window.Height = 900; await Drain(window);
    }
    private static T Find<T>(DependencyObject root, string id) where T : FrameworkElement => Descendants<T>(root).Single(x => AutomationProperties.GetAutomationId(x) == id);
    private static T Ancestor<T>(DependencyObject node) where T : DependencyObject => VisualTreeHelper.GetParent(node) is T result ? result : Ancestor<T>(VisualTreeHelper.GetParent(node));
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T item) yield return item; foreach (var descendant in Descendants<T>(child)) yield return descendant; } }
    private sealed class BindingErrors : TraceListener
    { public List<string> Messages { get; } = []; public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Messages.Add(message); } public override void WriteLine(string? message) => Write(message); }
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
