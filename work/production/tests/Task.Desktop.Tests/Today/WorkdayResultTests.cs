using System.IO;
using Task.Application;
using Task.Desktop.Infrastructure;
using Task.Desktop.Modes;
using Task.Desktop.Personal;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Today;

public sealed class WorkdayResultTests : IDisposable
{
    internal sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static readonly DateOnly Friday = new(2026, 11, 27);
    internal static readonly DateTimeOffset Morning = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "task-workday", Guid.NewGuid().ToString("N"));
    private readonly Guid _user = Guid.NewGuid();
    private ViewStateStore Preferences(string mode = "personal") => new(_root, mode, _user, "test");
    internal static DesktopUserSettings Settings => new(1, "ru-RU", "24h", 1, "09:00", "18:00", [6, 7], 60, 10, false, false, true, "show_actions");

    [Fact]
    public async System.Threading.Tasks.Task PhrasesAdvanceWithoutRepetitionAcrossRestartsWeekendsAndManualReplacement()
    {
        var clock = new Clock(Morning);
        using var model = new PersonalApplicationModel(_root, clock);
        var seen = new HashSet<string>();
        string last;
        await using (var state = Preferences())
        using (var vm = model.CreateWorkday(state)!)
        {
            await vm.TickAsync();
            for (var i = 0; i < WorkdayPhrases.All.Length; i++)
            {
                if (i > 0) await vm.AnotherPhraseCommand.ExecuteAsync();
                Assert.True(seen.Add(vm.Phrase));
            }
            last = vm.Phrase;
        }
        await using var reloaded = Preferences();
        using var next = model.CreateWorkday(reloaded)!;
        clock.Now = Morning.AddDays(1);
        await next.TickAsync();
        Assert.Equal(last, next.Phrase);
        clock.Now = Morning.AddDays(3);
        await next.TickAsync();
        Assert.Equal(WorkdayPhrases.All[0], next.Phrase);
    }

    [Fact]
    public void WeekAndCutoffUseSavedFirstDayWeekendAndTimeZone()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
        var calendar = WorkdayCalendar.From(Settings with { TimeZone = zone.Id, FirstDayOfWeek = 7, WeekendDays = [5, 6] }, TimeZoneInfo.Utc)!;
        var thursday = Friday.AddDays(-1);
        var end = calendar.Instant(thursday, new(18, 0));
        Assert.Equal(9, end.UtcDateTime.Hour);
        Assert.Equal(thursday.AddDays(-1), calendar.LastClosedDay(end.AddTicks(-1)));
        Assert.Equal((thursday.AddDays(-4), thursday), calendar.LastClosedWeek(end));
        Assert.Equal(calendar.LastClosedWeek(end), calendar.LastClosedWeek(end.AddDays(3)));
        Assert.Null(WorkdayCalendar.From(Settings with { WeekendDays = [1, 2, 3, 4, 5, 6, 7] }, zone)!.LastClosedDay(end));
    }

    [Fact]
    public void SqliteResultUsesPlanAtCutoffEvenAfterMoveCompletionAndRestart()
    {
        var clock = new Clock(Morning);
        var cutoff = Morning.AddHours(9);
        var day = new TaskTodayWindow(Friday, Morning.AddHours(-9), Morning.AddHours(15));
        Guid rescheduled;
        using (var store = new PersonalTaskStore(new(_root), clock))
        {
            var completed = store.Create(new("Согласовать закупку", DesktopTaskPriority.Normal, deadlineAtUtc: Morning.AddHours(1)));
            store.Transition(new(completed.Id, 1, DesktopTaskStatus.Completed));
            rescheduled = store.Create(new("Перенесена вечером", DesktopTaskPriority.Normal, card: new() { ScheduledDate = Friday })).Id;
            var movedBefore = store.Create(new("Перенесена утром", DesktopTaskPriority.Normal, card: new() { ScheduledDate = Friday }));
            store.Patch(new(movedBefore.Id, 1, cardPatch: """{"scheduledDate":"2026-11-30"}"""));
            var late = store.Create(new("Завершена вечером", DesktopTaskPriority.Normal, card: new() { ScheduledDate = Friday }));
            clock.Now = cutoff.AddMinutes(2);
            store.Patch(new(rescheduled, 1, cardPatch: """{"scheduledDate":"2026-11-30"}"""));
            store.Transition(new(late.Id, 1, DesktopTaskStatus.Completed));
        }
        using var restarted = new PersonalTaskStore(new(_root), clock);
        Assert.Equal(new WorkdayResult(1, 3), restarted.ReadDay(day, cutoff, TimeZoneInfo.Utc));
        var week = restarted.ReadWeek(day.FromUtc.AddDays(-4), cutoff);
        Assert.Equal(1, week.Total);
        Assert.Equal("Согласовать закупку", Assert.Single(week.Items).Title);
    }

    [Fact]
    public async System.Threading.Tasks.Task DayEndDeliverySkipsClosedAppEmptyAndDisabledAndClaimsOncePerMode()
    {
        var clock = new Clock(Morning);
        var count = 0;
        WorkdayResult? result = null;
        bool Present(string text) { Assert.Contains("из 4", text); count++; return true; }
        WorkdayViewModel Create(ViewStateStore store) => new(store, clock,
            _ => System.Threading.Tasks.Task.FromResult<DesktopUserSettings?>(Settings),
            (_, _, _) => System.Threading.Tasks.Task.FromResult(result),
            (_, _, _) => System.Threading.Tasks.Task.FromResult<WorkweekResults?>(null), () => true, () => true,
            _ => System.Threading.Tasks.Task.FromResult(true));

        await using var state = Preferences();
        using var vm = Create(state);
        await vm.TickAsync(Present);
        clock.Now = Morning.AddHours(9);
        await vm.TickAsync(Present);
        Assert.Equal(0, count);
        result = new(3, 4);
        clock.Now = clock.Now.AddMinutes(2);
        await vm.TickAsync(Present);
        await vm.TickAsync(Present);
        Assert.Equal(1, count);
        using var restarted = Create(state);
        await restarted.TickAsync(Present);
        Assert.Equal(1, count);
        await restarted.OpenDayAsync();
        Assert.Contains("3 из 4", restarted.DayText);
        clock.Now = Morning.AddDays(3);
        await vm.TickAsync(Present);
        vm.NotifyAtDayEnd = false;
        clock.Now = clock.Now.AddHours(9);
        await vm.TickAsync(Present);
        Assert.Equal(1, count);
        vm.NotifyAtDayEnd = true;
        result = new(0, 0);
        await vm.TickAsync(Present);
        Assert.Equal(1, count);

        // A second session spanning the same cutoff shares the durable claim.
        clock.Now = Morning;
        using var earlier = Create(state);
        await using var corporate = Preferences("company");
        using var otherMode = Create(corporate);
        clock.Now = Morning.AddHours(9);
        result = new(4, 4);
        await earlier.TickAsync(Present);
        Assert.Equal(1, count);
        await otherMode.TickAsync(Present);
        Assert.Equal(2, count);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
