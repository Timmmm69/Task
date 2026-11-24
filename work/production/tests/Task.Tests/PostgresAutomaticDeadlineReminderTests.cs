using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using Task.Application;
using Task.Application.Security;
using Task.Domain;
using Task.Infrastructure.Persistence;

namespace Task.Tests;

public sealed partial class PostgresProductApiTests
{
    private static void DeadlineRoles(Database db)
        => db.Sql("INSERT INTO iam.user_roles(user_account_id,role_id) VALUES($1,$3),($2,$3) ON CONFLICT DO NOTHING;", db.User, db.OtherUser, db.Role("system_employee"));
    private static T DeadlineValue<T>(Database db, string sql, params object[] parameters)
    { using var c = db.Source.CreateCommand(sql); foreach (var p in parameters) c.Parameters.AddWithValue(p); using var r = c.ExecuteReader(); r.Read(); return r.GetFieldValue<T>(0); }
    private static Guid DeadlineTask(Database db, DateTimeOffset? due, JsonObject? card = null)
    {
        var id = Guid.NewGuid();
        db.Sql("INSERT INTO core.objects(id,organization_id,object_type,created_at,created_by,updated_at,updated_by) VALUES($1,$2,'task',clock_timestamp(),$3,clock_timestamp(),$3);", id, db.Organization, db.User);
        db.Sql("INSERT INTO work.tasks(id,organization_id,title,deadline_at,card_content) VALUES($1,$2,'Автоматическое напоминание',$3,$4::jsonb);", id, db.Organization, due is null ? DBNull.Value : due.Value, card?.ToJsonString() ?? "{}");
        return id;
    }
    private static async System.Threading.Tasks.Task DeliverDeadlines(Database db)
    {
        var worker = db.Runtime.CreateBackgroundDeliveryStore(); await worker.MaterializeDueRemindersAsync(100);
        foreach (var delivery in await worker.ClaimReminderDeliveriesAsync("auto-test", Guid.NewGuid(), TimeSpan.FromMinutes(1), 100)) await worker.DeliverReminderAsync(delivery);
    }

    [RequiresPostgresFact]
    public async System.Threading.Tasks.Task AutomaticDeadline_UpgradePreservesScheduledZeroOffsetDelivery()
    {
        using var db = Database.Create(21)!;
        DeadlineRoles(db);
        var id = DeadlineTask(db, DateTimeOffset.UtcNow.AddHours(1), new() { ["deadlineReminderOffsetMinutes"] = 0 });
        var originalRule = DeadlineValue<Guid>(db, "SELECT id FROM calendar.reminders WHERE target_object_id=$1", id);
        await db.Runtime.CreateMigrator().ApplyPendingAsync();

        var due = DateTimeOffset.UtcNow.AddSeconds(-5);
        db.Sql("UPDATE calendar.reminders SET absolute_trigger_at=$2,next_trigger_at=$2 WHERE target_object_id=$1;", id, due);
        db.Sql("UPDATE work.tasks SET deadline_at=$2 WHERE id=$1;", id, due);
        db.Sql("UPDATE calendar.task_deadline_scan SET next_check_at='-infinity' WHERE task_id=$1;", id);
        await DeliverDeadlines(db);
        await DeliverDeadlines(db);

        Assert.Equal(originalRule, DeadlineValue<Guid>(db, "SELECT id FROM calendar.reminders WHERE target_object_id=$1", id));
        Assert.Equal("delivered", DeadlineValue<string>(db, "SELECT status FROM calendar.reminders WHERE target_object_id=$1", id));
        Assert.Equal(1, db.Count("notify.notifications"));

        var historical = DeadlineTask(db, due, new() { ["deadlineReminderOffsetMinutes"] = 0 });
        await DeliverDeadlines(db);
        Assert.Equal("expired", DeadlineValue<string>(db, "SELECT status FROM calendar.reminders WHERE target_object_id=$1", historical));
        Assert.Equal(1, db.Count("notify.notifications"));
    }

