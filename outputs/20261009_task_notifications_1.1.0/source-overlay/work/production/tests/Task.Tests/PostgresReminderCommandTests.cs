using System.Text.Json.Nodes;
using Task.Application.ProductData;

namespace Task.Tests;

public sealed partial class PostgresProductApiTests
{
    private static ProductApiResponse ReminderCall(Database db, string operation, string body = "{}", Guid? id = null, int? version = null,
        string? key = null, Guid? user = null, Dictionary<string,string>? query = null)
    {
        query = query is null ? new() : new(query);
        query["includeOccurrence"] = "true";
        return db.Call("reminders", operation, body, id, version, key, user: user, query: query);
    }
    private static Guid ReminderTarget(Database db)
    {
        var id = Guid.NewGuid();
        db.Sql("INSERT INTO core.objects(id,organization_id,object_type,created_at,created_by,updated_at,updated_by) VALUES($1,$2,'task',clock_timestamp(),$3,clock_timestamp(),$3);", id, db.Organization, db.User);
        db.Sql("INSERT INTO work.tasks(id,organization_id,title,start_at_utc,deadline_at) VALUES($1,$2,'Reminder target',clock_timestamp()+interval '1 day',clock_timestamp()+interval '2 days');", id, db.Organization);
        return id;
    }
    private static string ReminderBody(Database db, Guid target, string trigger = "absolute", DateTimeOffset? due = null) => new JsonObject
    {
        ["targetObjectId"] = target, ["recipientUserId"] = db.User, ["triggerType"] = trigger,
        ["absoluteTriggerAt"] = trigger == "absolute" ? JsonValue.Create(due ?? DateTimeOffset.UtcNow.AddHours(2)) : null,
        ["offsetMinutes"] = null
    }.ToJsonString();
    private static int OccurrenceVersion(ProductApiResponse result) => result.Body!["currentOccurrence"]!["version"]!.GetValue<int>();

    [Fact]
    public void ReminderCommands_CanonicalDtoRuntimePermissionRevocationAndTransactionalRollback()
    {
        using var db = Database.Create(); if (db is null) return;
        var target = ReminderTarget(db);
        db.Sql("INSERT INTO iam.user_roles(user_account_id,role_id) VALUES($1,$2);",db.User,db.Role("system_employee"));
        var body = ReminderBody(db,target);
        var created = db.Call("reminders","create",body,key:"reminder-permission-001",admin:false,revalidatePermissions:true);
        Assert.Null(created.Body!["currentOccurrence"]); Assert.Null(created.Body["targetTitle"]);
        Assert.Null(created.Body["snoozeCount"]); Assert.Null(created.Body["createdBy"]);
        var page = db.Call("reminders","list",admin:false,revalidatePermissions:true).Body!;
        Assert.Null(page["hasMore"]); Assert.True(page.AsObject().ContainsKey("total"));
        db.Sql("INSERT INTO iam.role_permissions(role_id,permission_code,effect) VALUES($1,'task.read','deny');",db.Role("system_employee"));
        Assert.Equal(404,Assert.Throws<ProductApiException>(()=>db.Call("reminders","create",body,key:"reminder-permission-001",admin:false,revalidatePermissions:true)).Status);
        db.Sql("DELETE FROM iam.role_permissions WHERE role_id=$1 AND permission_code='task.read' AND effect='deny';",db.Role("system_employee"));
        db.Sql("INSERT INTO iam.role_permissions(role_id,permission_code,effect) VALUES($1,'reminder.manageown','deny');",db.Role("system_employee"));
        Assert.Equal(403,Assert.Throws<ProductApiException>(()=>db.Call("reminders","create",body,key:"reminder-permission-001",admin:false,revalidatePermissions:true)).Status);
        Assert.Equal(1,db.Count("calendar.reminders"));
        db.Sql("CREATE FUNCTION public.fail_reminder_event() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.event_type='ReminderCreated' THEN RAISE EXCEPTION 'fixture rollback'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_reminder_event BEFORE INSERT ON governance.domain_events FOR EACH ROW EXECUTE FUNCTION public.fail_reminder_event();");
        Assert.Throws<Npgsql.PostgresException>(()=>db.Call("reminders","create",body));
        Assert.Equal(1,db.Count("calendar.reminders")); Assert.Equal(1,db.Count("calendar.reminder_occurrences")); Assert.Equal(1,db.Count("governance.domain_events"));
    }

