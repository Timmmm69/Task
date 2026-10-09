using Microsoft.Data.Sqlite;
using Task.Desktop.Calendar;
using Task.Domain;
using Task.Domain.Reminders;

namespace Task.Desktop.Personal;

public sealed record PersonalReminder(Guid Id, long Version, Guid TargetId, DesktopScheduleItemType TargetKind,
    ReminderTriggerType TriggerType, int? OffsetMinutes, DateTimeOffset? AbsoluteAt, DateTimeOffset? DueAt,
    string State, string? DedupeKey = null, DateTimeOffset? DeliveredAt = null, string? LastDelivery = null);
public sealed record PersonalNotification(Guid Id, Guid ReminderId, string DedupeKey, Guid TargetId,
    string Title, DateTimeOffset DueAt, DateTimeOffset DeliveredAt, bool IsRead, string PresentationState)
{
    public string DeliveredText => DeliveredAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    public override string ToString() => Title;
}

public sealed partial class PersonalTaskStore
{
    private static PersonalReminder ReadReminder(SqliteDataReader r) => new(Guid.Parse(r.GetString(0)), r.GetInt64(1),
        Guid.Parse(r.GetString(2)), (DesktopScheduleItemType)r.GetInt32(3), (ReminderTriggerType)r.GetInt32(4),
        r.IsDBNull(5) ? null : r.GetInt32(5), ReadInstant(r, 6), ReadInstant(r, 7), r.GetString(8),
        r.IsDBNull(9) ? null : r.GetString(9), ReadInstant(r, 10), r.IsDBNull(11) ? null : r.GetString(11));
    private IReadOnlyList<PersonalReminder> Reminders(SqliteTransaction? tx)
    {
        using var c = Command("SELECT * FROM personal_reminders ORDER BY id;", tx); using var r = c.ExecuteReader();
        var result = new List<PersonalReminder>(); while (r.Read()) result.Add(ReadReminder(r)); return result;
    }
    public IReadOnlyList<PersonalReminder> Reminders() => Locked(() => Reminders(null));
    private (bool Valid, string Title, DateTimeOffset? Start, DateTimeOffset? Deadline) ReminderTarget(PersonalReminder reminder, SqliteTransaction tx)
    {
        if (reminder.TargetKind == DesktopScheduleItemType.Task)
        {
            var task = Get(reminder.TargetId, tx);
            return (task is not null && !TaskRules.IsTerminal((TaskWorkStatus)task.Status), task?.Title ?? "", task?.StartAtUtc, task?.DeadlineAtUtc);
        }
        var e = Event(reminder.TargetId, tx); return (e?.Status == "scheduled", e?.Title ?? "", e?.StartAtUtc, e?.EndAtUtc);
    }
    private static DateTimeOffset? ReminderDue(PersonalReminder r, DateTimeOffset? start, DateTimeOffset? deadline) => r.TriggerType switch
    {
        ReminderTriggerType.Absolute => r.AbsoluteAt,
        ReminderTriggerType.AtStart => start,
        ReminderTriggerType.AtDeadline => deadline,
        ReminderTriggerType.BeforeStart => start?.AddMinutes(-r.OffsetMinutes!.Value),
        ReminderTriggerType.BeforeDeadline => deadline?.AddMinutes(-r.OffsetMinutes!.Value),
        _ => throw new ArgumentException("Unknown reminder trigger.")
    };
    private void PersistReminder(PersonalReminder reminder, SqliteTransaction tx)
    {
        using var c = Command("""
            INSERT INTO personal_reminders VALUES ($id,$version,$target,$kind,$type,$offset,$absolute,$due,$state,$key,$delivered,$metadata)
            ON CONFLICT(id) DO UPDATE SET version=$version,due_at=$due,state=$state,dedupe_key=$key,
            delivered_at=$delivered,last_delivery=$metadata;
            """, tx, ("$id", reminder.Id.ToString("D")), ("$version", reminder.Version), ("$target", reminder.TargetId.ToString("D")),
            ("$kind", (int)reminder.TargetKind), ("$type", (int)reminder.TriggerType), ("$offset", reminder.OffsetMinutes),
            ("$absolute", Instant(reminder.AbsoluteAt)), ("$due", Instant(reminder.DueAt)), ("$state", reminder.State),
            ("$key", reminder.DedupeKey), ("$delivered", Instant(reminder.DeliveredAt)), ("$metadata", reminder.LastDelivery));
        c.ExecuteNonQuery();
    }
    public PersonalReminder AddReminder(Guid targetId, DesktopScheduleItemType kind, ReminderTriggerType type, int? offset = null, DateTimeOffset? absolute = null) => Locked(() =>
    {
        _ = ReminderTrigger.Create(type, offset, absolute);
        if (!Enum.IsDefined(kind)) throw new ArgumentException("Unknown reminder target type.");
        using var tx = _database.Connection.BeginTransaction();
        var reminder = new PersonalReminder(Guid.NewGuid(), 1, targetId, kind, type, offset, absolute, null, "pending");
        var target = ReminderTarget(reminder, tx); if (!target.Valid) throw new ArgumentException("Choose an active Personal task or event.");
        var due = ReminderDue(reminder, target.Start, target.Deadline);
        if (due is null) throw new ArgumentException("Для этого напоминания нужно точное время начала или срока. Для задачи без времени выберите конкретное время напоминания.");
        reminder = reminder with { DueAt = due, DedupeKey = ReminderOccurrenceKey.From(reminder.Id, due.Value).Value };
        PersistReminder(reminder, tx); tx.Commit(); return reminder;
    });
    public PersonalReminder CancelReminder(Guid id, long version) => Locked(() =>
    {
        using var tx = _database.Connection.BeginTransaction(); var old = Reminders(tx).SingleOrDefault(r => r.Id == id) ?? throw new PersonalTaskNotFoundException();
        if (old.Version != version) throw new PersonalVersionConflictException();
        if (old.State is "cancelled" or "expired") throw new ArgumentException("Reminder is already terminal.");
        var cancelled = old with { Version = old.Version + 1, State = "cancelled" }; PersistReminder(cancelled, tx); tx.Commit(); return cancelled;
    });
    /// <summary>Reconciles against current target schedules. Notification record and delivery commit atomically.</summary>
    public int ReconcileReminders() => Locked(() =>
    {
        var now = _clock.GetUtcNow(); var delivered = 0; using var tx = _database.Connection.BeginTransaction();
        foreach (var old in Reminders(tx).Where(r => r.State is "pending" or "due"))
        {
            var target = ReminderTarget(old, tx);
            if (!target.Valid) { PersistReminder(old with { Version = old.Version + 1, State = "expired" }, tx); continue; }
            var due = ReminderDue(old, target.Start, target.Deadline);
            var updated = old with { DueAt = due, DedupeKey = due is { } at ? ReminderOccurrenceKey.From(old.Id, at).Value : null };
            if (due is null || due > now)
            {
                updated = updated with { State = "pending" };
                if (updated != old) PersistReminder(updated with { Version = old.Version + 1 }, tx);
                continue;
            }
            // Recover legacy/persisted due state through the same dedupe constraint.
            updated = updated with { State = "due", Version = old.Version + 1 }; PersistReminder(updated, tx);
            using var notification = Command("""
                INSERT INTO personal_notifications(id,reminder_id,dedupe_key,target_id,title,due_at,delivered_at)
                VALUES ($id,$reminder,$key,$target,$title,$due,$now) ON CONFLICT(dedupe_key) DO NOTHING;
                """, tx, ("$id", Guid.NewGuid().ToString("D")), ("$reminder", old.Id.ToString("D")), ("$key", updated.DedupeKey),
                ("$target", old.TargetId.ToString("D")), ("$title", target.Title), ("$due", Instant(due)), ("$now", Instant(now)));
            delivered += notification.ExecuteNonQuery();
            PersistReminder(updated with
            {
                State = "delivered",
                Version = updated.Version + 1,
                DeliveredAt = now,
                LastDelivery = "Personal notification center; Windows presentation tracked separately"
            }, tx);
        }
        tx.Commit(); return delivered;
    });
    private IReadOnlyList<PersonalNotification> Notifications(SqliteTransaction? tx)
    {
        using var c = Command("SELECT * FROM personal_notifications ORDER BY delivered_at DESC,id;", tx); using var r = c.ExecuteReader();
        var result = new List<PersonalNotification>();
        while (r.Read()) result.Add(new(Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), r.GetString(2), Guid.Parse(r.GetString(3)),
            r.GetString(4), ReadInstant(r, 5)!.Value, ReadInstant(r, 6)!.Value, r.GetBoolean(7), r.GetString(8)));
        return result;
    }
    public IReadOnlyList<PersonalNotification> Notifications() => Locked(() => Notifications(null));
    public void MarkAllPersonalNotificationsRead() => Locked(() =>
    {
        using var c = Command("UPDATE personal_notifications SET is_read=1;", null); return c.ExecuteNonQuery();
    });
    public PersonalReminder SnoozeNotification(Guid notificationId, DateTimeOffset? until = null) => Locked(() =>
    {
        using var tx = _database.Connection.BeginTransaction();
        var notification = Notifications(tx).SingleOrDefault(n => n.Id == notificationId) ?? throw new PersonalTaskNotFoundException();
        var old = Reminders(tx).Single(r => r.Id == notification.ReminderId);
        if (notification.IsRead || old.State != "delivered") throw new ArgumentException("Это напоминание уже обработано.");
        var target = ReminderTarget(old, tx);
        if (!target.Valid) throw new ArgumentException("Задача или событие больше неактуальны.");
        var due = until ?? _clock.GetUtcNow().AddMinutes(ReadWorkspaceSettings("notifications", DefaultWorkspaceNotifications, tx).DefaultSnoozeMinutes);
        if (due <= _clock.GetUtcNow() || due > _clock.GetUtcNow().AddDays(7)) throw new ArgumentException("Выберите время в ближайшие семь дней.");
        var reminder = new PersonalReminder(Guid.NewGuid(), 1, old.TargetId, old.TargetKind, ReminderTriggerType.Absolute,
            null, due, due, "pending");
        reminder = reminder with { DedupeKey = ReminderOccurrenceKey.From(reminder.Id, due).Value };
        PersistReminder(reminder, tx);
        PersistReminder(old with { State = "cancelled", Version = old.Version + 1 }, tx);
        using var read = Command("UPDATE personal_notifications SET is_read=1 WHERE id=$id;", tx, ("$id", notificationId.ToString("D")));
        read.ExecuteNonQuery(); tx.Commit(); return reminder;
    });
    public void MarkNotificationRead(Guid id) => Locked(() =>
    {
        using var c = Command("UPDATE personal_notifications SET is_read=1 WHERE id=$id;", null, ("$id", id.ToString("D"))); return c.ExecuteNonQuery();
    });
    public IReadOnlyList<PersonalNotification> ClaimPresentations() => Locked(() =>
    {
        using var tx = _database.Connection.BeginTransaction();
        var allowed = CanPresentPersonalNotification(tx);
        var reminders = Reminders(tx).ToDictionary(r => r.Id);
        var pending = allowed ? Notifications(tx).Where(n => n.PresentationState == "pending" && !n.IsRead
            && reminders.TryGetValue(n.ReminderId, out var r) && r.State == "delivered" && ReminderTarget(r, tx).Valid).ToArray() : [];
        if (allowed)
        {
            var eligible = pending.Select(n => n.Id).ToHashSet();
            foreach (var n in Notifications(tx).Where(n => n.PresentationState == "pending"))
            {
                using var claim = Command("UPDATE personal_notifications SET presentation_state=$state WHERE id=$id AND presentation_state='pending';", tx,
                    ("$id", n.Id.ToString("D")), ("$state", eligible.Contains(n.Id) ? "claimed" : "suppressed"));
                claim.ExecuteNonQuery();
            }
        }
        tx.Commit(); return (IReadOnlyList<PersonalNotification>)pending;
    });
    public void CompletePresentation(Guid id, bool accepted) => Locked(() =>
    {
        using var c = Command("UPDATE personal_notifications SET presentation_state=$state WHERE id=$id AND presentation_state='claimed';", null,
            ("$id", id.ToString("D")), ("$state", accepted ? "submitted" : "pending")); return c.ExecuteNonQuery();
    });
}

/// <summary>Runs only within Personal. Caller supplies the wake/timer cadence; TimeProvider drives every decision.</summary>
public sealed class PersonalPlanningScheduler(PersonalTaskStore store, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    public string? LastError { get; private set; }
    public int RunPass()
    {
        LastError = null;
        foreach (var series in store.RecurrenceSeries().Where(s => s.Definition.Status == "active"))
        {
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(series.Definition.TimeZone)).DateTime);
            var through = today.AddDays(62); var cursor = series.Definition.NextGenerationDate ?? series.Definition.OccurrenceStartDate;
            if (cursor > through) continue;
            var end = cursor.AddDays(Math.Min(366, through.DayNumber - cursor.DayNumber));
            try { store.ExecuteRecurrence(new DesktopRecurrenceCommand.Generate(series.Id, series.Version, end, $"local-horizon-{series.Id:N}-{series.Version}-{end:yyyy-MM-dd}")); }
            catch (PersonalVersionConflictException) { /* next pass reloads */ }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or SqliteException or System.IO.IOException or UnauthorizedAccessException) { LastError = e.Message; }
        }
        return store.ReconcileReminders();
    }
}
