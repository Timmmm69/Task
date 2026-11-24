using System.IO;
using Microsoft.Data.Sqlite;
using Task.Desktop.Modes;
using Task.Desktop.Personal;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;
using Task.Domain;

namespace Task.Desktop.Tests.TaskScreen;

public sealed class MonthlyPlanTests
{
    [Fact]
    public async System.Threading.Tasks.Task MonthNavigationFiltersAndRetriesTheSameCreate()
    {
        var clock = new Clock(new(2026, 12, 10, 0, 0, 0, TimeSpan.Zero));
        var completed = ActionTasksClient.Source("Завершённая") with { Status = DesktopTaskStatus.Completed, Card = new() { PlanningMonth = "2027-01" } };
        var january = ActionTasksClient.Source("Январская") with { Card = new() { ScheduledDate = new(2027, 1, 31) } };
        var undated = ActionTasksClient.Source("Без месяца");
        var client = new ActionTasksClient(completed, january, undated);
        Guid? opened = null;
        using var plan = new MonthlyTasksViewModel(client, () => true, () => true, (id, _) =>
        {
            opened = id;
            return System.Threading.Tasks.Task.FromResult(true);
        }, clock);

        await plan.ActivateAsync();
        Assert.Equal("2026", plan.YearText);
        Assert.Empty(plan.Rows);
        await plan.NextCommand.ExecuteAsync();
        Assert.Equal("2027", plan.YearText);
        Assert.Equal(1, plan.SelectedMonth);
        Assert.Equal(2, plan.Rows.Count);
        plan.HideCompleted = true;
        Assert.Equal(january.Id, Assert.Single(plan.Rows).Source.Id);
        await plan.OpenCommand.ExecuteAsync(Assert.Single(plan.Rows));
        Assert.Equal(january.Id, opened);
        await plan.WithoutMonthCommand.ExecuteAsync();
        Assert.Equal(undated.Id, Assert.Single(plan.Rows).Source.Id);
        await plan.PreviousCommand.ExecuteAsync();
        Assert.Equal("Без месяца", plan.PeriodText);
        await plan.WithoutMonthCommand.ExecuteAsync();

        client.Failure = new DesktopTaskWriteResult<DesktopTaskDto>.ServerUnavailable();
        plan.Title = "  Подготовить сводку  ";
        await plan.CreateCommand.ExecuteAsync();
        Assert.Equal("  Подготовить сводку  ", plan.Title);
        await plan.PreviousCommand.ExecuteAsync();
        await plan.NextCommand.ExecuteAsync();
        client.Failure = null;
        await plan.CreateCommand.ExecuteAsync();
        Assert.Same(client.Creates[0], client.Creates[1]);
        var request = client.Creates[1];
        Assert.Equal("Подготовить сводку", request.Title);
        Assert.Equal("2027-01", request.Card!.PlanningMonth);
        Assert.Null(request.StartAtUtc);
        Assert.Null(request.DeadlineAtUtc);
        Assert.Null(request.Card.DeadlineReminderOffsetMinutes);
        Assert.Equal("", plan.Title);
        Assert.Equal(2, plan.Rows.Count);
        plan.YearText = "0001";
        await plan.ApplyYearCommand.ExecuteAsync();
        Assert.False(plan.PreviousCommand.CanExecute(null));
        await plan.CurrentCommand.ExecuteAsync();
        Assert.Equal(12, plan.SelectedMonth);
        Assert.Equal("2026", plan.YearText);
    }

