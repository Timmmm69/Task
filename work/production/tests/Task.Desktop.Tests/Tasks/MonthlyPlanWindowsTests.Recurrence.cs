using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using Task.Desktop.Modes;
using Task.Desktop.TaskApi;
using Task.Desktop.Personal;

namespace Task.Desktop.Tests.TaskScreen;

public sealed partial class MonthlyPlanWindowsTests
{
    private static async System.Threading.Tasks.Task CheckMonthOnlyRecurrenceAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "task-recurrence-wpf-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var model = new PersonalApplicationModel(root);
            var source = model.Store.Create(new("Подготовить отчёт", DesktopTaskPriority.Normal,
                card: new() { Description = Instruction, PlanningMonth = "2027-12" }));
            model.Store.WriteChecklist(source.Id, source.Version, null, Steps[0], null, false);
            var window = new PersonalWindow(model, enableWindowsIntegrations: false) { Width = 1120, Height = 850 };
            try
            {
                window.Show();
                var shell = (PersonalShellViewModel)window.DataContext;
                await WaitFor(() => shell.CanNavigate);
                var navigation = Assert.IsType<ListBox>(window.FindName("SectionsList"));
                navigation.SelectedItem = shell.Sections.Single(section => section.Route == "calendar");
                Assert.True(shell.IsCalendar);
                var recurrence = model.Calendar!.Recurrence!;
                await WaitFor(() => recurrence.OpenCommand.CanExecute(null) && !model.IsBusy);
                Invoke(Find<Button>(window, "PersonalOpenRecurrences"));
                await WaitFor(() => Descendants(window).OfType<DatePicker>()
                    .Any(picker => picker.IsVisible && AutomationProperties.GetAutomationId(picker) == "RecurrenceStartDate"));

                await recurrence.LoadSourcesCommand.ExecuteAsync();
                var sourcePicker = Find<ComboBox>(window, "RecurrenceSourceTask");
                sourcePicker.SelectedItem = Assert.Single(recurrence.SourceTasks);
                await WaitFor(() => recurrence.Editor.MonthOnly);
                Assert.True(Find<CheckBox>(window, "RecurrenceMonthOnly").IsChecked);
                Assert.DoesNotContain(Descendants(window).OfType<DatePicker>(),
                    picker => picker.IsVisible && AutomationProperties.GetAutomationId(picker) == "RecurrenceStartDate");

                var frequency = Find<ComboBox>(window, "RecurrenceFrequency");
                frequency.SelectedValue = "yearly";
                Find<ComboBox>(window, "RecurrenceFirstMonth").SelectedValue = 2;
                Find<TextBox>(window, "RecurrenceFirstYear").Text = "2028";
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                await recurrence.PreviewCommand.ExecuteAsync();
                Assert.Contains("2028", recurrence.PreviewText);
                Assert.DoesNotContain("01.02", recurrence.PreviewText);
                Find<TextBox>(window, "RecurrenceFirstYear").BringIntoView();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Capture(window, "recurrence-month-only-yearly");

                Invoke(Find<Button>(window, "RecurrenceSave"));
                await WaitFor(() => recurrence.Selected?.Definition.MonthOnly == true && !recurrence.IsBusy);
                var saved = Assert.Single(recurrence.Occurrences);
                Assert.Equal(new DateOnly(2028, 2, 1), saved.LocalDate);
                var instance = model.Store.Get(saved.TaskId)!;
                Assert.Equal("2028-02", instance.Card!.PlanningMonth);
                Assert.Null(instance.Card.ScheduledDate);
                Assert.Null(instance.DeadlineAtUtc);
                Assert.Equal(Steps[0], Assert.Single(model.Store.Checklist(instance.Id)).Text);

                Find<TextBox>(window, "RecurrenceFirstYear").Text = "2029";
                Invoke(Find<Button>(window, "RecurrenceSave"));
                await WaitFor(() => recurrence.Selected?.Definition.OccurrenceStartDate == new DateOnly(2029, 2, 1) && !recurrence.IsBusy);
                Assert.Equal("yearly", recurrence.Editor.Frequency);
                Assert.Equal(2, recurrence.Editor.FirstMonth);
                Assert.True(Find<CheckBox>(window, "RecurrenceMonthOnly").IsChecked);
            }
            finally { window.Close(); }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
