using Task.Application.Calendar;
using Task.Domain;
using Task.Domain.Calendar;

namespace Task.Tests.Calendar;

public sealed class CalendarEventLifecycleServiceTests
{
    private static readonly Guid EventId = Guid.Parse("019fb732-ad08-7de1-b27d-c86bae8a2937");
    private static readonly Guid OrganizationId = Guid.Parse("751fa8ce-5cc3-4d98-8574-1108080b2ff4");
    private static readonly Guid OtherOrganizationId = Guid.Parse("b2c31488-6a1e-4d5e-9c7b-21d09f3e8a51");
    private static readonly Guid CreatorId = Guid.Parse("3077f0f8-536f-4988-bd73-6f26265d0b92");
    private static readonly Guid ActorId = Guid.Parse("957c3a11-d6f2-4a2a-bb1b-0945a0f6a820");
    private static readonly Guid UserAttendeeId = Guid.Parse("1a4b6c2d-9e80-4f3a-b5d7-c2e18a94f061");
    private static readonly Guid ContactId = Guid.Parse("6f3d4a2b-8c17-4e9a-a4c5-d1b07e2f93ac");
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 17, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ChangedAt = CreatedAt.AddHours(1);
    private static readonly DateTimeOffset LaterAt = ChangedAt.AddHours(1);

    [Fact]
    public void Create_AddsExactlyOnceAndReturnsNewEventAtVersionOne()
    {
        var store = new FakeCalendarEventStore();
        var service = new CalendarEventLifecycleService(store);

        var calendarEvent = service.Create(
            EventId, OrganizationId, CreatorId, null, "  Standup  ", null, TimedTiming(), CreatedAt);

        Assert.Equal(1, store.AddCalls);
        Assert.Equal(0, store.SaveCalls);
        Assert.Equal("Standup", calendarEvent.Title);
        Assert.Equal(CalendarEventStatus.Scheduled, calendarEvent.Status);
        Assert.Equal(1, calendarEvent.Metadata.Version);
        Assert.Equal(EntityLifecycleState.Active, calendarEvent.Metadata.LifecycleState);
        Assert.Empty(calendarEvent.UserAttendees);
        Assert.Empty(calendarEvent.ContactAttendees);
    }

    [Fact]
    public void Create_WithAttendees_StoresCollections()
    {
        var store = new FakeCalendarEventStore();
        var service = new CalendarEventLifecycleService(store);
        var userAttendees = new[] { NewUserAttendee() };
        var contactAttendees = new[] { NewContactAttendee() };

        var calendarEvent = service.Create(
            EventId, OrganizationId, CreatorId, null, "Planning", null, TimedTiming(), CreatedAt,
            userAttendees, contactAttendees);

        var stored = store.Get(EventId, OrganizationId);
        Assert.NotNull(stored);
        Assert.Single(stored.UserAttendees);
        Assert.Single(stored.ContactAttendees);
        Assert.Same(calendarEvent, stored);
    }

    [Fact]
    public void UpdateDetails_AppliesAndSavesExactlyOnce()
    {
        var store = StoreWith(NewEvent());
        var service = new CalendarEventLifecycleService(store);

        var updated = service.UpdateDetails(
            OrganizationId, EventId, 1, ActorId, ChangedAt, null, "Retro", "Agenda", AllDayTiming());

        Assert.Equal("Retro", updated.Title);
        Assert.Equal("Agenda", updated.Description);
        Assert.True(updated.Timing.IsAllDay);
        Assert.Equal(2, updated.Metadata.Version);
        Assert.Equal(1, store.SaveCalls);
    }

    [Fact]
    public void NoOpUpdateDetails_ReturnsSameInstanceWithoutVersionBump()
    {
        var store = StoreWith(NewEvent());
        var service = new CalendarEventLifecycleService(store);

        var result = service.UpdateDetails(
            OrganizationId, EventId, 1, ActorId, ChangedAt, null, "Standup", null, TimedTiming());

        Assert.Same(store.Get(EventId, OrganizationId), result);
        Assert.Equal(1, result.Metadata.Version);
    }

    [Fact]
    public void Cancel_TransitionsToCancelledAndSavesExactlyOnce()
    {
        var store = StoreWith(NewEvent());
        var service = new CalendarEventLifecycleService(store);

        var cancelled = service.Cancel(OrganizationId, EventId, 1, ActorId, ChangedAt);

        Assert.Equal(CalendarEventStatus.Cancelled, cancelled.Status);
        Assert.Equal(2, cancelled.Metadata.Version);
        Assert.Equal(1, store.SaveCalls);
    }

