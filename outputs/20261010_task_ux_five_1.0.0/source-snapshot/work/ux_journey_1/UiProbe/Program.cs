using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Task.Desktop;
using Task.Desktop.Modes;
using Task.Desktop.Personal;
using Task.Desktop.ViewModels;

internal static class Program
{
    private static readonly List<string> Checks = [];
    private static string _root = "";
    private static readonly BindingErrors Errors = new();

    [STAThread]
    private static void Main(string[] args)
    {
        _root = Path.GetFullPath(args[0]); Directory.CreateDirectory(_root);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Task.Desktop;component/Resources/Theme.xaml", UriKind.Relative) });
        foreach (var name in new[] { "IconKeyToGeometryConverter", "ShellNavigationWidthConverter", "WindowWidthAtLeastConverter", "ResponsiveGridLengthConverter", "TimelineLaneMarginConverter", "CalendarLaneWidthConverter", "CalendarLaneLeftConverter" })
            app.Resources["Task." + name] = Activator.CreateInstance(typeof(MainWindow).Assembly.GetType("Task.Desktop.Converters." + name)!);
        app.Resources["Task.BooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        PresentationTraceSources.DataBindingSource.Listeners.Add(Errors);
        app.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await Run();
                Check(Errors.Messages.Count == 0, "No WPF binding errors");
                File.WriteAllText(Path.Combine(_root, "ui-results.json"), JsonSerializer.Serialize(new { status = "PASS", checks = Checks, bindingErrors = Errors.Messages }, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(0);
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(_root, "ui-results.json"), JsonSerializer.Serialize(new { status = "FAIL", checks = Checks, error = error.ToString(), bindingErrors = Errors.Messages }, new JsonSerializerOptions { WriteIndented = true }));
                Console.Error.WriteLine(error); app.Shutdown(1);
            }
        });
        app.Run();
    }

    private static async System.Threading.Tasks.Task Run()
    {
        var selector = new ModeSelectorWindow(); selector.Show();
        Check(Find<Button>(selector, "SelectPersonalModeButton").Content.Equals("Личное пространство"), "Personal mode has plain Russian name");
        Check(Find<Button>(selector, "SelectCorporateModeButton").Content.Equals("Пространство компании"), "Corporate mode has plain Russian name");
        await Capture(selector, "mode-selector", 580, 480);
        await Capture(selector, "mode-selector-small", 440, 340);
        selector.Activate(); Find<Button>(selector, "SelectPersonalModeButton").Focus();
        Find<Button>(selector, "SelectPersonalModeButton").MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        Check(Find<Button>(selector, "SelectCorporateModeButton").IsKeyboardFocused, "Corporate mode remains reachable by keyboard at minimum size");
        selector.Close();

        using var store = new PersonalTaskStore(new(Path.Combine(_root, "corporate-fixture")));
        using var tasks = new TasksViewModel(new PersonalTasksClient(store), ["Task.Read", "Task.Create", "Task.Update"]);
        using var today = new TodayViewModel(new PersonalCalendarClient(store), ["Calendar.Read", "Task.Read"], tasksClient: new PersonalTasksClient(store));
        using var inbox = new InboxViewModel(new PersonalTasksClient(store), ["Task.Read", "Task.Create", "Task.Update"]);
        var projectClient = new FixtureProjects();
        using var projects = new ProjectsViewModel(projectClient, Guid.NewGuid(), ["Project.Read", "Project.Create"]);
        using var shell = new MainWindowViewModel(new Uri("https://fixture.invalid"), null, tasks, today: today, inbox: inbox, projects: projects);
        shell.SelectedSection = shell.Sections.Single(s => s.Route == "tasks");
        await tasks.ActivateAsync();
        var corporate = new MainWindow(shell); corporate.Show();
        shell.SelectedSection = shell.Sections.Single(s => s.Route == "today");
        await today.RefreshCommand.ExecuteAsync(); await Drain(corporate);
        var todayAction = Find<Button>(corporate, "TodayEmptyOpenTasks");
        Check(todayAction.IsVisible && todayAction.IsEnabled, "Corporate Today empty offers navigation to existing tasks");
        await shell.OpenTasksCommand.ExecuteAsync(); await tasks.ActivateAsync();
        Check(shell.SelectedSection.Route == "tasks", "Corporate Today empty action navigates to tasks");
        await Drain(corporate);
        Check(Find<Button>(corporate, "TasksEmptyCreateTask").IsVisible, "Corporate empty state offers task creation");
        Check(Find<TextBlock>(corporate, "SectionHelpText").Text.Contains("измените"), "Corporate section explains next step");
        await Capture(corporate, "corporate-empty", 800, 480);
        await tasks.NewTaskCommand.ExecuteAsync(); await Drain(corporate);
        var date = Find<TextBox>(corporate, "TaskScheduledDateTextBox");
        Check(!HasAncestor<Expander>(date), "Corporate day is outside advanced fields");
        Check(date.IsEnabled && date.IsVisible, "Corporate day is available on create");
        Check(Find<Button>(corporate, "SaveTaskButton").Content.Equals("Создать задачу"), "Corporate create label");
        Check(AutomationProperties.GetName(Find<Button>(corporate, "SaveTaskButton")) == "Создать задачу", "Corporate submit accessible name matches action");
        var title = Find<TextBox>(corporate, "TaskTitleTextBox");
        corporate.Activate(); title.Focus();
        Check(title.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)), "Corporate title can advance by Tab");
        Check(Keyboard.FocusedElement == Find<ComboBox>(corporate, "TaskPriorityComboBox"), "Corporate Tab moves title to priority");
        ((UIElement)Keyboard.FocusedElement).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        Check(Keyboard.FocusedElement == date, "Corporate Tab reaches day before exact start");
        tasks.Editor!.Title = "Подготовить договор";
        tasks.Editor.Card.Date = "20.12.2030";
        tasks.Editor.StartText = "21.12.2030 10:30"; await Drain(corporate);
        Check(!date.IsEnabled && tasks.Editor.Card.DateHint.Contains("очистите"), "Corporate exact start explains disabled day");
        tasks.Editor.StartText = ""; await Drain(corporate);
        Check(date.IsEnabled, "Clearing exact start restores day input");
        var rights = Descendants<TextBlock>(corporate).Single(x => x.Text.StartsWith("У вас нет разрешения назначать"));
        Check(rights.IsVisible && !rights.Text.Contains("Task.Assign"), "Assignment denial explains rights in plain language");
        tasks.Editor.Card.CanAssign = true; await Drain(corporate);
        Check(!rights.IsVisible, "Assignment denial disappears when allowed");
        var description = Find<TextBox>(corporate, "TaskDescriptionTextBox");
        description.Text = "Строка"; description.BringIntoView(); description.Focus();
        await PressNative(corporate, 0x0D);
        Check(description.Text.Contains('\n') && tasks.Editor is not null, "Ordinary Enter inserts newline in corporate task description");
        await Capture(corporate, "corporate-editor", 1200, 900);
        await Capture(corporate, "corporate-editor-small", 800, 480);
        var submit = Find<Button>(corporate, "SaveTaskButton");
        var submitPoint = submit.TranslatePoint(new Point(), corporate);
        Check(submit.IsVisible && submitPoint.Y + submit.ActualHeight <= corporate.ActualHeight, "Corporate footer bounded at minimum size");
        Check(!HasAncestor<ScrollViewer>(submit), "Corporate footer outside scroll content");
        var corporatePicker = Descendants<DatePicker>(corporate).First(x => x.IsVisible);
        corporatePicker.IsDropDownOpen = true; await Drain(corporate);
        Check(tasks.Editor is not null && corporatePicker.IsDropDownOpen, "Calendar opens without submitting form");
        corporatePicker.IsDropDownOpen = false;
        tasks.Editor!.Card.Date = "31.02.2026"; await Drain(corporate);
        Check(tasks.Editor.Card.CalendarDate is null && date.Text == "31.02.2026", "Invalid corporate day remains raw and blocks save");
        Check(!tasks.SaveEditorCommand.CanExecute(null), "Invalid date disables shared submit command");
        await Capture(corporate, "corporate-date-error", 1200, 900);
        corporatePicker.SelectedDate = new DateTime(2030, 12, 20); await Drain(corporate);
        Check(tasks.Editor.Card.Date == "20.12.2030", "Calendar selection updates raw day");
        date.Focus(); date.BringIntoView(); await Drain(corporate);
        Check(date.IsKeyboardFocused, "Corporate day can be focused in minimum window");
        await CaptureFocused(corporate, "corporate-day-small", date);
        tasks.SelectedStatusFilter = "В работе";
        date.Focus(); await PressNative(corporate, 0x0D, control: true); await Drain(corporate);
        Check(tasks.Editor is null, "Corporate Ctrl+Enter uses shared save command");
        var announcement = Find<TextBlock>(corporate, "TasksAnnouncement");
        Check(announcement.IsVisible && announcement.Opacity == 1 && announcement.Text.Contains("разделе «Задачи»"), "Corporate saved result is visible and names destination");
        Check(Find<Button>(corporate, "TasksOpenSavedTask").IsVisible, "Hidden saved task offers Open action");
        await tasks.OpenSavedTaskCommand.ExecuteAsync(); await Drain(corporate);
        Check(tasks.SelectedItem?.Title == "Подготовить договор", "Corporate Open selects saved task through filter");
        await Capture(corporate, "corporate-saved", 1200, 900);
        await tasks.EditTaskCommand.ExecuteAsync(); await Drain(corporate);
        Check(Find<Button>(corporate, "SaveTaskButton").Content.Equals("Сохранить изменения"), "Corporate edit label");
        await tasks.DiscardEditorCommand.ExecuteAsync();
        shell.SelectedSection = shell.Sections.Single(s => s.Route == "inbox"); await inbox.ActivateAsync(); await Drain(corporate);
        var corporateCapture = Find<TextBox>(corporate, "InboxCaptureTextBox"); corporateCapture.Focus(); inbox.CaptureText = "Company capture";
        await PressNative(corporate, 0x0D); await Drain(corporate);
        Check(inbox.CaptureText == "" && corporateCapture.IsKeyboardFocused, "Corporate Inbox Enter writes and restores capture focus");
        await inbox.ConvertCommand.ExecuteAsync(inbox.SelectedItem); await Drain(corporate);
        inbox.Conversion!.DeadlineText = "20.12.2030 17:00";
        var conversionTitle = Find<TextBox>(corporate, "InboxConversionTitleBox"); conversionTitle.Focus();
        await PressNative(corporate, 0x0D, control: true); await Drain(corporate);
        Check(inbox.Conversion is null && !inbox.Items.Any(x => x.Title == "Company capture"), "Inbox task conversion Ctrl+Enter uses same validated command");
        shell.SelectedSection = shell.Sections.Single(s => s.Route == "projects"); projects.Activate(); await projects.RefreshCommand.ExecuteAsync();
        await projects.NewProjectCommand.ExecuteAsync(); await Drain(corporate);
        var corporateProjectName = Find<TextBox>(corporate, "ProjectNameTextBox"); corporateProjectName.Focus(); projects.Editor!.Name = "Corporate keyboard project";
        var projectDescription = Descendants<TextBox>(corporate).First(x => x.IsVisible && x.AcceptsReturn);
        projectDescription.Text = "Строка"; projectDescription.Focus(); await PressNative(corporate, 0x0D);
        Check(projectDescription.Text.Contains('\n') && projects.Editor is not null, "Ordinary Enter remains newline in corporate project");
        corporateProjectName.Focus();
        await PressNative(corporate, 0x0D, control: true); await Drain(corporate);
        Check(projects.Editor?.Name == "Corporate keyboard project" && projectClient.Items.Count == 0, "Corporate project failure preserves editor draft");
        await Capture(corporate, "corporate-project-error", 1200, 900);
        await Capture(corporate, "corporate-project-small", 800, 480);
        projectClient.Fail = false; corporateProjectName.Focus(); await PressNative(corporate, 0x0D, control: true); await Drain(corporate);
        Check(projects.Editor is null && projectClient.Items.Single().Name == "Corporate keyboard project", "Corporate project Ctrl+Enter saves using existing command");
        corporate.Close();

        using var model = new PersonalApplicationModel(Path.Combine(_root, "personal-fixture"));
        var personal = new PersonalWindow(model); personal.Show();
        var personalShell = (PersonalShellViewModel)personal.DataContext;
        var vm = model.Tasks!; await vm.RefreshAsync(); await Drain(personal);
        Check(Find<Button>(personal, "PersonalEmptyOpenTasks").IsVisible, "Personal Today empty offers existing tasks");
        await vm.OpenTasksCommand.ExecuteAsync(); await Drain(personal);
        Check(personalShell.SelectedSection.Route == "tasks", "Personal empty action navigates shell");
        Check(Find<Button>(personal, "PersonalEmptyCreateTask").IsVisible, "Personal Tasks empty offers creation");
        await Capture(personal, "personal-empty", 800, 480);
        await vm.NewCommand.ExecuteAsync(); await Drain(personal);
        var personalDate = Find<TextBox>(personal, "PersonalScheduledDate");
        Check(personalDate.IsVisible && !HasAncestor<Expander>(personalDate), "Personal day is outside advanced fields");
        Check(Find<Button>(personal, "PersonalSaveTask").Content.Equals("Создать задачу"), "Personal create label");
        personal.Activate(); var personalTitle = Find<TextBox>(personal, "PersonalTaskTitle"); personalTitle.Focus();
        Check(personalTitle.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)), "Personal Tab advances from title");
        Check(Keyboard.FocusedElement is ComboBox, "Personal Tab follows explicit priority order");
        vm.Editor!.Title = "Подготовить личный план"; vm.Editor.Card.Date = "20.12.2030";
        var personalDescription = Find<TextBox>(personal, "PersonalTaskDescription"); personalDescription.Text = "Строка";
        personalDescription.BringIntoView(); personalDescription.Focus(); await PressNative(personal, 0x0D);
        Check(personalDescription.Text.Contains('\n') && vm.Editor is not null, "Ordinary Enter remains newline in personal task");
        await Capture(personal, "personal-editor", 1200, 900);
        await Capture(personal, "personal-editor-small", 800, 480);
        personalDate.Focus(); personalDate.BringIntoView(); await Drain(personal);
        Check(personalDate.IsKeyboardFocused, "Personal day can be focused in minimum window");
        await CaptureFocused(personal, "personal-day-small", personalDate);
        personalDate.Focus(); await PressNative(personal, 0x0D, control: true); await Drain(personal);
        Check(vm.Editor is null, "Personal Ctrl+Enter uses shared save command");
        Check(Find<Button>(personal, "PersonalOpenSavedTask").IsVisible, "Personal saved result offers Open action");
        await vm.OpenSavedTaskCommand.ExecuteAsync(); await Drain(personal);
        Check(personalShell.SelectedSection.Route == "tasks" && vm.Selected?.Title == "Подготовить личный план", "Personal Open selects persisted task");
        await Capture(personal, "personal-saved", 1200, 900);
        await vm.EditCommand.ExecuteAsync(); await Drain(personal);
        Check(Find<Button>(personal, "PersonalSaveTask").Content.Equals("Сохранить изменения"), "Personal edit label");
        await vm.CancelCommand.ExecuteAsync();
        vm.Selected = vm.Items.Single();
        vm.CheckText = "Подтверждённый пункт"; await vm.AddCheckCommand.ExecuteAsync(); await Drain(personal);
        Descendants<Expander>(personal).Single(x => Equals(x.Header, "Сведения о задаче")).IsExpanded = true; await Drain(personal);
        var checkbox = Descendants<CheckBox>(personal).Single(x => AutomationProperties.GetName(x) == "Подтверждённый пункт");
        checkbox.Focus(); await PressNative(personal, 0x20); await Drain(personal);
        Check(vm.Checklist.Single().Completed, "Checklist Space writes through existing versioned command");
        Check(Descendants<CheckBox>(personal).Single(x => AutomationProperties.GetName(x) == "Подтверждённый пункт").IsChecked == true, "Checkbox matches confirmed result");
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(_root, "personal-fixture", "Personal", "tasks.db")};Pooling=False"))
        {
            connection.Open(); using var sql = connection.CreateCommand();
            sql.CommandText = "CREATE TRIGGER reject_toggle BEFORE UPDATE ON tasks BEGIN SELECT RAISE(ABORT, 'fixture'); END;"; sql.ExecuteNonQuery();
            checkbox = Descendants<CheckBox>(personal).Single(x => AutomationProperties.GetName(x) == "Подтверждённый пункт");
            checkbox.Focus(); await PressNative(personal, 0x20); await Drain(personal);
            Check(vm.Checklist.Single().Completed && checkbox.IsChecked == true, "Failed checkbox write retains confirmed tick");
            await Capture(personal, "personal-checklist-error", 1200, 900);
            sql.CommandText = "DROP TRIGGER reject_toggle;"; sql.ExecuteNonQuery();
        }
        checkbox.Focus(); await PressNative(personal, 0x20); await Drain(personal);
        Check(!vm.Checklist.Single().Completed, "Checklist Space can return item to incomplete");
        checkbox = Descendants<CheckBox>(personal).Single(x => AutomationProperties.GetName(x) == "Подтверждённый пункт");
        checkbox.Focus(); await PressNative(personal, 0x20); await Drain(personal);
        await Capture(personal, "personal-checklist", 1200, 900);
        await vm.TransitionCommand.ExecuteAsync(Task.Desktop.TaskApi.DesktopTaskStatus.Completed);
        vm.HideCompleted = true; await Drain(personal);
        Check(vm.Items.Count == 0 && vm.HasHiddenTasks && vm.Selected is null, "Filter clears hidden selection and shows explanation");
        await Capture(personal, "personal-filter", 800, 480);
        await vm.ShowCompletedCommand.ExecuteAsync();
        personalShell.SelectedSection = personalShell.Sections.Single(s => s.Route == "inbox"); await Drain(personal);
        var capture = Find<TextBox>(personal, "PersonalCaptureText"); capture.Focus(); vm.CaptureText = "Клавиатурная запись";
        await PressNative(personal, 0x0D); await Drain(personal);
        Check(vm.CaptureText == "" && capture.IsKeyboardFocused && vm.Items.Single().Title == "Клавиатурная запись", "Inbox Enter confirms write and retains capture focus");
        personalShell.SelectedSection = personalShell.Sections.Single(s => s.Route == "projects");
        await model.Planning!.NewProjectCommand.ExecuteAsync(); await Drain(personal);
        var projectName = Find<TextBox>(personal, "PersonalProjectName"); projectName.Focus(); model.Planning.ProjectName = "Клавиатурный проект";
        var personalProjectDescription = Descendants<TextBox>(personal).First(x => x.IsVisible && x.AcceptsReturn);
        personalProjectDescription.Text = "Строка"; personalProjectDescription.Focus(); await PressNative(personal, 0x0D);
        Check(personalProjectDescription.Text.Contains('\n') && model.Planning.IsProjectEditing, "Ordinary Enter remains newline in personal project");
        await Capture(personal, "personal-project", 1200, 900); await Capture(personal, "personal-project-small", 800, 480);
        projectName.Focus();
        await PressNative(personal, 0x0D, control: true); await Drain(personal);
        Check(!model.Planning.IsProjectEditing && model.Planning.Projects.Single().Name == "Клавиатурный проект", "Personal project Ctrl+Enter preserves save click path");
        personal.Close();
        var input = new TextBox { Text = "draft", DataContext = new object() };
        var next = new Button { Content = "Next" };
        var panel = new StackPanel(); panel.Children.Add(input); panel.Children.Add(next);
        var focusWindow = new Window { Content = new UserControl { Content = panel }, Width = 400, Height = 200 };
        using var focusCommand = new AsyncCommand((_, _) =>
        {
            input.Text = ""; input.DataContext = new object(); input.Visibility = Visibility.Collapsed; next.Focus();
            return System.Threading.Tasks.Task.CompletedTask;
        });
        Task.Desktop.Infrastructure.FormKeyboard.SetCapture(input, focusCommand);
        focusWindow.Show(); input.Focus(); await PressNative(focusWindow, 0x0D);
        Check(next.IsKeyboardFocused, "Deferred capture focus cannot enter hidden view or changed account context");
        focusWindow.Close();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    private static async System.Threading.Tasks.Task PressNative(Window window, byte key, bool control = false)
    {
        window.Activate();
        try
        {
            if (control) keybd_event(0x11, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, 0, UIntPtr.Zero); keybd_event(key, 0, 2, UIntPtr.Zero);
        }
        finally { if (control) keybd_event(0x11, 0, 2, UIntPtr.Zero); }
        await System.Threading.Tasks.Task.Delay(500); await Drain(window);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Checks.Add(name); Console.WriteLine("PASS " + name);
    }
    private static async System.Threading.Tasks.Task Drain(Window window)
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
    }
    private static async System.Threading.Tasks.Task Capture(Window window, string name, double width, double height)
    {
        window.Width = width; window.Height = height; await Drain(window);
        var visual = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
        foreach (var input in Descendants<TextBox>(visual).Where(x => x.IsVisible && AutomationProperties.GetAutomationId(x) is "TaskTitleTextBox" or "PersonalTaskTitle"))
        {
            for (var parent = VisualTreeHelper.GetParent(input); parent is not null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is ScrollViewer scroll) { scroll.ScrollToTop(); break; }
        }
        await Drain(window);
        Render(window, name, visual);
        Check(visual.ActualWidth > 0 && visual.ActualHeight > 0, $"Rendered {name} at {width}x{height}");
    }
    private static async System.Threading.Tasks.Task CaptureFocused(Window window, string name, TextBox field)
    {
        await Drain(window);
        var point = field.TranslatePoint(new Point(0, 0), window);
        Check(point.Y >= 0 && point.Y + field.ActualHeight <= window.ActualHeight, "Focused day is in window bounds: " + name);
        Render(window, name, (FrameworkElement)VisualTreeHelper.GetChild(window, 0));
    }
    private static void Render(Window window, string name, FrameworkElement visual)
    {
        foreach (var dpi in new[] { 96, 144 })
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth * dpi / 96), (int)Math.Ceiling(visual.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
            var backdrop = new DrawingVisual();
            using (var drawing = backdrop.RenderOpen()) drawing.DrawRectangle(window.Background ?? SystemColors.WindowBrush, null, new Rect(visual.RenderSize));
            bitmap.Render(backdrop);
            bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(_root, $"{name}-{dpi}.png")); encoder.Save(file);
        }
    }
    private static T Find<T>(DependencyObject root, string id) where T : FrameworkElement => Descendants<T>(root).Single(x => AutomationProperties.GetAutomationId(x) == id);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T item) yield return item;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static bool HasAncestor<T>(DependencyObject item) where T : DependencyObject
    {
        for (var parent = VisualTreeHelper.GetParent(item); parent is not null; parent = VisualTreeHelper.GetParent(parent)) if (parent is T) return true;
        return false;
    }
    private sealed class BindingErrors : TraceListener
    {
        public List<string> Messages { get; } = [];
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Messages.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
}
