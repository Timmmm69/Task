using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using Task.Desktop.Modes;
using Task.Desktop.Personal;
using Task.Desktop.Security;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;
using Task.Desktop.Views;
using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.Tests.TaskScreen;

public sealed partial class MonthlyPlanWindowsTests
{
    private const string ChildFlag = "TASK_MONTHLY_PLAN_WINDOWS_PROBE";
    private static readonly string LongTitle = string.Concat(Enumerable.Repeat("Подготовить и согласовать подробный отчёт по результатам работы. ", 7)).Trim();

    [Fact]
    public async AsyncTask BothShellsOpenCanonicalTaskFromKeyboardAndChooseTwoDayPreset()
    {
        if (Environment.GetEnvironmentVariable(ChildFlag) != "1")
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            start.ArgumentList.Add("vstest");
            start.ArgumentList.Add(typeof(MonthlyPlanWindowsTests).Assembly.Location);
            start.ArgumentList.Add($"/TestCaseFilter:FullyQualifiedName~{nameof(MonthlyPlanWindowsTests)}");
            start.Environment[ChildFlag] = "1";
            using var child = Process.Start(start)!;
            var stdout = child.StandardOutput.ReadToEndAsync();
            var stderr = child.StandardError.ReadToEndAsync();
            try
            {
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
                Assert.True(child.ExitCode == 0, await stdout + await stderr);
            }
            finally { if (!child.HasExited) child.Kill(entireProcessTree: true); }
            return;
        }

