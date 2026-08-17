using Task.Domain.Calendar;

namespace Task.Tests.Calendar;

public sealed class CalendarAttendeeTests
{
    private static readonly Guid UserAccountId = Guid.Parse("ad23960f-d96b-4780-aee2-822316e3c22b");
    private static readonly Guid ContactId = Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479");
    private static readonly DateTimeOffset RespondedAt = new(2026, 8, 17, 12, 30, 0, TimeSpan.Zero);

    public static TheoryData<CalendarAttendeeRole> AllRoles =>
        Enum.GetValues<CalendarAttendeeRole>().Aggregate(new TheoryData<CalendarAttendeeRole>(), (d, v) => { d.Add(v); return d; });

    public static TheoryData<CalendarAttendeeResponseStatus> AllStatuses =>
        Enum.GetValues<CalendarAttendeeResponseStatus>()
            .Aggregate(new TheoryData<CalendarAttendeeResponseStatus>(), (d, v) => { d.Add(v); return d; });

    [Fact]
    public void EventAttendee_PreservesAllValidFields()
    {
        var attendee = EventAttendee.Create(
            UserAccountId, CalendarAttendeeRole.Required, CalendarAttendeeResponseStatus.Accepted, RespondedAt);

        Assert.Equal(UserAccountId, attendee.UserAccountId);
        Assert.Equal(CalendarAttendeeRole.Required, attendee.Role);
        Assert.Equal(CalendarAttendeeResponseStatus.Accepted, attendee.ResponseStatus);
        Assert.Equal(RespondedAt, attendee.RespondedAtUtc);
    }

    [Fact]
    public void ContactAttendee_PreservesAllValidFields()
    {
        var attendee = ContactAttendee.Create(
            ContactId, CalendarAttendeeRole.Observer, CalendarAttendeeResponseStatus.Declined, RespondedAt);

        Assert.Equal(ContactId, attendee.ContactId);
        Assert.Equal(CalendarAttendeeRole.Observer, attendee.Role);
        Assert.Equal(CalendarAttendeeResponseStatus.Declined, attendee.ResponseStatus);
        Assert.Equal(RespondedAt, attendee.RespondedAtUtc);
    }

    [Theory]
    [MemberData(nameof(AllRoles))]
    public void EventAttendee_AcceptsEveryDeclaredRole(CalendarAttendeeRole role)
    {
        var attendee = EventAttendee.Create(UserAccountId, role, CalendarAttendeeResponseStatus.Pending, null);

        Assert.Equal(role, attendee.Role);
    }

    [Theory]
    [MemberData(nameof(AllRoles))]
    public void ContactAttendee_AcceptsEveryDeclaredRole(CalendarAttendeeRole role)
    {
        var attendee = ContactAttendee.Create(ContactId, role, CalendarAttendeeResponseStatus.Pending, null);

        Assert.Equal(role, attendee.Role);
    }

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public void EventAttendee_AcceptsEveryDeclaredResponseStatus(CalendarAttendeeResponseStatus status)
    {
        var attendee = EventAttendee.Create(UserAccountId, CalendarAttendeeRole.Required, status, null);

        Assert.Equal(status, attendee.ResponseStatus);
    }

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public void ContactAttendee_AcceptsEveryDeclaredResponseStatus(CalendarAttendeeResponseStatus status)
    {
        var attendee = ContactAttendee.Create(ContactId, CalendarAttendeeRole.Required, status, null);

        Assert.Equal(status, attendee.ResponseStatus);
    }

    [Fact]
    public void EventAttendee_AcceptsNullRespondedAtUtc()
    {
        var attendee = EventAttendee.Create(
            UserAccountId, CalendarAttendeeRole.Optional, CalendarAttendeeResponseStatus.Pending, null);

        Assert.Null(attendee.RespondedAtUtc);
    }

