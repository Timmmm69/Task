namespace Task.Domain.Reminders;

/// <summary>
/// Root of the Reminder aggregate. Immutable: every visible transition
/// returns a new instance whose <see cref="SyncableEntityMetadata"/> records
/// the change and advances the version. A reminder never touches the
/// Task/Event schedule; snooze changes only reminder fields (BR-045, AC-045).
/// </summary>
public sealed class Reminder
{
    private Reminder(
        SyncableEntityMetadata metadata,
        Guid targetObjectId,
        Guid recipientUserId,
        ReminderTrigger trigger,
        DateTimeOffset nextTriggerAt,
        ReminderStatus status,
        DateTimeOffset? snoozedUntil,
        DateTimeOffset? deliveredAt)
    {
        Metadata = metadata;
        TargetObjectId = targetObjectId;
        RecipientUserId = recipientUserId;
        Trigger = trigger;
        NextTriggerAt = nextTriggerAt;
        Status = status;
        SnoozedUntil = snoozedUntil;
        DeliveredAt = deliveredAt;
    }

    public SyncableEntityMetadata Metadata { get; }

    public Guid TargetObjectId { get; }

    public Guid RecipientUserId { get; }

    public ReminderTrigger Trigger { get; }

    /// <summary>The instant the reminder is currently scheduled to fire.</summary>
    public DateTimeOffset NextTriggerAt { get; }

    public ReminderStatus Status { get; }

    /// <summary>
    /// Present exactly while the reminder is <see cref="ReminderStatus.Snoozed"/>
    /// and equal to <see cref="NextTriggerAt"/>.
    /// </summary>
    public DateTimeOffset? SnoozedUntil { get; }

    /// <summary>Instant of a completed delivery; present only for a delivered reminder.</summary>
    public DateTimeOffset? DeliveredAt { get; }

