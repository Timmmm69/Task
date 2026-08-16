namespace Task.Domain.Recurrence;

/// <summary>
/// A single materialized occurrence of a recurrence series. Immutable: every
/// transition returns a new instance. The occurrence key is deterministic
/// (the ISO-8601 local date), and its date must match the occurrence date
/// (BR-041, AC-041).
/// </summary>
public sealed record RecurrenceOccurrence
{
    private RecurrenceOccurrence(
        SyncableEntityMetadata metadata,
        Guid seriesId,
        OccurrenceKey occurrenceKey,
        DateOnly localDate,
        RecurrenceOccurrenceStatus status,
        Guid? taskId)
    {
        Metadata = metadata;
        SeriesId = seriesId;
        OccurrenceKey = occurrenceKey;
        LocalDate = localDate;
        Status = status;
        TaskId = taskId;
    }

    public SyncableEntityMetadata Metadata { get; }

    public Guid SeriesId { get; }

    public OccurrenceKey OccurrenceKey { get; }

    public DateOnly LocalDate { get; }

    public RecurrenceOccurrenceStatus Status { get; }

    /// <summary>
    /// Task materialized for a generated occurrence; set only when the
    /// occurrence has been generated.
    /// </summary>
    public Guid? TaskId { get; }

    /// <summary>
    /// Creates a planned occurrence. The local date is derived from the
    /// deterministic key, so the two can never diverge.
    /// </summary>
    public static RecurrenceOccurrence Create(
        Guid id,
        Guid organizationId,
        Guid seriesId,
        Guid createdBy,
        OccurrenceKey occurrenceKey,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(occurrenceKey);
        if (seriesId == Guid.Empty)
        {
            throw new ArgumentException("Series identifier must not be empty.", nameof(seriesId));
        }

        var metadata = SyncableEntityMetadata.Create(id, organizationId, createdBy, createdAtUtc);
        return new RecurrenceOccurrence(
            metadata,
            seriesId,
            occurrenceKey,
            occurrenceKey.LocalDate,
            RecurrenceOccurrenceStatus.Planned,
            taskId: null);
    }

    /// <summary>
    /// Rebuilds an occurrence from persisted state and validates its
    /// invariants, including the key/date match and that a generated
    /// occurrence references its materialized task.
    /// </summary>
    public static RecurrenceOccurrence Reconstitute(
        SyncableEntityMetadata metadata,
        Guid seriesId,
        OccurrenceKey occurrenceKey,
        DateOnly localDate,
        RecurrenceOccurrenceStatus status,
        Guid? taskId)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(occurrenceKey);
        if (seriesId == Guid.Empty)
        {
            throw new ArgumentException("Series identifier must not be empty.", nameof(seriesId));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), "Unknown occurrence status.");
        }

        if (occurrenceKey.LocalDate != localDate)
        {
            throw new ArgumentException(
                "The occurrence date must match the date encoded in the occurrence key.",
                nameof(localDate));
        }

        if (status == RecurrenceOccurrenceStatus.Generated && taskId is null)
        {
            throw new ArgumentException("A generated occurrence must reference its materialized task.", nameof(taskId));
        }

        if (taskId == Guid.Empty)
        {
            throw new ArgumentException("The occurrence task identifier must not be empty.", nameof(taskId));
        }

        return new RecurrenceOccurrence(metadata, seriesId, occurrenceKey, localDate, status, taskId);
    }

    /// <summary>
    /// Materializes the occurrence's task. Only a planned occurrence can be
    /// generated, and the task identifier must be present.
    /// </summary>
    public RecurrenceOccurrence MarkGenerated(Guid actorId, DateTimeOffset occurredAtUtc, Guid taskId)
    {
        if (Status != RecurrenceOccurrenceStatus.Planned)
        {
            throw new InvalidOperationException("Only a planned occurrence can be generated.");
        }

        if (taskId == Guid.Empty)
        {
            throw new ArgumentException("The task identifier must not be empty.", nameof(taskId));
        }

        return new RecurrenceOccurrence(
            Metadata.RecordVisibleChange(actorId, occurredAtUtc),
            SeriesId,
            OccurrenceKey,
            LocalDate,
            RecurrenceOccurrenceStatus.Generated,
            taskId);
    }

    /// <summary>
    /// Skips the occurrence. Skipping is allowed for a planned or generated
    /// occurrence only; the reference to the materialized task, when present,
    /// is retained.
    /// </summary>
    public RecurrenceOccurrence Skip(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        if (Status is not (RecurrenceOccurrenceStatus.Planned or RecurrenceOccurrenceStatus.Generated))
        {
            throw new InvalidOperationException("Only a planned or generated occurrence can be skipped.");
        }

        return new RecurrenceOccurrence(
            Metadata.RecordVisibleChange(actorId, occurredAtUtc),
            SeriesId,
            OccurrenceKey,
            LocalDate,
            RecurrenceOccurrenceStatus.Skipped,
            TaskId);
    }

    /// <summary>
    /// Cancels the occurrence. Cancelling is allowed for a planned or
    /// generated occurrence only.
    /// </summary>
    public RecurrenceOccurrence Cancel(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        if (Status is not (RecurrenceOccurrenceStatus.Planned or RecurrenceOccurrenceStatus.Generated))
        {
            throw new InvalidOperationException("Only a planned or generated occurrence can be cancelled.");
        }

        return new RecurrenceOccurrence(
            Metadata.RecordVisibleChange(actorId, occurredAtUtc),
            SeriesId,
            OccurrenceKey,
            LocalDate,
            RecurrenceOccurrenceStatus.Cancelled,
            TaskId);
    }
}