    [Fact]
    public async System.Threading.Tasks.Task AutomaticDeadline_IdleAndUnrelatedUpdatesDoNotRewriteScansAndRecoveryIsBounded()
    {
        using var db = Database.Create(); if (db is null) return; DeadlineRoles(db);
        var first = DeadlineTask(db, DateTimeOffset.UtcNow.AddHours(1));
        var second = DeadlineTask(db, DateTimeOffset.UtcNow.AddHours(1));
        var checkedAt = DeadlineValue<DateTimeOffset>(db, "SELECT checked_at FROM calendar.task_deadline_scan WHERE task_id=$1;", first);
        db.Sql("UPDATE work.tasks SET title='Уточнённый заголовок' WHERE id=$1;", first);
        var worker = db.ApiRuntime.CreateBackgroundDeliveryStore();
        Assert.Equal(0, await worker.MaterializeDueRemindersAsync(100));
        Assert.Equal(checkedAt, DeadlineValue<DateTimeOffset>(db, "SELECT checked_at FROM calendar.task_deadline_scan WHERE task_id=$1;", first));

        db.Sql("UPDATE calendar.task_deadline_scan SET next_check_at='-infinity';");
        Assert.Equal(0, await worker.MaterializeDueRemindersAsync(1));
        Assert.Equal(1L, DeadlineValue<long>(db, "SELECT count(*) FROM calendar.task_deadline_scan WHERE next_check_at='-infinity';"));
        await System.Threading.Tasks.Task.WhenAll(worker.MaterializeDueRemindersAsync(1), worker.MaterializeDueRemindersAsync(1));
        Assert.Equal(0L, DeadlineValue<long>(db, "SELECT count(*) FROM calendar.task_deadline_scan WHERE next_check_at='-infinity';"));
        Assert.Equal(2, db.Count("calendar.reminders"));
        Assert.Equal(0, db.Count("calendar.reminder_occurrences"));

        db.Sql("UPDATE work.tasks SET card_content=jsonb_build_object('assigneeIds', jsonb_build_array($2::uuid)) WHERE id=$1;", second, db.OtherUser);
        db.Sql("UPDATE iam.user_accounts SET account_status='blocked' WHERE id=$1;", db.User);
        Assert.Equal(first, DeadlineValue<Guid>(db, "SELECT task_id FROM calendar.task_deadline_scan WHERE next_check_at='-infinity';"));
        Assert.Equal(1L, DeadlineValue<long>(db, "SELECT count(*) FROM calendar.task_deadline_scan WHERE next_check_at='-infinity';"));
        await worker.MaterializeDueRemindersAsync(100);
        Assert.Equal("cancelled", DeadlineValue<string>(db, "SELECT status FROM calendar.reminders WHERE target_object_id=$1 AND recipient_user_id=$2;", first, db.User));
        Assert.Equal("scheduled", DeadlineValue<string>(db, "SELECT status FROM calendar.reminders WHERE target_object_id=$1 AND recipient_user_id=$2;", second, db.OtherUser));
    }

    [Fact]
    public async System.Threading.Tasks.Task AutomaticDeadline_UpgradeV18ReconcilesExistingRowsWithoutCreatingDuplicates()
    {
        using var db = Database.Create(schemaVersion: 18); if (db is null) return; DeadlineRoles(db);
        var id = DeadlineTask(db, DateTimeOffset.UtcNow.AddHours(1));
        var rule = DeadlineValue<Guid>(db, "SELECT id FROM calendar.reminders WHERE target_object_id=$1;", id);
        var manual = Id(ReminderCall(db, "create", ReminderBody(db, id, "at_deadline"), key: "upgrade-reminder-01"));
        db.Sql("UPDATE work.tasks SET deadline_at=statement_timestamp()+interval '2 hours' WHERE id=$1;", id);
        var migrator = db.Runtime.CreateMigrator();
        Assert.Equal(18, (await migrator.InspectAsync()).ActualMigrationVersion);
        migrator.ApplyPending();
        migrator.ApplyPending();

        Assert.Equal(1, db.Count("calendar.reminder_recovery_queue"));
        await db.ApiRuntime.CreateBackgroundDeliveryStore().MaterializeDueRemindersAsync(100);
        Assert.Equal(rule, DeadlineValue<Guid>(db, "SELECT id FROM calendar.reminders WHERE target_object_id=$1 AND managed_source='task_deadline';", id));
        Assert.Equal(2, db.Count("calendar.reminders"));
        Assert.Equal(0, db.Count("calendar.reminder_recovery_queue"));
        Assert.True(DeadlineValue<bool>(db, "SELECT r.next_trigger_at=t.deadline_at FROM calendar.reminders r JOIN work.tasks t ON t.id=r.target_object_id WHERE r.id=$1;", manual));
        Assert.Equal("cancelled", DeadlineValue<string>(db, "SELECT status FROM calendar.reminder_occurrences WHERE reminder_id=$1;", manual));
        Assert.True(DeadlineValue<bool>(db, "SELECT next_check_at > clock_timestamp() FROM calendar.task_deadline_scan WHERE task_id=$1;", id));
    }

