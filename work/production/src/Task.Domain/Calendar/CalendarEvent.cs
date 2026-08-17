using Task.Domain;

namespace Task.Domain.Calendar;

/// <summary>
/// Root of the CalendarEvent aggregate (OpenAPI <c>CalendarEvent</c>):
/// scalar core fields plus attendee collections — recurrence, lifecycle
/// archive/trash, endpoints, persistence and UI are separate slices.
/// Immutable: every visible change returns a new instance whose
/// <see cref="SyncableEntityMetadata"/> records the change and advances the
/// version. In this slice the lifecycle is restricted to Active; archive and
/// trash transitions are a separate lifecycle packet.
/// </summary>
public sealed class CalendarEvent
{
    private CalendarEvent(
        SyncableEntityMetadata metadata,
        Guid? projectId,
        string title,
        string? description,
        CalendarEventTiming timing,
        CalendarEventStatus status,
        IReadOnlyList<EventAttendee> userAttendees,
        IReadOnlyList<ContactAttendee> contactAttendees)
    {
        Metadata = metadata;
        ProjectId = projectId;
        Title = title;
        Description = description;
        Timing = timing;
        Status = status;
        UserAttendees = userAttendees;
        ContactAttendees = contactAttendees;
    }

    public SyncableEntityMetadata Metadata { get; }

    public Guid? ProjectId { get; }

    public string Title { get; }

    public string? Description { get; }

    public CalendarEventTiming Timing { get; }

    public CalendarEventStatus Status { get; }

    /// <summary>
    /// User attendees of the event (OpenAPI <c>userAttendees</c>). Immutable
    /// snapshot in the supplied order; never null, at most 500 entries.
    /// </summary>
    public IReadOnlyList<EventAttendee> UserAttendees { get; }

    /// <summary>
    /// Contact attendees of the event (OpenAPI <c>contactAttendees</c>).
    /// Immutable snapshot in the supplied order; never null, at most 500
    /// entries.
    /// </summary>
    public IReadOnlyList<ContactAttendee> ContactAttendees { get; }

    /// <summary>
    /// Creates a scheduled event (OpenAPI <c>CalendarEventCreate</c> scalar
    /// fields). <paramref name="timing"/> is required and keeps its own
    /// timezone/UTC/all-day validation; it is not re-validated here.
    /// </summary>
    public static CalendarEvent Create(
        Guid id,
        Guid organizationId,
        Guid creatorId,
        Guid? projectId,
        string title,
        string? description,
        CalendarEventTiming timing,
        DateTimeOffset createdAtUtc)
    {
        return Create(
            id,
            organizationId,
            creatorId,
            projectId,
            title,
            description,
            timing,
            createdAtUtc,
            Array.Empty<EventAttendee>(),
            Array.Empty<ContactAttendee>());
    }

    /// <summary>
    /// Creates a scheduled event (OpenAPI <c>CalendarEventCreate</c> scalar
    /// fields) with attendee collections (OpenAPI <c>userAttendees</c> and
    /// <c>contactAttendees</c>). <paramref name="timing"/> is required and
    /// keeps its own timezone/UTC/all-day validation; it is not re-validated
    /// here.
    /// </summary>
    public static CalendarEvent Create(
        Guid id,
        Guid organizationId,
        Guid creatorId,
        Guid? projectId,
        string title,
        string? description,
        CalendarEventTiming timing,
        DateTimeOffset createdAtUtc,
        IEnumerable<EventAttendee> userAttendees,
        IEnumerable<ContactAttendee> contactAttendees)
    {
        ArgumentNullException.ThrowIfNull(timing);
        var metadata = SyncableEntityMetadata.Create(id, organizationId, creatorId, createdAtUtc);

        return new CalendarEvent(
            metadata,
            NormalizeProjectId(projectId),
            NormalizeTitle(title),
            NormalizeDescription(description),
            timing,
            CalendarEventStatus.Scheduled,
            NormalizeUserAttendees(userAttendees),
            NormalizeContactAttendees(contactAttendees));
    }

