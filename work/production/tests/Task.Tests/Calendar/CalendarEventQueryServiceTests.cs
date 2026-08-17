using Task.Application.Calendar;
using Task.Domain;
using Task.Domain.Calendar;

namespace Task.Tests.Calendar;

public sealed class CalendarEventQueryServiceTests
{
    private static readonly Guid EventId = Guid.Parse("019fe66f-1629-7083-b547-37430d2e71a4");
    private static readonly Guid OrganizationId = Guid.Parse("751fa8ce-5cc3-4d98-8574-1108080b2ff4");
    private static readonly Guid OtherOrganizationId = Guid.Parse("b2c31488-6a1e-4d5e-9c7b-21d09f3e8a51");
    private static readonly Guid CreatorId = Guid.Parse("3077f0f8-536f-4988-bd73-6f26265d0b92");
    private static readonly Guid ActorId = Guid.Parse("957c3a11-d6f2-4a2a-bb1b-0945a0f6a820");
    private static readonly Guid UserAttendeeId = Guid.Parse("1a4b6c2d-9e80-4f3a-b5d7-c2e18a94f061");
    private static readonly Guid ContactId = Guid.Parse("6f3d4a2b-8c17-4e9a-a4c5-d1b07e2f93ac");
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 17, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ChangedAt = CreatedAt.AddHours(1);