    [Fact]
    public async System.Threading.Tasks.Task AutomaticDeadline_TaskWriteTransactionAndReplayDeliverThroughExistingWorker()
    {
        using var db = Database.Create(); if (db is null) return; DeadlineRoles(db);
        var service = new TaskCreateCommandService(db.ApiRuntime.CreateTaskWriteCommandExecutor());
        var scopeVersion = ScalarReturn<long>(db, "SELECT version FROM iam.authorization_scope_versions WHERE user_account_id=$1", db.User);
        var context = new AuthenticatedRequestContext(db.User, db.Session, db.Organization, 1, scopeVersion, Guid.NewGuid().ToString(), "deadline-test");
        var command = service.CreateCommand(context, "automatic-create-001", "{\"title\":\"Автоматически\"}",
            new("Автоматически", TaskPriority.Normal, true, null, DateTimeOffset.UtcNow.AddMinutes(10)),
            t => new(201, new Dictionary<string, string>(), JsonSerializer.Serialize(new { id = t.Metadata.Id, version = t.Metadata.Version }), t.Metadata.Id));
        var result = await service.ExecuteAsync(command); Assert.Equal(TaskWriteCommandDisposition.Executed, result.Disposition);
        Assert.Equal(TaskWriteCommandDisposition.Replayed, (await service.ExecuteAsync(command)).Disposition); Assert.Equal(1, db.Count("work.tasks")); Assert.Equal(1, db.Count("calendar.reminders"));
        Assert.Equal(15, JsonNode.Parse(result.HttpResult!.BodyJson)!["deadlineReminderOffsetMinutes"]!.GetValue<int>());
        await DeliverDeadlines(db); Assert.Equal(1, db.Count("notify.notifications")); await DeliverDeadlines(db); Assert.Equal(1, db.Count("notify.notifications"));
        var notification = Assert.Single(db.Call("notifications", "list").Body!["items"]!.AsArray()); Assert.Equal("reminder", notification!["notificationType"]!.GetValue<string>());
        Assert.Equal(command.TaskId, Guid.Parse(notification["sourceObjectId"]!.ToString()));
    }

    [Fact]
    public async System.Threading.Tasks.Task AutomaticDeadline_DefaultSnapshotsExplicitOffAndPrivateRecipientsAreIsolated()
    {
        using var db = Database.Create(); if (db is null) return; DeadlineRoles(db);
        db.Call("user-settings", "patch", "{\"defaultReminderOffsetMinutes\":30}", version: 1);
        db.Call("user-settings", "patch", "{\"defaultReminderOffsetMinutes\":5}", version: 1, user: db.OtherUser, session: db.OtherSession);
        var id = DeadlineTask(db, DateTimeOffset.UtcNow.AddHours(1), new() { ["assigneeIds"] = new JsonArray(db.User, db.OtherUser) });
        Assert.Equal(2, db.Count("calendar.reminders"));
        db.Sql("UPDATE work.tasks SET card_content=card_content||'{\"deadlineReminderOffsetMinutes\":-1}'::jsonb WHERE id=$1;", id);
        Assert.Equal(-1, DeadlineValue<int>(db, "SELECT offset_minutes FROM calendar.task_deadline_preferences WHERE task_id=$1 AND recipient_user_id=$2", id, db.User));
        Assert.Equal(5, DeadlineValue<int>(db, "SELECT offset_minutes FROM calendar.task_deadline_preferences WHERE task_id=$1 AND recipient_user_id=$2", id, db.OtherUser));
        db.Call("user-settings", "patch", "{\"defaultReminderOffsetMinutes\":60}", version: 2);
        db.Sql("UPDATE work.tasks SET title='Другой текст',deadline_at=clock_timestamp()+interval '4 minutes' WHERE id=$1;", id);
        await DeliverDeadlines(db);
        Assert.Equal(1, db.Count("notify.notifications")); Assert.Equal(db.OtherUser, DeadlineValue<Guid>(db, "SELECT recipient_user_id FROM notify.notifications"));
        var reads = db.Runtime.CreateTaskReadStore();
        Assert.Equal(-1, (await reads.GetVisibleByIdAsync(db.Organization, id, db.User))!.Content!.DeadlineReminderOffsetMinutes);
        Assert.Equal(5, (await reads.GetVisibleByIdAsync(db.Organization, id, db.OtherUser))!.Content!.DeadlineReminderOffsetMinutes);
        Assert.False(DeadlineValue<bool>(db, "SELECT card_content ? 'deadlineReminderOffsetMinutes' FROM work.tasks WHERE id=$1", id));
    }

