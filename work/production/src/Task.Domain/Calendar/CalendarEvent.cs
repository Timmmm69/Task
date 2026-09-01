using Task.Domain;

namespace Task.Domain.Calendar;

/// <summary>
/// Root of the CalendarEvent aggregate (OpenAPI <c>CalendarEvent</c>):
/// scalar core fields, attendee collections and the archive/trash lifecycle —
/// recurrence, endpoints, persistence and UI are separate slices.
/// Immutable: every visible change returns a new instance whose
/// <see cref="SyncableEntityMetadata"/> records the change and advances the
/// version. Lifecycle transitions (archive, unarchive, trash, restore)
/// delegate to the metadata and preserve every business field; mutating
/// methods (UpdateDetails, Cancel, Reschedule, ReplaceAttendees) require an
/// Active lifecycle.
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
    /// state: a defined status, valid scalar fields and valid lifecycle
    /// metadata (Active, Archived or Trashed).
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
    /// scalar fields, valid attendee collections and valid lifecycle metadata
    /// (Active, Archived or Trashed).
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
    /// Applies the complete writable <c>CalendarEventPatch</c> projection in
    /// one aggregate transition. This keeps a multi-field HTTP PATCH atomic
    /// and advances the optimistic-concurrency version at most once.
    /// </summary>
    public CalendarEvent ApplyPatch(
        Guid actorId,
        DateTimeOffset occurredAtUtc,
        Guid? projectId,
        string title,
        string? description,
        CalendarEventTiming timing,
        CalendarEventStatus status,
        IEnumerable<EventAttendee> userAttendees,
        IEnumerable<ContactAttendee> contactAttendees)
    {
        ArgumentNullException.ThrowIfNull(timing);
        EnsureActive("An archived or trashed event must be restored before it can be updated.");
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), "Unknown calendar event status.");
        }

        var normalizedProjectId = NormalizeProjectId(projectId);
        var normalizedTitle = NormalizeTitle(title);
        var normalizedDescription = NormalizeDescription(description);
        var normalizedUserAttendees = NormalizeUserAttendees(userAttendees);
        var normalizedContactAttendees = NormalizeContactAttendees(contactAttendees);

        if (normalizedProjectId == ProjectId &&
            normalizedTitle == Title &&
            normalizedDescription == Description &&
            timing == Timing &&
            status == Status &&
            UserAttendees.SequenceEqual(normalizedUserAttendees) &&
            ContactAttendees.SequenceEqual(normalizedContactAttendees))
        {
            return this;
        }

        return new CalendarEvent(
            Metadata.RecordVisibleChange(actorId, occurredAtUtc),
            normalizedProjectId,
            normalizedTitle,
            normalizedDescription,
            timing,
            status,
            normalizedUserAttendees,
            normalizedContactAttendees);
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

    /// <summary>
    /// Archives the event (OpenAPI <c>POST /api/v1/calendar-events/{id}/archive</c>).
    /// Only an active event can be archived; <see cref="SyncableEntityMetadata.Archive"/>
    /// records the transition and advances the version exactly once. Business
    /// fields are preserved.
    /// </summary>
    public CalendarEvent Archive(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        return new CalendarEvent(
            Metadata.Archive(actorId, occurredAtUtc),
            ProjectId,
            Title,
            Description,
            Timing,
            Status,
            UserAttendees,
            ContactAttendees);
    }

    /// <summary>
    /// Returns an archived event to the active state (OpenAPI
    /// <c>POST /api/v1/calendar-events/{id}/unarchive</c>). Only an archived
    /// event can be unarchived; <see cref="SyncableEntityMetadata.RestoreFromArchive"/>
    /// records the transition and advances the version exactly once. Business
    /// fields are preserved.
    /// </summary>
    public CalendarEvent RestoreFromArchive(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        return new CalendarEvent(
            Metadata.RestoreFromArchive(actorId, occurredAtUtc),
            ProjectId,
            Title,
            Description,
            Timing,
            Status,
            UserAttendees,
            ContactAttendees);
    }

    /// <summary>
    /// Moves the event to trash (OpenAPI <c>DELETE /api/v1/calendar-events/{id}</c>).
    /// An active or archived event can be trashed; a trashed event cannot be
    /// trashed again. <see cref="SyncableEntityMetadata.MoveToTrash"/> records
    /// the prior lifecycle state and advances the version exactly once.
    /// Business fields are preserved.
    /// </summary>
    public CalendarEvent MoveToTrash(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        return new CalendarEvent(
            Metadata.MoveToTrash(actorId, occurredAtUtc),
            ProjectId,
            Title,
            Description,
            Timing,
            Status,
            UserAttendees,
            ContactAttendees);
    }

    /// <summary>
    /// Restores an event from trash (OpenAPI
    /// <c>POST /api/v1/calendar-events/{id}/restore</c>). The event returns to
    /// the lifecycle state recorded before it was trashed (Active or Archived).
    /// <see cref="SyncableEntityMetadata.RestoreFromTrash"/> clears the trash
    /// metadata and advances the version exactly once. Business fields are
    /// preserved.
    /// </summary>
    public CalendarEvent RestoreFromTrash(Guid actorId, DateTimeOffset occurredAtUtc)
    {
        return new CalendarEvent(
            Metadata.RestoreFromTrash(actorId, occurredAtUtc),
            ProjectId,
            Title,
            Description,
            Timing,
            Status,
            UserAttendees,
            ContactAttendees);
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