    [Theory]
    [InlineData("absolute")][InlineData("before_start")][InlineData("before_deadline")][InlineData("at_start")][InlineData("at_deadline")]
    public void ReminderCommands_CreateAllTriggersPersistDefaultAndReplayWithoutDuplicates(string trigger)
    {
        using var db = Database.Create(); if (db is null) return;
        var target = ReminderTarget(db);
        db.Call("user-settings", "patch", "{\"defaultReminderOffsetMinutes\":37}", version: 1);
        var body = ReminderBody(db, target, trigger);
        var created = ReminderCall(db, "create", body, key: "reminder-create-001");
        Assert.Equal(201, created.Status); Assert.Equal(1, created.Version);
        var id = Id(created);
        Assert.True(JsonNode.DeepEquals(created.Body, ReminderCall(db, "create", body, key: "reminder-create-001").Body));
        Assert.Equal(1, db.Count("calendar.reminders")); Assert.Equal(1, db.Count("calendar.reminder_occurrences"));
        Assert.Equal(2, db.Count("governance.domain_events")); Assert.Equal(2, db.Count("governance.outbox_messages"));
        Assert.Equal(1, OccurrenceVersion(ReminderCall(db, "get", id: id)));
        if (trigger.StartsWith("before_", StringComparison.Ordinal)) Assert.Equal(37, created.Body!["offsetMinutes"]!.GetValue<int>());
        Assert.Single(ReminderCall(db, "list").Body!["items"]!.AsArray());
        Assert.Single(ReminderCall(db, "upcoming", query: new() { ["until"] = DateTimeOffset.UtcNow.AddDays(3).ToString("O") }).Body!.AsArray());
        Assert.Empty(ReminderCall(db, "list", user: db.OtherUser).Body!["items"]!.AsArray());
        Assert.Equal(404, Assert.Throws<ProductApiException>(() => ReminderCall(db, "get", id: id, user: db.OtherUser)).Status);
        Assert.Equal(403, Assert.Throws<ProductApiException>(() => ReminderCall(db, "create", ReminderBody(db, target).Replace(db.User.ToString(), db.OtherUser.ToString()))).Status);
        db.Sql("UPDATE core.objects SET lifecycle_state='trashed',lifecycle_state_before_trash='active',updated_at=statement_timestamp(),deleted_at=statement_timestamp(),deleted_by=$2 WHERE id=$1;", target, db.User);
        Assert.Equal(404, Assert.Throws<ProductApiException>(() => ReminderCall(db, "create", body, key: "reminder-create-001")).Status);
    }

