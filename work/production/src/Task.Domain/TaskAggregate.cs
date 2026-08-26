namespace Task.Domain;

/// <summary>
/// Root of the Task aggregate. Immutable: every transition returns a new instance
/// whose SyncableEntityMetadata records the change and advances the version.
/// Overdue is not stored here; it is computed later from the deadline and terminal state.
/// </summary>
public sealed class TaskAggregate
{
    private TaskAggregate(
        SyncableEntityMetadata metadata,
        string title,
        TaskWorkStatus workStatus,
        DateTimeOffset? completedAtUtc,
        Guid? completedBy,
        TaskPriority priority,
        TaskSchedule schedule)
    {
        Metadata = metadata;
        Title = title;
        WorkStatus = workStatus;
        CompletedAtUtc = completedAtUtc;
        CompletedBy = completedBy;
        Priority = priority;
        Schedule = schedule;
    }

    public SyncableEntityMetadata Metadata { get; }

    public string Title { get; }

    public TaskWorkStatus WorkStatus { get; }

    public TaskPriority Priority { get; }

    public TaskSchedule Schedule { get; }

    public DateTimeOffset? CompletedAtUtc { get; }

    public Guid? CompletedBy { get; }

    public static TaskAggregate Create(
        Guid id,
        Guid organizationId,
        Guid creatorId,
        string title,
        DateTimeOffset createdAtUtc,
        TaskPriority priority = TaskPriority.Normal,
        TaskSchedule? schedule = null)
    {
        var normalizedTitle = EnsureValidTitle(title);
        if (!Enum.IsDefined(priority))
        {
            throw new ArgumentOutOfRangeException(nameof(priority), "Unknown task priority.");
        }

        var metadata = SyncableEntityMetadata.Create(id, organizationId, creatorId, createdAtUtc);
        return new TaskAggregate(
            metadata,
            normalizedTitle,
            TaskWorkStatus.New,
            completedAtUtc: null,
            completedBy: null,
            priority,
            schedule ?? TaskSchedule.Create(null, null));
    }