    /// <summary>
    /// Creates a reminder in the <see cref="ReminderStatus.Scheduled"/> status
    /// with exactly one trigger mode. The effective firing instant must not
    /// precede creation and, for an absolute trigger, must equal
    /// <see cref="ReminderTrigger.AbsoluteTriggerAt"/>.
    /// </summary>
    public static Reminder Create(
        Guid id,
        Guid organizationId,
        Guid targetObjectId,
        Guid recipientUserId,
        ReminderTrigger trigger,
        DateTimeOffset nextTriggerAt,
        Guid createdBy,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        EnsureIdentifier(targetObjectId, nameof(targetObjectId));
        EnsureIdentifier(recipientUserId, nameof(recipientUserId));
        EnsureUtc(nextTriggerAt, nameof(nextTriggerAt));

        if (trigger.Type == ReminderTriggerType.Absolute && trigger.AbsoluteTriggerAt != nextTriggerAt)
        {
            throw new ArgumentException(
                "For an absolute trigger the effective firing instant must equal absoluteTriggerAt.",
                nameof(nextTriggerAt));
        }

        if (nextTriggerAt < createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nextTriggerAt),
                "The effective firing instant must not precede creation.");
        }

        var metadata = SyncableEntityMetadata.Create(id, organizationId, createdBy, createdAtUtc);
        return new Reminder(
            metadata,
            targetObjectId,
            recipientUserId,
            trigger,
            nextTriggerAt,
            ReminderStatus.Scheduled,
            snoozedUntil: null,
            deliveredAt: null);
    }

    /// <summary>
    /// Rebuilds a reminder from persisted state and validates all invariants,
    /// including the trigger configuration, deliver/snooze consistency and
    /// the cancelled-and-trashed pairing.
    /// </summary>
    public static Reminder Reconstitute(
        SyncableEntityMetadata metadata,
        Guid targetObjectId,
        Guid recipientUserId,
        ReminderTrigger trigger,
        DateTimeOffset nextTriggerAt,
        ReminderStatus status,
        DateTimeOffset? snoozedUntil,
        DateTimeOffset? deliveredAt)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(trigger);
        EnsureIdentifier(targetObjectId, nameof(targetObjectId));
        EnsureIdentifier(recipientUserId, nameof(recipientUserId));
        EnsureUtc(nextTriggerAt, nameof(nextTriggerAt));

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), "Unknown reminder status.");
        }

        if (nextTriggerAt < metadata.CreatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nextTriggerAt),
                "The effective firing instant must not precede creation.");
        }

        EnsureOptionalUtc(snoozedUntil, nameof(snoozedUntil));
        EnsureOptionalUtc(deliveredAt, nameof(deliveredAt));

        var isSnoozed = status == ReminderStatus.Snoozed;
        if (isSnoozed != (snoozedUntil is not null))
        {
            throw new ArgumentException("snoozedUntil must be present exactly while the reminder is snoozed.", nameof(snoozedUntil));
        }

        if (isSnoozed && snoozedUntil!.Value != nextTriggerAt)
        {
            throw new ArgumentException(
                "While snoozed, snoozedUntil must equal the effective firing instant.",
                nameof(snoozedUntil));
        }

        var isDelivered = status == ReminderStatus.Delivered;
        if (isDelivered != (deliveredAt is not null))
        {
            throw new ArgumentException("deliveredAt must be present exactly for a delivered reminder.", nameof(deliveredAt));
        }

        if (deliveredAt is not null &&
            (deliveredAt.Value < metadata.CreatedAtUtc || deliveredAt.Value > metadata.UpdatedAtUtc))
        {
            throw new ArgumentOutOfRangeException(
                nameof(deliveredAt),
                "deliveredAt must be UTC and between creation and the last update.");
        }

        if (status == ReminderStatus.Cancelled != (metadata.LifecycleState == EntityLifecycleState.Trashed))
        {
            throw new ArgumentException(
                "A cancelled reminder must be in trash and only a trashed reminder can be cancelled.",
                nameof(status));
        }

        if (metadata.LifecycleState is not (EntityLifecycleState.Active or EntityLifecycleState.Trashed))
        {
            throw new ArgumentException("A reminder cannot be archived.", nameof(metadata));
        }

        return new Reminder(
            metadata,
            targetObjectId,
            recipientUserId,
            trigger,
            nextTriggerAt,
            status,
            snoozedUntil,
            deliveredAt);
    }

    /// <summary>
    /// Marks the reminder due. Allowed from <see cref="ReminderStatus.Scheduled"/>
    /// and from <see cref="ReminderStatus.Snoozed"/>; leaving snooze clears
    /// snoozedUntil while the previously configured trigger is retained
    /// (BR-045, AC-045).
    /// </summary>
    public Reminder MarkDue(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        EnsureActive("A cancelled or expired reminder cannot fire.");
        if (Status is not (ReminderStatus.Scheduled or ReminderStatus.Snoozed))
        {
            throw new InvalidOperationException("Only a scheduled or snoozed reminder can become due.");
        }

        return new Reminder(
            Metadata.RecordVisibleChange(actorId, occurredAtUtc),
            TargetObjectId,
            RecipientUserId,
            Trigger,
            NextTriggerAt,
            ReminderStatus.Due,
            snoozedUntil: null,
            deliveredAt: null);
    }

    /// <summary>
    /// Marks the reminder delivered. Only a due reminder can be delivered.
    /// </summary>
    public Reminder MarkDelivered(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        EnsureActive("A cancelled or expired reminder cannot be delivered.");
        if (Status != ReminderStatus.Due)
        {
            throw new InvalidOperationException("Only a due reminder can be delivered.");
        }

        return new Reminder(
            Metadata.RecordVisibleChange(actorId, occurredAtUtc),
            TargetObjectId,
            RecipientUserId,
            Trigger,
            NextTriggerAt,
            ReminderStatus.Delivered,
            snoozedUntil: null,
            deliveredAt: occurredAtUtc);
    }

    /// <summary>
    /// Snoozes the reminder until a UTC instant strictly later than the
    /// current firing instant. Snooze changes only reminder fields:
    /// status becomes <see cref="ReminderStatus.Snoozed"/>, snoozedUntil and
    /// the effective firing instant become <paramref name="until"/>; the
    /// trigger and the Task/Event schedule are not touched (BR-045, AC-045).
    /// </summary>
    public Reminder Snooze(Guid actorId, DateTimeOffset until, DateTimeOffset occurredAtUtc)
    {
        EnsureActive("A cancelled or expired reminder cannot be snoozed.");
        if (IsTerminal)
        {
            throw new InvalidOperationException("A cancelled or expired reminder cannot be snoozed.");
        }

        EnsureUtc(until, nameof(until));
        if (until <= NextTriggerAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(until),
                "A snooze target must be strictly later than the current firing instant.");
        }

        return new Reminder(
            Metadata.RecordVisibleChange(actorId, occurredAtUtc),
            TargetObjectId,
            RecipientUserId,
            Trigger,
            until,
            ReminderStatus.Snoozed,
            snoozedUntil: until,
            deliveredAt: null);
    }

    /// <summary>
    /// Cancels the reminder: mirrors DELETE /api/v1/reminders/{id} by
    /// moving the aggregate into trash so it hides from active views, and
    /// setting the terminal <see cref="ReminderStatus.Cancelled"/> status.
    /// Allowed from any active status.
    /// </summary>
    public Reminder Cancel(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        EnsureActive("A cancelled or expired reminder cannot be cancelled.");
        if (IsTerminal)
        {
            throw new InvalidOperationException("A cancelled or expired reminder cannot be cancelled.");
        }

        return new Reminder(
            Metadata.MoveToTrash(actorId, occurredAtUtc),
            TargetObjectId,
            RecipientUserId,
            Trigger,
            NextTriggerAt,
            ReminderStatus.Cancelled,
            snoozedUntil: null,
            deliveredAt: null);
    }

    /// <summary>
    /// Marks the reminder expired. Allowed from any active status; the
    /// reminder is not sent to trash. Delivery and snooze fields are cleared.
    /// </summary>
    public Reminder Expire(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        EnsureActive("A cancelled or expired reminder cannot expire.");
        if (IsTerminal)
        {
            throw new InvalidOperationException("A cancelled or expired reminder cannot expire.");
        }

        return new Reminder(
            Metadata.RecordVisibleChange(actorId, occurredAtUtc),
            TargetObjectId,
            RecipientUserId,
            Trigger,
            NextTriggerAt,
            ReminderStatus.Expired,
            snoozedUntil: null,
            deliveredAt: null);
    }

    /// <summary>
    /// Reschedules a cancelled reminder: mirrors POST /api/v1/reminders/{id}
    /// /reschedule by restoring the aggregate from trash, returning to
    /// <see cref="ReminderStatus.Scheduled"/> and accepting a new future
    /// effective firing instant. The configured trigger stays untouched and
    /// the restore reason is a boundary/API detail.
    /// </summary>
    public Reminder Reschedule(Guid actorId, DateTimeOffset nextTriggerAt, DateTimeOffset occurredAtUtc)
    {
        if (Status != ReminderStatus.Cancelled || Metadata.LifecycleState != EntityLifecycleState.Trashed)
        {
            throw new InvalidOperationException("Only a cancelled reminder in trash can be rescheduled.");
        }

        EnsureUtc(nextTriggerAt, nameof(nextTriggerAt));
        if (nextTriggerAt <= occurredAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nextTriggerAt),
                "A rescheduled firing instant must be in the future.");
        }

        return new Reminder(
            Metadata.RestoreFromTrash(actorId, occurredAtUtc),
            TargetObjectId,
            RecipientUserId,
            Trigger,
            nextTriggerAt,
            ReminderStatus.Scheduled,
            snoozedUntil: null,
            deliveredAt: null);
    }

    private bool IsTerminal =>
        Status is ReminderStatus.Cancelled or ReminderStatus.Expired;

    private void EnsureActive(string message)
    {
        if (Metadata.LifecycleState != EntityLifecycleState.Active)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void EnsureIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", parameterName);
        }
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must use the UTC offset.", parameterName);
        }
    }

    private static void EnsureOptionalUtc(DateTimeOffset? value, string parameterName)
    {
        if (value.HasValue)
        {
            EnsureUtc(value.Value, parameterName);
        }
    }
}