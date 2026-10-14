using Microsoft.Data.Sqlite;
using Task.Desktop.Calendar;
using Task.Domain;
using Task.Domain.Reminders;

namespace Task.Desktop.Personal;

public sealed partial class PersonalTaskStore
{
    private const string DeadlineSource = "task_deadline";
    private void InitializeDeadlineReminders()
    {
        using var tx = _database.Connection.BeginTransaction();
        using var check = Command("SELECT value FROM personal_metadata WHERE key='DeadlineReminderBackfillV5';", tx);
        if (check.ExecuteScalar() is null)
        {
            ReconcileAllDeadlineReminders(tx, backfill: true);
            using var mark = Command("INSERT INTO personal_metadata VALUES('DeadlineReminderBackfillV5','complete');", tx);
            mark.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private void ReconcileAllDeadlineReminders(SqliteTransaction tx, bool backfill = false)
    {
        using var c = Command("SELECT id FROM tasks;", tx);
        var ids = new List<Guid>();
        using (var reader = c.ExecuteReader()) while (reader.Read()) ids.Add(Guid.Parse(reader.GetString(0)));
        foreach (var id in ids) ReconcileDeadlineReminder(id, tx, backfill);
    }

    private void ReconcileDeadlineReminder(Guid id, SqliteTransaction tx, bool backfill = false)
    {
        var old = Reminders(tx).SingleOrDefault(r => r.TargetId == id && r.ManagedSource == DeadlineSource);
        var task = Get(id, tx);
        var card = task?.Card;
        if (task is null || TaskRules.IsTerminal((TaskWorkStatus)task.Status) || task.DeadlineAtUtc is null && card?.DeadlineDate is null)
        {
            if (old is not null && old.State != "expired") PersistReminder(old with { State = "expired", Version = old.Version + 1 }, tx);
            using var suppress = Command("UPDATE personal_notifications SET presentation_state='suppressed' WHERE target_id=$id AND presentation_state='pending' AND ($all=1 OR reminder_id=$rule);", tx,
                ("$id", id.ToString("D")), ("$all", task is null || TaskRules.IsTerminal((TaskWorkStatus)task.Status) ? 1 : 0), ("$rule", old?.Id.ToString("D")));
            suppress.ExecuteNonQuery();
            return;
        }
        var settings = ReadWorkspaceSettings("workspace", DefaultWorkspaceSettings, tx);
        var offset = card!.DeadlineReminderOffsetMinutes;
        using var schedule = Command("SELECT deadline_reminder_time,deadline_suppressed_due FROM tasks WHERE id=$id;", tx, ("$id", id.ToString("D")));
        string? time, suppressed;
        using (var r = schedule.ExecuteReader()) { r.Read(); time = r.IsDBNull(0) ? null : r.GetString(0); suppressed = r.IsDBNull(1) ? null : r.GetString(1); }
        if (offset is null)
        {
            var manual = backfill ? Reminders(tx).FirstOrDefault(r => r.TargetId == id && r.ManagedSource is null
                && r.TriggerType is ReminderTriggerType.BeforeDeadline or ReminderTriggerType.AtDeadline) : null;
            offset = manual is null ? settings.DefaultReminderOffsetMinutes : manual.State is "cancelled" or "expired" ? -1 : manual.OffsetMinutes ?? 0;
        }
        time ??= settings.DateOnlyReminderTime;
        var due = DeadlineReminderTime.Due(task.DeadlineAtUtc, card.DeadlineDate, card.DeadlineTimeZone, time, offset.Value);
        if (backfill && due <= _clock.GetUtcNow()) suppressed = Instant(due);
        using var snapshot = Command("UPDATE tasks SET deadline_reminder_offset=$offset,deadline_reminder_time=$time,deadline_suppressed_due=$suppressed WHERE id=$id;", tx,
            ("$id", id.ToString("D")), ("$offset", offset), ("$time", time), ("$suppressed", suppressed));
        snapshot.ExecuteNonQuery();
        if (due is null)
        {
            if (old is not null && old.State != "cancelled") PersistReminder(old with { State = "cancelled", Version = old.Version + 1 }, tx);
            return;
        }
        var zone = card.DeadlineTimeZone is { } z ? TimeZoneInfo.FindSystemTimeZoneById(z) : TimeZoneInfo.Local;
        var overdue = task.DeadlineAtUtc is { } exact ? exact <= _clock.GetUtcNow()
            : card.DeadlineDate < DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), zone).DateTime);
        var delivered = Notifications(tx).Any(n => n.TargetId == id && n.DueAt == due);
        var state = overdue || suppressed == Instant(due) ? "expired" : delivered ? "delivered" : "pending";
        var rule = old ?? new PersonalReminder(Guid.NewGuid(), 1, id, DesktopScheduleItemType.Task, ReminderTriggerType.Absolute,
            null, due, due, state, ManagedSource: DeadlineSource);
        var updated = rule with { AbsoluteAt = due, DueAt = due, State = state, DedupeKey = ReminderOccurrenceKey.From(rule.Id, due.Value).Value };
        if (old is null || updated != old) PersistReminder(updated with { Version = old is null ? 1 : old.Version + 1 }, tx);
    }
}