    /// <summary>
    /// Reconstructs an event from persisted state. Accepts only fully valid
    /// state: a defined status, valid scalar fields and an Active lifecycle
    /// (archive/trash metadata belongs to a separate lifecycle packet).
    /// </summary>
    public static CalendarEvent Reconstitute(
        SyncableEntityMetadata metadata,
        Guid? projectId,
        string title,
        string? description,
        CalendarEventTiming timing,
        CalendarEventStatus status)
    {
        return Reconstitute(
            metadata,
            projectId,
            title,
            description,
            timing,
            status,
            Array.Empty<EventAttendee>(),
            Array.Empty<ContactAttendee>());
    }

    /// <summary>
    /// Reconstructs an event from persisted state, including both attendee
    /// collections. Accepts only fully valid state: a defined status, valid
    /// scalar fields, valid attendee collections and an Active lifecycle
    /// (archive/trash metadata belongs to a separate lifecycle packet).
    /// </summary>
    public static CalendarEvent Reconstitute(
        SyncableEntityMetadata metadata,
        Guid? projectId,
        string title,
        string? description,
        CalendarEventTiming timing,
        CalendarEventStatus status,
        IEnumerable<EventAttendee> userAttendees,
        IEnumerable<ContactAttendee> contactAttendees)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(timing);

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), "Unknown calendar event status.");
        }

        if (metadata.LifecycleState != EntityLifecycleState.Active)
        {
            throw new InvalidOperationException(
                "Only an active event can be reconstituted; archive and trash lifecycle are a separate packet.");
        }

        return new CalendarEvent(
            metadata,
            NormalizeProjectId(projectId),
            NormalizeTitle(title),
            NormalizeDescription(description),
            timing,
            status,
            NormalizeUserAttendees(userAttendees),
            NormalizeContactAttendees(contactAttendees));
    }

    /// <summary>
    /// Applies valid scalar changes to an active event: <c>projectId</c>,
    /// <c>title</c>, <c>description</c> and <c>timing</c> (OpenAPI
    /// <c>CalendarEventPatch</c> scalar fields). When every value equals the
    /// current one the same instance is returned without a version bump;
    /// otherwise <see cref="SyncableEntityMetadata.RecordVisibleChange"/> is
    /// applied exactly once.
    /// </summary>
    public CalendarEvent UpdateDetails(
        Guid actorId,
        DateTimeOffset occurredAtUtc,
        Guid? projectId,
        string title,
        string? description,
        CalendarEventTiming timing)
    {
        ArgumentNullException.ThrowIfNull(timing);
        EnsureActive("An archived or trashed event must be restored before it can be updated.");
        var normalizedProjectId = NormalizeProjectId(projectId);
        var normalizedTitle = NormalizeTitle(title);
        var normalizedDescription = NormalizeDescription(description);

        if (normalizedProjectId == ProjectId &&
            normalizedTitle == Title &&
            normalizedDescription == Description &&
            timing == Timing)
        {
            return this;
        }

        return new CalendarEvent(
            Metadata.RecordVisibleChange(actorId, occurredAtUtc),
            normalizedProjectId,
            normalizedTitle,
            normalizedDescription,
            timing,
            Status,
            UserAttendees,
            ContactAttendees);
    }

    /// <summary>
    /// Cancels a scheduled event (status transition only; it is not a
    /// deletion and never moves the event to trash).
    /// </summary>
    public CalendarEvent Cancel(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        EnsureActive("An archived or trashed event must be restored before it can be cancelled.");
        if (Status != CalendarEventStatus.Scheduled)
        {
            throw new InvalidOperationException("Only a scheduled event can be cancelled.");
        }

        return new CalendarEvent(
            Metadata.RecordVisibleChange(actorId, occurredAtUtc),
            ProjectId,
            Title,
            Description,
            Timing,
            CalendarEventStatus.Cancelled,
            UserAttendees,
            ContactAttendees);
    }

    /// <summary>
    /// Reschedules a cancelled event back to the scheduled status.
    /// </summary>
    public CalendarEvent Reschedule(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        EnsureActive("An archived or trashed event must be restored before it can be rescheduled.");
        if (Status != CalendarEventStatus.Cancelled)
        {
            throw new InvalidOperationException("Only a cancelled event can be rescheduled.");
        }

        return new CalendarEvent(
            Metadata.RecordVisibleChange(actorId, occurredAtUtc),
            ProjectId,
            Title,
            Description,
            Timing,
            CalendarEventStatus.Scheduled,
            UserAttendees,
            ContactAttendees);
    }

    /// <summary>
    /// Replaces both attendee collections of an active event (OpenAPI
    /// <c>AttendeesReplace</c>: <c>users</c> and <c>contacts</c>, each at
    /// most 500 entries; duplicates are allowed and cross-kind rules are not
    /// defined by the contract). Applies to scheduled and cancelled events.
    /// When the new collections are sequence-equal to the current ones the
    /// same instance is returned without a version bump; otherwise
    /// <see cref="SyncableEntityMetadata.RecordVisibleChange"/> is applied
    /// exactly once.
    /// </summary>
    public CalendarEvent ReplaceAttendees(
        Guid actorId,
        DateTimeOffset occurredAtUtc,
        IEnumerable<EventAttendee> userAttendees,
        IEnumerable<ContactAttendee> contactAttendees)
    {
        var normalizedUserAttendees = NormalizeUserAttendees(userAttendees);
        var normalizedContactAttendees = NormalizeContactAttendees(contactAttendees);
        EnsureActive("An archived or trashed event must be restored before its attendees can be replaced.");

        if (UserAttendees.SequenceEqual(normalizedUserAttendees) &&
            ContactAttendees.SequenceEqual(normalizedContactAttendees))
        {
            return this;
        }

        return new CalendarEvent(
            Metadata.RecordVisibleChange(actorId, occurredAtUtc),
            ProjectId,
            Title,
            Description,
            Timing,
            Status,
            normalizedUserAttendees,
            normalizedContactAttendees);
    }

    private void EnsureActive(string message)
    {
        if (Metadata.LifecycleState != EntityLifecycleState.Active)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static Guid? NormalizeProjectId(Guid? projectId)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project identifier must not be empty.", nameof(projectId));
        }

        return projectId;
    }

    private static string NormalizeTitle(string title)
    {
        var normalizedTitle = title?.Trim();
        if (string.IsNullOrEmpty(normalizedTitle))
        {
            throw new ArgumentException("Event title must not be empty.", nameof(title));
        }

        if (normalizedTitle.Length > 500)
        {
            throw new ArgumentException("Event title must not exceed 500 characters.", nameof(title));
        }

        return normalizedTitle;
    }

    private static string? NormalizeDescription(string? description)
    {
        if (description is not null && description.Length > 20000)
        {
            throw new ArgumentException("Event description must not exceed 20000 characters.", nameof(description));
        }

        return description;
    }

    private static IReadOnlyList<EventAttendee> NormalizeUserAttendees(IEnumerable<EventAttendee> userAttendees)
    {
        ArgumentNullException.ThrowIfNull(userAttendees);
        var attendees = userAttendees.ToArray();
        if (attendees.Length > 500)
        {
            throw new ArgumentException("User attendees must not exceed 500 entries.", nameof(userAttendees));
        }

        for (var i = 0; i < attendees.Length; i++)
        {
            if (attendees[i] is null)
            {
                throw new ArgumentException("User attendees must not contain null entries.", nameof(userAttendees));
            }
        }

        return Array.AsReadOnly(attendees);
    }

    private static IReadOnlyList<ContactAttendee> NormalizeContactAttendees(IEnumerable<ContactAttendee> contactAttendees)
    {
        ArgumentNullException.ThrowIfNull(contactAttendees);
        var attendees = contactAttendees.ToArray();
        if (attendees.Length > 500)
        {
            throw new ArgumentException("Contact attendees must not exceed 500 entries.", nameof(contactAttendees));
        }

        for (var i = 0; i < attendees.Length; i++)
        {
            if (attendees[i] is null)
            {
                throw new ArgumentException("Contact attendees must not contain null entries.", nameof(contactAttendees));
            }
        }

        return Array.AsReadOnly(attendees);
    }
}