    [Fact]
    public async System.Threading.Tasks.Task AutomaticDeadline_ChangedDeadlineOrAssigneesCancelsClaimedOccurrence()
    {
        using var db = Database.Create(); if (db is null) return; DeadlineRoles(db);
        var id = DeadlineTask(db, DateTimeOffset.UtcNow.AddMinutes(10));
        var worker = db.Runtime.CreateBackgroundDeliveryStore(); await worker.MaterializeDueRemindersAsync(100);
        var stale = Assert.Single(await worker.ClaimReminderDeliveriesAsync("stale", Guid.NewGuid(), TimeSpan.FromMinutes(1), 100));
        db.Sql("UPDATE work.tasks SET deadline_at=clock_timestamp()+interval '2 hours',card_content='{\"assigneeIds\":[\"" + db.OtherUser + "\"]}' WHERE id=$1;", id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.DeliverReminderAsync(stale)); Assert.Equal(0, db.Count("notify.notifications"));
        Assert.Equal("cancelled", DeadlineValue<string>(db, "SELECT status FROM calendar.reminder_occurrences WHERE id=$1", stale.OccurrenceId));
        db.Sql("UPDATE work.tasks SET deadline_at=clock_timestamp()+interval '10 minutes' WHERE id=$1;", id);
        await DeliverDeadlines(db); Assert.Equal(db.OtherUser, DeadlineValue<Guid>(db, "SELECT recipient_user_id FROM notify.notifications"));
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("cancelled")]
    [InlineData("archived")]
    [InlineData("trashed")]
    [InlineData("removed")]
    [InlineData("blocked")]
    [InlineData("denied")]
    public async System.Threading.Tasks.Task AutomaticDeadline_LifecycleRightsAndAccountChangesPreventStaleDelivery(string state)
    {
        using var db = Database.Create(); if (db is null) return; DeadlineRoles(db);
        var id = DeadlineTask(db, DateTimeOffset.UtcNow.AddMinutes(10));
        if (state is "completed" or "cancelled") db.Sql("UPDATE work.tasks SET status=$2,completed_at=CASE WHEN $2='completed' THEN statement_timestamp() END,completed_by=CASE WHEN $2='completed' THEN $3::uuid END WHERE id=$1;", id, state, db.User);
        else if (state == "removed") db.Sql("UPDATE work.tasks SET deadline_at=NULL WHERE id=$1;", id);
        else if (state == "blocked") db.Sql("UPDATE iam.user_accounts SET account_status='blocked' WHERE id=$1;", db.User);
        else if (state == "denied") db.Sql("INSERT INTO iam.role_permissions(role_id,permission_code,effect) VALUES($1,'task.read','deny');", db.Role("system_employee"));
        else if (state == "archived") db.Sql("UPDATE core.objects SET lifecycle_state='archived',updated_at=statement_timestamp(),archived_at=statement_timestamp() WHERE id=$1;", id);
        else db.Sql("UPDATE core.objects SET lifecycle_state='trashed',updated_at=statement_timestamp(),lifecycle_state_before_trash='active',deleted_at=statement_timestamp(),deleted_by=$2 WHERE id=$1;", id, db.User);
        await DeliverDeadlines(db); Assert.Equal(0, db.Count("notify.notifications"));
    }

