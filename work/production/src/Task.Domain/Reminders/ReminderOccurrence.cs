namespace Task.Domain.Reminders;

/// <summary>
/// A single materialized firing of a reminder. Immutable: every transition
/// returns a new instance whose <see cref="SyncableEntityMetadata"/> records
/// the change and advances the version. The deterministic occurrence key
/// carries the (reminderId, dueAt UTC) pair, so the due instant can never
/// diverge from the key (BR-046, AC-046).
/// </summary>
public sealed record ReminderOccurrence
{
    private ReminderOccurrence(
        SyncableEntityMetadata metadata,
        ReminderOccurrenceKey occurrenceKey,
        ReminderOccurrenceStatus status,
        int attemptCount,
        DateTimeOffset nextAttemptAt)
    {
        Metadata = metadata;
        OccurrenceKey = occurrenceKey;
        Status = status;
        AttemptCount = attemptCount;
        NextAttemptAt = nextAttemptAt;
    }

    public SyncableEntityMetadata Metadata { get; }

    public ReminderOccurrenceKey OccurrenceKey { get; }

    /// <summary>Identifier of the reminder this firing belongs to (from the key).</summary>
    public Guid ReminderId => OccurrenceKey.ReminderId;

    /// <summary>UTC instant the firing is due on (from the key).</summary>
    public DateTimeOffset DueAtUtc => OccurrenceKey.DueAtUtc;

    public ReminderOccurrenceStatus Status { get; }

    /// <summary>Non-negative number of delivery attempts already made.</summary>
    public int AttemptCount { get; }

    /// <summary>
    /// UTC instant the occurrence is scheduled to be attempted next; a
    /// created occurrence is first attempted at its due instant. Retry
    /// policy stays a worker/boundary responsibility.
    /// </summary>
    public DateTimeOffset NextAttemptAt { get; }

    /// <summary>
    /// Creates a pending occurrence. The due instant is derived from the
    /// deterministic key, so the two can never diverge, and the first
    /// attempt is scheduled at the due instant.
    /// </summary>
    public static ReminderOccurrence Create(
        Guid id,
        Guid organizationId,
        Guid reminderId,
        Guid createdBy,
        ReminderOccurrenceKey occurrenceKey,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(occurrenceKey);
        if (reminderId != occurrenceKey.ReminderId)
        {
            throw new ArgumentException(
                "The reminder identifier must match the identifier encoded in the occurrence key.",
                nameof(reminderId));
        }

        var metadata = SyncableEntityMetadata.Create(id, organizationId, createdBy, createdAtUtc);
        return new ReminderOccurrence(
            metadata,
            occurrenceKey,
            ReminderOccurrenceStatus.Created,
            attemptCount: 0,
            occurrenceKey.DueAtUtc);
    }

