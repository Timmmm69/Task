namespace Task.Domain.Recurrence;

/// <summary>
/// Root of the recurrence aggregate: the schedule, time zone, status and task
/// template of a repeating task series. Immutable: every transition returns a
/// new instance. The series always carries a valid time zone and at most one
/// termination mode (BR-040, AC-040), and cancelling it never moves it into
/// the universal trash (BR-043, AC-043).
/// </summary>
public sealed record RecurrenceSeries
{
    private RecurrenceSeries(
        SyncableEntityMetadata metadata,
        RecurrenceSeriesStatus status,
        string timeZoneId,
        RecurrenceRule rule,
        DateOnly nextGenerationDate,
        RecurrenceTaskTemplate template)
    {
        Metadata = metadata;
        Status = status;
        TimeZoneId = timeZoneId;
        Rule = rule;
        NextGenerationDate = nextGenerationDate;
        Template = template;
    }

    public SyncableEntityMetadata Metadata { get; }

    public RecurrenceSeriesStatus Status { get; }

    /// <summary>IANA time-zone identifier of the series (BR-040, AC-040).</summary>
    public string TimeZoneId { get; }

    public RecurrenceRule Rule { get; }

    /// <summary>
    /// First date of the next generation window; always on or after the rule
    /// start date (OpenAPI <c>RecurrenceSeries.nextGenerationDate</c>).
    /// </summary>
    public DateOnly NextGenerationDate { get; }

    public RecurrenceTaskTemplate Template { get; }

    /// <summary>
    /// Creates a new series. The series must start active or paused and the
    /// time zone must resolve to a system time zone.
    /// </summary>
    public static RecurrenceSeries Create(
        Guid id,
        Guid organizationId,
        Guid creatorId,
        DateTimeOffset createdAtUtc,
        RecurrenceSeriesStatus status,
        string timeZoneId,
        RecurrenceRule rule,
        RecurrenceTaskTemplate template)
    {
        if (status is not (RecurrenceSeriesStatus.Active or RecurrenceSeriesStatus.Paused))
        {
            throw new ArgumentException("A new series must be created active or paused.", nameof(status));
        }

        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(template);
        var normalizedTimeZone = NormalizeTimeZone(timeZoneId);
        var metadata = SyncableEntityMetadata.Create(id, organizationId, creatorId, createdAtUtc);

        return new RecurrenceSeries(
            metadata,
            status,
            normalizedTimeZone,
            rule,
            rule.OccurrenceStartDate,
            template);
    }

