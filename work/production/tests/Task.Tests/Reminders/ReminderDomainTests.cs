using Task.Domain;
using Task.Domain.Reminders;

namespace Task.Tests.Reminders;

public sealed class ReminderDomainTests
{
    private static readonly Guid ReminderId = Guid.Parse("7ab1f2c3-4d5e-4f6a-9b8c-1d2e3f4a5b6c");
    private static readonly Guid OrganizationId = Guid.Parse("751fa8ce-5cc3-4d98-8574-1108080b2ff4");
    private static readonly Guid TargetObjectId = Guid.Parse("957c3a11-d6f2-4a2a-bb1b-0945a0f6a820");
    private static readonly Guid RecipientId = Guid.Parse("3077f0f8-536f-4988-bd73-6f26265d0b92");
    private static readonly Guid ActorId = Guid.Parse("b64fbeec-f0f4-4f5f-9967-ea2ce57be461");
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 16, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_AcceptsEveryValidTriggerMode()
    {
        var absolute = ReminderTrigger.Create(ReminderTriggerType.Absolute, null, CreatedAt.AddHours(2));
        var beforeStart = ReminderTrigger.Create(ReminderTriggerType.BeforeStart, 30, null);
        var beforeDeadline = ReminderTrigger.Create(ReminderTriggerType.BeforeDeadline, 525600, null);
        var atStart = ReminderTrigger.Create(ReminderTriggerType.AtStart, null, null);
        var atDeadline = ReminderTrigger.Create(ReminderTriggerType.AtDeadline, null, null);

        Assert.Equal(ReminderTriggerType.Absolute, absolute.Type);
        Assert.Equal(CreatedAt.AddHours(2), absolute.AbsoluteTriggerAt);
        Assert.Null(absolute.OffsetMinutes);

        Assert.Equal(30, beforeStart.OffsetMinutes);
        Assert.Null(beforeStart.AbsoluteTriggerAt);
        Assert.Equal(525600, beforeDeadline.OffsetMinutes);
        Assert.Null(atStart.OffsetMinutes);
        Assert.Null(atStart.AbsoluteTriggerAt);
        Assert.Null(atDeadline.OffsetMinutes);
        Assert.Null(atDeadline.AbsoluteTriggerAt);

        var reminder = Reminder.Create(
            ReminderId,
            OrganizationId,
            TargetObjectId,
            RecipientId,
            absolute,
            absolute.AbsoluteTriggerAt!.Value,
            ActorId,
            CreatedAt);
        Assert.Equal(ReminderStatus.Scheduled, reminder.Status);
        Assert.Equal(1, reminder.Metadata.Version);
        Assert.Equal(ReminderTriggerType.Absolute, reminder.Trigger.Type);
    }