    public static TaskAggregate Reconstitute(
        SyncableEntityMetadata metadata,
        string title,
        TaskWorkStatus workStatus,
        DateTimeOffset? completedAtUtc,
        Guid? completedBy,
        TaskPriority priority,
        TaskSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(schedule);
        var normalizedTitle = EnsureValidTitle(title);

        if (!Enum.IsDefined(workStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(workStatus), "Unknown task work status.");
        }

        if (!Enum.IsDefined(priority))
        {
            throw new ArgumentOutOfRangeException(nameof(priority), "Unknown task priority.");
        }

        var hasCompletion = completedAtUtc is not null && completedBy is not null;
        if ((completedAtUtc is null) != (completedBy is null) ||
            (workStatus == TaskWorkStatus.Completed) != hasCompletion)
        {
            throw new ArgumentException("Completion fields must be present only for a completed task.");
        }

        if (completedAtUtc is not null &&
            (completedAtUtc.Value.Offset != TimeSpan.Zero ||
             completedAtUtc.Value < metadata.CreatedAtUtc ||
             completedAtUtc.Value > metadata.UpdatedAtUtc))
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedAtUtc),
                "Completion timestamp must be UTC and between creation and the last update.");
        }

        if (completedBy == Guid.Empty)
        {
            throw new ArgumentException("Completion actor must not be empty.", nameof(completedBy));
        }

        return new TaskAggregate(
            metadata,
            normalizedTitle,
            workStatus,
            completedAtUtc,
            completedBy,
            priority,
            schedule);
    }

    public TaskAggregate Rename(Guid actorId, string title, DateTimeOffset occurredAtUtc)
    {
        EnsureActive("An archived or trashed task must be restored before it can be renamed.");
        EnsureNotTerminal("A completed or cancelled task cannot be renamed.");
        var normalizedTitle = EnsureValidTitle(title);
        var metadata = Metadata.RecordVisibleChange(actorId, occurredAtUtc);

        return new TaskAggregate(metadata, normalizedTitle, WorkStatus, CompletedAtUtc, CompletedBy, Priority, Schedule);
    }

    public TaskAggregate Start(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        EnsureActive("An archived or trashed task must be restored before it can be started.");
        if (WorkStatus != TaskWorkStatus.New)
        {
            throw new InvalidOperationException("Only a new task can be started.");
        }

        return new TaskAggregate(
            Metadata.RecordVisibleChange(actorId, occurredAtUtc),
            Title,
            TaskWorkStatus.InProgress,
            CompletedAtUtc,
            CompletedBy,
            Priority,
            Schedule);
    }

    public TaskAggregate SubmitForReview(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        EnsureActive("An archived or trashed task must be restored before it can be submitted for review.");
        if (WorkStatus != TaskWorkStatus.InProgress)
        {
            throw new InvalidOperationException("Only an in-progress task can be submitted for review.");
        }

        return new TaskAggregate(
            Metadata.RecordVisibleChange(actorId, occurredAtUtc),
            Title,
            TaskWorkStatus.Review,
            CompletedAtUtc,
            CompletedBy,
            Priority,
            Schedule);
    }

    public TaskAggregate Complete(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        EnsureActive("An archived or trashed task must be restored before it can be completed.");
        if (IsTerminal)
        {
            throw new InvalidOperationException("A completed or cancelled task cannot be completed.");
        }

        return new TaskAggregate(
            Metadata.RecordVisibleChange(actorId, occurredAtUtc),
            Title,
            TaskWorkStatus.Completed,
            occurredAtUtc,
            actorId,
            Priority,
            Schedule);
    }

    public TaskAggregate Cancel(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        EnsureActive("An archived or trashed task must be restored before it can be cancelled.");
        if (IsTerminal)
        {
            throw new InvalidOperationException("A completed or cancelled task cannot be cancelled.");
        }

        return new TaskAggregate(
            Metadata.RecordVisibleChange(actorId, occurredAtUtc),
            Title,
            TaskWorkStatus.Cancelled,
            completedAtUtc: null,
            completedBy: null,
            Priority,
            Schedule);
    }

    public TaskAggregate Archive(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        EnsureActive("Only an active task can be archived.");
        if (!IsTerminal)
        {
            throw new InvalidOperationException("Only a completed or cancelled task can be archived.");
        }

        return new TaskAggregate(
            Metadata.Archive(actorId, occurredAtUtc),
            Title,
            WorkStatus,
            CompletedAtUtc,
            CompletedBy,
            Priority,
            Schedule);
    }

    public TaskAggregate RestoreFromArchive(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        return new TaskAggregate(
            Metadata.RestoreFromArchive(actorId, occurredAtUtc),
            Title,
            WorkStatus,
            CompletedAtUtc,
            CompletedBy,
            Priority,
            Schedule);
    }

    public TaskAggregate MoveToTrash(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        EnsureActive("Only an active task can be moved to trash.");
        if (!IsTerminal)
        {
            throw new InvalidOperationException("Only a completed or cancelled task can be moved to trash.");
        }

        return new TaskAggregate(
            Metadata.MoveToTrash(actorId, occurredAtUtc),
            Title,
            WorkStatus,
            CompletedAtUtc,
            CompletedBy,
            Priority,
            Schedule);
    }

    public TaskAggregate RestoreFromTrash(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        return new TaskAggregate(
            Metadata.RestoreFromTrash(actorId, occurredAtUtc),
            Title,
            WorkStatus,
            CompletedAtUtc,
            CompletedBy,
            Priority,
            Schedule);
    }

    public TaskAggregate ChangePriority(Guid actorId, TaskPriority priority, DateTimeOffset occurredAtUtc)
    {
        EnsureActive("An archived or trashed task must be restored before its priority can be changed.");
        EnsureNotTerminal("A completed or cancelled task cannot be reprioritized.");
        if (!Enum.IsDefined(priority))
        {
            throw new ArgumentOutOfRangeException(nameof(priority), "Unknown task priority.");
        }

        if (priority == Priority)
        {
            return this;
        }

        return new TaskAggregate(
            Metadata.RecordVisibleChange(actorId, occurredAtUtc),
            Title,
            WorkStatus,
            CompletedAtUtc,
            CompletedBy,
            priority,
            Schedule);
    }

    public TaskAggregate Reschedule(Guid actorId, TaskSchedule schedule, DateTimeOffset occurredAtUtc)
    {
        EnsureActive("An archived or trashed task must be restored before it can be rescheduled.");
        EnsureNotTerminal("A completed or cancelled task cannot be rescheduled.");
        if (schedule is null)
        {
            throw new ArgumentNullException(nameof(schedule));
        }

        if (schedule == Schedule)
        {
            return this;
        }

        return new TaskAggregate(
            Metadata.RecordVisibleChange(actorId, occurredAtUtc),
            Title,
            WorkStatus,
            CompletedAtUtc,
            CompletedBy,
            Priority,
            schedule);
    }

    /// <summary>
    /// Atomically applies one presence-aware update to the stored editable fields.
    /// A null <paramref name="title"/> or <paramref name="priority"/> leaves the value
    /// unchanged; an unspecified <see cref="OptionalInstant"/> schedule bound leaves it
    /// unchanged, while an explicitly specified bound replaces it (null clears it).
    /// The resulting aggregate state is validated as a whole and, when it actually
    /// differs, exactly one visible change advances the version. When the final values
    /// equal the current ones, the same instance is returned without a domain event.
    /// </summary>
    public TaskAggregate UpdateEditableFields(
        Guid actorId,
        DateTimeOffset occurredAtUtc,
        string? title,
        TaskPriority? priority,
        OptionalInstant startsAtUtc,
        OptionalInstant deadlineAt)
    {
        EnsureActive("An archived or trashed task must be restored before its fields can be updated.");
        EnsureNotTerminal("A completed or cancelled task cannot be updated.");

        var effectiveTitle = title is null ? Title : EnsureValidTitle(title);
        var effectivePriority = Priority;
        if (priority is not null)
        {
            if (!Enum.IsDefined(priority.Value))
            {
                throw new ArgumentOutOfRangeException(nameof(priority), "Unknown task priority.");
            }

            effectivePriority = priority.Value;
        }

        var effectiveStart = startsAtUtc.Specified ? startsAtUtc.Value : Schedule.StartsAtUtc;
        var effectiveDeadline = deadlineAt.Specified ? deadlineAt.Value : Schedule.DeadlineUtc;
        var effectiveSchedule = TaskSchedule.Create(effectiveStart, effectiveDeadline);

        if (effectiveTitle == Title && effectivePriority == Priority && effectiveSchedule == Schedule)
        {
            return this;
        }

        return new TaskAggregate(
            Metadata.RecordVisibleChange(actorId, occurredAtUtc),
            effectiveTitle,
            WorkStatus,
            CompletedAtUtc,
            CompletedBy,
            effectivePriority,
            effectiveSchedule);
    }

    public bool IsOverdue(DateTimeOffset nowUtc) =>
        TaskOverduePolicy.IsOverdue(WorkStatus, Schedule.DeadlineUtc, nowUtc);

    private bool IsTerminal =>
        WorkStatus == TaskWorkStatus.Completed || WorkStatus == TaskWorkStatus.Cancelled;

    private void EnsureActive(string message)
    {
        if (Metadata.LifecycleState != EntityLifecycleState.Active)
        {
            throw new InvalidOperationException(message);
        }
    }

    private void EnsureNotTerminal(string message)
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static string EnsureValidTitle(string title)
    {
        var normalizedTitle = title?.Trim();
        if (string.IsNullOrEmpty(normalizedTitle))
        {
            throw new ArgumentException("Task title must not be empty.", nameof(title));
        }

        if (normalizedTitle.Length > 500)
        {
            throw new ArgumentException("Task title must not exceed 500 characters.", nameof(title));
        }

        return normalizedTitle;
    }
}

/// <summary>
/// Presence-aware value for one optional schedule bound: when
/// <see cref="Specified"/> is false the bound stays unchanged; when it is true the
/// bound is replaced by <see cref="Value"/>, and a null value clears the bound.
/// </summary>
public readonly record struct OptionalInstant(bool Specified, DateTimeOffset? Value)
{
    public static OptionalInstant Unspecified => default;

    public static OptionalInstant Set(DateTimeOffset value) => new(true, value);

    public static OptionalInstant Clear() => new(true, null);
}