    [Fact]
    public void Reschedule_TransitionsBackToScheduledAndSavesExactlyOnce()
    {
        var store = StoreWith(NewEvent().Cancel(ActorId, ChangedAt));
        var service = new CalendarEventLifecycleService(store);

        var rescheduled = service.Reschedule(OrganizationId, EventId, 2, ActorId, LaterAt);

        Assert.Equal(CalendarEventStatus.Scheduled, rescheduled.Status);
        Assert.Equal(3, rescheduled.Metadata.Version);
        Assert.Equal(1, store.SaveCalls);
    }

    [Fact]
    public void ReplaceAttendees_ReplacesCollectionsAndSavesExactlyOnce()
    {
        var store = StoreWith(NewEvent());
        var service = new CalendarEventLifecycleService(store);

        var replaced = service.ReplaceAttendees(
            OrganizationId, EventId, 1, ActorId, ChangedAt,
            new[] { NewUserAttendee() }, new[] { NewContactAttendee() });

        Assert.Single(replaced.UserAttendees);
        Assert.Single(replaced.ContactAttendees);
        Assert.Equal(2, replaced.Metadata.Version);
        Assert.Equal(1, store.SaveCalls);
    }

    [Fact]
    public void Archive_TransitionsToArchivedAndSavesExactlyOnce()
    {
        var store = StoreWith(NewEvent());
        var service = new CalendarEventLifecycleService(store);

        var archived = service.Archive(OrganizationId, EventId, 1, ActorId, ChangedAt);

        Assert.Equal(EntityLifecycleState.Archived, archived.Metadata.LifecycleState);
        Assert.Equal(ChangedAt, archived.Metadata.ArchivedAtUtc);
        Assert.Equal(1, store.SaveCalls);
    }

    [Fact]
    public void RestoreFromArchive_ReturnsEventToActiveAndSavesExactlyOnce()
    {
        var store = StoreWith(NewEvent().Archive(ActorId, ChangedAt));
        var service = new CalendarEventLifecycleService(store);

        var restored = service.RestoreFromArchive(OrganizationId, EventId, 2, ActorId, LaterAt);

        Assert.Equal(EntityLifecycleState.Active, restored.Metadata.LifecycleState);
        Assert.Null(restored.Metadata.ArchivedAtUtc);
        Assert.Equal(1, store.SaveCalls);
    }

    [Fact]
    public void MoveToTrash_TransitionsToTrashedAndSavesExactlyOnce()
    {
        var store = StoreWith(NewEvent());
        var service = new CalendarEventLifecycleService(store);

        var trashed = service.MoveToTrash(OrganizationId, EventId, 1, ActorId, ChangedAt);

        Assert.Equal(EntityLifecycleState.Trashed, trashed.Metadata.LifecycleState);
        Assert.Equal(EntityLifecycleState.Active, trashed.Metadata.LifecycleStateBeforeTrash);
        Assert.Equal(1, store.SaveCalls);
    }

    [Fact]
    public void RestoreFromTrash_ReturnsEventToPreviousStateAndSavesExactlyOnce()
    {
        var store = StoreWith(NewEvent().MoveToTrash(ActorId, ChangedAt));
        var service = new CalendarEventLifecycleService(store);

        var restored = service.RestoreFromTrash(OrganizationId, EventId, 2, ActorId, LaterAt);

        Assert.Equal(EntityLifecycleState.Active, restored.Metadata.LifecycleState);
        Assert.Null(restored.Metadata.LifecycleStateBeforeTrash);
        Assert.Equal(1, store.SaveCalls);
    }

