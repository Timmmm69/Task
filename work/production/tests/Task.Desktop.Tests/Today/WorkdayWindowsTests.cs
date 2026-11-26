using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Task.Desktop.Controls;
using Task.Desktop.Infrastructure;
using Task.Desktop.Modes;
using Task.Desktop.Personal;
using Task.Desktop.TaskApi;
using Task.Desktop.Tests.TaskScreen;
using Task.Desktop.ViewModels;
using Task.Desktop.Views;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.Tests.Today;

public sealed class WorkdayWindowsTests
{
    [Fact]
    public async AsyncTask Shells_DateFromFreeCellAndWorkdayCardsAtTwoSizes()
    {
        if (Environment.GetEnvironmentVariable("TASK_WORKDAY_UI_CHILD") != "1")
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("vstest");
            start.ArgumentList.Add(GetType().Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + GetType().FullName + "." + nameof(Shells_DateFromFreeCellAndWorkdayCardsAtTwoSizes));
            start.Environment["TASK_WORKDAY_UI_CHILD"] = "1";
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2)); }
            finally { if (!process.HasExited) process.Kill(true); }
            Assert.True(process.ExitCode == 0, await stdout + await stderr);
            return;
        }
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                var root = Path.Combine(Path.GetTempPath(), "task-workday-ui", Guid.NewGuid().ToString("N"));
                try
                {
                    var app = new App(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    var clock = new WorkdayResultTests.Clock(WorkdayResultTests.Morning);
                    using var model = new PersonalApplicationModel(root, clock);
                    var store = model.Store;
                    var today = DateOnly.FromDateTime(clock.Now.UtcDateTime);
                    foreach (var title in new[] { "Провести инвентаризацию", "Согласовать закупку", "Отправить макет" })
                    {
                        var task = store.Create(new(title, DesktopTaskPriority.Normal, card: new() { ScheduledDate = today }));
                        store.Transition(new(task.Id, 1, DesktopTaskStatus.Completed));
                    }
                    clock.Now = clock.Now.AddHours(10);
                    await using var personalPrefs = new ViewStateStore(root, "personal", store.LocalActorId, "device");
                    var personal = new PersonalWindow(model, false, personalPrefs);
                    try
                    {
                        Show(personal);
                        await Until(() => !model.IsBusy);
                        var shell = (PersonalShellViewModel)personal.DataContext;
                        await shell.Workday!.TickAsync();
                        await CheckCards(personal, shell.Workday, "personal");
                        if (Environment.GetEnvironmentVariable("TASK_WORKDAY_TEST_WINDOWS_SHELL") == "1")
                        {
                            using var tray = new PersonalWindowsNotifications(personal, tip: "Task · проверка уведомлений");
                            Assert.True(tray.IsAvailable);
                            Assert.True(tray.SubmitMessage("Task · проверка итога дня", WorkdayViewModel.Describe(new(3, 3))));
                        }
                        shell.SelectedSection = shell.Sections.First(s => s.Route == "calendar");
                        var noteDate = await CheckCalendar(personal, model.Calendar!, "personal", () => model.Tasks!.HasCaptureScheduledDate);
                        Assert.Equal(noteDate, model.Tasks!.CaptureScheduledDate);
                        Assert.Null(model.Tasks.Editor);
                        Assert.Equal(3, store.List().Count);
                    }
                    finally { personal.Close(); }

                    await using var corporatePrefs = new ViewStateStore(root, "corp/org", store.LocalActorId, "device");
                    var tasks = new PersonalTasksClient(store);
                    var cal = new PersonalCalendarClient(store);
                    string[] caps = ["Calendar.Read", "CalendarEvent.Create", "Task.Read", "Task.Create", "History.Read", "Inbox.ReadOwn", "Inbox.ManageOwn"];
                    var calendar = new CalendarViewModel(cal, caps, clock: clock.GetUtcNow, timeZone: TimeZoneInfo.Utc);
                    var summary = new TodayViewModel(cal, caps, TimeZoneInfo.Utc, clock.GetUtcNow, tasks, store.LocalActorId);
                    var inboxClient = new InboxViewModelTests.FakeInboxClient();
                    var inbox = new InboxViewModel(inboxClient, caps);
                    using var corporate = new MainWindowViewModel(new Uri("https://task.test"), null, calendar: calendar, today: summary, inbox: inbox, viewState: corporatePrefs)
                    { Workday = model.CreateWorkday(corporatePrefs) };
                    var window = new MainWindow(corporate);
                    try
                    {
                        Show(window);
                        await corporate.Workday!.TickAsync();
                        await CheckCards(window, corporate.Workday, "corporate");
                        corporate.SelectedSection = corporate.Sections.First(s => s.Route == "calendar");
                        var date = await CheckCalendar(window, calendar, "corporate", () => inbox.HasCaptureScheduledDate);
                        Assert.Equal(date, inbox.CaptureScheduledDate);
                        Assert.Null(inboxClient.LastCreate);
                        inbox.CaptureText = "Черновик";
                        corporate.SelectedSection = corporate.Sections.First(s => s.Route == "calendar");
                        await Until(() => !calendar.IsBusy);
                        Assert.False(calendar.RecordThoughtCommand.CanExecute(date));
                        inbox.CaptureText = ""; inbox.ClearCalendarDate();
                        calendar.UpdateConnectivity(false);
                        Assert.False(calendar.RecordThoughtCommand.CanExecute(date));
                    }
                    finally { window.Close(); }
                    finished.SetResult();
                }
                catch (Exception ex) { finished.TrySetException(ex); }
                finally
                {
                    dispatcher.InvokeShutdown();
                    if (Directory.Exists(root)) Directory.Delete(root, true);
                }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(95));
    }

    private static void Show(Window window)
    {
        window.ShowInTaskbar = false; window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000; window.Width = 1180; window.Height = 800; window.Show();
    }

    private static async AsyncTask CheckCards(Window window, WorkdayViewModel model, string mode)
    {
        await Until(() => Descendants<WorkdayFooter>(window).Any(f => f.IsVisible) && model.Phrase.Length > 0);
        var footer = Descendants<WorkdayFooter>(window).Single(f => f.IsVisible);
        var phrase = model.Phrase;
        var next = Descendants<Button>(footer).Single(b => Equals(b.Content, "Другая фраза"));
        Invoke(next);
        await Until(() => model.Phrase != phrase);
        var day = (Expander)footer.FindName("DayCard");
        var week = (Expander)footer.FindName("WeekCard");
        day.IsExpanded = true; week.IsExpanded = true;
        await Until(() => model.DayText.Contains("3 из 3") && model.WeekText.Contains("Ещё 1 задача завершена"));
        footer.BringIntoView(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var cheer = (Button)footer.FindName("Cheer");
        Save(window, mode + "-today-normal.png");
        Assert.True(cheer.IsVisible, $"Cheer hidden; expanded={day.IsExpanded}, actual={day.ActualHeight}");
        Assert.True(cheer.Focusable && cheer.IsTabStop);
        window.Activate(); Keyboard.Focus(cheer);
        Assert.True(cheer.IsKeyboardFocused || cheer.IsFocused, "Cheer could not receive focus.");
        var inputSource = PresentationSource.FromVisual(cheer)!;
        cheer.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, inputSource, Environment.TickCount, Key.Space) { RoutedEvent = Keyboard.KeyDownEvent });
        cheer.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, inputSource, Environment.TickCount, Key.Space) { RoutedEvent = Keyboard.KeyUpEvent });
        var confetti = (Canvas)footer.FindName("Confetti");
        if (SystemParameters.ClientAreaAnimation) await Until(() => confetti.Children.Count == 9);
        Save(window, mode + "-today-normal.png");
        footer.PlayReaction(false);
        Assert.Empty(confetti.Children);
        Assert.Same(SystemColors.HighlightBrush, cheer.BorderBrush);
        await Until(() => cheer.ReadLocalValue(Control.BorderBrushProperty) == DependencyProperty.UnsetValue);

        var logo = Descendants<WinkingLogo>(window).Single();
        var face = (Canvas)logo.FindName("Face");
        long now = Stopwatch.GetTimestamp();
        for (var i = 0; i < 3; i++) logo.RegisterPress(now + i, true);
        Assert.True(face.HasAnimatedProperties);
        await Until(() => face.Opacity > 0.9);
        Save(logo, mode + "-logo-wink.png");
        await Until(() => face.Opacity == 0);
        for (var i = 0; i < 3; i++) logo.RegisterPress(now + i, false);
        Assert.Equal(0d, face.Opacity);
        window.Width = 820; window.Height = 600;
        footer.BringIntoView(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
        Assert.True(footer.ActualWidth <= window.ActualWidth);
        Save(window, mode + "-today-compact.png");
    }

    private static async Task<DateOnly> CheckCalendar(Window window, CalendarViewModel calendar, string mode, Func<bool> opened)
    {
        await Until(() => calendar.Days.Count > 0 && !calendar.IsBusy);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        var cells = Descendants<FrameworkElement>(window).Where(element => element.IsVisible && element is Grid or Border
            && element.ContextMenu is not null && element.DataContext is CalendarDayViewModel day && day.Date.ToDateTime(TimeOnly.MinValue) != calendar.SelectedDate
            && element.ContextMenu.Items.OfType<MenuItem>().Any(item => Equals(item.Header, "Записать мысль"))).ToArray();
        Assert.True(cells.Length > 0, $"No free cell in {mode}. Mode={calendar.ViewMode}. Visible day elements={Descendants<FrameworkElement>(window).Count(e => e.IsVisible && e.DataContext is CalendarDayViewModel)}. Menus={Descendants<FrameworkElement>(window).Count(e => e.IsVisible && e.ContextMenu != null)}");
        var cell = cells[0];
        var target = ((CalendarDayViewModel)cell.DataContext).Date;
        cell.BringIntoView(); var menu = cell.ContextMenu!;
        menu.PlacementTarget = cell; menu.Placement = PlacementMode.Center; menu.IsOpen = true;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var item = menu.Items.OfType<MenuItem>().Single();
        Assert.True(item.IsEnabled); Assert.Equal(cell.DataContext, item.CommandParameter);
        Save(menu, mode + "-calendar-menu.png");
        ((IInvokeProvider)new MenuItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)).Invoke();
        await Until(opened);
        window.UpdateLayout(); Save(window, mode + "-calendar-draft.png");
        return target;
    }

    private static void Invoke(Button button) =>
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();

    private static async AsyncTask Until(Func<bool> condition)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition() && deadline.Elapsed < TimeSpan.FromSeconds(8))
            await AsyncTask.Delay(25);
        Assert.True(condition(), "WPF did not reach the expected state.");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T result) yield return result;
            foreach (var element in Descendants<T>(child)) yield return element;
        }
    }

    private static void Save(FrameworkElement element, string name)
    {
        if (Environment.GetEnvironmentVariable("TASK_WORKDAY_UI_EVIDENCE") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, name)); encoder.Save(output);
    }
}
