namespace Task.Domain.Recurrence;

/// <summary>
/// Immutable template used to materialize the tasks of a recurrence series.
/// Mirrors the OpenAPI <c>RecurrenceTaskTemplate</c> schema and carries value
/// equality so that a no-op template update can be detected.
/// </summary>
public sealed class RecurrenceTaskTemplate
{
    private RecurrenceTaskTemplate(
        Guid? projectId,
        string title,
        string? description,
        Guid authorUserId,
        Guid? requesterUserId,
        Guid? primaryCounterpartyObjectId,
        TaskPriority priority,
        int? plannedDurationMinutes,
        int? deadlineOffsetMinutes,
        IReadOnlyList<Guid> assigneeIds,
        IReadOnlyList<Guid> watcherIds,
        IReadOnlyList<RecurrenceTemplateChecklist> checklists,
        IReadOnlyList<RecurrenceTemplateReminderRule> reminderRules,
        long templateVersion)
    {
        ProjectId = projectId;
        Title = title;
        Description = description;
        AuthorUserId = authorUserId;
        RequesterUserId = requesterUserId;
        PrimaryCounterpartyObjectId = primaryCounterpartyObjectId;
        Priority = priority;
        PlannedDurationMinutes = plannedDurationMinutes;
        DeadlineOffsetMinutes = deadlineOffsetMinutes;
        AssigneeIds = assigneeIds;
        WatcherIds = watcherIds;
        Checklists = checklists;
        ReminderRules = reminderRules;
        TemplateVersion = templateVersion;
    }

    public Guid? ProjectId { get; }

    public string Title { get; }

    public string? Description { get; }

    public Guid AuthorUserId { get; }

    public Guid? RequesterUserId { get; }

    public Guid? PrimaryCounterpartyObjectId { get; }

    public TaskPriority Priority { get; }

    /// <summary>Scheduled duration in minutes, when fixed; bound to 1..10080 (one week).</summary>
    public int? PlannedDurationMinutes { get; }

    public int? DeadlineOffsetMinutes { get; }

    public IReadOnlyList<Guid> AssigneeIds { get; }

    public IReadOnlyList<Guid> WatcherIds { get; }

    public IReadOnlyList<RecurrenceTemplateChecklist> Checklists { get; }

    public IReadOnlyList<RecurrenceTemplateReminderRule> ReminderRules { get; }

    /// <summary>Contract version of the template; must be positive.</summary>
    public long TemplateVersion { get; }

    /// <summary>
    /// Creates a template. The title is required (1..500 characters); assignee
    /// and watcher lists each hold at most 100 distinct users; checklists and
    /// reminder rules each hold at most 50 entries; the version must be positive.
    /// </summary>
    public static RecurrenceTaskTemplate Create(
        Guid? projectId,
        string title,
        string? description,
        Guid authorUserId,
        Guid? requesterUserId,
        Guid? primaryCounterpartyObjectId,
        TaskPriority priority,
        int? plannedDurationMinutes,
        int? deadlineOffsetMinutes,
        IReadOnlyList<Guid>? assigneeIds,
        IReadOnlyList<Guid>? watcherIds,
        IReadOnlyList<RecurrenceTemplateChecklist>? checklists,
        IReadOnlyList<RecurrenceTemplateReminderRule>? reminderRules,
        long templateVersion)
    {
        var normalizedTitle = title?.Trim();
        if (string.IsNullOrEmpty(normalizedTitle))
        {
            throw new ArgumentException("Task template title must not be empty.", nameof(title));
        }

        if (normalizedTitle.Length > 500)
        {
            throw new ArgumentException("Task template title must not exceed 500 characters.", nameof(title));
        }

        var normalizedDescription = description?.Trim();
        if (normalizedDescription is not null && normalizedDescription.Length > 50000)
        {
            throw new ArgumentException("Task template description must not exceed 50000 characters.", nameof(description));
        }

        if (authorUserId == Guid.Empty)
        {
            throw new ArgumentException("Task template author must not be empty.", nameof(authorUserId));
        }

        if (requesterUserId == Guid.Empty)
        {
            throw new ArgumentException("Task template requester must not be empty.", nameof(requesterUserId));
        }

        if (primaryCounterpartyObjectId == Guid.Empty)
        {
            throw new ArgumentException("Task template counterparty must not be empty.", nameof(primaryCounterpartyObjectId));
        }

        if (!Enum.IsDefined(priority))
        {
            throw new ArgumentOutOfRangeException(nameof(priority), "Unknown task priority.");
        }

        if (plannedDurationMinutes is not null && plannedDurationMinutes is < 1 or > 10080)
        {
            throw new ArgumentOutOfRangeException(
                nameof(plannedDurationMinutes),
                "Planned duration must be between 1 and 10080 minutes.");
        }

        var normalizedAssigneeIds = NormalizeUserIds(assigneeIds, nameof(assigneeIds), maxCount: 100);
        var normalizedWatcherIds = NormalizeUserIds(watcherIds, nameof(watcherIds), maxCount: 100);

        var normalizedChecklists = checklists ?? [];
        if (normalizedChecklists.Count > 50)
        {
            throw new ArgumentException("A task template must not contain more than 50 checklists.", nameof(checklists));
        }

        var normalizedReminderRules = reminderRules ?? [];
        if (normalizedReminderRules.Count > 50)
        {
            throw new ArgumentException("A task template must not contain more than 50 reminder rules.", nameof(reminderRules));
        }

        if (templateVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(templateVersion), "Template version must be positive.");
        }