        var done = new TaskCompletionSource(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        var ui = new Thread(() =>
        {
            var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
            app.Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await CheckPersonalAsync();
                    await CheckCorporateAsync();
                    await CheckMonthOnlyRecurrenceAsync();
                    done.SetResult();
                }
                catch (Exception error) { done.SetException(error); }
                finally { app.Shutdown(); }
            });
            Dispatcher.Run();
        })
        { IsBackground = true };
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(45));
    }

    private static async AsyncTask CheckPersonalAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "task-month-wpf-" + Guid.NewGuid().ToString("N"));
        try
        {
            Guid taskId;
            using (var model = new PersonalApplicationModel(root))
            {
                var window = new PersonalWindow(model, enableWindowsIntegrations: false);
                var shell = (PersonalShellViewModel)window.DataContext;
                try
                {
                    window.Show();
                    await WaitFor(() => !model.IsBusy);
                    var navigation = Assert.IsType<ListBox>(window.FindName("SectionsList"));
                    navigation.SelectedItem = shell.Sections.Single(section => section.Route == "monthly-plan");
                    var plan = shell.MonthlyPlan!;
                    await WaitFor(() => !plan.IsLoading && plan.CanEnterTitle);
                    await CreateAndCheckLayout(window, plan, "personal");
                    var row = Assert.Single(plan.Rows);
                    taskId = row.Source.Id;
                    AssertNoFolderAction(window);
                    OpenWithEnter(window, row);
                    await WaitFor(() => shell.IsTasks && model.Tasks!.Selected?.Source.Id == row.Source.Id);
                    await model.Tasks!.EditCommand.ExecuteAsync();
                    model.Tasks.Editor!.Card.Description = Instruction;
                    ChooseDeadline(window, model.Tasks.Editor!, "PersonalDeadline");
                    await model.Tasks.SaveCommand.ExecuteAsync();
                    Assert.Equal(2880, model.Store.Get(row.Source.Id)!.Card!.DeadlineReminderOffsetMinutes);
                    Assert.Equal(row.Source.Id, Assert.Single(model.Store.Reminders()).TargetId);
                    var tasks = model.Tasks;
                    await ExerciseDocumentsAsync(window, plan, () => tasks.Documents!, () => tasks.Checklist,
                        text => tasks.CheckText = text, () => tasks.ToggleCheckCommand,
                        () => navigation.SelectedItem = shell.Sections.Single(section => section.Route == "monthly-plan"),
                        () => OpenWithEnter(window, Assert.Single(plan.Rows)),
                        () => shell.IsTasks && tasks.Selected?.Source.Id == taskId, Path.Combine(root, "documents"), "personal");
                    await WaitFor(() => tasks.TransitionCommand.CanExecute(DesktopTaskStatus.Completed));
                    Assert.True(await tasks.TransitionCommand.ExecuteAsync(DesktopTaskStatus.Completed));
                    Assert.True(model.Store.Get(taskId)!.Status == DesktopTaskStatus.Completed, tasks.Message);
                }
                finally { window.Close(); }
            }
            using var reopened = new PersonalApplicationModel(root);
            var completed = reopened.Store.Get(taskId)!;
            Assert.Equal(Instruction, completed.Card!.Description);
            Assert.Equal(DesktopTaskStatus.Completed, completed.Status);
            AssertChecklist(reopened.Store.Checklist(taskId));
            Assert.Equal(4, reopened.Store.PersonalLinks(taskId).Items.Count);
            var link = reopened.Store.PersonalLinks(taskId).Items[0];
            Assert.Throws<PersonalTransitionException>(() => reopened.Store.RemovePersonalLink(taskId, completed.Version, link.Id));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async AsyncTask CheckCorporateAsync()
    {
        var endpoint = Environment.GetEnvironmentVariable("TASK_MONTHLY_PLAN_E2E_URL");
        var dataDirectory = Environment.GetEnvironmentVariable("TASK_MONTHLY_PLAN_E2E_APPDATA");
        var server = new Uri(endpoint ?? "https://task.example.test");
        using var http = new HttpClient();
        using var session = dataDirectory is null ? null
            : new SessionService(new DesktopAuthApiClient(http, server.AbsoluteUri), new DesktopCredentialVault(dataDirectory),
                "Task monthly plan QA", ClientPlatform.Windows, "1.0.0");
        if (session is not null)
        {
            await session.RestoreAsync(CancellationToken.None);
            Assert.Equal(SessionAuthState.SignedIn, session.CurrentState);
        }
        IDesktopTasksApiClient client = session is null ? new ActionTasksClient() : new DesktopTasksApiClient(http, server, session);
        Guid taskId;
        var work = session is null ? null : new DesktopWorkApiClient(http, server, session);
        var capabilities = session?.CurrentSessionMetadata?.Capabilities ?? ["Task.Read", "Task.Create", "Task.Update", "Task.ChangeStatus"];
        using var tasks = new TasksViewModel(client, capabilities, work: work);
        using var shell = new MainWindowViewModel(server, null, tasks: tasks);
        var window = new MainWindow(shell);
        try
        {
            window.Show();
            var navigation = Assert.IsType<ListBox>(window.FindName("NavigationListBox"));
            navigation.SelectedItem = shell.Sections.Single(section => section.Route == "monthly-plan");
            var plan = shell.MonthlyPlan!;
            await WaitFor(() => !plan.IsLoading && plan.CanEnterTitle);
            Assert.False(shell.IsReadOnlyMode);
            Assert.Contains("запись доступна", shell.ConnectionContext);
            if (session is not null) await SelectVerificationYear(window, plan);
            await CreateAndCheckLayout(window, plan, "corporate");
            var row = Assert.Single(plan.Rows);
            taskId = row.Source.Id;
            AssertNoFolderAction(window);
            OpenWithClick(window);
            await WaitFor(() => shell.IsTasksSectionSelected && tasks.SelectedDetails?.Id == row.Source.Id);
            await tasks.EditTaskCommand.ExecuteAsync();
            await WaitFor(() => tasks.Editor is not null);
            window.UpdateLayout();
            tasks.Editor!.Card.Description = Instruction;
            ChooseDeadline(window, tasks.Editor!, "TaskDeadlineTextBox");
            var patch = tasks.Editor!.BuildPatchCommand()!;
            Assert.Equal(row.Source.Id, patch.Id);
            Assert.Contains("\"deadlineReminderOffsetMinutes\":2880", patch.CardPatch);
            if (session is null)
            {
                await tasks.CancelEditorCommand.ExecuteAsync();
                await tasks.DiscardEditorCommand.ExecuteAsync();
            }
            else
            {
                await tasks.SaveEditorCommand.ExecuteAsync();
                Assert.Null(tasks.Editor);
                await ExerciseDocumentsAsync(window, plan, () => tasks.Workspace!.Documents!, () => tasks.Workspace!.Checklist,
                    text => tasks.Workspace!.CheckText = text, () => tasks.Workspace!.ToggleCheckCommand,
                    () => shell.SelectedSection = shell.Sections.Single(section => section.Route == "monthly-plan"),
                    () => OpenWithEnter(window, Assert.Single(plan.Rows)),
                    () => shell.IsTasksSectionSelected && tasks.SelectedDetails?.Id == taskId,
                    Path.Combine(dataDirectory!, "task-documents-fixture"), "corporate");
            }
        }
        finally { window.CloseForAuthenticationTransition(); }
        if (work is not null) await CheckCorporateRestartAsync(client, server, taskId, capabilities, work);
    }

    private static async AsyncTask CheckCorporateRestartAsync(IDesktopTasksApiClient client, Uri server, Guid taskId,
        IEnumerable<string> capabilities, DesktopWorkApiClient work)
    {
        using var tasks = new TasksViewModel(client, capabilities, work: work);
        using var shell = new MainWindowViewModel(server, null, tasks: tasks);
        var window = new MainWindow(shell);
        try
        {
            window.Show();
            shell.SelectedSection = shell.Sections.Single(section => section.Route == "monthly-plan");
            var plan = shell.MonthlyPlan!;
            await WaitFor(() => !plan.IsLoading);
            await SelectVerificationYear(window, plan);
            var row = Assert.Single(plan.Rows, item => item.Source.Id == taskId);
            Assert.Equal(2880, row.Source.Card!.DeadlineReminderOffsetMinutes);
            Assert.Equal(Instruction, row.Source.Card.Description);
            OpenWithEnter(window, row);
            await WaitFor(() => tasks.SelectedDetails?.Id == taskId && tasks.CanComplete);
            await WaitFor(() => tasks.Workspace?.Documents?.Items.Count == 4);
            AssertChecklist(tasks.Workspace!.Checklist);
            Assert.True(await tasks.TransitionCommand.ExecuteAsync(DesktopTaskStatus.Completed));
            Assert.True(await tasks.ConfirmTransitionCommand.ExecuteAsync());
            var stored = Assert.IsType<DesktopTasksApiResult<DesktopTaskDto>.Succeeded>(await client.GetTaskByIdAsync(taskId));
            Assert.Equal(DesktopTaskStatus.Completed, stored.Value.Status);
            await WaitFor(() => tasks.Workspace?.Documents?.Items.Count == 4);
            AssertChecklist(tasks.Workspace!.Checklist);
            Assert.False(tasks.Workspace.Documents!.CanRemove);
            Assert.False(tasks.Workspace.Documents.CanAdd);
            shell.SelectedSection = shell.Sections.Single(section => section.Route == "monthly-plan");
            await WaitFor(() => !plan.IsLoading);
            Assert.Equal("Действий не требуется", Assert.Single(plan.Rows, item => item.Source.Id == taskId).UrgencyText);
            if (Environment.GetEnvironmentVariable("TASK_MONTHLY_PLAN_E2E_PAGED_YEAR") is { } pagedYear)
            {
                var firstPage = Assert.IsType<DesktopTasksApiResult<DesktopTaskPage>.Succeeded>(await client.GetTasksAsync());
                Assert.NotNull(firstPage.Value.NextCursor);
                await SelectVerificationYear(window, plan, pagedYear);
                Assert.Equal(125, plan.Rows.Select(row => row.Source.Id).Distinct().Count());
                Assert.Equal(125, plan.Rows.Count);
            }
        }
        finally { window.CloseForAuthenticationTransition(); }
    }

    private static async AsyncTask CreateAndCheckLayout(Window window, MonthlyTasksViewModel plan, string mode)
    {
        var input = Find<TextBox>(window, "MonthlyTitle");
        input.Text = LongTitle;
        var create = Find<Button>(window, "MonthlyCreate");
        Invoke(create);
        await WaitFor(() => plan.Rows.Count == 1 && !plan.IsSaving);
        Assert.Equal(LongTitle, Assert.Single(plan.Rows).Title);
        foreach (var (width, height) in new[] { (1200, 900), (1024, 768), (900, 600), (800, 480) })
        {
            window.Width = width;
            window.Height = height;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var list = Find<ListBox>(window, "MonthlyTasksList");
            list.ScrollIntoView(Assert.Single(plan.Rows));
            window.UpdateLayout();
            var item = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(0));
            var focusVisual = item.FocusVisualStyle;
            Assert.Same(item.FindResource("Task.FocusVisualStyle"), focusVisual);
            Assert.True(item.ActualWidth <= list.ActualWidth);
            Assert.True(item.ActualHeight <= list.ActualHeight - 2);
            Assert.Contains(LongTitle, AutomationProperties.GetName(item));
            Assert.InRange(Descendants(item).OfType<TextBlock>().Single(text => text.Text == LongTitle).ActualHeight, 1, 52);
            Assert.True(create.IsVisible);
            var month = Find<ComboBox>(window, "MonthlyMonth");
            month.IsDropDownOpen = true;
            Assert.True(month.IsDropDownOpen);
            month.IsDropDownOpen = false;
            Capture(window, $"{mode}-{width}x{height}");
        }
    }

    private static void ChooseDeadline(Window window, TaskEditorViewModel editor, string field)
    {
        var deadline = Find<TextBox>(window, field);
        deadline.Text = DateTime.Today.AddDays(4).ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture);
        var reminder = Find<ComboBox>(window, "TaskDeadlineReminder");
        reminder.BringIntoView();
        window.UpdateLayout();
        reminder.IsDropDownOpen = true;
        reminder.SelectedItem = Assert.Single(reminder.Items.OfType<TaskReminderOption>(), option => option.Text == "За 2 дня");
        reminder.IsDropDownOpen = false;
        Assert.Equal(2880, editor.ReminderOption.Minutes);
        Assert.Contains("Напомним", editor.ReminderPreview);
        Assert.True(Find<CheckBox>(window, "TaskPlanningMonthAutomatic").IsVisible);
    }

    private static void OpenWithEnter(Window window, MonthlyTaskRow row)
    {
        var list = Find<ListBox>(window, "MonthlyTasksList");
        list.SelectedItem = row;
        var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(list), 0, Key.Enter)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        list.RaiseEvent(key);
        Assert.True(key.Handled);
    }

    private static void OpenWithClick(Window window)
    {
        var list = Find<ListBox>(window, "MonthlyTasksList");
        var row = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(0));
        var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        { RoutedEvent = Mouse.PreviewMouseUpEvent };
        row.RaiseEvent(click);
        Assert.True(click.Handled);
    }

    private static async AsyncTask SelectVerificationYear(Window window, MonthlyTasksViewModel plan, string? year = null)
    {
        year ??= Environment.GetEnvironmentVariable("TASK_MONTHLY_PLAN_E2E_YEAR") ?? "2099";
        Find<TextBox>(window, "MonthlyYear").Text = year;
        var button = Find<Button>(window, "MonthlyApplyYear");
        Invoke(button);
        await WaitFor(() => !plan.IsLoading && plan.PeriodText.EndsWith(year, StringComparison.Ordinal));
    }

    private static void Invoke(Button button) =>
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();

    private static async AsyncTask WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await AsyncTask.Delay(25);
        Assert.True(condition());
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static T Find<T>(DependencyObject root, string id) where T : FrameworkElement =>
        Assert.Single(Descendants(root).OfType<T>(), e => e.IsVisible && AutomationProperties.GetAutomationId(e) == id);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("TASK_MONTHLY_PLAN_EVIDENCE");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }
}
