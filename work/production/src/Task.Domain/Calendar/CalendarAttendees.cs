namespace Task.Domain.Calendar;

/// <summary>
/// User attendee of a calendar event (OpenAPI <c>EventAttendee</c>:
/// <c>userAccountId</c>, <c>role</c>, <c>responseStatus</c>,
/// <c>respondedAt</c>). Immutable value object: identifiers must not be
/// empty, enum values must be defined and a non-null <c>respondedAt</c>
/// must use the UTC offset. There is no relationship between the response
/// status and <c>respondedAt</c> — the contract declares the timestamp
/// nullable independently of the status.
/// </summary>
public sealed record EventAttendee
{
    private EventAttendee(
        Guid userAccountId,
        CalendarAttendeeRole role,
        CalendarAttendeeResponseStatus responseStatus,
        DateTimeOffset? respondedAtUtc)
    {
        UserAccountId = userAccountId;
        Role = role;
        ResponseStatus = responseStatus;
        RespondedAtUtc = respondedAtUtc;
    }

    public Guid UserAccountId { get; }

    public CalendarAttendeeRole Role { get; }

    public CalendarAttendeeResponseStatus ResponseStatus { get; }

    /// <summary>RFC 3339 instant with the UTC offset; null while no response is recorded.</summary>
    public DateTimeOffset? RespondedAtUtc { get; }

    public static EventAttendee Create(
        Guid userAccountId,
        CalendarAttendeeRole role,
        CalendarAttendeeResponseStatus responseStatus,
        DateTimeOffset? respondedAtUtc)
    {
        AttendeeValidation.EnsureIdentifier(userAccountId, nameof(userAccountId));
        AttendeeValidation.EnsureRole(role);
        AttendeeValidation.EnsureResponseStatus(responseStatus);
        AttendeeValidation.EnsureOptionalUtc(respondedAtUtc, nameof(respondedAtUtc));

        return new EventAttendee(userAccountId, role, responseStatus, respondedAtUtc);
    }
}

/// <summary>
/// Contact attendee of a calendar event (OpenAPI <c>ContactAttendee</c>:
/// <c>contactId</c>, <c>role</c>, <c>responseStatus</c>,
/// <c>respondedAt</c>). Immutable value object with the same invariants as
/// <see cref="EventAttendee"/>: non-empty identifier, defined enum values,
/// UTC-only optional response timestamp.
/// </summary>
public sealed record ContactAttendee
{
    private ContactAttendee(
        Guid contactId,
        CalendarAttendeeRole role,
        CalendarAttendeeResponseStatus responseStatus,
        DateTimeOffset? respondedAtUtc)
    {
        ContactId = contactId;
        Role = role;
        ResponseStatus = responseStatus;
        RespondedAtUtc = respondedAtUtc;
    }

    public Guid ContactId { get; }

    public CalendarAttendeeRole Role { get; }

    public CalendarAttendeeResponseStatus ResponseStatus { get; }

    /// <summary>RFC 3339 instant with the UTC offset; null while no response is recorded.</summary>
    public DateTimeOffset? RespondedAtUtc { get; }

    public static ContactAttendee Create(
        Guid contactId,
        CalendarAttendeeRole role,
        CalendarAttendeeResponseStatus responseStatus,
        DateTimeOffset? respondedAtUtc)
    {
        AttendeeValidation.EnsureIdentifier(contactId, nameof(contactId));
        AttendeeValidation.EnsureRole(role);
        AttendeeValidation.EnsureResponseStatus(responseStatus);
        AttendeeValidation.EnsureOptionalUtc(respondedAtUtc, nameof(respondedAtUtc));

        return new ContactAttendee(contactId, role, responseStatus, respondedAtUtc);
    }
}

/// <summary>
/// Private validation shared by the attendee value objects in this file
/// (no public surface; not a framework abstraction).
/// </summary>
internal static class AttendeeValidation
{
    internal static void EnsureIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", parameterName);
        }
    }

    internal static void EnsureRole(CalendarAttendeeRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), "Unknown attendee role.");
        }
    }

    internal static void EnsureResponseStatus(CalendarAttendeeResponseStatus responseStatus)
    {
        if (!Enum.IsDefined(responseStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(responseStatus), "Unknown attendee response status.");
        }
    }

    internal static void EnsureOptionalUtc(DateTimeOffset? value, string parameterName)
    {
        if (value.HasValue && value.Value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must use the UTC offset.", parameterName);
        }
    }
}