        return new RecurrenceTaskTemplate(
            projectId,
            normalizedTitle,
            normalizedDescription,
            authorUserId,
            requesterUserId,
            primaryCounterpartyObjectId,
            priority,
            plannedDurationMinutes,
            deadlineOffsetMinutes,
            normalizedAssigneeIds,
            normalizedWatcherIds,
            normalizedChecklists,
            normalizedReminderRules,
            templateVersion);
    }

    /// <inheritdoc />
    public bool Equals(RecurrenceTaskTemplate? other) =>
        other is not null &&
        ProjectId == other.ProjectId &&
        Title == other.Title &&
        Description == other.Description &&
        AuthorUserId == other.AuthorUserId &&
        RequesterUserId == other.RequesterUserId &&
        PrimaryCounterpartyObjectId == other.PrimaryCounterpartyObjectId &&
        Priority == other.Priority &&
        PlannedDurationMinutes == other.PlannedDurationMinutes &&
        DeadlineOffsetMinutes == other.DeadlineOffsetMinutes &&
        TemplateVersion == other.TemplateVersion &&
        AssigneeIds.SequenceEqual(other.AssigneeIds) &&
        WatcherIds.SequenceEqual(other.WatcherIds) &&
        Checklists.SequenceEqual(other.Checklists) &&
        ReminderRules.SequenceEqual(other.ReminderRules);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as RecurrenceTaskTemplate);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ProjectId);
        hash.Add(Title);
        hash.Add(Description);
        hash.Add(AuthorUserId);
        hash.Add(RequesterUserId);
        hash.Add(PrimaryCounterpartyObjectId);
        hash.Add(Priority);
        hash.Add(PlannedDurationMinutes);
        hash.Add(DeadlineOffsetMinutes);
        hash.Add(TemplateVersion);
        foreach (var id in AssigneeIds)
        {
            hash.Add(id);
        }

        foreach (var id in WatcherIds)
        {
            hash.Add(id);
        }

        foreach (var checklist in Checklists)
        {
            hash.Add(checklist);
        }

        foreach (var rule in ReminderRules)
        {
            hash.Add(rule);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(RecurrenceTaskTemplate? left, RecurrenceTaskTemplate? right) =>
        ReferenceEquals(left, right) || (left is not null && left.Equals(right));

    public static bool operator !=(RecurrenceTaskTemplate? left, RecurrenceTaskTemplate? right) => !(left == right);

    private static IReadOnlyList<Guid> NormalizeUserIds(IReadOnlyList<Guid>? values, string parameterName, int maxCount)
    {
        if (values is null || values.Count == 0)
        {
            return [];
        }

        if (values.Count > maxCount)
        {
            throw new ArgumentException($"{parameterName} must not contain more than {maxCount} entries.", parameterName);
        }

        var copy = new Guid[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            if (value == Guid.Empty)
            {
                throw new ArgumentException($"{parameterName} must not contain an empty identifier.", parameterName);
            }

            copy[index] = value;
        }

        if (copy.Distinct().Count() != copy.Length)
        {
            throw new ArgumentException($"{parameterName} must not contain duplicates.", parameterName);
        }

        return copy;
    }
}