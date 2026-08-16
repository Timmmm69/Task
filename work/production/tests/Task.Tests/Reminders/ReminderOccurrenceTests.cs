using Task.Domain;
using Task.Domain.Reminders;

namespace Task.Tests.Reminders;

public sealed class ReminderOccurrenceTests
{
    private static readonly Guid OccurrenceId = Guid.Parse("5a3c81d7-2e4b-4f6a-9a2d-1c4e6f8a0b2d");
    private static readonly Guid OrganizationId = Guid.Parse("751fa8ce-5cc3-4d98-8574-1108080b2ff4");
    private static readonly Guid ReminderId = Guid.Parse("7ab1f2c3-4d5e-4f6a-9b8c-1d2e3f4a5b6c");
    private static readonly Guid ActorId = Guid.Parse("3077f0f8-536f-4988-bd73-6f26265d0b92");
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 16, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DueAt = new(2026, 8, 16, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_OpensAsCreatedWithDeterministicKeyAndFirstAttemptAtDue()
    {
        var occurrence = Occurrence();

        Assert.Equal(ReminderOccurrenceStatus.Created, occurrence.Status);
        Assert.Equal(0, occurrence.AttemptCount);
        Assert.Equal(DueAt, occurrence.NextAttemptAt);
        Assert.Equal(ReminderId, occurrence.ReminderId);
        Assert.Equal(DueAt, occurrence.DueAtUtc);
        Assert.Equal(1, occurrence.Metadata.Version);
        Assert.Equal($"{ReminderId:D}|{DueAt:O}", occurrence.OccurrenceKey.Value);
    }

    [Fact]
    public void Key_IsDeterministicAndRoundTrips()
    {
        var first = ReminderOccurrenceKey.From(ReminderId, DueAt);
        var second = ReminderOccurrenceKey.From(ReminderId, DueAt);

        Assert.Equal(first, second);
        Assert.Equal(first.Value, second.Value);
        Assert.Equal(first, ReminderOccurrenceKey.FromValue(first.Value));
        Assert.Equal(DueAt, ReminderOccurrenceKey.FromValue(first.Value).DueAtUtc);

        var otherReminder = ReminderOccurrenceKey.From(Guid.NewGuid(), DueAt);
        var otherDue = ReminderOccurrenceKey.From(ReminderId, DueAt.AddMinutes(1));
        Assert.NotEqual(first, otherReminder);
        Assert.NotEqual(first, otherDue);
    }

    [Fact]
    public void Key_RejectsEmptyReminderAndMalformedOrNonUtcValues()
    {
        Assert.Throws<ArgumentException>(() => ReminderOccurrenceKey.From(Guid.Empty, DueAt));
        Assert.Throws<ArgumentException>(() => ReminderOccurrenceKey.From(
            ReminderId, new DateTimeOffset(2026, 8, 16, 14, 0, 0, TimeSpan.FromHours(5))));
        Assert.Throws<ArgumentException>(() => ReminderOccurrenceKey.FromValue(""));
        Assert.Throws<ArgumentException>(() => ReminderOccurrenceKey.FromValue("no-separator"));
        Assert.Throws<ArgumentException>(() => ReminderOccurrenceKey.FromValue($"{Guid.NewGuid():D}|"));
        Assert.Throws<ArgumentException>(() => ReminderOccurrenceKey.FromValue("|2026-08-16T09:00:00.0000000+00:00"));
        Assert.Throws<ArgumentException>(() => ReminderOccurrenceKey.FromValue(
            $"{Guid.NewGuid():D}|2026-08-16T20:00:00.0000000+05:00"));
        Assert.Throws<ArgumentException>(() => ReminderOccurrenceKey.FromValue(
            new string('x', ReminderOccurrenceKey.MaxLength + 1)));
    }

    [Fact]
    public void Create_RejectsEmptyIdentifiersAndKeyMismatch()
    {
        Assert.Throws<ArgumentException>(() => Occurrence(occurrenceId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Occurrence(createdBy: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Occurrence(reminderId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => ReminderOccurrence.Create(
            OccurrenceId, OrganizationId, Guid.NewGuid(), ActorId, ReminderOccurrenceKey.From(ReminderId, DueAt), CreatedAt));
        Assert.Throws<ArgumentNullException>(() =>
            ReminderOccurrence.Create(OccurrenceId, OrganizationId, ReminderId, ActorId, null!, CreatedAt));
    }

    [Fact]
    public void Claim_Deliver_Fail_DeadLetter_AdvanceAttemptsAndStatuses()
    {
        var claimed = Occurrence().Claim(ActorId, DueAt);
        Assert.Equal(ReminderOccurrenceStatus.Claimed, claimed.Status);
        Assert.Equal(1, claimed.AttemptCount);
        Assert.Equal(DueAt, claimed.NextAttemptAt);
        Assert.Equal(2, claimed.Metadata.Version);

        var delivered = claimed.MarkDelivered(ActorId, DueAt.AddMinutes(1));
        Assert.Equal(ReminderOccurrenceStatus.Delivered, delivered.Status);
        Assert.Equal(1, delivered.AttemptCount);
        Assert.Equal(3, delivered.Metadata.Version);

        var failed = Occurrence().Claim(ActorId, DueAt).Fail(ActorId, DueAt.AddMinutes(1));
        Assert.Equal(ReminderOccurrenceStatus.Failed, failed.Status);
        Assert.Equal(1, failed.AttemptCount);

        var retried = failed.Claim(ActorId, DueAt.AddMinutes(2));
        Assert.Equal(ReminderOccurrenceStatus.Claimed, retried.Status);
        Assert.Equal(2, retried.AttemptCount);

        var deadLetter = retried.DeadLetter(ActorId, DueAt.AddMinutes(3));
        Assert.Equal(ReminderOccurrenceStatus.DeadLetter, deadLetter.Status);
        Assert.Equal(2, deadLetter.AttemptCount);

        var directDeadLetter = Occurrence().Claim(ActorId, DueAt).DeadLetter(ActorId, DueAt.AddMinutes(1));
        Assert.Equal(ReminderOccurrenceStatus.DeadLetter, directDeadLetter.Status);
    }

    [Fact]
    public void Claim_IsRejectedBeforeTheFirstAttemptInstant()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Occurrence().Claim(ActorId, CreatedAt.AddMinutes(30)));
        Assert.Equal(
            ReminderOccurrenceStatus.Claimed,
            Occurrence().Claim(ActorId, DueAt).Status);
    }

    [Fact]
    public void Transitions_RejectIllegalStatuses()
    {
        var created = Occurrence();
        var claimed = created.Claim(ActorId, DueAt);
        var delivered = claimed.MarkDelivered(ActorId, DueAt.AddMinutes(1));
        var deadLettered = Occurrence().Claim(ActorId, DueAt).DeadLetter(ActorId, DueAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => created.MarkDelivered(ActorId, DueAt.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => created.Fail(ActorId, DueAt.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => created.DeadLetter(ActorId, DueAt.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => claimed.Claim(ActorId, DueAt.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => delivered.Claim(ActorId, DueAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => delivered.Fail(ActorId, DueAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => deadLettered.Claim(ActorId, DueAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => deadLettered.MarkDelivered(ActorId, DueAt.AddMinutes(2)));
    }

    [Fact]
    public void Dismiss_CancelsPendingOccurrenceAndIsIdempotent()
    {
        var dismissed = Occurrence().Dismiss(ActorId, CreatedAt.AddMinutes(1));
        Assert.Equal(ReminderOccurrenceStatus.Cancelled, dismissed.Status);
        Assert.Equal(2, dismissed.Metadata.Version);

        var same = dismissed.Dismiss(ActorId, CreatedAt.AddMinutes(2));
        Assert.Same(dismissed, same);
        Assert.Equal(2, same.Metadata.Version);

        var claimedDismissed = Occurrence().Claim(ActorId, DueAt).Dismiss(ActorId, DueAt.AddMinutes(1));
        Assert.Equal(ReminderOccurrenceStatus.Cancelled, claimedDismissed.Status);

        var failedDismissed = Occurrence().Claim(ActorId, DueAt).Fail(ActorId, DueAt.AddMinutes(1))
            .Dismiss(ActorId, DueAt.AddMinutes(2));
        Assert.Equal(ReminderOccurrenceStatus.Cancelled, failedDismissed.Status);
    }

    [Fact]
    public void Dismiss_RejectsDeliveredAndDeadLetteredOccurrences()
    {
        var delivered = Occurrence().Claim(ActorId, DueAt).MarkDelivered(ActorId, DueAt.AddMinutes(1));
        var deadLettered = Occurrence().Claim(ActorId, DueAt).DeadLetter(ActorId, DueAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => delivered.Dismiss(ActorId, DueAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => deadLettered.Dismiss(ActorId, DueAt.AddMinutes(2)));
    }

    [Fact]
    public void Deduplicate_KeepsFirstOccurrencePerKeyInInputOrder()
    {
        var first = Occurrence();
        var duplicate = Occurrence();
        var otherReminder = Occurrence(reminderId: Guid.NewGuid());
        var otherDue = Occurrence(dueAt: DueAt.AddMinutes(5));

        var result = ReminderOccurrence.Deduplicate(new[] { first, duplicate, otherReminder, otherDue, duplicate });

        Assert.Equal(3, result.Count);
        Assert.Same(first, result[0]);
        Assert.Same(otherReminder, result[1]);
        Assert.Same(otherDue, result[2]);
    }

    [Fact]
    public void Deduplicate_PreservesInputOrderAndDoesNotReorder()
    {
        var a = Occurrence(dueAt: DueAt.AddHours(3));
        var b = Occurrence(dueAt: DueAt.AddHours(1));
        var c = Occurrence(dueAt: DueAt.AddHours(2));

        var result = ReminderOccurrence.Deduplicate(new[] { a, b, c });

        Assert.Equal(3, result.Count);
        Assert.Same(a, result[0]);
        Assert.Same(b, result[1]);
        Assert.Same(c, result[2]);
    }

    [Fact]
    public void Deduplicate_RejectsNullInputAndNullItems()
    {
        Assert.Throws<ArgumentNullException>(() => ReminderOccurrence.Deduplicate(null!));
        Assert.Throws<ArgumentException>(() => ReminderOccurrence.Deduplicate(new ReminderOccurrence[] { null! }));
    }

    [Fact]
    public void Reconstitute_ValidatesStatusAttemptsAndTimestamps()
    {
        var metadata = SyncableEntityMetadata.Create(OccurrenceId, OrganizationId, ActorId, CreatedAt);

        var claimed = ReminderOccurrence.Reconstitute(
            metadata,
            ReminderOccurrenceKey.From(ReminderId, DueAt),
            ReminderOccurrenceStatus.Claimed,
            attemptCount: 1,
            nextAttemptAt: DueAt);
        Assert.Equal(ReminderOccurrenceStatus.Claimed, claimed.Status);

        Assert.Throws<ArgumentOutOfRangeException>(() => ReminderOccurrence.Reconstitute(
            metadata, ReminderOccurrenceKey.From(ReminderId, DueAt), (ReminderOccurrenceStatus)99, 0, DueAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReminderOccurrence.Reconstitute(
            metadata, ReminderOccurrenceKey.From(ReminderId, DueAt), ReminderOccurrenceStatus.Created, -1, DueAt));
        Assert.Throws<ArgumentException>(() => ReminderOccurrence.Reconstitute(
            metadata,
            ReminderOccurrenceKey.From(ReminderId, DueAt),
            ReminderOccurrenceStatus.Created,
            0,
            new DateTimeOffset(2026, 8, 16, 14, 0, 0, TimeSpan.FromHours(5))));
        Assert.Throws<ArgumentException>(() =>
            ReminderOccurrence.Reconstitute(
                metadata, ReminderOccurrenceKey.From(ReminderId, new DateTimeOffset(2026, 8, 16, 14, 0, 0, TimeSpan.FromHours(5))),
                ReminderOccurrenceStatus.Created, 0, DueAt));
        Assert.Throws<ArgumentNullException>(() => ReminderOccurrence.Reconstitute(
            null!, ReminderOccurrenceKey.From(ReminderId, DueAt), ReminderOccurrenceStatus.Created, 0, DueAt));
        Assert.Throws<ArgumentNullException>(() => ReminderOccurrence.Reconstitute(
            metadata, null!, ReminderOccurrenceStatus.Created, 0, DueAt));
    }

    [Fact]
    public void Operations_RejectBackdatedAndNonUtcTimestamps()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Occurrence().Claim(ActorId, CreatedAt.AddMinutes(-1)));
        Assert.Throws<ArgumentException>(() => Occurrence().Claim(
            ActorId, new DateTimeOffset(2026, 8, 16, 14, 0, 0, TimeSpan.FromHours(5))));

        var claimed = Occurrence().Claim(ActorId, DueAt);
        Assert.Throws<ArgumentOutOfRangeException>(() => claimed.MarkDelivered(ActorId, CreatedAt));
    }

    private static ReminderOccurrence Occurrence(
        Guid? occurrenceId = null,
        Guid? reminderId = null,
        Guid? createdBy = null,
        DateTimeOffset? dueAt = null) =>
        ReminderOccurrence.Create(
            occurrenceId ?? OccurrenceId,
            OrganizationId,
            reminderId ?? ReminderId,
            createdBy ?? ActorId,
            ReminderOccurrenceKey.From(reminderId ?? ReminderId, dueAt ?? DueAt),
            CreatedAt);
}