    [Fact]
    public async System.Threading.Tasks.Task AutomaticDeadline_MultipleAssigneesEachReceiveOneAndWatchersReceiveNone()
    {
        using var db = Database.Create(); if (db is null) return; DeadlineRoles(db);
        DeadlineTask(db, DateTimeOffset.UtcNow.AddMinutes(10), new() { ["assigneeIds"] = new JsonArray(db.User, db.OtherUser) });
        await DeliverDeadlines(db); Assert.Equal(2, db.Count("notify.notifications"));
        Assert.Equal(2L, DeadlineValue<long>(db, "SELECT count(DISTINCT recipient_user_id) FROM notify.notifications"));
        DeadlineTask(db, DateTimeOffset.UtcNow.AddMinutes(10), new() { ["watcherIds"] = new JsonArray(db.OtherUser) });
        await DeliverDeadlines(db); Assert.Equal(3, db.Count("notify.notifications"));
        Assert.Equal(1L, DeadlineValue<long>(db, "SELECT count(*) FROM notify.notifications WHERE recipient_user_id=$1", db.OtherUser));
    }

    [Fact]
    public async System.Threading.Tasks.Task AutomaticDeadline_ManualSameTimeDedupesDifferentTimeSurvivesAndCancelledOwnRuleStaysOff()
    {
        using var db = Database.Create(); if (db is null) return; DeadlineRoles(db);
        var id = DeadlineTask(db, DateTimeOffset.UtcNow.AddMinutes(10));
        var due = DeadlineValue<DateTimeOffset>(db, "SELECT next_trigger_at FROM calendar.reminders WHERE target_object_id=$1", id);
        ReminderCall(db, "create", ReminderBody(db, id, due: due));
        ReminderCall(db, "create", ReminderBody(db, id, due: DateTimeOffset.UtcNow.AddMinutes(-1)));
        await DeliverDeadlines(db); Assert.Equal(2, db.Count("notify.notifications")); Assert.Equal(3L, DeadlineValue<long>(db, "SELECT count(*) FROM calendar.reminder_occurrences WHERE status='delivered'"));
        var own = ReminderCall(db, "list").Body!["items"]!.AsArray().Select(n => n!.AsObject()).Single(n => Guid.Parse(n["id"]!.ToString()) == DeadlineValue<Guid>(db, "SELECT id FROM calendar.reminders WHERE managed_source='task_deadline'"));
        ReminderCall(db, "cancel", id: Guid.Parse(own["id"]!.ToString()), version: own["version"]!.GetValue<int>());
        db.Sql("UPDATE work.tasks SET deadline_at=clock_timestamp()+interval '2 hours' WHERE id=$1;", id);
        Assert.Equal(-1, DeadlineValue<int>(db, "SELECT offset_minutes FROM calendar.task_deadline_preferences"));
    }