    [Fact]
    public void GetById_ProjectsEveryScalarFieldAndTiming()
    {
        var service = new CalendarEventQueryService(
            StoreWith(NewEvent("  Standup  ", "Daily sync", TimedTiming())));

        var details = service.GetById(OrganizationId, EventId);

        Assert.NotNull(details);
        Assert.Equal(EventId, details.Id);
        Assert.Equal(OrganizationId, details.OrganizationId);
        Assert.Null(details.ProjectId);
        Assert.Equal("Standup", details.Title);
        Assert.Equal("Daily sync", details.Description);
        Assert.Equal(new DateOnly(2026, 8, 17), details.EventDate);
        Assert.False(details.IsAllDay);
        Assert.Equal(new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero), details.StartAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero), details.EndAtUtc);
        Assert.Equal("UTC", details.TimeZoneId);
        Assert.Equal(CalendarEventStatus.Scheduled, details.Status);
        Assert.Equal(EntityLifecycleState.Active, details.LifecycleState);
        Assert.Equal(1, details.Version);
        Assert.Equal(CreatedAt, details.CreatedAtUtc);
        Assert.Equal(CreatedAt, details.UpdatedAtUtc);
    }

    [Fact]
    public void GetById_ProjectsAttendeeCollectionsInStoredOrder()
    {
        var userAttendees = new[]
        {
            EventAttendee.Create(UserAttendeeId, CalendarAttendeeRole.Required, CalendarAttendeeResponseStatus.Pending, null),
            EventAttendee.Create(ActorId, CalendarAttendeeRole.Observer, CalendarAttendeeResponseStatus.Accepted, ChangedAt),
        };
        var contactAttendees = new[]
        {
            ContactAttendee.Create(ContactId, CalendarAttendeeRole.Optional, CalendarAttendeeResponseStatus.Tentative, ChangedAt),
        };
        var service = new CalendarEventQueryService(
            StoreWith(CalendarEvent.Create(
                EventId, OrganizationId, CreatorId, null, "Standup", null, TimedTiming(), CreatedAt,
                userAttendees, contactAttendees)));

        var details = service.GetById(OrganizationId, EventId);

        Assert.NotNull(details);
        Assert.Equal(2, details.UserAttendees.Count);
        Assert.Equal(UserAttendeeId, details.UserAttendees[0].UserAccountId);
        Assert.Equal(CalendarAttendeeRole.Observer, details.UserAttendees[1].Role);
        Assert.Single(details.ContactAttendees);
        Assert.Equal(CalendarAttendeeResponseStatus.Tentative, details.ContactAttendees[0].ResponseStatus);
    }

    [Fact]
    public void GetById_ProjectsAllDayEventWithoutInstants()
    {
        var service = new CalendarEventQueryService(StoreWith(NewEvent("Holiday", null, AllDayTiming())));

        var details = service.GetById(OrganizationId, EventId);

        Assert.NotNull(details);
        Assert.True(details.IsAllDay);
        Assert.Null(details.StartAtUtc);
        Assert.Null(details.EndAtUtc);
        Assert.Equal(new DateOnly(2026, 8, 18), details.EventDate);
    }

    [Fact]
    public void GetById_ProjectsCancelledAndTrashedEventState()
    {
        var trashed = NewEvent("Standup", null, TimedTiming())
            .Cancel(ActorId, ChangedAt)
            .MoveToTrash(ActorId, ChangedAt.AddMinutes(5));
        var service = new CalendarEventQueryService(StoreWith(trashed));

        var details = service.GetById(OrganizationId, EventId);

        Assert.NotNull(details);
        Assert.Equal(CalendarEventStatus.Cancelled, details.Status);
        Assert.Equal(EntityLifecycleState.Trashed, details.LifecycleState);
        Assert.Equal(3, details.Version);
    }

    [Fact]
    public void GetById_ForMissingEvent_ReturnsNull()
    {
        var service = new CalendarEventQueryService(new EmptyCalendarEventStore());

        Assert.Null(service.GetById(OrganizationId, EventId));
    }

    [Fact]
    public void GetById_ForEventOfAnotherOrganization_ReturnsNull()
    {
        var service = new CalendarEventQueryService(StoreWith(NewEvent("Standup", null, TimedTiming())));

        Assert.Null(service.GetById(OtherOrganizationId, EventId));
    }

    [Fact]
    public void GetById_NeverMutatesTheAggregate()
    {
        var store = StoreWith(NewEvent("Standup", null, TimedTiming()));
        var service = new CalendarEventQueryService(store);

        service.GetById(OrganizationId, EventId);
        service.GetById(OrganizationId, EventId);

        var stored = store.Get(EventId, OrganizationId);
        Assert.NotNull(stored);
        Assert.Equal(1, stored.Metadata.Version);
        Assert.Equal(CalendarEventStatus.Scheduled, stored.Status);
    }

    private static CalendarEvent NewEvent(string title, string? description, CalendarEventTiming timing) =>
        CalendarEvent.Create(EventId, OrganizationId, CreatorId, null, title, description, timing, CreatedAt);

    private static CalendarEventTiming TimedTiming() =>
        CalendarEventTiming.CreateTimed(
            new DateOnly(2026, 8, 17),
            new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero),
            "UTC");

    private static CalendarEventTiming AllDayTiming() =>
        CalendarEventTiming.CreateAllDay(new DateOnly(2026, 8, 18), "UTC");

    private static FakeCalendarEventStore StoreWith(CalendarEvent calendarEvent)
    {
        var store = new FakeCalendarEventStore();
        store.Add(calendarEvent);
        return store;
    }

    private sealed class EmptyCalendarEventStore : ICalendarEventStore
    {
        public CalendarEvent? Get(Guid eventId, Guid organizationId) => null;

        public void Add(CalendarEvent calendarEvent)
        {
        }

        public void Save(CalendarEvent calendarEvent, int expectedVersion)
        {
        }
    }

    private sealed class FakeCalendarEventStore : ICalendarEventStore
    {
        private readonly Dictionary<(Guid OrganizationId, Guid EventId), CalendarEvent> _events = new();

        public CalendarEvent? Get(Guid eventId, Guid organizationId)
        {
            _events.TryGetValue((organizationId, eventId), out var calendarEvent);
            return calendarEvent;
        }

        public void Add(CalendarEvent calendarEvent)
        {
            _events[(calendarEvent.Metadata.OrganizationId, calendarEvent.Metadata.Id)] = calendarEvent;
        }

        public void Save(CalendarEvent calendarEvent, int expectedVersion)
        {
            _events[(calendarEvent.Metadata.OrganizationId, calendarEvent.Metadata.Id)] = calendarEvent;
        }
    }
}
