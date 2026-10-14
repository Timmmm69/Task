using System.IO;
using Microsoft.Data.Sqlite;
using Task.Desktop.Calendar;
using Task.Desktop.Personal;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;
using Task.Domain;
using Task.Domain.Reminders;

namespace Task.Desktop.Tests.Personal;

public sealed class AutomaticDeadlineReminderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Task-auto-deadline", Guid.NewGuid().ToString("N"));
    private PersonalDataPaths Paths => new(_root);
    private readonly Clock _clock = new(new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero));
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    { public DateTimeOffset Now = now; public override DateTimeOffset GetUtcNow() => Now; }
    private PersonalTaskStore Open() => new(Paths, _clock);
    private DesktopTaskDto Create(PersonalTaskStore store, int minutes = 60, int? offset = null) => store.Create(new("Срок", DesktopTaskPriority.Normal,
        deadlineAtUtc: _clock.Now.AddMinutes(minutes), card: new() { DeadlineReminderOffsetMinutes = offset }));

    [Fact]
    public void CreatePatchRemoveAndExplicitOptOut_AreTransactionalAndPersistAcrossRestart()
    {
        Guid id;
        using (var store = Open())
        {
            store.Create(new("Без срока", DesktopTaskPriority.Normal)); Assert.Empty(store.Reminders());
            var task = Create(store); id = task.Id;
            var rule = Assert.Single(store.Reminders()); Assert.Equal(_clock.Now.AddMinutes(45), rule.DueAt);
            task = store.Patch(new(id, task.Version, deadlineAtUtc: DesktopTaskField<DateTimeOffset?>.From(_clock.Now.AddHours(2))));
            Assert.Equal(rule.Id, Assert.Single(store.Reminders()).Id); Assert.Equal(_clock.Now.AddMinutes(105), store.Reminders()[0].DueAt);
            task = store.Patch(new(id, task.Version, cardPatch: "{\"deadlineReminderOffsetMinutes\":-1}"));
            task = store.Patch(new(id, task.Version, title: DesktopTaskField<string>.From("Изменено")));
            Assert.Equal(-1, task.Card!.DeadlineReminderOffsetMinutes); Assert.Equal("cancelled", store.Reminders()[0].State);
            store.SaveWorkspaceSettings(store.WorkspaceSettings() with { DefaultReminderOffsetMinutes = 30 });
            Assert.Equal(-1, store.Get(id)!.Card!.DeadlineReminderOffsetMinutes);
            Assert.Equal(30, Create(store).Card!.DeadlineReminderOffsetMinutes);
            store.Patch(new(id, task.Version, deadlineAtUtc: DesktopTaskField<DateTimeOffset?>.From(null)));
        }
        using var restarted = Open(); Assert.Equal(-1, restarted.Get(id)!.Card!.DeadlineReminderOffsetMinutes);
        Assert.DoesNotContain(restarted.Reminders(), r => r.TargetId == id && r.State == "pending");
    }

    [Fact]
    public void ElapsedTrigger_DeliversOnceManualCollisionDedupesAndSnoozeKeepsHistory()
    {
        using var store = Open(); var task = Create(store, 10);
        store.AddReminder(task.Id, DesktopScheduleItemType.Task, ReminderTriggerType.BeforeDeadline, 15);
        store.AddReminder(task.Id, DesktopScheduleItemType.Task, ReminderTriggerType.Absolute, absolute: _clock.Now.AddMinutes(2));
        Assert.Equal(1, store.ReconcileReminders()); Assert.Equal(0, store.ReconcileReminders());
        var notification = Assert.Single(store.Notifications()); Assert.Single(store.ClaimPresentations());
        store.CompletePresentation(notification.Id, true); Assert.Empty(store.ClaimPresentations());
        store.SnoozeNotification(notification.Id, _clock.Now.AddMinutes(1));
        _clock.Now = _clock.Now.AddMinutes(1); Assert.Equal(1, store.ReconcileReminders());
        Assert.Equal(2, store.Notifications().Count); Assert.True(store.Notifications().Single(n => n.Id == notification.Id).IsRead);
        _clock.Now = _clock.Now.AddMinutes(1); Assert.Equal(1, store.ReconcileReminders()); Assert.Equal(3, store.Notifications().Count);
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("cancel")]
    [InlineData("archive")]
    [InlineData("trash")]
    [InlineData("remove")]
    public void TerminalOrRemovedTask_DoesNotDeliverStaleReminder(string action)
    {
        using var store = Open(); var task = Create(store);
        if (action is "complete" or "cancel") store.Transition(new(task.Id, task.Version, action == "complete" ? DesktopTaskStatus.Completed : DesktopTaskStatus.Cancelled));
        else if (action == "remove") store.Patch(new(task.Id, task.Version, deadlineAtUtc: DesktopTaskField<DateTimeOffset?>.From(null)));
        else store.ChangeWorkspaceLifecycle(task.Id, task.Version, action);
        _clock.Now = _clock.Now.AddMinutes(50); Assert.Equal(0, store.ReconcileReminders()); Assert.Empty(store.Notifications());
    }

    [Fact]
    public void Restore_RearmsUndeliveredFutureDeadlineButNeverRepeatsDeliveredTime()
    {
        using var store = Open(); var task = Create(store);
        store.ChangeWorkspaceLifecycle(task.Id, task.Version, "archive");
        var item = store.WorkspaceObjects().Single(o => o.Id == task.Id);
        store.ChangeWorkspaceLifecycle(task.Id, item.Version, "unarchive");
        _clock.Now = _clock.Now.AddMinutes(50); Assert.Equal(1, store.ReconcileReminders());
        item = store.WorkspaceObjects().Single(o => o.Id == task.Id); store.ChangeWorkspaceLifecycle(task.Id, item.Version, "archive");
        item = store.WorkspaceObjects().Single(o => o.Id == task.Id); store.ChangeWorkspaceLifecycle(task.Id, item.Version, "unarchive");
        Assert.Equal(0, store.ReconcileReminders()); Assert.Single(store.Notifications());
    }

    [Fact]
    public void DateOnlyDeadline_IsSeparateFromCalendarAndUsesFrozenLocalHourAndCustomOffset()
    {
        using var store = Open(); store.SaveWorkspaceSettings(store.WorkspaceSettings() with { DateOnlyReminderTime = "09:00", DefaultReminderOffsetMinutes = 30 });
        var card = new TaskCardContent { DeadlineDate = new(2026, 10, 15), DeadlineTimeZone = "Europe/Minsk", ScheduledDate = new(2026, 10, 16), DeadlineReminderOffsetMinutes = 60 };
        var task = store.Create(new("Только день", DesktopTaskPriority.Normal, card: card));
        Assert.Null(task.DeadlineAtUtc); Assert.Null(task.StartAtUtc); Assert.Equal(card.ScheduledDate, task.Card!.ScheduledDate);
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 5, 0, 0, TimeSpan.Zero), Assert.Single(store.Reminders()).DueAt);
        store.SaveWorkspaceSettings(store.WorkspaceSettings() with { DateOnlyReminderTime = "11:00", DefaultReminderOffsetMinutes = 5 });
        store.ReconcileReminders(); Assert.Equal("09:00", store.Get(task.Id)!.Card!.DeadlineReminderTime); Assert.Equal(60, store.Get(task.Id)!.Card!.DeadlineReminderOffsetMinutes);
    }

    [Theory]
    [InlineData("desktop")]
    [InlineData("quiet")]
    [InlineData("disabled")]
    [InlineData("silent")]
    public void ExistingPresentationPreferences_AreHonoredAfterAutomaticDelivery(string mode)
    {
        using var store = Open(); Create(store, 10);
        var p = store.WorkspaceNotificationPreferences();
        p = mode switch
        {
            "desktop" => p with { DesktopEnabled = false },
            "disabled" => p with { Enabled = false },
            "silent" => p with { SoundEnabled = false },
            _ => p with { QuietHoursStart = "07:00", QuietHoursEnd = "09:00", QuietHoursTimeZone = "UTC" }
        };
        store.SaveWorkspaceNotifications(p); Assert.Equal(1, store.ReconcileReminders()); Assert.Single(store.Notifications());
        if (mode == "silent") { Assert.Single(store.ClaimPresentations()); Assert.False(store.WorkspaceNotificationPreferences().SoundEnabled); }
        else Assert.Empty(store.ClaimPresentations());
    }

    [Fact]
    public void CommandReplayAndRestart_ReturnSameTaskAndDoNotMultiplyRules()
    {
        var command = new DesktopCreateTaskCommand("Повтор", DesktopTaskPriority.Normal, deadlineAtUtc: _clock.Now.AddHours(1)); Guid id;
        using (var store = Open()) { var first = store.Create(command); id = first.Id; Assert.Equal(id, store.Create(command).Id); Assert.Single(store.List()); Assert.Single(store.Reminders()); }
        using var restarted = Open(); Assert.Equal(id, restarted.Create(command).Id);
        var patch = new DesktopPatchTaskCommand(id, 1, deadlineAtUtc: DesktopTaskField<DateTimeOffset?>.From(_clock.Now.AddMinutes(10)));
        Assert.Equal(2, restarted.Patch(patch).Version); Assert.Equal(2, restarted.Patch(patch).Version);
        Assert.Equal(1, restarted.ReconcileReminders()); Assert.Equal(0, restarted.ReconcileReminders()); Assert.Single(restarted.Reminders());
    }

    [Fact]
    public void UpgradeV4_BackfillsFutureRulesWithoutAvalancheAndRetainsManualAndOverdueData()
    {
        using (var db = new PersonalDatabase(Paths, targetVersion: 4))
        {
            using var c = db.Connection.CreateCommand();
            c.CommandText = "INSERT INTO tasks(id,version,created_at,updated_at,title,status,priority,deadline) VALUES($id,1,$now,$now,$title,0,1,$due);";
            foreach (var (minutes, title) in new[] { (60, "future"), (10, "elapsed"), (-10, "overdue") })
            { c.Parameters.Clear(); c.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); c.Parameters.AddWithValue("$now", _clock.Now.ToString("O")); c.Parameters.AddWithValue("$title", title); c.Parameters.AddWithValue("$due", _clock.Now.AddMinutes(minutes).ToString("O")); c.ExecuteNonQuery(); }
        }
        using (var store = Open())
        {
            Assert.Equal(5, PersonalDatabaseValidation.ValidateFile(Paths.DatabasePath)); Assert.Equal(3, store.List().Count);
            Assert.Equal(0, store.ReconcileReminders()); Assert.Empty(store.Notifications());
            _clock.Now = _clock.Now.AddMinutes(50); Assert.Equal(1, store.ReconcileReminders()); Assert.Single(store.Notifications());
        }
        using var reopened = Open(); Assert.Equal(0, reopened.ReconcileReminders()); Assert.Single(reopened.Notifications());
    }

    [Fact]
    public void Editor_ParsesDateOnlyAndKeepsExplicitOptOutAfterOtherEdits()
    {
        var editor = new TaskEditorViewModel(TaskEditorMode.Create, personal: true) { Title = "День", DeadlineText = "15.10.2026" };
        editor.ReminderOption = editor.ReminderOptions.Single(o => o.Minutes == -1);
        var command = editor.BuildCreateCommand()!; Assert.Null(command.DeadlineAtUtc); Assert.Equal(new DateOnly(2026, 10, 15), command.Card!.DeadlineDate);
        Assert.Contains("выключено", editor.ReminderPreview); editor.Title = "Новый текст"; Assert.Equal(-1, editor.BuildCreateCommand()!.Card!.DeadlineReminderOffsetMinutes);
    }

    [Fact]
    public void Dst_UsesFirstValidMinuteAndEarliestOverlapInstant()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 0, 0, TimeSpan.Zero), DeadlineReminderTime.Anchor(new(2026, 3, 29), new(2, 30), zone));
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), DeadlineReminderTime.Anchor(new(2026, 10, 25), new(2, 30), zone));
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
