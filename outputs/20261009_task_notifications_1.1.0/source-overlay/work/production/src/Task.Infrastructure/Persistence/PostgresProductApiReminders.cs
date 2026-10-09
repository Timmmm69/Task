using System.Globalization;
using System.Text.Json.Nodes;
using Npgsql;
using Task.Application.ProductData;

namespace Task.Infrastructure.Persistence;

internal sealed partial class PostgresProductApiStore
{
    private const string ReminderVisibility = "r.organization_id=@org AND r.recipient_user_id=@user AND o.lifecycle_state<>'trashed' AND " +
        "iam.object_allowed(@org,o.id,@user,CASE o.object_type WHEN 'task' THEN 'task.read' ELSE 'calendar.read' END,@admin)";
    private const string ReminderFrom = " FROM calendar.reminders r JOIN core.objects o ON o.organization_id=r.organization_id AND o.id=r.target_object_id ";

    private ProductApiResponse Reminders(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r)
    {
        if (!r.Permissions.Contains("Reminder.ManageOwn")) throw Error(403, "FORBIDDEN", "Reminder management permission is required.");
        if (r.Route.Operation is "list" or "upcoming")
        {
            ValidateQuery(r, "limit page cursor filter sort until targetObjectId status includeOccurrence");
            var limit = QueryInt(r, "limit", 50, 1, r.Route.Operation == "upcoming" ? 500 : 200);
            if (r.Route.Operation == "upcoming")
            {
                var until = r.Query.TryGetValue("until", out var raw) ? ReminderInstant(raw) : DateTimeOffset.UtcNow.AddDays(7);
                var occurrences = Many(c, t, "SELECT to_jsonb(x)" + ReminderFrom + "JOIN calendar.reminder_occurrences x ON x.reminder_id=r.id AND x.organization_id=r.organization_id " +
                    "WHERE " + ReminderVisibility + " AND r.status IN ('scheduled','due','snoozed') AND x.status IN ('created','claimed','failed') " +
                    "AND x.due_at=r.next_trigger_at AND x.due_at<=@until ORDER BY x.due_at,x.id LIMIT @limit;", r, ("until", until), ("limit", limit));
                return new(new JsonArray(occurrences.Select(x => (JsonNode?)PublicOccurrence(x)).ToArray()));
            }
            var filter = r.Query.ContainsKey("filter") ? ParseReminderFilter(r.Query["filter"]) : new JsonObject();
            var target = r.Query.GetValueOrDefault("targetObjectId") ?? Text(filter, "targetObjectId");
            var status = r.Query.GetValueOrDefault("status") ?? Text(filter, "status");
            if (target is not null && (!Guid.TryParse(target, out var targetId) || targetId == Guid.Empty)) throw Invalid("Invalid reminder target filter.");
            if (status is not null && status is not ("scheduled" or "due" or "delivered" or "snoozed" or "cancelled" or "expired")) throw Invalid("Invalid reminder status filter.");
            if (r.Query.TryGetValue("sort", out var sort) && sort != "id") throw Invalid("Supported reminder sort is id.");
            var (after, _) = ReadCursor(r);
            var page = QueryInt(r, "page", 1, 1, 10000);
            var rows = Many(c, t, "SELECT to_jsonb(r)||jsonb_build_object('targetTitle',coalesce(task.title,event.title))" + ReminderFrom +
                "LEFT JOIN work.tasks task ON task.id=o.id LEFT JOIN calendar.events event ON event.id=o.id WHERE " + ReminderVisibility +
                " AND (@after='' OR r.id>nullif(@after,'')::uuid) AND (@target='' OR r.target_object_id=nullif(@target,'')::uuid) " +
                "AND (@status='' OR r.status=@status) ORDER BY r.id LIMIT @limit OFFSET @skip;", r,
                ("after", after), ("target", target ?? ""), ("status", status ?? ""), ("limit", limit + 1), ("skip", (page - 1) * limit));
            var more = rows.Count > limit;
            rows = rows.Take(limit).ToList();
            foreach (var row in rows) AddOccurrence(c, t, r, row);
            return new(new JsonObject { ["items"] = new JsonArray(rows.Select(x => (JsonNode?)PublicReminder(r,x)).ToArray()), ["nextCursor"] = more ? Cursor(r, GuidValue(rows[^1], "id").ToString()) : null, ["total"] = null });
        }
        ValidateQuery(r, "includeOccurrence");
        if (r.Route.Operation == "create")
        {
            var body = r.Body.DeepClone().AsObject();
            ValidateReminderFields(body);
            foreach (var name in new[] { "targetObjectId", "recipientUserId", "triggerType" }) if (body[name] is null) throw Invalid(name + " is required.");
            if (GuidValue(body, "recipientUserId") != r.UserId) throw Error(403, "FORBIDDEN", "Only own reminders may be managed.");
            var due = ResolveReminder(c, t, r, body);
            var id = Guid.NewGuid();
            Run(c, t, "INSERT INTO calendar.reminders(id,organization_id,target_object_id,recipient_user_id,trigger_type,offset_minutes,absolute_trigger_at,next_trigger_at,created_by) " +
                "VALUES(@id,@org,@target,@user,@trigger,nullif(@offset,'')::integer,nullif(@absolute,'')::timestamptz,@due,@user);", r,
                ("id", id), ("target", GuidValue(body, "targetObjectId")), ("trigger", Text(body, "triggerType")), ("offset", body["offsetMinutes"]?.ToString() ?? ""),
                ("absolute", body["absoluteTriggerAt"]?.ToString() ?? ""), ("due", due));
            CreateOccurrence(c, t, r, id, due);
            var created = RequireReminder(c, t, r with { Id = id });
            Record(c, t, r, "reminder", id, Version(created), null, created);
            return new(PublicReminder(r,created), 201, Version(created));
        }
        var current = RequireReminder(c, t, r);
        if (r.Route.Operation == "get") return new(PublicReminder(r,current), Version: Version(current));
        var occurrenceAction = r.Route.Operation is "snooze" or "dismiss";
        var occurrence = current["currentOccurrence"] as JsonObject;
        var expected = occurrenceAction ? occurrence is null ? 0 : Version(occurrence) : Version(current);
        if (r.ExpectedVersion is null) throw Error(428, "PRECONDITION_REQUIRED", "If-Match is required.");
        if (r.ExpectedVersion != expected) throw Error(412, "VERSION_CONFLICT", "Reminder or current occurrence changed.");
        var updated = current.DeepClone().AsObject();
        DateTimeOffset? next = null;
        switch (r.Route.Operation)
        {
            case "patch":
                ValidateReminderFields(r.Body);
                if (r.Body.Count == 0) throw Invalid("At least one writable field is required.");
                if (Text(current, "status") is "cancelled" or "expired") throw Error(409, "INVALID_STATE_TRANSITION", "Restore before editing.");
                foreach (var pair in r.Body) updated[pair.Key] = pair.Value?.DeepClone();
                if (GuidValue(updated, "recipientUserId") != r.UserId) throw Error(403, "FORBIDDEN", "Only own reminders may be managed.");
                next = ResolveReminder(c, t, r, updated);
                updated["status"] = "scheduled";
                break;
            case "cancel":
                ValidateFields(r.Body, "");
                updated["status"] = "cancelled";
                break;
            case "reschedule":
                ValidateFields(r.Body, "reason expectedVersion");
                if (r.Body["expectedVersion"] is { } restoreVersion && restoreVersion.GetValue<int>() != expected) throw Invalid("expectedVersion must match If-Match.");
                if (Text(current, "status") is not ("cancelled" or "expired")) throw Error(409, "INVALID_STATE_TRANSITION", "Only cancelled or expired reminders may be restored.");
                next = ResolveReminder(c, t, r, updated);
                if (next <= DateTimeOffset.UtcNow) throw Invalid("Restore requires a future trigger; edit the target schedule first.");
                updated["status"] = "scheduled";
                break;
            case "snooze":
            case "dismiss":
                ValidateFields(r.Body, r.Route.Operation == "snooze" ? "until expectedVersion" : "expectedVersion");
                if (r.Body["expectedVersion"]?.GetValue<int>() != expected) throw Invalid("expectedVersion must match occurrence If-Match.");
                if (Text(current, "status") is "cancelled" or "expired" || occurrence is null || Text(occurrence, "status") is not ("created" or "claimed" or "delivered" or "failed"))
                    throw Error(409, "INVALID_STATE_TRANSITION", "Current occurrence is not actionable.");
                if (r.Route.Operation == "snooze")
                {
                    if (current["snoozeCount"]!.GetValue<int>() >= 100) throw Error(409, "INVALID_STATE_TRANSITION", "Snooze limit reached.");
                    next = ReminderInstant(Text(r.Body, "until"));
                    if (next <= DateTimeOffset.UtcNow) throw Invalid("Snooze must be in the future.");
                    RequireReminderTarget(c, t, r, GuidValue(current, "targetObjectId"), true);
                    updated["status"] = "snoozed";
                }
                else updated["status"] = "cancelled";
                break;
            default: throw Invalid("Unsupported reminder operation.");
        }
        var keepsOccurrence = r.Route.Operation == "patch" && next == ReminderInstant(Text(current, "nextTriggerAt"));
        if (keepsOccurrence) updated["status"] = current["status"]?.DeepClone();
        else CancelOccurrences(c, t, r, GuidValue(current, "id"));
        Run(c, t, "UPDATE calendar.reminders SET target_object_id=@target,trigger_type=@trigger,offset_minutes=nullif(@offset,'')::integer," +
            "absolute_trigger_at=nullif(@absolute,'')::timestamptz,next_trigger_at=@due,status=@status,snooze_count=snooze_count+@increment," +
            "version=version+1,updated_at=clock_timestamp() WHERE organization_id=@org AND id=@id AND recipient_user_id=@user;", r,
            ("id", r.Id), ("target", GuidValue(updated, "targetObjectId")), ("trigger", Text(updated, "triggerType")), ("offset", updated["offsetMinutes"]?.ToString() ?? ""),
            ("absolute", updated["absoluteTriggerAt"]?.ToString() ?? ""), ("due", next ?? ReminderInstant(Text(current, "nextTriggerAt"))),
            ("status", Text(updated, "status")), ("increment", r.Route.Operation == "snooze" ? 1 : 0));
        if (!keepsOccurrence && next is { } dueAt) CreateOccurrence(c, t, r, r.Id!.Value, dueAt);
        var result = RequireReminder(c, t, r);
        Record(c, t, r, "reminder", r.Id!.Value, Version(result), current, result);
        if (r.Route.Operation == "dismiss") return new(null, 204);
        if (r.Route.Operation == "snooze") return new(result["currentOccurrence"]!.DeepClone(), Version: Version(result["currentOccurrence"]!.AsObject()));
        return new(PublicReminder(r,result), r.Route.Operation == "cancel" ? 202 : 200, Version(result));
    }