    /// <summary>
    /// Rebuilds an occurrence from persisted state and validates its
    /// invariants: key/due consistency, attempt count, status and UTC
    /// timestamps.
    /// </summary>
    public static ReminderOccurrence Reconstitute(
        SyncableEntityMetadata metadata,
        ReminderOccurrenceKey occurrenceKey,
        ReminderOccurrenceStatus status,
        int attemptCount,
        DateTimeOffset nextAttemptAt)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(occurrenceKey);

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), "Unknown occurrence status.");
        }

        if (attemptCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptCount), "Attempt count must not be negative.");
        }

        if (nextAttemptAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must use the UTC offset.", nameof(nextAttemptAt));
        }

        return new ReminderOccurrence(metadata, occurrenceKey, status, attemptCount, nextAttemptAt);
    }

    /// <summary>
    /// Claims the occurrence for delivery. Allowed for a created or failed
    /// occurrence; claiming counts one attempt.
    /// </summary>
    public ReminderOccurrence Claim(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        if (Status is not (ReminderOccurrenceStatus.Created or ReminderOccurrenceStatus.Failed))
        {
            throw new InvalidOperationException("Only a created or failed occurrence can be claimed.");
        }

        return new ReminderOccurrence(
            Metadata.RecordVisibleChange(actorId, occurredAtUtc),
            OccurrenceKey,
            ReminderOccurrenceStatus.Claimed,
            checked(AttemptCount + 1),
            NextAttemptAt);
    }

    /// <summary>
    /// Marks the claimed occurrence as delivered.
    /// </summary>
    public ReminderOccurrence MarkDelivered(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        if (Status != ReminderOccurrenceStatus.Claimed)
        {
            throw new InvalidOperationException("Only a claimed occurrence can be delivered.");
        }

        return new ReminderOccurrence(
            Metadata.RecordVisibleChange(actorId, occurredAtUtc),
            OccurrenceKey,
            ReminderOccurrenceStatus.Delivered,
            AttemptCount,
            NextAttemptAt);
    }

    /// <summary>
    /// Marks the claimed occurrence as failed; the next attempt stays
    /// scheduled by the worker policy and is not changed here.
    /// </summary>
    public ReminderOccurrence Fail(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        if (Status != ReminderOccurrenceStatus.Claimed)
        {
            throw new InvalidOperationException("Only a claimed occurrence can fail.");
        }

        return new ReminderOccurrence(
            Metadata.RecordVisibleChange(actorId, occurredAtUtc),
            OccurrenceKey,
            ReminderOccurrenceStatus.Failed,
            AttemptCount,
            NextAttemptAt);
    }

    /// <summary>
    /// Sends a failed occurrence to the delivery dead letter. Allowed for a
    /// claimed or failed occurrence.
    /// </summary>
    public ReminderOccurrence DeadLetter(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        if (Status is not (ReminderOccurrenceStatus.Claimed or ReminderOccurrenceStatus.Failed))
        {
            throw new InvalidOperationException("Only a claimed or failed occurrence can be dead-lettered.");
        }

        return new ReminderOccurrence(
            Metadata.RecordVisibleChange(actorId, occurredAtUtc),
            OccurrenceKey,
            ReminderOccurrenceStatus.DeadLetter,
            AttemptCount,
            NextAttemptAt);
    }

    /// <summary>
    /// Dismisses a pending occurrence (POST /api/v1/reminders/{id}/dismiss
    /// domain part, without If-Match or idempotency handling). Pending
    /// created, claimed and failed occurrences are cancelled; a cancelled
    /// occurrence makes dismiss idempotent and returns this instance without
    /// a version bump.
    /// </summary>
    public ReminderOccurrence Dismiss(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        if (Status == ReminderOccurrenceStatus.Cancelled)
        {
            return this;
        }

        if (Status is not (ReminderOccurrenceStatus.Created or ReminderOccurrenceStatus.Claimed or ReminderOccurrenceStatus.Failed))
        {
            throw new InvalidOperationException("Only a pending occurrence can be dismissed.");
        }

        return new ReminderOccurrence(
            Metadata.RecordVisibleChange(actorId, occurredAtUtc),
            OccurrenceKey,
            ReminderOccurrenceStatus.Cancelled,
            AttemptCount,
            NextAttemptAt);
    }

    /// <summary>
    /// Deterministic de-duplication of an occurrence collection: the first
    /// occurrence of every (reminderId, dueAt UTC) key is kept in input
    /// order and a repeated key does not produce a second result
    /// (BR-046, AC-046). This is the domain pre-requisite of server-side
    /// de-duplication; persistence constraints and worker claiming remain
    /// out of scope.
    /// </summary>
    public static IReadOnlyList<ReminderOccurrence> Deduplicate(IEnumerable<ReminderOccurrence> occurrences)
    {
        ArgumentNullException.ThrowIfNull(occurrences);

        var seen = new HashSet<ReminderOccurrenceKey>();
        var result = new List<ReminderOccurrence>();
        foreach (var occurrence in occurrences)
        {
            if (occurrence is null)
            {
                throw new ArgumentException("An occurrence collection must not contain null items.", nameof(occurrences));
            }

            if (seen.Add(occurrence.OccurrenceKey))
            {
                result.Add(occurrence);
            }
        }

        return result;
    }
}