    /// <summary>
    /// Rebuilds a series from persisted state. A completed series must carry a
    /// termination mode, and the next generation date must not precede the rule
    /// start date.
    /// </summary>
    public static RecurrenceSeries Reconstitute(
        SyncableEntityMetadata metadata,
        RecurrenceSeriesStatus status,
        string timeZoneId,
        RecurrenceRule rule,
        DateOnly nextGenerationDate,
        RecurrenceTaskTemplate template)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(template);
        var normalizedTimeZone = NormalizeTimeZone(timeZoneId);

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), "Unknown series status.");
        }

        if (status == RecurrenceSeriesStatus.Completed && rule.UntilDate is null && rule.MaxOccurrences is null)
        {
            throw new ArgumentException("A completed series must have a termination mode.");
        }

        if (nextGenerationDate < rule.OccurrenceStartDate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nextGenerationDate),
                "The next generation date must not be earlier than the occurrence start date.");
        }

        return new RecurrenceSeries(metadata, status, normalizedTimeZone, rule, nextGenerationDate, template);
    }

    /// <summary>
    /// Pauses generation. Only an active series can be paused.
    /// </summary>
    public RecurrenceSeries Pause(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        if (Status != RecurrenceSeriesStatus.Active)
        {
            throw new InvalidOperationException("Only an active series can be paused.");
        }

        return With(Metadata.RecordVisibleChange(actorId, occurredAtUtc), RecurrenceSeriesStatus.Paused);
    }

    /// <summary>
    /// Resumes a paused series. Only a paused series can be resumed
    /// (AC-412: "Возобновить приостановленную серию").
    /// </summary>
    public RecurrenceSeries Resume(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        if (Status != RecurrenceSeriesStatus.Paused)
        {
            throw new InvalidOperationException("Only a paused series can be resumed.");
        }

        return With(Metadata.RecordVisibleChange(actorId, occurredAtUtc), RecurrenceSeriesStatus.Active);
    }

    /// <summary>
    /// Cancels the series without moving it into the universal trash: the
    /// lifecycle metadata remains active, and only the series status becomes
    /// cancelled (BR-043, AC-043).
    /// </summary>
    public RecurrenceSeries Cancel(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        if (Status is not (RecurrenceSeriesStatus.Active or RecurrenceSeriesStatus.Paused))
        {
            throw new InvalidOperationException("A cancelled or completed series cannot be cancelled.");
        }

        return With(Metadata.RecordVisibleChange(actorId, occurredAtUtc), RecurrenceSeriesStatus.Cancelled);
    }

    /// <summary>
    /// Marks the series completed. Completion requires a termination mode; a
    /// caller proves the termination was reached through
    /// <see cref="IsTerminationReached"/>.
    /// </summary>
    public RecurrenceSeries Complete(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        if (Status is not (RecurrenceSeriesStatus.Active or RecurrenceSeriesStatus.Paused))
        {
            throw new InvalidOperationException("A cancelled or completed series cannot be completed.");
        }

        if (Rule.UntilDate is null && Rule.MaxOccurrences is null)
        {
            throw new InvalidOperationException("An open-ended series must not be completed; it has no termination mode.");
        }

        return With(Metadata.RecordVisibleChange(actorId, occurredAtUtc), RecurrenceSeriesStatus.Completed);
    }

    /// <summary>
    /// Applies a new rule to the series. Only active or paused series can be
    /// re-ruled; an equal rule is a no-op.
    /// </summary>
    public RecurrenceSeries UpdateRule(Guid actorId, DateTimeOffset occurredAtUtc, RecurrenceRule newRule)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(newRule);
        if (newRule == Rule)
        {
            return this;
        }

        var metadata = Metadata.RecordVisibleChange(actorId, occurredAtUtc);
        return With(metadata, nextGenerationDate: Max(NextGenerationDate, newRule.OccurrenceStartDate), rule: newRule);
    }

    /// <summary>
    /// Applies a new task template to the series. Only active or paused series
    /// can be re-templated; an equal template is a no-op.
    /// </summary>
    public RecurrenceSeries UpdateTemplate(Guid actorId, DateTimeOffset occurredAtUtc, RecurrenceTaskTemplate newTemplate)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(newTemplate);
        if (newTemplate == Template)
        {
            return this;
        }

        return With(Metadata.RecordVisibleChange(actorId, occurredAtUtc), template: newTemplate);
    }

    /// <summary>
    /// Advances the generation cursor. Only active or paused series can
    /// advance, and the new cursor must not go backwards.
    /// </summary>
    public RecurrenceSeries AdvanceHorizon(Guid actorId, DateTimeOffset occurredAtUtc, DateOnly nextGenerationDate)
    {
        EnsureMutable();
        if (nextGenerationDate < NextGenerationDate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nextGenerationDate),
                "The next generation date must not move backwards.");
        }

        return With(Metadata.RecordVisibleChange(actorId, occurredAtUtc), nextGenerationDate: nextGenerationDate);
    }

    /// <summary>
    /// True when a termination mode is present and reached: the horizon has
    /// passed the until date, or the total count of materialized occurrences
    /// has reached the maximum (BR-040, AC-040).
    /// </summary>
    public bool IsTerminationReached(DateOnly throughDate, int totalGeneratedCount)
    {
        if (Rule.UntilDate is not null && throughDate >= Rule.UntilDate.Value)
        {
            return true;
        }

        return Rule.MaxOccurrences is not null && totalGeneratedCount >= Rule.MaxOccurrences.Value;
    }

    /// <summary>
    /// True while the series still generates occurrences: it is active,
    /// neither paused, cancelled, nor completed.
    /// </summary>
    public bool IsGenerating => Status == RecurrenceSeriesStatus.Active;

    private void EnsureMutable()
    {
        if (Status is not (RecurrenceSeriesStatus.Active or RecurrenceSeriesStatus.Paused))
        {
            throw new InvalidOperationException("A cancelled or completed series cannot be changed.");
        }
    }

    private RecurrenceSeries With(
        SyncableEntityMetadata metadata,
        RecurrenceSeriesStatus? status = null,
        RecurrenceRule? rule = null,
        DateOnly? nextGenerationDate = null,
        RecurrenceTaskTemplate? template = null) =>
        new(
            metadata,
            status ?? Status,
            TimeZoneId,
            rule ?? Rule,
            nextGenerationDate ?? NextGenerationDate,
            template ?? Template);

    private static DateOnly Max(DateOnly left, DateOnly right) => left >= right ? left : right;

    private static string NormalizeTimeZone(string timeZoneId)
    {
        if (timeZoneId is null)
        {
            throw new ArgumentNullException(nameof(timeZoneId));
        }

        var normalized = timeZoneId.Trim();
        if (normalized.Length == 0 || normalized.Length > 64)
        {
            throw new ArgumentException("The time zone identifier must be between 1 and 64 characters.", nameof(timeZoneId));
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(normalized, out _))
        {
            throw new ArgumentException("The time zone identifier must resolve to a known time zone.", nameof(timeZoneId));
        }

        return normalized;
    }
}