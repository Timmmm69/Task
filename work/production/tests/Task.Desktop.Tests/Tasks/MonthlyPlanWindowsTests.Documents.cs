using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Tests.TaskScreen;

public sealed partial class MonthlyPlanWindowsTests
{
    private const string Instruction = "Сверьте входящие данные и используйте рабочую папку.";
    private static readonly string[] Steps = ["Сверить данные", "Открыть рабочую папку"];

    private static async System.Threading.Tasks.Task ExerciseDocumentsAsync(Window window, MonthlyTasksViewModel plan,
        Func<TaskDocumentsViewModel> documents, Func<IReadOnlyList<TaskWorkspaceItem>> checklist,
        Action<string> setStep, Func<AsyncCommand> toggleStep, Action goToPlan,
        Action reopenCard, Func<bool> cardOpened, string root, string mode)
    {
        await WaitFor(() => documents()?.CanAddPath == true);
        foreach (var step in Steps)
        {
            var count = checklist().Count;
            setStep(step);
            await WaitFor(() => Find<Button>(window, "TaskChecklistAdd").IsEnabled);
            Invoke(Find<Button>(window, "TaskChecklistAdd"));
            await WaitFor(() => checklist().Count == count + 1);
        }
        await WaitFor(() => toggleStep().CanExecute(checklist()[0]));
        Assert.True(await toggleStep().ExecuteAsync(checklist()[0]));
        await WaitFor(() => checklist().FirstOrDefault()?.Completed == true);
        AssertChecklist(checklist());

        Directory.CreateDirectory(root);
        var unc = Environment.GetEnvironmentVariable("TASK_MONTHLY_DOCUMENTS_UNC_ROOT");
        var first = Directory.CreateDirectory(Path.Combine(unc is null ? root : Path.Combine(unc, mode), "Первая папка")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(root, "Вторая папка")).FullName;
        var missing = Directory.CreateDirectory(Path.Combine(root, "Недоступная папка")).FullName;
        var file = Path.Combine(root, "Памятка.txt");
        File.WriteAllText(file, "Синтетический пример");
        var launch = Environment.GetEnvironmentVariable("TASK_MONTHLY_DOCUMENTS_LAUNCH") == "1";

        async System.Threading.Tasks.Task AddPath(string path)
        {
            await WaitFor(() => documents().CanAddPath && !documents().IsMutating);
            var count = documents().Items.Count;
            Find<Expander>(window, "TaskDocumentAttach").IsExpanded = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Find<TextBox>(window, "TaskDocumentPath").Text = path;
            Invoke(Find<Button>(window, "TaskDocumentLinkPath"));
            await WaitFor(() => documents().Items.Count == count + 1 && !documents().IsMutating);
        }

        await AddPath(first);
        goToPlan();
        await WaitFor(() => !plan.IsLoading && plan.Rows.SingleOrDefault()?.HasFolders == true);
        var folder = Find<Button>(window, "MonthlyFolder");
        if (launch)
        {
            Invoke(folder);
            await WaitFor(() => plan.Message == "Папка открыта в Проводнике.");
        }
        reopenCard();
        await WaitFor(() => cardOpened() && documents()?.Items.Count == 1);
        AssertChecklist(checklist());

        await AddPath(second);
        await AddPath(missing);
        await AddPath(file);
        Directory.Delete(missing);
        var unavailable = Assert.Single(documents().Items, item => item.Name == Path.GetFileName(missing));
        var open = Descendants(window).OfType<Button>().Single(button => button.IsVisible
            && AutomationProperties.GetAutomationId(button) == "TaskDocumentOpen" && Equals(button.CommandParameter, unavailable));
        Invoke(open);
        await WaitFor(() => documents().Message.Contains("недоступны", StringComparison.Ordinal));
        Assert.Contains(unavailable, documents().Items);

        var fileRow = Assert.Single(documents().Items, item => item.Name == Path.GetFileName(file));
        if (launch)
        {
            await documents().OpenCommand.ExecuteAsync(fileRow);
            Assert.Equal("Файл передан Windows для открытия.", documents().Message);
        }
        Assert.True(await documents().RemoveCommand.ExecuteAsync(fileRow));
        await WaitFor(() => documents().Items.Count == 3 && documents().CanAdd);
        Assert.Equal("Синтетический пример", File.ReadAllText(file));

        Find<Expander>(window, "TaskDocumentAttach").IsExpanded = true;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Find<TextBox>(window, "TaskDocumentQuery").Text = Path.GetFileName(file);
        Invoke(Find<Button>(window, "TaskDocumentSearch"));
        await WaitFor(() => documents().Choices.Any(choice => choice.Id == fileRow.TargetId));
        Find<ComboBox>(window, "TaskDocumentChoice").SelectedItem = Assert.Single(documents().Choices, choice => choice.Id == fileRow.TargetId);
        Invoke(Find<Button>(window, "TaskDocumentLink"));
        await WaitFor(() => documents().Items.Count == 4 && documents().CanAddPath);
        Assert.Equal(fileRow.TargetId, Assert.Single(documents().Items, item => item.Name == Path.GetFileName(file)).TargetId);

        Find<Expander>(window, "TaskDocumentAttach").IsExpanded = true;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Find<TextBox>(window, "TaskDocumentPath").Text = first;
        Invoke(Find<Button>(window, "TaskDocumentLinkPath"));
        await WaitFor(() => documents().Message == "Эта ссылка уже есть у задачи.");
        Assert.Equal(4, documents().Items.Count);
        Find<TextBox>(window, "TaskDocumentPath").Text = "";
        Find<Expander>(window, "TaskDocumentAttach").IsExpanded = false;
        open = Descendants(window).OfType<Button>().First(button => button.IsVisible
            && AutomationProperties.GetAutomationId(button) == "TaskDocumentOpen");
        open.BringIntoView();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Capture(window, $"{mode}-task-documents");

        goToPlan();
        await WaitFor(() => !plan.IsLoading && plan.Rows.SingleOrDefault()?.HasFolders == true);
        Invoke(Find<Button>(window, "MonthlyFolder"));
        await WaitFor(() => plan.FolderChoices.Count == 2);
        var menu = Assert.Single(PresentationSource.CurrentSources.Cast<PresentationSource>()
            .Select(source => source.RootVisual).OfType<Visual>().SelectMany(Descendants).OfType<ContextMenu>(), control => control.IsOpen);
        Assert.Equal(new[] { Path.GetFileName(second), Path.GetFileName(first) }.Order(),
            menu.Items.OfType<MenuItem>().Select(item => ((TextBlock)item.Header).Text).Order());
        if (launch)
        {
            var item = (MenuItem)menu.Items[0];
            ((IInvokeProvider)new MenuItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)!).Invoke();
            await WaitFor(() => plan.Message == "Папка открыта в Проводнике.");
        }
        menu.IsOpen = false;
        Capture(window, $"{mode}-monthly-folder-action");
        reopenCard();
        await WaitFor(() => cardOpened() && documents()?.Items.Count == 4);
    }

    private static void AssertNoFolderAction(Window window) =>
        Assert.DoesNotContain(Descendants(window).OfType<Button>(), button =>
            button.IsVisible && AutomationProperties.GetAutomationId(button) == "MonthlyFolder");

    private static void AssertChecklist(IReadOnlyList<TaskWorkspaceItem> checklist)
    {
        Assert.Equal(Steps, checklist.Select(item => item.Text));
        Assert.True(checklist[0].Completed);
        Assert.False(checklist[1].Completed);
    }
}
