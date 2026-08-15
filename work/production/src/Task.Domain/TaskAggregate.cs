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
        Guid? completedBy)
    {
        Metadata = metadata;
        Title = title;
        WorkStatus = workStatus;
        CompletedAtUtc = completedAtUtc;
        CompletedBy = completedBy;
    }

    public SyncableEntityMetadata Metadata { get; }

    public string Title { get; }

    public TaskWorkStatus WorkStatus { get; }

    public DateTimeOffset? CompletedAtUtc { get; }

    public Guid? CompletedBy { get; }

    public static TaskAggregate Create(
        Guid id,
        Guid organizationId,
        Guid creatorId,
        string title,
        DateTimeOffset createdAtUtc)
    {
        var normalizedTitle = EnsureValidTitle(title);
        var metadata = SyncableEntityMetadata.Create(id, organizationId, creatorId, createdAtUtc);

        return new TaskAggregate(
            metadata,
            normalizedTitle,
            TaskWorkStatus.New,
            completedAtUtc: null,
            completedBy: null);
    }

    public TaskAggregate Rename(Guid actorId, string title, DateTimeOffset occurredAtUtc)
    {
        EnsureActive("An archived or trashed task must be restored before it can be renamed.");
        EnsureNotTerminal("A completed or cancelled task cannot be renamed.");
        var normalizedTitle = EnsureValidTitle(title);
        var metadata = Metadata.RecordVisibleChange(actorId, occurredAtUtc);

        return new TaskAggregate(metadata, normalizedTitle, WorkStatus, CompletedAtUtc, CompletedBy);
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
            CompletedBy);
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
            CompletedBy);
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
            actorId);
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
            completedBy: null);
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
            CompletedBy);
    }

    public TaskAggregate RestoreFromArchive(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        return new TaskAggregate(
            Metadata.RestoreFromArchive(actorId, occurredAtUtc),
            Title,
            WorkStatus,
            CompletedAtUtc,
            CompletedBy);
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
            CompletedBy);
    }

    public TaskAggregate RestoreFromTrash(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        return new TaskAggregate(
            Metadata.RestoreFromTrash(actorId, occurredAtUtc),
            Title,
            WorkStatus,
            CompletedAtUtc,
            CompletedBy);
    }

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

        return normalizedTitle;
    }
}