    [Fact]
    public void Create_RejectsContradictoryTriggerConfiguration()
    {
        Assert.Throws<ArgumentException>(() => ReminderTrigger.Create(ReminderTriggerType.Absolute, 30, null));
        Assert.Throws<ArgumentException>(() => ReminderTrigger.Create(ReminderTriggerType.Absolute, 30, CreatedAt.AddHours(2)));
        Assert.Throws<ArgumentException>(() => ReminderTrigger.Create(ReminderTriggerType.BeforeStart, null, CreatedAt.AddHours(2)));
        Assert.Throws<ArgumentException>(() => ReminderTrigger.Create(ReminderTriggerType.BeforeStart, 30, CreatedAt.AddHours(2)));
        Assert.Throws<ArgumentException>(() => ReminderTrigger.Create(ReminderTriggerType.BeforeDeadline, null, null));
        Assert.Throws<ArgumentException>(() => ReminderTrigger.Create(ReminderTriggerType.AtStart, 30, null));
        Assert.Throws<ArgumentException>(() => ReminderTrigger.Create(ReminderTriggerType.AtStart, null, CreatedAt.AddHours(2)));
        Assert.Throws<ArgumentException>(() => ReminderTrigger.Create(ReminderTriggerType.AtDeadline, 30, CreatedAt.AddHours(2)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ReminderTrigger.Create((ReminderTriggerType)99, null, null));
    }

    [Fact]
    public void Create_EnforcesOffsetBoundaries()
    {
        var zero = ReminderTrigger.Create(ReminderTriggerType.BeforeStart, 0, null);
        var max = ReminderTrigger.Create(ReminderTriggerType.BeforeDeadline, 525600, null);
        Assert.Equal(0, zero.OffsetMinutes);
        Assert.Equal(525600, max.OffsetMinutes);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ReminderTrigger.Create(ReminderTriggerType.BeforeStart, -1, null));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ReminderTrigger.Create(ReminderTriggerType.BeforeDeadline, 525601, null));
    }

    [Fact]
    public void Create_RejectsEmptyIdentifiersAndInvalidTimestamps()
    {
        var absolute = ReminderTrigger.Create(ReminderTriggerType.Absolute, null, CreatedAt.AddHours(2));
        var nonUtc = new DateTimeOffset(2026, 8, 16, 14, 0, 0, TimeSpan.FromHours(5));

        Assert.Throws<ArgumentException>(() => Reminder.Create(
            Guid.Empty, OrganizationId, TargetObjectId, RecipientId, absolute, absolute.AbsoluteTriggerAt!.Value, ActorId, CreatedAt));
        Assert.Throws<ArgumentException>(() => Reminder.Create(
            ReminderId, Guid.Empty, TargetObjectId, RecipientId, absolute, absolute.AbsoluteTriggerAt!.Value, ActorId, CreatedAt));
        Assert.Throws<ArgumentException>(() => Reminder.Create(
            ReminderId, OrganizationId, Guid.Empty, RecipientId, absolute, absolute.AbsoluteTriggerAt!.Value, ActorId, CreatedAt));
        Assert.Throws<ArgumentException>(() => Reminder.Create(
            ReminderId, OrganizationId, TargetObjectId, Guid.Empty, absolute, absolute.AbsoluteTriggerAt!.Value, ActorId, CreatedAt));
        Assert.Throws<ArgumentException>(() => Reminder.Create(
            ReminderId, OrganizationId, TargetObjectId, RecipientId, absolute, absolute.AbsoluteTriggerAt!.Value, Guid.Empty, CreatedAt));
        Assert.Throws<ArgumentException>(() => Reminder.Create(
            ReminderId, OrganizationId, TargetObjectId, RecipientId, absolute, nonUtc, ActorId, CreatedAt));
        Assert.Throws<ArgumentException>(() => ReminderTrigger.Create(
            ReminderTriggerType.Absolute, null, nonUtc));
        var atStart = ReminderTrigger.Create(ReminderTriggerType.AtStart, null, null);
        Assert.Throws<ArgumentOutOfRangeException>(() => Reminder.Create(
            ReminderId, OrganizationId, TargetObjectId, RecipientId, atStart, CreatedAt.AddMinutes(-1), ActorId, CreatedAt));
    }

    [Fact]
    public void Create_RejectsAbsoluteReminderWhoseFiringInstantDriftsFromAbsoluteTriggerAt()
    {
        var absolute = ReminderTrigger.Create(ReminderTriggerType.Absolute, null, CreatedAt.AddHours(2));

        Assert.Throws<ArgumentException>(() => Reminder.Create(
            ReminderId, OrganizationId, TargetObjectId, RecipientId, absolute, CreatedAt.AddHours(3), ActorId, CreatedAt));
    }

    [Fact]
    public void MarkDue_And_MarkDelivered_AdvanceAlongTheAllowedPath()
    {
        var reminder = MakeReminder(
            ReminderTrigger.Create(ReminderTriggerType.AtStart, null, null));

        var due = reminder.MarkDue(ActorId, CreatedAt.AddMinutes(1));
        Assert.Equal(ReminderStatus.Due, due.Status);
        Assert.Equal(2, due.Metadata.Version);

        var delivered = due.MarkDelivered(ActorId, CreatedAt.AddMinutes(2));
        Assert.Equal(ReminderStatus.Delivered, delivered.Status);
        Assert.Equal(CreatedAt.AddMinutes(2), delivered.DeliveredAt);
        Assert.Equal(3, delivered.Metadata.Version);
    }

    [Fact]
    public void MarkDue_And_MarkDelivered_RejectIllegalTransitions()
    {
        var reminder = MakeReminder(ReminderTrigger.Create(ReminderTriggerType.AtStart, null, null));
        var due = reminder.MarkDue(ActorId, CreatedAt.AddMinutes(1));
        var delivered = due.MarkDelivered(ActorId, CreatedAt.AddMinutes(2));
        var cancelled = reminder.Cancel(ActorId, CreatedAt.AddMinutes(1));
        var expired = reminder.Expire(ActorId, CreatedAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => reminder.MarkDelivered(ActorId, CreatedAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => due.MarkDue(ActorId, CreatedAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => delivered.MarkDelivered(ActorId, CreatedAt.AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() => delivered.MarkDue(ActorId, CreatedAt.AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() => cancelled.MarkDue(ActorId, CreatedAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => expired.MarkDelivered(ActorId, CreatedAt.AddMinutes(2)));
    }

    [Fact]
    public void Snooze_ChangesOnlyReminderFieldsAndKeepsTheTrigger()
    {
        var trigger = ReminderTrigger.Create(ReminderTriggerType.BeforeStart, 15, null);
        var reminder = MakeReminder(trigger);
        var until = CreatedAt.AddHours(3);

        var snoozed = reminder.Snooze(ActorId, until, CreatedAt.AddMinutes(1));

        Assert.Equal(ReminderStatus.Snoozed, snoozed.Status);
        Assert.Equal(until, snoozed.SnoozedUntil);
        Assert.Equal(until, snoozed.NextTriggerAt);
        Assert.Equal(trigger, snoozed.Trigger);
        Assert.Null(snoozed.DeliveredAt);
        Assert.Equal(TargetObjectId, snoozed.TargetObjectId);
        Assert.Equal(RecipientId, snoozed.RecipientUserId);
        Assert.Equal(2, snoozed.Metadata.Version);
    }

    [Fact]
    public void Snooze_IsAllowedForScheduledDueAndDeliveredButNotInThePast()
    {
        var reminder = MakeReminder(ReminderTrigger.Create(ReminderTriggerType.BeforeDeadline, 60, null));
        var due = reminder.MarkDue(ActorId, CreatedAt.AddMinutes(1));
        var delivered = due.MarkDelivered(ActorId, CreatedAt.AddMinutes(2));

        Assert.Equal(ReminderStatus.Snoozed, reminder.Snooze(ActorId, CreatedAt.AddHours(2), CreatedAt.AddMinutes(1)).Status);
        Assert.Equal(ReminderStatus.Snoozed, due.Snooze(ActorId, CreatedAt.AddHours(2), CreatedAt.AddMinutes(3)).Status);
        Assert.Equal(ReminderStatus.Snoozed, delivered.Snooze(ActorId, CreatedAt.AddHours(2), CreatedAt.AddMinutes(4)).Status);
        Assert.Null(delivered.Snooze(ActorId, CreatedAt.AddHours(2), CreatedAt.AddMinutes(4)).DeliveredAt);

        var snoozed = reminder.Snooze(ActorId, CreatedAt.AddHours(2), CreatedAt.AddMinutes(1));
        var reSnoozed = snoozed.Snooze(ActorId, CreatedAt.AddHours(4), CreatedAt.AddMinutes(3));
        Assert.Equal(CreatedAt.AddHours(4), reSnoozed.SnoozedUntil);
        Assert.Equal(reSnoozed.SnoozedUntil, reSnoozed.NextTriggerAt);

        Assert.Throws<ArgumentOutOfRangeException>(() => reminder.Snooze(ActorId, CreatedAt.AddMinutes(-1), CreatedAt.AddMinutes(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => reminder.Snooze(ActorId, CreatedAt.AddHours(1), CreatedAt.AddMinutes(1)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => reminder.Snooze(ActorId, CreatedAt.AddHours(1).AddTicks(-1), CreatedAt.AddMinutes(1)));
        Assert.Throws<ArgumentException>(() => reminder.Snooze(
            ActorId, new DateTimeOffset(2026, 8, 16, 14, 0, 0, TimeSpan.FromHours(5)), CreatedAt.AddMinutes(1)));
    }

    [Fact]
    public void LeavingSnoozeIntoDueOrDeliveredRetainsTheConfiguredTrigger()
    {
        var trigger = ReminderTrigger.Create(ReminderTriggerType.BeforeStart, 45, null);
        var snoozed = MakeReminder(trigger).Snooze(ActorId, CreatedAt.AddHours(2), CreatedAt.AddMinutes(1));

        var due = snoozed.MarkDue(ActorId, CreatedAt.AddHours(2));
        Assert.Equal(ReminderStatus.Due, due.Status);
        Assert.Null(due.SnoozedUntil);
        Assert.Equal(CreatedAt.AddHours(2), due.NextTriggerAt);
        Assert.Equal(trigger, due.Trigger);

        var delivered = due.MarkDelivered(ActorId, CreatedAt.AddHours(2).AddMinutes(1));
        Assert.Equal(trigger, delivered.Trigger);
        Assert.Null(delivered.SnoozedUntil);
    }

    [Fact]
    public void Cancel_MovesMetadataToTrashAndRescheduleRestoresIt()
    {
        var reminder = MakeReminder(ReminderTrigger.Create(ReminderTriggerType.Absolute, null, CreatedAt.AddHours(2)));

        var cancelled = reminder.Cancel(ActorId, CreatedAt.AddMinutes(1));
        Assert.Equal(ReminderStatus.Cancelled, cancelled.Status);
        Assert.Equal(EntityLifecycleState.Trashed, cancelled.Metadata.LifecycleState);
        Assert.Null(cancelled.SnoozedUntil);
        Assert.Null(cancelled.DeliveredAt);
        Assert.Equal(2, cancelled.Metadata.Version);
        Assert.NotNull(cancelled.Metadata.DeletedAtUtc);

        var restored = cancelled.Reschedule(ActorId, CreatedAt.AddHours(5), CreatedAt.AddMinutes(2));
        Assert.Equal(ReminderStatus.Scheduled, restored.Status);
        Assert.Equal(EntityLifecycleState.Active, restored.Metadata.LifecycleState);
        Assert.Equal(CreatedAt.AddHours(5), restored.NextTriggerAt);
        Assert.Equal(3, restored.Metadata.Version);
        Assert.Equal(reminder.Trigger, restored.Trigger);
    }

    [Fact]
    public void Cancel_And_Reschedule_RejectRepeatsAndIllegalStates()
    {
        var reminder = MakeReminder(ReminderTrigger.Create(ReminderTriggerType.AtDeadline, null, null));
        var cancelled = reminder.Cancel(ActorId, CreatedAt.AddMinutes(1));
        var delivered = reminder.MarkDue(ActorId, CreatedAt.AddMinutes(1)).MarkDelivered(ActorId, CreatedAt.AddMinutes(2));
        var expired = reminder.Expire(ActorId, CreatedAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => cancelled.Cancel(ActorId, CreatedAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => delivered.Cancel(ActorId, CreatedAt.AddMinutes(3)).Cancel(ActorId, CreatedAt.AddMinutes(4)));
        Assert.Throws<InvalidOperationException>(() => reminder.Reschedule(ActorId, CreatedAt.AddHours(2), CreatedAt.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => expired.Reschedule(ActorId, CreatedAt.AddHours(2), CreatedAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() =>
            cancelled.Reschedule(ActorId, CreatedAt.AddHours(2), CreatedAt.AddMinutes(2))
                .Reschedule(ActorId, CreatedAt.AddHours(3), CreatedAt.AddMinutes(3)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => cancelled.Reschedule(ActorId, CreatedAt.AddMinutes(1), CreatedAt.AddMinutes(2)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => cancelled.Reschedule(ActorId, CreatedAt.AddMinutes(2), CreatedAt.AddMinutes(2)));
    }

    [Fact]
    public void Expire_IsAllowedFromOnlyActiveStatuses()
    {
        var reminder = MakeReminder(ReminderTrigger.Create(ReminderTriggerType.BeforeStart, 30, null));

        var expired = reminder.Expire(ActorId, CreatedAt.AddMinutes(1));
        Assert.Equal(ReminderStatus.Expired, expired.Status);
        Assert.Equal(EntityLifecycleState.Active, expired.Metadata.LifecycleState);
        Assert.Null(expired.SnoozedUntil);
        Assert.Null(expired.DeliveredAt);
        Assert.Equal(2, expired.Metadata.Version);

        var snoozed = reminder.Snooze(ActorId, CreatedAt.AddHours(2), CreatedAt.AddMinutes(1));
        Assert.Equal(ReminderStatus.Expired, snoozed.Expire(ActorId, CreatedAt.AddMinutes(2)).Status);
        Assert.Throws<InvalidOperationException>(() => expired.Expire(ActorId, CreatedAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => reminder.Cancel(ActorId, CreatedAt.AddMinutes(1)).Expire(ActorId, CreatedAt.AddMinutes(2)));
    }

    [Fact]
    public void Reconstitute_ValidatesInvariants()
    {
        var trigger = ReminderTrigger.Create(ReminderTriggerType.BeforeDeadline, 120, null);
        var metadata = SyncableEntityMetadata.Create(ReminderId, OrganizationId, ActorId, CreatedAt);
        var fired = Reminder.Reconstitute(
            metadata,
            TargetObjectId,
            RecipientId,
            trigger,
            CreatedAt.AddHours(1),
            ReminderStatus.Scheduled,
            snoozedUntil: null,
            deliveredAt: null);
        Assert.Equal(ReminderStatus.Scheduled, fired.Status);

        var snoozeMetadata = SyncableEntityMetadata.Create(
            Guid.NewGuid(), OrganizationId, ActorId, CreatedAt).RecordVisibleChange(ActorId, CreatedAt.AddMinutes(5));
        var snoozed = Reminder.Reconstitute(
            snoozeMetadata,
            TargetObjectId,
            RecipientId,
            trigger,
            CreatedAt.AddHours(3),
            ReminderStatus.Snoozed,
            snoozedUntil: CreatedAt.AddHours(3),
            deliveredAt: null);
        Assert.Equal(ReminderStatus.Snoozed, snoozed.Status);

        var snoozedAbsolute = Reminder.Reconstitute(
            snoozeMetadata,
            TargetObjectId,
            RecipientId,
            ReminderTrigger.Create(ReminderTriggerType.Absolute, null, CreatedAt.AddHours(2)),
            CreatedAt.AddHours(3),
            ReminderStatus.Snoozed,
            snoozedUntil: CreatedAt.AddHours(3),
            deliveredAt: null);
        Assert.Equal(CreatedAt.AddHours(3), snoozedAbsolute.NextTriggerAt);
        Assert.Equal(CreatedAt.AddHours(2), snoozedAbsolute.Trigger.AbsoluteTriggerAt);

        Assert.Throws<ArgumentOutOfRangeException>(() => Reminder.Reconstitute(
            metadata, TargetObjectId, RecipientId, trigger, CreatedAt.AddHours(1), (ReminderStatus)99, null, null));
        Assert.Throws<ArgumentException>(() => Reminder.Reconstitute(
            metadata, TargetObjectId, RecipientId, trigger, CreatedAt.AddHours(1), ReminderStatus.Snoozed, null, null));
        Assert.Throws<ArgumentException>(() => Reminder.Reconstitute(
            metadata, TargetObjectId, RecipientId, trigger, CreatedAt.AddHours(1), ReminderStatus.Scheduled, CreatedAt.AddHours(3), null));
        Assert.Throws<ArgumentException>(() => Reminder.Reconstitute(
            metadata, TargetObjectId, RecipientId, trigger, CreatedAt.AddHours(3), ReminderStatus.Snoozed, CreatedAt.AddHours(2), null));
        Assert.Throws<ArgumentException>(() => Reminder.Reconstitute(
            snoozeMetadata, TargetObjectId, RecipientId, trigger, CreatedAt.AddHours(3), ReminderStatus.Delivered, null, null));
        Assert.Throws<ArgumentException>(() => Reminder.Reconstitute(
            snoozeMetadata, TargetObjectId, RecipientId, trigger, CreatedAt.AddHours(3), ReminderStatus.Scheduled, null, CreatedAt.AddMinutes(5)));

        var trashed = SyncableEntityMetadata.Create(Guid.NewGuid(), OrganizationId, ActorId, CreatedAt)
            .MoveToTrash(ActorId, CreatedAt.AddMinutes(1));
        Assert.Throws<ArgumentException>(() => Reminder.Reconstitute(
            trashed, TargetObjectId, RecipientId, trigger, CreatedAt.AddHours(1), ReminderStatus.Scheduled, null, null));
        var restoredTrashed = Reminder.Reconstitute(
            trashed, TargetObjectId, RecipientId, trigger, CreatedAt.AddHours(1), ReminderStatus.Cancelled, null, null);
        Assert.Equal(ReminderStatus.Cancelled, restoredTrashed.Status);
        Assert.Equal(EntityLifecycleState.Trashed, restoredTrashed.Metadata.LifecycleState);
    }

    [Fact]
    public void VisibleTransition_BumpsVersionExactlyOnce()
    {
        var reminder = MakeReminder(ReminderTrigger.Create(ReminderTriggerType.Absolute, null, CreatedAt.AddHours(2)));
        var changed = reminder.Snooze(ActorId, CreatedAt.AddHours(4), CreatedAt.AddMinutes(1));

        Assert.Equal(1, reminder.Metadata.Version);
        Assert.Equal(2, changed.Metadata.Version);
        Assert.Equal(ReminderStatus.Snoozed, changed.Status);
        Assert.Equal(CreatedAt.AddHours(4), changed.NextTriggerAt);
        Assert.True(changed.Metadata.UpdatedAtUtc >= changed.Metadata.CreatedAtUtc);
    }

    private static Reminder MakeReminder(ReminderTrigger trigger) =>
        Reminder.Create(
            ReminderId,
            OrganizationId,
            TargetObjectId,
            RecipientId,
            trigger,
            trigger.Type == ReminderTriggerType.Absolute
                ? trigger.AbsoluteTriggerAt!.Value
                : CreatedAt.AddHours(1),
            ActorId,
            CreatedAt);
}