    [Theory]
    [InlineData(null, 60)]
    [InlineData(2880, 2880)]
    public async System.Threading.Tasks.Task AutomaticDeadline_DateOnlyAndDstUseExplicitZoneWithoutInventingDeadline(int? preference, int interval)
    {
        using var db = Database.Create(); if (db is null) return; DeadlineRoles(db);
        db.Call("user-settings", "patch", "{\"dateOnlyReminderTime\":\"09:00\",\"defaultReminderOffsetMinutes\":60}", version: 1);
        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var card = new JsonObject { ["deadlineDate"] = day.ToString("yyyy-MM-dd"), ["deadlineTimeZone"] = "Europe/Minsk" };
        if (preference is { } offset) card["deadlineReminderOffsetMinutes"] = offset;
        var id = DeadlineTask(db, null, card);
        Assert.True(DeadlineValue<bool>(db, "SELECT deadline_at IS NULL AND start_at_utc IS NULL FROM work.tasks WHERE id=$1", id));
        var expected = new DateTimeOffset(day.ToDateTime(new TimeOnly(6, 0)), TimeSpan.Zero).AddMinutes(-interval);
        Assert.Equal(expected, DeadlineValue<DateTimeOffset>(db, "SELECT next_trigger_at FROM calendar.reminders WHERE target_object_id=$1", id));
        db.Call("user-settings", "patch", "{\"dateOnlyReminderTime\":\"11:00\"}", version: 2); await DeliverDeadlines(db);
        Assert.Equal("09:00:00", (await db.Runtime.CreateTaskReadStore().GetVisibleByIdAsync(db.Organization, id, db.User))!.Content!.DeadlineReminderTime);
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 0, 0, TimeSpan.Zero), DeadlineValue<DateTimeOffset>(db, "SELECT calendar.deadline_local_anchor('2026-03-29','02:30','Europe/Berlin')"));
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), DeadlineValue<DateTimeOffset>(db, "SELECT calendar.deadline_local_anchor('2026-10-25','02:30','Europe/Berlin')"));
    }

    [Fact]
    public async System.Threading.Tasks.Task AutomaticDeadline_ConcurrentManualCollisionAndRestoreDoNotRepeatDelivery()
    {
        using var db = Database.Create(); if (db is null) return; DeadlineRoles(db);
        var deadline = DateTimeOffset.UtcNow.AddMinutes(10);
        var id = DeadlineTask(db, deadline);
        ReminderCall(db, "create", ReminderBody(db, id, due: DeadlineValue<DateTimeOffset>(db, "SELECT next_trigger_at FROM calendar.reminders WHERE target_object_id=$1", id)));
        var worker = db.Runtime.CreateBackgroundDeliveryStore();
        await worker.MaterializeDueRemindersAsync(100);
        var claimed = await worker.ClaimReminderDeliveriesAsync("parallel-deadline", Guid.NewGuid(), TimeSpan.FromMinutes(1), 100);
        Assert.Equal(2, claimed.Count);
        await System.Threading.Tasks.Task.WhenAll(claimed.Select(delivery => worker.DeliverReminderAsync(delivery)));
        Assert.Equal(1, db.Count("notify.notifications"));
        db.Sql("UPDATE core.objects SET lifecycle_state='archived',updated_at=statement_timestamp(),archived_at=statement_timestamp() WHERE id=$1", id);
        db.Sql("UPDATE core.objects SET lifecycle_state='active',updated_at=statement_timestamp(),archived_at=NULL WHERE id=$1", id);
        await DeliverDeadlines(db); // New worker instance, same durable history.
        Assert.Equal(1, db.Count("notify.notifications"));

        var undelivered = DeadlineTask(db, DateTimeOffset.UtcNow.AddHours(1));
        db.Sql("UPDATE core.objects SET lifecycle_state='archived',updated_at=statement_timestamp(),archived_at=statement_timestamp() WHERE id=$1", undelivered);
        db.Sql("UPDATE core.objects SET lifecycle_state='active',updated_at=statement_timestamp(),archived_at=NULL WHERE id=$1", undelivered);
        Assert.Equal("scheduled", DeadlineValue<string>(db, "SELECT status::text FROM calendar.reminders WHERE target_object_id=$1", undelivered));
    }

    [Fact]
    public async System.Threading.Tasks.Task AutomaticDeadline_UpgradeV15IsIdempotentAndDoesNotSendElapsedTriggers()
    {
        using var db = Database.Create(schemaVersion: 15); if (db is null) return; DeadlineRoles(db);
        var future = DeadlineTask(db, DateTimeOffset.UtcNow.AddHours(1));
        DeadlineTask(db, DateTimeOffset.UtcNow.AddMinutes(10)); DeadlineTask(db, DateTimeOffset.UtcNow.AddMinutes(-10));
        ReminderCall(db, "create", ReminderBody(db, future, "before_deadline"));
        db.Runtime.CreateMigrator().ApplyPending(); db.Runtime.CreateMigrator().ApplyPending();
        var auto = ReminderCall(db, "list").Body!["items"]!.AsArray().Select(n => n!.AsObject()).Single(n => n["targetObjectId"]!.ToString() == future.ToString() && n["triggerType"]!.ToString() == "absolute");
        ReminderCall(db, "cancel", id: Guid.Parse(auto["id"]!.ToString()), version: auto["version"]!.GetValue<int>());
        // Restore the preference through an authorized task write; the old runtime role now has v16 rights.
        db.Sql("UPDATE work.tasks SET card_content=card_content||'{\"deadlineReminderOffsetMinutes\":15}'::jsonb WHERE id=$1", future);
        Assert.Equal(3, db.Count("work.tasks")); await DeliverDeadlines(db); Assert.Equal(0, db.Count("notify.notifications"));
        Assert.Equal(1L, DeadlineValue<long>(db, "SELECT count(*) FROM calendar.reminders WHERE managed_source='task_deadline' AND status='scheduled'"));
        Assert.Equal(1L, DeadlineValue<long>(db, "SELECT count(*) FROM calendar.reminders WHERE managed_source IS NULL"));
    }
}