    [Fact]
    public void ReminderCommands_PatchCancelRestoreConcurrencyAndOccurrenceHistory()
    {
        using var db = Database.Create(); if (db is null) return;
        var target = ReminderTarget(db);
        var due = DateTimeOffset.UtcNow.AddDays(1);
        var created = ReminderCall(db, "create", ReminderBody(db, target, due: due));
        var id = Id(created); var occurrence = created.Body!["currentOccurrence"]!["id"]!.GetValue<Guid>();
        var updated = ReminderCall(db, "patch", "{\"triggerType\":\"absolute\"}", id, 1);
        Assert.Equal(2, updated.Version); Assert.Equal(1, db.Count("calendar.reminder_occurrences"));
        Assert.Equal(occurrence, updated.Body!["currentOccurrence"]!["id"]!.GetValue<Guid>());
        Assert.Equal(412, Assert.Throws<ProductApiException>(() => ReminderCall(db, "patch", "{\"triggerType\":\"absolute\"}", id, 1)).Status);
        var cancelled = ReminderCall(db, "cancel", id: id, version: 2);
        Assert.Equal(202, cancelled.Status); Assert.Equal("cancelled", cancelled.Body!["status"]!.GetValue<string>());
        Assert.Empty(ReminderCall(db, "upcoming", query: new() { ["until"] = due.AddHours(1).ToString("O") }).Body!.AsArray());
        var restored = ReminderCall(db, "reschedule", "{\"expectedVersion\":3}", id, 3, key: "reminder-restore-001");
        Assert.Equal("scheduled", restored.Body!["status"]!.GetValue<string>());
        Assert.Equal("created", restored.Body!["currentOccurrence"]!["status"]!.GetValue<string>());
        Assert.Equal(1, db.Count("calendar.reminder_occurrences"));
        Assert.True(JsonNode.DeepEquals(restored.Body, ReminderCall(db, "reschedule", "{\"expectedVersion\":3}", id, 3, key: "reminder-restore-001").Body));
        var newTime = due.AddHours(1);
        var changed = ReminderCall(db, "patch", new JsonObject { ["absoluteTriggerAt"] = newTime }.ToJsonString(), id, restored.Version);
        Assert.Equal(2, db.Count("calendar.reminder_occurrences"));
        Assert.Equal(newTime.ToUnixTimeSeconds(), DateTimeOffset.Parse(changed.Body!["nextTriggerAt"]!.GetValue<string>()).ToUnixTimeSeconds());
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => ReminderCall(db, "patch", "{\"status\":\"scheduled\"}", id, changed.Version)).Status);
    }

    [Fact]
    public async System.Threading.Tasks.Task ReminderCommands_DeliverSnoozeReplayDismissAndRetainDeliveryHistory()
    {
        using var db = Database.Create(); if (db is null) return;
        var target = ReminderTarget(db);
        var created = ReminderCall(db, "create", ReminderBody(db, target, due: DateTimeOffset.UtcNow.AddMinutes(-1)));
        var id = Id(created);
        var worker = db.Runtime.CreateBackgroundDeliveryStore();
        Assert.Equal(1, await worker.MaterializeDueRemindersAsync(100));
        var delivery = Assert.Single(await worker.ClaimReminderDeliveriesAsync("test", Guid.NewGuid(), TimeSpan.FromMinutes(1), 100));
        await worker.DeliverReminderAsync(delivery);
        var delivered = ReminderCall(db, "get", id: id);
        var xv = OccurrenceVersion(delivered);
        var until = DateTimeOffset.UtcNow.AddMinutes(17);
        var body = new JsonObject { ["until"] = until, ["expectedVersion"] = xv }.ToJsonString();
        Assert.Equal(412, Assert.Throws<ProductApiException>(() => ReminderCall(db, "snooze", body, id, xv - 1, key: "reminder-snooze-stale")).Status);
        var snoozed = ReminderCall(db, "snooze", body, id, xv, key: "reminder-snooze-001");
        Assert.Equal(1, snoozed.Version);
        Assert.True(JsonNode.DeepEquals(snoozed.Body, ReminderCall(db, "snooze", body, id, xv, key: "reminder-snooze-001").Body));
        Assert.Equal(2, db.Count("calendar.reminder_occurrences")); Assert.Equal(1, db.Count("notify.notifications"));
        Assert.Equal("dismissed", db.Call("notifications", "get", id: delivery.OccurrenceId).Body!["status"]!.GetValue<string>());
        db.Sql("DO $$ BEGIN IF (SELECT status FROM calendar.reminder_occurrences WHERE id='" + delivery.OccurrenceId + "')<>'delivered' THEN RAISE EXCEPTION 'History changed'; END IF; END $$;");
        var current = ReminderCall(db, "get", id: id);
        Assert.Equal("snoozed", current.Body!["status"]!.GetValue<string>());
        Assert.Equal(until.ToUnixTimeSeconds(), DateTimeOffset.Parse(current.Body["nextTriggerAt"]!.GetValue<string>()).ToUnixTimeSeconds());
        var dismissed = ReminderCall(db, "dismiss", new JsonObject { ["expectedVersion"] = OccurrenceVersion(current) }.ToJsonString(), id, OccurrenceVersion(current));
        Assert.Equal(204, dismissed.Status);
        Assert.Equal("cancelled", ReminderCall(db, "get", id: id).Body!["status"]!.GetValue<string>());
    }

    [Fact]
    public async System.Threading.Tasks.Task ReminderCommands_CancelClaimedDeliveryAndRejectInactiveTargetOrInvalidSnooze()
    {
        using var db = Database.Create(); if (db is null) return;
        var target = ReminderTarget(db);
        var rule = ReminderCall(db, "create", ReminderBody(db, target, due: DateTimeOffset.UtcNow.AddMinutes(-1)));
        var worker = db.Runtime.CreateBackgroundDeliveryStore();
        await worker.MaterializeDueRemindersAsync(100);
        var delivery = Assert.Single(await worker.ClaimReminderDeliveriesAsync("test", Guid.NewGuid(), TimeSpan.FromMinutes(1), 100));
        rule = ReminderCall(db, "get", id: Id(rule));
        ReminderCall(db, "cancel", id: Id(rule), version: rule.Version);
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.DeliverReminderAsync(delivery));
        Assert.Equal(0, db.Count("notify.notifications"));
        db.Sql("UPDATE work.tasks SET status='completed',completed_at=clock_timestamp(),completed_by=$2 WHERE id=$1;", target, db.User);
        Assert.Equal(409, Assert.Throws<ProductApiException>(() => ReminderCall(db, "create", ReminderBody(db, target))).Status);
        db.Sql("UPDATE work.tasks SET status='new',completed_at=NULL,completed_by=NULL WHERE id=$1;", target);
        var second = ReminderCall(db, "create", ReminderBody(db, target));
        var xv = OccurrenceVersion(second);
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => ReminderCall(db, "snooze", new JsonObject { ["expectedVersion"] = xv, ["until"] = DateTimeOffset.UtcNow.AddMinutes(-1) }.ToJsonString(), Id(second), xv)).Status);
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => ReminderCall(db, "create", ReminderBody(db, target).Replace("absolute", "invalid"))).Status);
    }
}