    [Fact]
    public void ContactAttendee_AcceptsNullRespondedAtUtc()
    {
        var attendee = ContactAttendee.Create(
            ContactId, CalendarAttendeeRole.Optional, CalendarAttendeeResponseStatus.Pending, null);

        Assert.Null(attendee.RespondedAtUtc);
    }

    [Fact]
    public void EventAttendee_PreservesUtcRespondedAt()
    {
        var attendee = EventAttendee.Create(
            UserAccountId, CalendarAttendeeRole.Required, CalendarAttendeeResponseStatus.Tentative, RespondedAt);

        Assert.Equal(RespondedAt, attendee.RespondedAtUtc);
        Assert.Equal(TimeSpan.Zero, attendee.RespondedAtUtc!.Value.Offset);
    }

    [Fact]
    public void ContactAttendee_PreservesUtcRespondedAt()
    {
        var attendee = ContactAttendee.Create(
            ContactId, CalendarAttendeeRole.Required, CalendarAttendeeResponseStatus.Tentative, RespondedAt);

        Assert.Equal(RespondedAt, attendee.RespondedAtUtc);
        Assert.Equal(TimeSpan.Zero, attendee.RespondedAtUtc!.Value.Offset);
    }

    [Fact]
    public void EventAttendee_RejectsEmptyUserAccountId()
    {
        Assert.Throws<ArgumentException>(
            () => EventAttendee.Create(Guid.Empty, CalendarAttendeeRole.Required, CalendarAttendeeResponseStatus.Pending, null));
    }

    [Fact]
    public void ContactAttendee_RejectsEmptyContactId()
    {
        Assert.Throws<ArgumentException>(
            () => ContactAttendee.Create(Guid.Empty, CalendarAttendeeRole.Required, CalendarAttendeeResponseStatus.Pending, null));
    }

    [Fact]
    public void EventAttendee_RejectsUndefinedRole()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EventAttendee.Create(UserAccountId, (CalendarAttendeeRole)42, CalendarAttendeeResponseStatus.Pending, null));
    }

    [Fact]
    public void ContactAttendee_RejectsUndefinedRole()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ContactAttendee.Create(ContactId, (CalendarAttendeeRole)42, CalendarAttendeeResponseStatus.Pending, null));
    }

    [Fact]
    public void EventAttendee_RejectsUndefinedResponseStatus()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EventAttendee.Create(UserAccountId, CalendarAttendeeRole.Required, (CalendarAttendeeResponseStatus)42, null));
    }

    [Fact]
    public void ContactAttendee_RejectsUndefinedResponseStatus()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ContactAttendee.Create(ContactId, CalendarAttendeeRole.Required, (CalendarAttendeeResponseStatus)42, null));
    }

    [Fact]
    public void EventAttendee_RejectsNonUtcRespondedAt()
    {
        var nonUtc = new DateTimeOffset(2026, 8, 17, 12, 30, 0, TimeSpan.FromHours(2));

        Assert.Throws<ArgumentException>(
            () => EventAttendee.Create(UserAccountId, CalendarAttendeeRole.Required, CalendarAttendeeResponseStatus.Pending, nonUtc));
    }

    [Fact]
    public void ContactAttendee_RejectsNonUtcRespondedAt()
    {
        var nonUtc = new DateTimeOffset(2026, 8, 17, 12, 30, 0, TimeSpan.FromHours(2));

        Assert.Throws<ArgumentException>(
            () => ContactAttendee.Create(ContactId, CalendarAttendeeRole.Required, CalendarAttendeeResponseStatus.Pending, nonUtc));
    }

    [Fact]
    public void EventAttendee_PublicStateIsImmutableByApiShape()
    {
        Assert.All(
            typeof(EventAttendee).GetProperties(),
            property => Assert.False(property.CanWrite, $"{property.Name} must be read-only"));
    }

    [Fact]
    public void ContactAttendee_PublicStateIsImmutableByApiShape()
    {
        Assert.All(
            typeof(ContactAttendee).GetProperties(),
            property => Assert.False(property.CanWrite, $"{property.Name} must be read-only"));
    }
}