    [Fact]
    public async System.Threading.Tasks.Task PersonalUsesOneTaskAfterRestartAndClosesItsReminder()
    {
        var root = Path.Combine(Path.GetTempPath(), "task-month-test-" + Guid.NewGuid().ToString("N"));
        var clock = new Clock(new(2026, 12, 10, 0, 0, 0, TimeSpan.Zero));
        Guid id;
        try
        {
            using (var model = new PersonalApplicationModel(root, clock))
            using (var shell = new PersonalShellViewModel(model))
            {
                var tasks = model.Tasks!;
                var plan = tasks.MonthlyPlan;
                shell.SelectedSection = shell.Sections.Single(section => section.Route == "monthly-plan");
                await WaitForLoad(plan);
                plan.Title = "План без даты";
                await plan.CreateCommand.ExecuteAsync();
                var stored = Assert.Single(model.Store.List());
                id = Assert.Single(plan.Rows).Source.Id;
                Assert.Equal(id, stored.Id);
                Assert.Equal("2026-12", stored.Card!.PlanningMonth);
                Assert.Null(stored.Card.ScheduledDate);
                Assert.Null(stored.DeadlineAtUtc);
                Assert.Empty(model.Store.Reminders());
                shell.SelectedSection = shell.Sections.Single(section => section.Route == "tasks");
                Assert.Equal(id, Assert.Single(tasks.Items).Source.Id);
                shell.SelectedSection = shell.Sections.Single(section => section.Route == "monthly-plan");
                await WaitForLoad(plan);

                await plan.OpenCommand.ExecuteAsync(Assert.Single(plan.Rows));
                Assert.Equal("tasks", shell.SelectedSection.Route);
                Assert.Equal(id, tasks.Selected!.Source.Id);
                await tasks.EditCommand.ExecuteAsync();
                var editor = tasks.Editor!;
                Assert.False(editor.Card.DerivePlanningMonth);
                editor.Card.PlanningMonthText = "2027-01";
                editor.DeadlineText = "15.12.2026";
                editor.ReminderOption = editor.ReminderOptions.Single(option => option.Text == "За 2 дня");
                await tasks.SaveCommand.ExecuteAsync();
                Assert.Equal(id, Assert.Single(model.Store.Reminders()).TargetId);
            }

            using (var restarted = new PersonalApplicationModel(root, clock))
            using (var shell = new PersonalShellViewModel(restarted))
            {
                var tasks = restarted.Tasks!;
                var plan = tasks.MonthlyPlan;
                shell.SelectedSection = shell.Sections.Single(section => section.Route == "monthly-plan");
                await WaitForLoad(plan);
                Assert.Empty(plan.Rows);
                await plan.NextCommand.ExecuteAsync();
                await plan.OpenCommand.ExecuteAsync(Assert.Single(plan.Rows));
                var stored = tasks.Selected!.Source;
                Assert.Equal(id, stored.Id);
                Assert.Equal(2880, stored.Card!.DeadlineReminderOffsetMinutes);
                Assert.Equal(new DateOnly(2026, 12, 15), stored.Card.DeadlineDate);
                await tasks.EditCommand.ExecuteAsync();
                tasks.Editor!.Card.DerivePlanningMonth = true;
                Assert.Contains("\"planningMonth\":null", tasks.Editor.BuildPatchCommand()!.CardPatch);
                await tasks.SaveCommand.ExecuteAsync();
                Assert.Equal("2026-12", restarted.Store.Get(id)!.Card!.EffectivePlanningMonth);
                await tasks.TransitionCommand.ExecuteAsync(DesktopTaskStatus.Completed);
                clock.Now = new(2026, 12, 14, 0, 0, 0, TimeSpan.Zero);
                Assert.Equal(0, restarted.Store.ReconcileReminders());
                Assert.Empty(restarted.Store.Notifications());
                Assert.Equal(DesktopTaskStatus.Completed, restarted.Store.Get(id)!.Status);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task DateOnlyUrgencyFollowsDeadlineZoneAndIgnoresReminderPreference()
    {
        var clock = new Clock(new(2026, 12, 28, 20, 59, 59, TimeSpan.Zero));
        var dateOnly = ActionTasksClient.Source("Срок по Минску") with
        {
            Card = new TaskCardContent { DeadlineDate = new(2026, 12, 31), DeadlineTimeZone = "Europe/Minsk", DeadlineReminderOffsetMinutes = -1 }
        };
        using var plan = new MonthlyTasksViewModel(new ActionTasksClient(dateOnly), () => true, () => true,
            (_, _) => System.Threading.Tasks.Task.FromResult(true), clock);
        await plan.ActivateAsync();
        var row = Assert.Single(plan.Rows);
        Assert.Equal("Срок установлен", row.UrgencyText);
        clock.Now = clock.Now.AddSeconds(1);
        plan.RefreshUrgency();
        Assert.Equal("Срок близко", row.UrgencyText);
        clock.Now = clock.Now.AddDays(2);
        plan.RefreshUrgency();
        Assert.Equal("Срок сегодня", row.UrgencyText);
        clock.Now = clock.Now.AddDays(1);
        plan.RefreshUrgency();
        Assert.Equal("Просрочена", row.UrgencyText);
        Assert.Equal("Запланирована", row.StatusText);
        Assert.Equal(MonthlyUrgency.Closed, MonthlyDeadline.Evaluate(dateOnly with { Status = DesktopTaskStatus.Cancelled }, clock.Now, TimeZoneInfo.Utc).Urgency);
        Assert.Equal(MonthlyUrgency.NoDeadline, MonthlyDeadline.Evaluate(dateOnly with { Card = new() }, clock.Now, TimeZoneInfo.Utc).Urgency);

        var exact = dateOnly with { DeadlineAtUtc = clock.Now.AddHours(1), Card = null };
        Assert.Equal(MonthlyUrgency.Today, MonthlyDeadline.Evaluate(exact, clock.Now, TimeZoneInfo.Utc).Urgency);
        Assert.Equal(MonthlyUrgency.Overdue, MonthlyDeadline.Evaluate(exact, exact.DeadlineAtUtc.Value.AddTicks(1), TimeZoneInfo.Utc).Urgency);
    }

    private static async System.Threading.Tasks.Task WaitForLoad(MonthlyTasksViewModel plan)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (plan.IsLoading) await System.Threading.Tasks.Task.Delay(10, timeout.Token);
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
