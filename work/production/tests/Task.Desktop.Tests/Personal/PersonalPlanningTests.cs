using System.IO;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Task.Application.Calendar;
using Task.Desktop.Calendar;
using Task.Desktop.Modes;
using Task.Desktop.Personal;
using Task.Desktop.Projects;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;
using Task.Domain;
using Task.Domain.Recurrence;
using Task.Domain.Reminders;

namespace Task.Desktop.Tests.Personal;

public sealed class PersonalPlanningTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Task-planning-tests", Guid.NewGuid().ToString("N"));
    private PersonalDataPaths Paths => new(_root);
    private readonly Clock _clock = new(new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero));
    internal const string RemovePlanningSchema = """
        DROP VIEW personal_objects; DROP INDEX personal_tasks_active; DROP INDEX personal_projects_active;
        DROP TABLE object_links; DROP TABLE personal_file_locations; DROP TABLE personal_catalog;
        DROP TABLE personal_contacts; DROP TABLE personal_lifecycle; DROP TABLE personal_workspace_settings;
        DROP TRIGGER personal_task_project_insert; DROP TRIGGER personal_task_project_update;
        DROP TABLE personal_notifications; DROP TABLE personal_reminders; DROP TABLE personal_occurrences;
        DROP TABLE personal_series; DROP TABLE personal_events; DROP TABLE personal_projects; DROP TABLE personal_recurrence_commands;
        """;
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private PersonalTaskStore Open() => new(Paths, _clock);
    private void Sql(string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Paths.DatabasePath, Pooling = false }.ToString());
        connection.Open(); using var c = connection.CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery();
    }
    private static string Key() => Guid.NewGuid().ToString("N");
    private static RecurrenceDefinition Definition(PersonalTaskStore store, string frequency = "daily", bool allDay = false) => new()
    {
        Frequency = frequency == "workdays" ? "daily" : frequency,
        Interval = 1,
        Weekdays = frequency == "workdays" ? [1, 2, 3, 4, 5] : frequency == "weekly" ? [1, 3] : [],
        MonthDays = frequency is "monthly" or "yearly" ? [5] : [],
        MonthOfYear = frequency == "yearly" ? 10 : null,
        OccurrenceStartDate = new(2026, 10, 5),
        LocalStartTime = allDay ? null : new(9, 0),
        TimeZone = "UTC",
        Template = new() { Title = "Local recurring", Description = "Saved template", AuthorUserId = store.LocalActorId, PlannedDurationMinutes = allDay ? null : 45 }
    };
    private DesktopRecurrenceSeries CreateSeries(PersonalTaskStore store, RecurrenceDefinition definition) =>
        store.ExecuteRecurrence(new DesktopRecurrenceCommand.Save(null, null, definition, Key())).Series!;
    private static IReadOnlyList<RecurrenceOccurrenceDetails> Occurrences(PersonalTaskStore store, Guid id) =>
        store.ExecuteRecurrence(new DesktopRecurrenceCommand.Occurrences(id)).Occurrences!;
    [Fact]
    public void Projects_CreateEditStatusLifecycleAndTaskRelationSurviveRestart()
    {
        PersonalProject project; DesktopTaskDto task;
        using (var store = Open())
        {
            project = store.SaveProject(new(Guid.Empty, 0, "Personal project", "Description", DesktopProjectStatus.Planning, new(2026, 10, 5), new(2026, 12, 1), DefaultTimeZone: "UTC", ColorCode: "#336699"));
            task = store.Create(new("Project task", DesktopTaskPriority.High, card: new() { ProjectId = project.Id }));
            project = store.SaveProject(project with { Name = "Edited", Status = DesktopProjectStatus.Active });
            Assert.Throws<PersonalVersionConflictException>(() => store.SaveProject(project with { Version = 1 }));
            Assert.Throws<ArgumentException>(() => store.Create(new("Unknown project", DesktopTaskPriority.Normal, card: new() { ProjectId = Guid.NewGuid() })));
        }
        using var reopened = Open();
        Assert.Equal(project, Assert.Single(reopened.Projects())); Assert.Equal(project.Id, reopened.Get(task.Id)!.Card!.ProjectId);
        var archived = reopened.SaveProject(project with { Lifecycle = "archived", Status = DesktopProjectStatus.Completed, ActualEndAt = _clock.GetUtcNow() });
        Assert.Equal(3, archived.Version); Assert.Empty(reopened.Get(task.Id)!.AssigneeIds);
        Assert.Throws<ArgumentException>(() => reopened.Create(new("Archived project", DesktopTaskPriority.Normal, card: new() { ProjectId = project.Id })));
        Assert.DoesNotContain(typeof(PersonalProject).GetProperties(), p => p.Name.Contains("User") || p.Name.Contains("Member") || p.Name.Contains("Owner"));
    }
    [Fact]
    public async System.Threading.Tasks.Task Calendar_AllDayTimedProjectUpdateAndOverlapSurviveRestart()
    {
        DesktopCalendarEvent saved; Guid taskId;
        using (var store = Open())
        {
            var project = store.SaveProject(new(Guid.Empty, 0, "Project", null, DesktopProjectStatus.Active, null, null));
            var client = new PersonalCalendarClient(store);
            var date = new DateOnly(2026, 10, 5); var start = _clock.Now.AddHours(1);
            var allDay = store.SaveEvent(null, null, new(project.Id, "All-day", "date only", date, true, null, null, "UTC"));
            saved = store.SaveEvent(null, null, new(project.Id, "Timed", "first", date, false, start, start.AddHours(1), "UTC"));
            saved = store.SaveEvent(saved.Id, saved.Version, new(project.Id, "Edited event", "second", date, false, start, start.AddHours(1), "UTC"));
            taskId = store.Create(new("Scheduled", DesktopTaskPriority.High, start.AddMinutes(30), card: new() { ProjectId = project.Id, PlannedDurationMinutes = 45 })).Id;
            var dateTask = store.Create(new("Date-only", DesktopTaskPriority.Normal, card: new() { ScheduledDate = date }));
            var page = Assert.IsType<DesktopCalendarResult<DesktopSchedulePage>.Succeeded>(await client.GetScheduleAsync(start.AddHours(-2), start.AddDays(1), "UTC", default)).Value;
            Assert.Equal(4, page.Items.Count); Assert.True(page.Items.Single(i => i.ObjectId == dateTask.Id).IsAllDay);
            Assert.True(page.Items.Single(i => i.ObjectId == allDay.Id).IsAllDay);
            Assert.Equal(DesktopConflictSeverity.Warning, Assert.Single(store.Conflicts(start.AddHours(-2), start.AddDays(1))).Severity);
            Assert.Throws<PersonalVersionConflictException>(() => store.SaveEvent(saved.Id, 1, new(null, "Stale", null, date, true, null, null, "UTC")));
            Assert.Throws<ArgumentException>(() => store.SaveEvent(null, null, new(null, "Corp attendee", null, date, true, null, null, "UTC", Attendees: [new(Guid.NewGuid(), true, "required", "pending", null)])));
        }
        using var reopened = Open(); Assert.Equal(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(reopened.GetEvent(saved.Id)));
        Assert.Equal(saved.StartAtUtc!.Value.AddMinutes(30), reopened.Get(taskId)!.StartAtUtc);
        Assert.Equal(2, reopened.GetEvent(saved.Id)!.Version); Assert.Empty(reopened.GetEvent(saved.Id)!.Attendees);
    }
    [Theory]
    [InlineData("daily")]
    [InlineData("workdays")]
    [InlineData("weekly")]
    [InlineData("monthly")]
    [InlineData("yearly")]
    public void EveryExistingFrequency_ReopenGenerateSameHorizonHasNoDuplicates(string frequency)
    {
        DesktopRecurrenceSeries series; string before; int count;
        using (var store = Open())
        {
            var definition = Definition(store, frequency);
            series = CreateSeries(store, definition);
            var expected = RecurrenceGenerator.GenerateDates(definition.ToRule(), definition.OccurrenceStartDate, definition.OccurrenceStartDate.AddDays(62));
            Assert.Equal(expected, Occurrences(store, series.Id).Select(o => o.LocalDate));
            count = store.List().Count; before = JsonSerializer.Serialize(store.List());
        }
        using var reopened = Open();
        var response = reopened.ExecuteRecurrence(new DesktopRecurrenceCommand.Generate(series.Id, series.Version, new(2026, 12, 6), Key()));
        Assert.Equal(0, response.GeneratedCount); Assert.Equal(count, reopened.List().Count); Assert.Equal(before, JsonSerializer.Serialize(reopened.List()));
        Assert.Equal(count, Occurrences(reopened, series.Id).Select(o => o.TaskId).Distinct().Count());
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RecurringDateOnlyAndExactTime_PreserveLocalUtcShape(bool allDay)
    {
        Guid seriesId;
        using (var store = Open())
        {
            var series = CreateSeries(store, Definition(store, allDay: allDay) with { MaxOccurrences = 2 }); seriesId = series.Id;
            Assert.Equal(2, Occurrences(store, series.Id).Count);
        }
        using var reopened = Open();
        foreach (var occurrence in Occurrences(reopened, seriesId))
        {
            var task = reopened.Get(occurrence.TaskId)!;
            Assert.Equal(occurrence.LocalDate, task.Card!.ScheduledDate);
            if (allDay) { Assert.Null(task.StartAtUtc); Assert.Null(task.Card.StartTimeLocal); }
            else { Assert.Equal(new TimeOnly(9, 0), task.Card.StartTimeLocal); Assert.Equal(occurrence.LocalDate.ToDateTime(new(9, 0), DateTimeKind.Utc), task.StartAtUtc!.Value.UtcDateTime); }
        }
    }
    [Theory]
    [InlineData(3, 8, 2, 30, 7, 0, "shifted_forward")]
    [InlineData(11, 1, 1, 30, 5, 30, "earlier_offset")]
    public void RecurrenceDstGapAndOverlap_UseExistingPolicyPersistAdjustedLocalTime(int month, int day, int hour, int minute, int utcHour, int utcMinute, string adjustment)
    {
        var date = new DateOnly(2026, month, day); Guid taskId;
        using (var store = Open())
        {
            var definition = Definition(store) with { OccurrenceStartDate = date, LocalStartTime = new(hour, minute), TimeZone = "America/New_York", MaxOccurrences = 1 };
            var series = CreateSeries(store, definition); taskId = Assert.Single(Occurrences(store, series.Id)).TaskId;
            Assert.Equal(adjustment, RecurrenceService.PreviewDate(definition, date).DstAdjustment);
        }
        using var reopened = Open(); var task = reopened.Get(taskId)!;
        Assert.Equal(new DateTimeOffset(2026, month, day, utcHour, utcMinute, 0, TimeSpan.Zero), task.StartAtUtc);
        var local = TimeZoneInfo.ConvertTime(task.StartAtUtc!.Value, TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
        Assert.Equal(TimeOnly.FromDateTime(local.DateTime), task.Card!.StartTimeLocal);
        Assert.Equal(date, task.Card.ScheduledDate);
    }
    [Fact]
    public void ManualCalendarDstGapRejected_OverlapExplicitlyChoosesEarlierInstant()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var gap = new CalendarEventEditorViewModel(null, zone, personal: true) { Title = "Gap", Date = new(2026, 3, 8), StartTime = "02:30", EndTime = "03:30" };
        Assert.False(gap.TryBuild(zone, out _)); Assert.Contains("отсутствует", gap.ValidationMessage);
        var overlap = new CalendarEventEditorViewModel(null, zone, personal: true) { Title = "Overlap", Date = new(2026, 11, 1), StartTime = "01:30", EndTime = "02:30" };
        Assert.True(overlap.TryBuild(zone, out var command)); Assert.Equal(new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero), command.StartAtUtc);
        Assert.Throws<ArgumentException>(() => PersonalTimePolicy.ToUtc(new(2026, 3, 8, 2, 30, 0), zone));
        Assert.Equal(command.StartAtUtc, PersonalTimePolicy.ToUtc(new(2026, 11, 1, 1, 30, 0), zone));
        Assert.False(overlap.AllowAttendees);
        var card = new TaskCardContent { ScheduledDate = new(2026, 11, 1), StartTimeLocal = new(1, 30), ScheduleTimeZone = zone.Id };
        card.Validate(command.StartAtUtc, allowEarlierAmbiguousInstant: true);
        Assert.Throws<ArgumentException>(() => card.Validate(command.StartAtUtc));
        Assert.Throws<ArgumentException>(() => card.Validate(command.StartAtUtc!.Value.AddHours(1), allowEarlierAmbiguousInstant: true));
    }
    [Fact]
    public void UntilAndCount_EnforceExistingTerminationAndPauseResumeCancel()
    {
        using var store = Open();
        var until = CreateSeries(store, Definition(store) with { UntilDate = new(2026, 10, 7) }); Assert.Equal(3, Occurrences(store, until.Id).Count);
        var count = CreateSeries(store, Definition(store) with { MaxOccurrences = 2 }); Assert.Equal(2, Occurrences(store, count.Id).Count);
        var series = CreateSeries(store, Definition(store));
        var paused = store.ExecuteRecurrence(new DesktopRecurrenceCommand.SetStatus(series.Id, series.Version, "paused", Key())).Series!;
        Assert.Equal(0, store.ExecuteRecurrence(new DesktopRecurrenceCommand.Generate(paused.Id, paused.Version, new(2027, 1, 1), Key())).GeneratedCount);
        var resumed = store.ExecuteRecurrence(new DesktopRecurrenceCommand.SetStatus(paused.Id, paused.Version, "active", Key())).Series!;
        var generated = store.ExecuteRecurrence(new DesktopRecurrenceCommand.Generate(resumed.Id, resumed.Version, new(2027, 1, 1), Key())).Series!;
        Assert.True(Occurrences(store, generated.Id).Count > 63);
        var cancelled = store.ExecuteRecurrence(new DesktopRecurrenceCommand.SetStatus(generated.Id, generated.Version, "cancelled", Key())).Series!;
        Assert.All(Occurrences(store, cancelled.Id), o => Assert.Equal("cancelled", o.Status));
        Assert.Throws<ArgumentException>(() => store.ExecuteRecurrence(new DesktopRecurrenceCommand.SetStatus(cancelled.Id, cancelled.Version, "active", Key())));
    }
    [Theory]
    [InlineData(RecurrenceChangeScope.ThisOccurrence)]
    [InlineData(RecurrenceChangeScope.ThisAndFuture)]
    [InlineData(RecurrenceChangeScope.EntireSeries)]
    public void ScopedChanges_PreserveCompletedAndIndependentOverridesAcrossRestart(RecurrenceChangeScope scope)
    {
        Guid seriesId; long seriesVersion;
        using (var store = Open())
        {
            var series = CreateSeries(store, Definition(store) with { MaxOccurrences = 5 }); seriesId = series.Id;
            var occurrences = Occurrences(store, series.Id);
            store.Transition(new(occurrences[0].TaskId, 1, DesktopTaskStatus.Completed));
            store.Patch(new(occurrences[4].TaskId, 1, title: DesktopTaskField<string>.From("Manual change")));
            var target = occurrences[2];
            var response = store.ExecuteRecurrence(new DesktopRecurrenceCommand.Apply(series.Id, series.Version, target.LocalDate, target.TaskVersion, scope, "Scoped title", "high", 30, Key()));
            seriesVersion = response.Series!.Version;
            var after = Occurrences(store, seriesId);
            Assert.Equal("completed", after[0].Status); Assert.Equal("Manual change", after[4].Title);
            Assert.Equal("Scoped title", after[2].Title);
            Assert.Equal(scope == RecurrenceChangeScope.EntireSeries ? "Scoped title" : "Local recurring", after[1].Title);
            Assert.Equal(scope == RecurrenceChangeScope.ThisOccurrence ? "Local recurring" : "Scoped title", after[3].Title);
        }
        using var reopened = Open();
        var old = reopened.RecurrenceSeries().Single(s => s.Id == seriesId);
        reopened.ExecuteRecurrence(new DesktopRecurrenceCommand.Save(seriesId, seriesVersion, old.Definition with { Template = old.Definition.Template with { Title = "Rule edit" } }, Key()));
        var afterPatch = Occurrences(reopened, seriesId);
        Assert.Equal(scope == RecurrenceChangeScope.ThisOccurrence ? "Scoped title" : "Rule edit", afterPatch[2].Title);
        Assert.Equal("Manual change", afterPatch[4].Title); Assert.Equal("completed", afterPatch[0].Status);
    }
    [Fact]
    public void RecurrenceCommandReplayAndSqlFailure_AreAtomicAcrossReopen()
    {
        DesktopRecurrenceCommand.Save request; int taskCount;
        using (var store = Open())
        {
            request = new(null, null, Definition(store), Key()); var reply = store.ExecuteRecurrence(request); taskCount = store.List().Count;
            Assert.Equal(reply.Series!.Id, store.ExecuteRecurrence(request).Series!.Id);
        }
        using (var store = Open())
        {
            Assert.Equal(taskCount, store.List().Count); Assert.Single(store.RecurrenceSeries());
            Assert.Equal(taskCount, store.ExecuteRecurrence(request).GeneratedCount);
            Assert.Throws<ArgumentException>(() => store.ExecuteRecurrence(request with { Definition = request.Definition with { Template = request.Definition.Template with { Title = "Different" } } }));
        }
        Sql("CREATE TRIGGER fail_occurrence BEFORE INSERT ON personal_occurrences BEGIN SELECT RAISE(ABORT,'test'); END;");
        using var reopened = Open(); Assert.Throws<SqliteException>(() => CreateSeries(reopened, Definition(reopened)));
        Assert.Equal(taskCount, reopened.List().Count); Assert.Single(reopened.RecurrenceSeries());
    }
    [Fact]
    public void Reminders_PendingDueSleepResumeRestartAndPresentationDedupe()
    {
        Guid reminderId, taskId; DateTimeOffset due;
        using (var store = Open())
        {
            due = _clock.Now.AddHours(2); var task = store.Create(new("Reminder task", DesktopTaskPriority.Normal, due)); taskId = task.Id;
            var r = store.AddReminder(task.Id, DesktopScheduleItemType.Task, ReminderTriggerType.AtStart); reminderId = r.Id;
            Assert.Equal("pending", r.State); Assert.Equal(0, store.ReconcileReminders()); Assert.Empty(store.Notifications());
        }
        _clock.Now = due.AddHours(3); // deterministic sleep/wake and missed startup
        using (var reopened = Open())
        {
            var scheduler = new PersonalPlanningScheduler(reopened, _clock); Assert.Equal(1, scheduler.RunPass());
            Assert.Equal("delivered", Assert.Single(reopened.Reminders()).State);
            var n = Assert.Single(reopened.Notifications()); Assert.Equal(taskId, n.TargetId); Assert.Equal(due, n.DueAt);
            Assert.Equal(ReminderOccurrenceKey.From(reminderId, due).Value, n.DedupeKey); Assert.NotNull(reopened.Reminders()[0].LastDelivery);
            Assert.Single(reopened.ClaimPresentations()); Assert.Empty(reopened.ClaimPresentations());
            Assert.Equal(0, scheduler.RunPass());
        }
        using var again = Open(); Assert.Equal(0, again.ReconcileReminders()); Assert.Single(again.Notifications()); Assert.Empty(again.ClaimPresentations());
        Assert.Equal("claimed", again.Notifications()[0].PresentationState); // interruption after claim cannot repeat a Windows popup
    }
    [Fact]
    public void SnoozePersistsNewOccurrenceUsesDefaultAndCannotDuplicate()
    {
        Guid id; Guid next;
        using (var store = Open())
        {
            var prefs = store.WorkspaceNotificationPreferences();
            store.SaveWorkspaceNotifications(prefs with { DefaultSnoozeMinutes = 30 });
            var task = store.Create(new("Snooze task", DesktopTaskPriority.Normal, _clock.Now));
            store.AddReminder(task.Id, DesktopScheduleItemType.Task, ReminderTriggerType.AtStart);
            Assert.Equal(1, store.ReconcileReminders()); id = Assert.Single(store.Notifications()).Id;
            var snoozed = store.SnoozeNotification(id); next = snoozed.Id;
            Assert.Equal(_clock.Now.AddMinutes(30), snoozed.DueAt); Assert.True(store.Notifications()[0].IsRead);
            Assert.Throws<ArgumentException>(() => store.SnoozeNotification(id));
            Assert.Equal(2, store.Reminders().Count); Assert.Equal(0, store.ReconcileReminders());
        }
        _clock.Now = _clock.Now.AddMinutes(30);
        using var reopened = Open(); Assert.Equal(1, reopened.ReconcileReminders());
        Assert.Equal(2, reopened.Notifications().Count); Assert.Equal(next, reopened.Notifications().First(n => n.Id != id).ReminderId);
        Assert.Equal(0, reopened.ReconcileReminders());
        reopened.MarkAllPersonalNotificationsRead(); Assert.All(reopened.Notifications(), n => Assert.True(n.IsRead));
    }
    [Fact]
    public void PersonalPopupDefersAndRetriesButCannotSnoozeCancelledTarget()
    {
        using var store = Open();
        var task = store.Create(new("Target", DesktopTaskPriority.Normal, _clock.Now));
        store.AddReminder(task.Id, DesktopScheduleItemType.Task, ReminderTriggerType.AtStart); store.ReconcileReminders();
        store.SaveWorkspaceNotifications(store.WorkspaceNotificationPreferences() with { DesktopEnabled = false });
        Assert.Empty(store.ClaimPresentations()); Assert.Equal("pending", Assert.Single(store.Notifications()).PresentationState);
        store.SaveWorkspaceNotifications(store.WorkspaceNotificationPreferences() with { DesktopEnabled = true });
        var notification = Assert.Single(store.ClaimPresentations()); store.CompletePresentation(notification.Id, false);
        Assert.Equal("pending", Assert.Single(store.Notifications()).PresentationState);
        Assert.Single(store.ClaimPresentations()); store.CompletePresentation(notification.Id, true); Assert.Empty(store.ClaimPresentations());
        store.Transition(new(task.Id, 1, DesktopTaskStatus.Cancelled));
        Assert.Throws<ArgumentException>(() => store.SnoozeNotification(notification.Id)); Assert.Single(store.Reminders());
    }
    [Fact]
    public void ReminderRecalculationAndTerminalTargets_DoNotDeliverStaleNotifications()
    {
        using var store = Open(); var start = _clock.Now.AddHours(1);
        var task = store.Create(new("Move", DesktopTaskPriority.Normal, start));
        store.AddReminder(task.Id, DesktopScheduleItemType.Task, ReminderTriggerType.BeforeStart, 15);
        store.Patch(new(task.Id, 1, startAtUtc: DesktopTaskField<DateTimeOffset?>.From(start.AddHours(2))));
        _clock.Now = start; Assert.Equal(0, store.ReconcileReminders()); Assert.Equal(start.AddHours(2).AddMinutes(-15), store.Reminders()[0].DueAt);
        store.Transition(new(task.Id, 2, DesktopTaskStatus.Cancelled)); _clock.Now = start.AddHours(4);
        Assert.Equal(0, store.ReconcileReminders()); Assert.Equal("expired", store.Reminders()[0].State); Assert.Empty(store.Notifications());
        var dateOnly = store.Create(new("Date", DesktopTaskPriority.Normal, card: new() { ScheduledDate = new(2026, 10, 5) }));
        Assert.Throws<ArgumentException>(() => store.AddReminder(dateOnly.Id, DesktopScheduleItemType.Task, ReminderTriggerType.AtStart));
        store.AddReminder(dateOnly.Id, DesktopScheduleItemType.Task, ReminderTriggerType.Absolute, absolute: _clock.Now);
        Assert.Equal(1, store.ReconcileReminders());
    }
    [Fact]
    public void ReminderPersistedDueAndDeliveryFailure_RecoveryIsAtomic()
    {
        using (var store = Open())
        {
            var e = store.SaveEvent(null, null, new(null, "Event reminder", null, new(2026, 10, 5), false, _clock.Now, _clock.Now.AddHours(1), "UTC"));
            store.AddReminder(e.Id, DesktopScheduleItemType.CalendarEvent, ReminderTriggerType.AtStart);
        }
        Sql("UPDATE personal_reminders SET state='due'; CREATE TRIGGER fail_notification BEFORE INSERT ON personal_notifications BEGIN SELECT RAISE(ABORT,'test'); END;");
        using (var store = Open()) { Assert.Throws<SqliteException>(() => store.ReconcileReminders()); Assert.Equal("due", store.Reminders()[0].State); Assert.Empty(store.Notifications()); }
        Sql("DROP TRIGGER fail_notification;");
        using var reopened = Open(); Assert.Equal(1, reopened.ReconcileReminders()); Assert.Equal(0, reopened.ReconcileReminders()); Assert.Single(reopened.Notifications());
    }
    [Fact]
    public void V2Migration_PreservesTaskChecklistAndRejectsFailureWithoutReset()
    {
        DesktopTaskDto original;
        using (var store = Open()) { original = store.Create(new("Previous stage", DesktopTaskPriority.Normal)); store.WriteChecklist(original.Id, 1, null, "Keep checklist", null, false); }
        Sql(RemovePlanningSchema + " PRAGMA user_version=2;");
        using (var upgraded = Open()) { Assert.Equal(original.Title, upgraded.Get(original.Id)!.Title); Assert.Single(upgraded.Checklist(original.Id)); Assert.Empty(upgraded.Projects()); }
        Sql(RemovePlanningSchema + " PRAGMA user_version=2; CREATE TABLE personal_events(id TEXT);");
        var before = File.ReadAllBytes(Paths.DatabasePath); Assert.Throws<InvalidDataException>(() => Open()); Assert.Equal(before, File.ReadAllBytes(Paths.DatabasePath));
    }
    [Fact]
    public async System.Threading.Tasks.Task ModeSwitchDrafts_SaveAllLocalModulesAndCancelPreservesDrafts()
    {
        using (var model = new PersonalApplicationModel(_root, _clock))
        {
            await model.Planning!.NewProjectCommand.ExecuteAsync(); model.Planning.ProjectName = "Guard project";
            await model.Tasks!.NewCommand.ExecuteAsync(); model.Tasks.Editor!.Title = "Guard task";
            await model.Calendar!.ActivateAsync(); await model.Calendar.NewEventCommand.ExecuteAsync();
            model.Calendar.Editor!.Title = "Guard event"; model.Calendar.Editor.Date = new(2026, 10, 5); model.Calendar.Editor.IsAllDay = true;
            await model.Calendar.Recurrence!.OpenCommand.ExecuteAsync(); model.Calendar.Recurrence.Editor.Title = "Guard series"; model.Calendar.Recurrence.Editor.StartDate = new(2026, 10, 5);
            Assert.False(await ModeSwitchGuard.PrepareAsync(model.InspectDrafts(), () => !model.IsBusy, (_, _) => ModeSwitchDecision.Cancel));
            Assert.NotNull(model.Tasks.Editor); Assert.NotNull(model.Calendar.Editor); Assert.True(model.Planning.IsProjectEditing);
            Assert.True(await ModeSwitchGuard.PrepareAsync(model.InspectDrafts(), () => !model.IsBusy, (_, canSave) => canSave ? ModeSwitchDecision.Save : ModeSwitchDecision.Cancel));
            Assert.Null(model.Tasks.Editor); Assert.Null(model.Calendar.Editor); Assert.False(model.Planning.IsProjectEditing);
        }
        using var reopened = Open(); Assert.Single(reopened.Projects()); Assert.Single(reopened.Events()); Assert.Single(reopened.RecurrenceSeries());
        Assert.Contains(reopened.List(), t => t.Title == "Guard task");
    }
    [Fact]
    public async System.Threading.Tasks.Task OfflineWorkflowThroughPersonalContext_HasZeroCorporateRequestsAndNoDuplicateRecords()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        Directory.CreateDirectory(_root);
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var settings = $"{{\"version\":1,\"baseUrl\":\"https://127.0.0.1:{endpoint.Port}/\"}}";
        File.WriteAllText(Path.Combine(_root, "server-settings.json"), settings);
        Guid seriesId; string before;
        try
        {
            using (var model = new PersonalApplicationModel(_root, _clock))
            {
                var planning = model.Planning!; await planning.NewProjectCommand.ExecuteAsync(); planning.ProjectName = "Offline"; await planning.SaveProjectCommand.ExecuteAsync();
                await model.Tasks!.NewCommand.ExecuteAsync(); model.Tasks.Editor!.Title = "Offline task";
                model.Tasks.Editor.Card.Project = model.Tasks.Editor.Card.Projects.Single(p => p.Id == planning.Projects[0].Id);
                model.Tasks.Editor.StartText = _clock.Now.AddHours(1).ToLocalTime().ToString("g", CultureInfo.GetCultureInfo("ru-RU"));
                await model.Tasks.SaveCommand.ExecuteAsync(); Assert.Null(model.Tasks.Editor);
                await model.Calendar!.ActivateAsync(); await model.Calendar.NewEventCommand.ExecuteAsync();
                model.Calendar.Editor!.Title = "Offline event"; model.Calendar.Editor.Date = new(2026, 10, 5); model.Calendar.Editor.IsAllDay = true;
                await model.Calendar.SaveEventCommand.ExecuteAsync(); Assert.Null(model.Calendar.Editor);
                await model.Calendar.Recurrence!.OpenCommand.ExecuteAsync(); model.Calendar.Recurrence.Editor.Title = "Offline series";
                model.Calendar.Recurrence.Editor.Frequency = "daily"; model.Calendar.Recurrence.Editor.StartDate = new(2026, 10, 5); model.Calendar.Recurrence.Editor.Termination = "count"; model.Calendar.Recurrence.Editor.Count = "65";
                await model.Calendar.Recurrence.SaveCommand.ExecuteAsync(); seriesId = Assert.Single(model.Calendar.Recurrence.Series).Id;
                planning.Refresh(); planning.Target = planning.Targets.Single(t => t.Title == "Offline task"); planning.Trigger = planning.Triggers.Single(t => t.Type == ReminderTriggerType.AtStart);
                await planning.AddReminderCommand.ExecuteAsync(); Assert.Single(planning.Reminders);
                before = JsonSerializer.Serialize(planning.Projects);
            }
            using (var store = Open())
            {
                Assert.Equal(before, JsonSerializer.Serialize(store.Projects())); Assert.Single(store.Events());
                var series = store.RecurrenceSeries().Single(s => s.Id == seriesId);
                var generated = store.ExecuteRecurrence(new DesktopRecurrenceCommand.Generate(series.Id, series.Version, new(2026, 12, 7), Key())); Assert.Equal(1, generated.GeneratedCount);
                Assert.Equal(0, store.ExecuteRecurrence(new DesktopRecurrenceCommand.Generate(series.Id, generated.Series!.Version, new(2026, 12, 7), Key())).GeneratedCount);
                _clock.Now = _clock.Now.AddHours(2); Assert.Equal(1, store.ReconcileReminders());
            }
            using var reopened = Open(); Assert.Equal(0, reopened.ReconcileReminders()); Assert.Single(reopened.Notifications()); Assert.False(listener.Pending());
            Assert.False(File.Exists(Path.Combine(_root, "credentials.bin")));
            Assert.Equal(settings, File.ReadAllText(Path.Combine(_root, "server-settings.json")));
        }
        finally { listener.Stop(); }
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
