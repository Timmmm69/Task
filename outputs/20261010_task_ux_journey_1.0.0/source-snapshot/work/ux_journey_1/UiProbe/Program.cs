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
        using var shell = new MainWindowViewModel(new Uri("https://fixture.invalid"), null, tasks, today: today);
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
        await Capture(corporate, "corporate-editor", 1200, 900);
        await Capture(corporate, "corporate-editor-small", 800, 480);
        date.Focus(); date.BringIntoView(); await Drain(corporate);
        Check(date.IsKeyboardFocused, "Corporate day can be focused in minimum window");
        await CaptureFocused(corporate, "corporate-day-small", date);
        tasks.SelectedStatusFilter = "В работе";
        await tasks.SaveEditorCommand.ExecuteAsync(); await Drain(corporate);
        var announcement = Find<TextBlock>(corporate, "TasksAnnouncement");
        Check(announcement.IsVisible && announcement.Opacity == 1 && announcement.Text.Contains("разделе «Задачи»"), "Corporate saved result is visible and names destination");
        Check(Find<Button>(corporate, "TasksOpenSavedTask").IsVisible, "Hidden saved task offers Open action");
        await tasks.OpenSavedTaskCommand.ExecuteAsync(); await Drain(corporate);
        Check(tasks.SelectedItem?.Title == "Подготовить договор", "Corporate Open selects saved task through filter");
        await Capture(corporate, "corporate-saved", 1200, 900);
        await tasks.EditTaskCommand.ExecuteAsync(); await Drain(corporate);
        Check(Find<Button>(corporate, "SaveTaskButton").Content.Equals("Сохранить изменения"), "Corporate edit label");
        await tasks.DiscardEditorCommand.ExecuteAsync();
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
        await Capture(personal, "personal-editor", 1200, 900);
        await Capture(personal, "personal-editor-small", 800, 480);
        personalDate.Focus(); personalDate.BringIntoView(); await Drain(personal);
        Check(personalDate.IsKeyboardFocused, "Personal day can be focused in minimum window");
        await CaptureFocused(personal, "personal-day-small", personalDate);
        await vm.SaveCommand.ExecuteAsync(); await Drain(personal);
        Check(Find<Button>(personal, "PersonalOpenSavedTask").IsVisible, "Personal saved result offers Open action");
        await vm.OpenSavedTaskCommand.ExecuteAsync(); await Drain(personal);
        Check(personalShell.SelectedSection.Route == "tasks" && vm.Selected?.Title == "Подготовить личный план", "Personal Open selects persisted task");
        await Capture(personal, "personal-saved", 1200, 900);
        await vm.EditCommand.ExecuteAsync(); await Drain(personal);
        Check(Find<Button>(personal, "PersonalSaveTask").Content.Equals("Сохранить изменения"), "Personal edit label");
        await vm.CancelCommand.ExecuteAsync(); personal.Close();
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