    [Fact]
    public void Operation_OnEventOfAnotherOrganization_ThrowsKeyNotFoundExceptionAndDoesNotSave()
    {
        var store = StoreWith(NewEvent());
        var service = new CalendarEventLifecycleService(store);

        Assert.Throws<KeyNotFoundException>(() =>
            service.Cancel(OtherOrganizationId, EventId, 1, ActorId, ChangedAt));

        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public void Operation_OnMissingEvent_ThrowsKeyNotFoundExceptionAndDoesNotSave()
    {
        var store = new FakeCalendarEventStore();
        var service = new CalendarEventLifecycleService(store);

        Assert.Throws<KeyNotFoundException>(() =>
            service.Archive(OrganizationId, EventId, 1, ActorId, ChangedAt));

        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public void StaleExpectedVersion_ThrowsConcurrencyExceptionWithFieldsAndDoesNotSave()
    {
        var store = StoreWith(NewEvent().Cancel(ActorId, ChangedAt));
        var service = new CalendarEventLifecycleService(store);

        var exception = Assert.Throws<CalendarEventConcurrencyException>(() =>
            service.Reschedule(OrganizationId, EventId, 1, ActorId, LaterAt));

        Assert.Equal(EventId, exception.EventId);
        Assert.Equal(1, exception.ExpectedVersion);
        Assert.Equal(2, exception.ActualVersion);
        Assert.IsAssignableFrom<InvalidOperationException>(exception);
        Assert.Contains("expected version 1", exception.Message);
        Assert.Contains("actual version is 2", exception.Message);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public void InvalidTransition_DomainException_DoesNotCallSave()
    {
        var store = StoreWith(NewEvent().Cancel(ActorId, ChangedAt));
        var service = new CalendarEventLifecycleService(store);

        Assert.Throws<InvalidOperationException>(() =>
            service.Cancel(OrganizationId, EventId, 2, ActorId, LaterAt));

        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public void Operation_OnArchivedEvent_DomainException_DoesNotCallSave()
    {
        var store = StoreWith(NewEvent().Archive(ActorId, ChangedAt));
        var service = new CalendarEventLifecycleService(store);

        Assert.Throws<InvalidOperationException>(() =>
            service.UpdateDetails(
                OrganizationId, EventId, 2, ActorId, LaterAt, null, "Forbidden", null, TimedTiming()));

        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public void Save_ReceivesOriginalExpectedVersion_AndResultHasIncrementedVersion()
    {
        var store = StoreWith(NewEvent());
        var service = new CalendarEventLifecycleService(store);

        service.Cancel(OrganizationId, EventId, 1, ActorId, ChangedAt);

        Assert.Equal(1, store.LastSavedExpectedVersion);
        Assert.NotNull(store.LastSavedEvent);
        Assert.Equal(2, store.LastSavedEvent.Metadata.Version);
    }

    private static CalendarEvent NewEvent() =>
        CalendarEvent.Create(EventId, OrganizationId, CreatorId, null, "Standup", null, TimedTiming(), CreatedAt);

    private static CalendarEventTiming TimedTiming() =>
        CalendarEventTiming.CreateTimed(
            new DateOnly(2026, 8, 17),
            new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero),
            "UTC");

    private static CalendarEventTiming AllDayTiming() =>
        CalendarEventTiming.CreateAllDay(new DateOnly(2026, 8, 18), "UTC");

    private static EventAttendee NewUserAttendee() =>
        EventAttendee.Create(UserAttendeeId, CalendarAttendeeRole.Required, CalendarAttendeeResponseStatus.Pending, null);

    private static ContactAttendee NewContactAttendee() =>
        ContactAttendee.Create(ContactId, CalendarAttendeeRole.Optional, CalendarAttendeeResponseStatus.Pending, null);

    private static FakeCalendarEventStore StoreWith(CalendarEvent calendarEvent)
    {
        var store = new FakeCalendarEventStore();
        store.Add(calendarEvent);
        return store;
    }

    private sealed class FakeCalendarEventStore : ICalendarEventStore
    {
        private readonly Dictionary<(Guid OrganizationId, Guid EventId), CalendarEvent> _events = new();

        public int AddCalls { get; private set; }

        public int SaveCalls { get; private set; }

        public CalendarEvent? LastSavedEvent { get; private set; }

        public int? LastSavedExpectedVersion { get; private set; }

        public CalendarEvent? Get(Guid eventId, Guid organizationId)
        {
            _events.TryGetValue((organizationId, eventId), out var calendarEvent);
            return calendarEvent;
        }

        public void Add(CalendarEvent calendarEvent)
        {
            AddCalls++;
            _events[(calendarEvent.Metadata.OrganizationId, calendarEvent.Metadata.Id)] = calendarEvent;
        }

        public void Save(CalendarEvent calendarEvent, int expectedVersion)
        {
            SaveCalls++;
            LastSavedEvent = calendarEvent;
            LastSavedExpectedVersion = expectedVersion;
            _events[(calendarEvent.Metadata.OrganizationId, calendarEvent.Metadata.Id)] = calendarEvent;
        }
    }
}