    private static JsonObject ParseReminderFilter(string raw)
    {
        try { var filter = JsonNode.Parse(raw)?.AsObject() ?? throw Invalid("Invalid reminder filter."); ValidateFields(filter, "targetObjectId status"); return filter; }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException) { throw Invalid("Invalid reminder filter."); }
    }
    private static void ValidateReminderFields(JsonObject body)
    {
        var copy = body.DeepClone().AsObject();
        if (copy.ContainsKey("offsetMinutes"))
        {
            if (copy["offsetMinutes"] is { } offset && (offset.GetValueKind() != System.Text.Json.JsonValueKind.Number || !((JsonValue)offset).TryGetValue<int>(out var value) || value is < 0 or > 525600)) throw Invalid("Invalid reminder offset.");
            copy.Remove("offsetMinutes");
        }
        if (copy.ContainsKey("absoluteTriggerAt"))
        { if (copy["absoluteTriggerAt"] is { } instant) { if (instant.GetValueKind() != System.Text.Json.JsonValueKind.String) throw Invalid("Invalid absolute reminder time."); _ = ReminderInstant(instant.GetValue<string>()); } copy.Remove("absoluteTriggerAt"); }
        ValidateFields(copy, "targetObjectId recipientUserId triggerType");
    }
    private static DateTimeOffset ReminderInstant(string? raw) => raw is { Length: > 0 } &&
        (raw.EndsWith('Z') || raw.Length >= 6 && (raw[^6] is '+' or '-') && raw[^3] == ':') &&
        DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant)
        ? new DateTimeOffset(instant.UtcTicks / 10 * 10, TimeSpan.Zero) : throw Invalid("Use an RFC3339 instant with timezone.");

    private static JsonObject RequireReminderTarget(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r, Guid target, bool actionable)
    {
        var row = One(c, t, "SELECT jsonb_build_object('objectType',o.object_type,'lifecycleState',o.lifecycle_state,'status',coalesce(task.status,event.status)," +
            "'startAt',coalesce(task.start_at_utc,event.start_at_utc),'deadlineAt',coalesce(task.deadline_at,event.end_at_utc)) " +
            "FROM core.objects o LEFT JOIN work.tasks task ON task.id=o.id LEFT JOIN calendar.events event ON event.id=o.id " +
            "WHERE o.organization_id=@org AND o.id=@target AND o.object_type IN ('task','calendar_event') AND o.lifecycle_state<>'trashed' " +
            "AND iam.object_allowed(@org,o.id,@user,CASE o.object_type WHEN 'task' THEN 'task.read' ELSE 'calendar.read' END,@admin) FOR SHARE OF o;", r, ("target", target));
        if (row is null) throw Error(404, "OBJECT_NOT_VISIBLE", "Reminder target is not visible.");
        if (actionable && (Text(row, "lifecycleState") != "active" || Text(row, "status") is null or "completed" or "cancelled")) throw Error(409, "INVALID_STATE_TRANSITION", "Reminder target is no longer active.");
        return row;
    }
    private static DateTimeOffset ResolveReminder(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r, JsonObject rule)
    {
        var target = RequireReminderTarget(c, t, r, GuidValue(rule, "targetObjectId"), true);
        var type = Text(rule, "triggerType");
        if (type == "absolute")
        {
            if (rule["offsetMinutes"] is not null) throw Invalid("Absolute trigger has no offset.");
            var absolute = ReminderInstant(Text(rule, "absoluteTriggerAt"));
            rule["absoluteTriggerAt"] = JsonValue.Create(absolute);
            return absolute;
        }
        if (type is not ("before_start" or "before_deadline" or "at_start" or "at_deadline") || rule["absoluteTriggerAt"] is not null) throw Invalid("Invalid reminder trigger.");
        var before = type.StartsWith("before_", StringComparison.Ordinal);
        if (!before && rule["offsetMinutes"] is not null) throw Invalid("At-time triggers have no offset.");
        if (before && rule["offsetMinutes"] is null)
        {
            var settings = One(c, t, "SELECT jsonb_build_object('offset',default_reminder_offset_minutes) FROM org.user_settings WHERE organization_id=@org AND user_account_id=@user;", r);
            rule["offsetMinutes"] = settings?["offset"]?.DeepClone() ?? JsonValue.Create(15);
        }
        var due = ReminderInstant(Text(target, type.EndsWith("start", StringComparison.Ordinal) ? "startAt" : "deadlineAt"));
        var offset = before ? rule["offsetMinutes"]!.GetValue<int>() : 0;
        if (offset is < 0 or > 525600) throw Invalid("Invalid reminder offset.");
        return due.AddMinutes(-offset);
    }
    private static JsonObject RequireReminder(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r)
    {
        var row = One(c, t, "SELECT to_jsonb(r)||jsonb_build_object('targetTitle',coalesce(task.title,event.title))" + ReminderFrom +
            "LEFT JOIN work.tasks task ON task.id=o.id LEFT JOIN calendar.events event ON event.id=o.id WHERE " + ReminderVisibility + " AND r.id=@id FOR UPDATE OF r;", r, ("id", r.Id));
        if (row is null) throw Error(404, "OBJECT_NOT_VISIBLE", "Reminder is not visible.");
        RequireReminderTarget(c, t, r, GuidValue(row, "targetObjectId"), false);
        AddOccurrence(c, t, r, row);
        return row;
    }
    private static void AddOccurrence(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r, JsonObject row)
    {
        row.Remove("createdBy");
        var occurrence = One(c, t, "SELECT to_jsonb(x) FROM calendar.reminder_occurrences x WHERE organization_id=@org AND reminder_id=@reminder " +
            "AND due_at=@due ORDER BY created_at DESC,id DESC LIMIT 1 FOR UPDATE;", r, ("reminder", GuidValue(row, "id")), ("due", ReminderInstant(Text(row, "nextTriggerAt"))));
        row["currentOccurrence"] = occurrence is null ? null : PublicOccurrence(occurrence);
        row["snoozedUntil"] = Text(row, "status") == "snoozed" ? row["nextTriggerAt"]?.DeepClone() : null;
    }
    private static JsonObject PublicOccurrence(JsonObject row)
    {
        var fields = "id organizationId version reminderId dueAt status attemptCount nextAttemptAt createdAt updatedAt".Split(' ').ToHashSet();
        return new JsonObject(row.Where(p => fields.Contains(p.Key)).Select(p => KeyValuePair.Create(p.Key,p.Value?.DeepClone())));
    }
    private static JsonObject PublicReminder(ProductApiRequest r, JsonObject row)
    {
        var extended = r.Query.GetValueOrDefault("includeOccurrence", "false");
        if (extended is not ("true" or "false")) throw Invalid("includeOccurrence must be true or false.");
        var fields = "id organizationId version createdAt updatedAt targetObjectId recipientUserId triggerType offsetMinutes absoluteTriggerAt nextTriggerAt status snoozedUntil deliveredAt".Split(' ').ToHashSet();
        if (extended == "true") { fields.Add("currentOccurrence"); fields.Add("targetTitle"); }
        return new JsonObject(row.Where(p => fields.Contains(p.Key)).Select(p => KeyValuePair.Create(p.Key,p.Value?.DeepClone())));
    }
    private static void CreateOccurrence(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r, Guid id, DateTimeOffset due)
    {
        var count = Run(c, t, "INSERT INTO calendar.reminder_occurrences(id,organization_id,reminder_id,due_at,next_attempt_at,idempotency_key) " +
            "VALUES(@occurrence,@org,@reminder,@due,@due,@key) ON CONFLICT(reminder_id,due_at) DO UPDATE SET " +
            "status='created',attempt_count=0,next_attempt_at=EXCLUDED.next_attempt_at,claimed_at=NULL,delivered_at=NULL,last_error_code=NULL " +
            "WHERE calendar.reminder_occurrences.status='cancelled' AND NOT EXISTS(SELECT 1 FROM notify.notifications n WHERE n.id=calendar.reminder_occurrences.id);", r,
            ("occurrence", Guid.NewGuid()), ("reminder", id), ("due", due), ("key", id + "|" + due.ToString("O")));
        if (count != 1) throw Error(409, "INVALID_STATE_TRANSITION", "This trigger has already been delivered. Choose a new time.");
    }
    private static void CancelOccurrences(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r, Guid id)
    {
        Run(c, t, "UPDATE calendar.reminder_occurrences SET status='cancelled',claimed_by_worker=NULL,lock_token=NULL,lease_expires_at=NULL,heartbeat_at=NULL " +
            "WHERE organization_id=@org AND reminder_id=@reminder AND status IN ('created','claimed','failed') " +
            "AND due_at=(SELECT next_trigger_at FROM calendar.reminders WHERE id=@reminder AND organization_id=@org); " +
            "UPDATE core.objects o SET version=o.version+1,updated_at=clock_timestamp(),updated_by=@user FROM notify.notifications n JOIN calendar.reminder_occurrences x ON x.id=n.id " +
            "WHERE o.id=n.id AND o.organization_id=@org AND x.reminder_id=@reminder AND x.due_at=(SELECT next_trigger_at FROM calendar.reminders WHERE id=@reminder) " +
            "AND n.recipient_user_id=@user AND n.status IN ('pending','delivered'); " +
            "UPDATE notify.notifications n SET status='dismissed',dismissed_at=clock_timestamp() FROM calendar.reminder_occurrences x WHERE n.organization_id=@org " +
            "AND n.recipient_user_id=@user AND n.id=x.id AND x.reminder_id=@reminder AND x.due_at=(SELECT next_trigger_at FROM calendar.reminders WHERE id=@reminder) " +
            "AND n.status IN ('pending','delivered');", r, ("reminder", id));
    }
}
