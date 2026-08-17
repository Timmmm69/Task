using Task.Domain;
using Task.Domain.Calendar;

namespace Task.Tests.Calendar;

public sealed class CalendarEventTests
{
    private static readonly Guid EventId = Guid.Parse("2d4a9f1e-7b3c-4d8e-9f0a-1b2c3d4e5f60");
    private static readonly Guid OrganizationId = Guid.Parse("e10d93fd-0ad4-44b0-a1db-e0fd62884971");
    private static readonly Guid CreatorId = Guid.Parse("ad23960f-d96b-4780-aee2-822316e3c22b");
    private static readonly Guid ActorId = Guid.Parse("ad43fc14-8080-4a24-9be1-a86410d5ae88");
    private static readonly Guid ProjectId = Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479");
    private static readonly Guid UserId1 = Guid.Parse("7e1f3b2a-4c5d-4e6f-8a90-1b2c3d4e5f61");
    private static readonly Guid UserId2 = Guid.Parse("7e1f3b2a-4c5d-4e6f-8a90-1b2c3d4e5f62");
    private static readonly Guid ContactId1 = Guid.Parse("7e1f3b2a-4c5d-4e6f-8a90-1b2c3d4e5f63");
    private static readonly Guid ContactId2 = Guid.Parse("7e1f3b2a-4c5d-4e6f-8a90-1b2c3d4e5f64");
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OccurredAt = CreatedAt.AddHours(1);

    private static readonly CalendarEventTiming TimedTiming = CalendarEventTiming.Create(
        new DateOnly(2026, 8, 17),
        isAllDay: false,
        new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 8, 17, 13, 0, 0, TimeSpan.Zero),
        "Europe/Berlin");

    private static readonly CalendarEventTiming AllDayTiming = CalendarEventTiming.CreateAllDay(
        new DateOnly(2026, 8, 17),
        "Europe/Berlin");

    private static CalendarEvent CreateScheduled() =>
        CalendarEvent.Create(EventId, OrganizationId, CreatorId, ProjectId, "Status meeting", "Agenda", TimedTiming, CreatedAt);

    private static CalendarEvent ReconstituteScheduled(int version = 1, string title = "Status meeting") =>
        CalendarEvent.Reconstitute(
            SyncableEntityMetadata.Reconstitute(
                EventId, OrganizationId, CreatorId, CreatedAt, CreatorId, CreatedAt, version, EntityLifecycleState.Active,
                null, null, null, null),
            ProjectId, title, "Agenda", TimedTiming, CalendarEventStatus.Scheduled);

    private static EventAttendee MakeUserAttendee(
        Guid userId,
        CalendarAttendeeRole role = CalendarAttendeeRole.Required,
        CalendarAttendeeResponseStatus responseStatus = CalendarAttendeeResponseStatus.Pending) =>
        EventAttendee.Create(userId, role, responseStatus, respondedAtUtc: null);

    private static ContactAttendee MakeContactAttendee(
        Guid contactId,
        CalendarAttendeeRole role = CalendarAttendeeRole.Optional,
        CalendarAttendeeResponseStatus responseStatus = CalendarAttendeeResponseStatus.Accepted) =>
        ContactAttendee.Create(contactId, role, responseStatus, respondedAtUtc: null);

    private static SyncableEntityMetadata ActiveMetadata(int version = 1) =>
        SyncableEntityMetadata.Reconstitute(
            EventId, OrganizationId, CreatorId, CreatedAt, CreatorId, CreatedAt, version, EntityLifecycleState.Active,
            null, null, null, null);

    [Fact]
    public void Create_WithTimedTiming_CreatesScheduledActiveEvent()
    {
        var e = CreateScheduled();

        Assert.Equal(EventId, e.Metadata.Id);
        Assert.Equal(OrganizationId, e.Metadata.OrganizationId);
        Assert.Equal(EntityLifecycleState.Active, e.Metadata.LifecycleState);
        Assert.Equal(1, e.Metadata.Version);
        Assert.Equal(CalendarEventStatus.Scheduled, e.Status);
        Assert.Equal(ProjectId, e.ProjectId);
        Assert.Equal("Status meeting", e.Title);
        Assert.Equal("Agenda", e.Description);
        Assert.Equal(TimedTiming, e.Timing);
    }

    [Fact]
    public void Create_WithAllDayTiming_CreatesScheduledActiveEvent()
    {
        var e = CalendarEvent.Create(EventId, OrganizationId, CreatorId, projectId: null, "Holiday", null, AllDayTiming, CreatedAt);

        Assert.Equal(CalendarEventStatus.Scheduled, e.Status);
        Assert.Equal(AllDayTiming, e.Timing);
        Assert.True(e.Timing.IsAllDay);
        Assert.Null(e.ProjectId);
        Assert.Null(e.Description);
    }

    [Fact]
    public void Create_TrimsTitle()
    {
        var e = CalendarEvent.Create(EventId, OrganizationId, CreatorId, null, "  Status meeting  ", null, TimedTiming, CreatedAt);

        Assert.Equal("Status meeting", e.Title);
    }

    [Fact]
    public void Create_RejectsEmptyId()
    {
        Assert.Throws<ArgumentException>(
            () => CalendarEvent.Create(Guid.Empty, OrganizationId, CreatorId, null, "X", null, TimedTiming, CreatedAt));
    }

    [Fact]
    public void Create_RejectsEmptyOrganizationId()
    {
        Assert.Throws<ArgumentException>(
            () => CalendarEvent.Create(EventId, Guid.Empty, CreatorId, null, "X", null, TimedTiming, CreatedAt));
    }

    [Fact]
    public void Create_RejectsEmptyCreatorId()
    {
        Assert.Throws<ArgumentException>(
            () => CalendarEvent.Create(EventId, OrganizationId, Guid.Empty, null, "X", null, TimedTiming, CreatedAt));
    }

    [Fact]
    public void Create_RejectsEmptyProjectId()
    {
        Assert.Throws<ArgumentException>(
            () => CalendarEvent.Create(EventId, OrganizationId, CreatorId, Guid.Empty, "X", null, TimedTiming, CreatedAt));
    }

    [Fact]
    public void Create_RejectsEmptyTitle()
    {
        Assert.Throws<ArgumentException>(
            () => CalendarEvent.Create(EventId, OrganizationId, CreatorId, null, "", null, TimedTiming, CreatedAt));
    }

    [Fact]
    public void Create_RejectsWhitespaceTitle()
    {
        Assert.Throws<ArgumentException>(
            () => CalendarEvent.Create(EventId, OrganizationId, CreatorId, null, "   ", null, TimedTiming, CreatedAt));
    }

    [Fact]
    public void Create_RejectsTitleOver500Characters()
    {
        Assert.Throws<ArgumentException>(
            () => CalendarEvent.Create(EventId, OrganizationId, CreatorId, null, new string('x', 501), null, TimedTiming, CreatedAt));
    }

    [Fact]
    public void Create_RejectsDescriptionOver20000Characters()
    {
        Assert.Throws<ArgumentException>(
            () => CalendarEvent.Create(EventId, OrganizationId, CreatorId, null, "X", new string('x', 20001), TimedTiming, CreatedAt));
    }

    [Fact]
    public void Create_RejectsNullTiming()
    {
        Assert.Throws<ArgumentNullException>(
            () => CalendarEvent.Create(EventId, OrganizationId, CreatorId, null, "X", null, null!, CreatedAt));
    }

    [Fact]
    public void Reconstitute_RestoresPersistedFieldsWithoutAdvancingVersion()
    {
        var e = CalendarEvent.Reconstitute(
            SyncableEntityMetadata.Reconstitute(
                EventId, OrganizationId, CreatorId, CreatedAt, ActorId, OccurredAt, 7, EntityLifecycleState.Active,
                null, null, null, null),
            ProjectId, "  Persisted event  ", "Notes", AllDayTiming, CalendarEventStatus.Cancelled);

        Assert.Equal("Persisted event", e.Title);
        Assert.Equal(7, e.Metadata.Version);
        Assert.Equal(CalendarEventStatus.Cancelled, e.Status);
        Assert.Equal(AllDayTiming, e.Timing);
        Assert.Equal(ActorId, e.Metadata.UpdatedBy);
    }

    [Fact]
    public void Reconstitute_RejectsNullMetadata()
    {
        Assert.Throws<ArgumentNullException>(
            () => CalendarEvent.Reconstitute(null!, null, "X", null, TimedTiming, CalendarEventStatus.Scheduled));
    }

    [Fact]
    public void Reconstitute_RejectsNullTiming()
    {
        Assert.Throws<ArgumentNullException>(
            () => CalendarEvent.Reconstitute(
                SyncableEntityMetadata.Reconstitute(EventId, OrganizationId, CreatorId, CreatedAt, CreatorId, CreatedAt, 1, EntityLifecycleState.Active, null, null, null, null),
                null, "X", null, null!, CalendarEventStatus.Scheduled));
    }

    [Fact]
    public void Reconstitute_RejectsEmptyProjectId()
    {
        Assert.Throws<ArgumentException>(
            () => CalendarEvent.Reconstitute(
                SyncableEntityMetadata.Reconstitute(EventId, OrganizationId, CreatorId, CreatedAt, CreatorId, CreatedAt, 1, EntityLifecycleState.Active, null, null, null, null),
                Guid.Empty, "X", null, TimedTiming, CalendarEventStatus.Scheduled));
    }

    [Fact]
    public void Reconstitute_RejectsEmptyTitle()
    {
        Assert.Throws<ArgumentException>(
            () => CalendarEvent.Reconstitute(
                SyncableEntityMetadata.Reconstitute(EventId, OrganizationId, CreatorId, CreatedAt, CreatorId, CreatedAt, 1, EntityLifecycleState.Active, null, null, null, null),
                null, "  ", null, TimedTiming, CalendarEventStatus.Scheduled));
    }

    [Fact]
    public void Reconstitute_RejectsTitleOver500Characters()
    {
        Assert.Throws<ArgumentException>(
            () => CalendarEvent.Reconstitute(
                SyncableEntityMetadata.Reconstitute(EventId, OrganizationId, CreatorId, CreatedAt, CreatorId, CreatedAt, 1, EntityLifecycleState.Active, null, null, null, null),
                null, new string('x', 501), null, TimedTiming, CalendarEventStatus.Scheduled));
    }

    [Fact]
    public void Reconstitute_RejectsDescriptionOver20000Characters()
    {
        Assert.Throws<ArgumentException>(
            () => CalendarEvent.Reconstitute(
                SyncableEntityMetadata.Reconstitute(EventId, OrganizationId, CreatorId, CreatedAt, CreatorId, CreatedAt, 1, EntityLifecycleState.Active, null, null, null, null),
                null, "X", new string('x', 20001), TimedTiming, CalendarEventStatus.Scheduled));
    }

    [Fact]
    public void Reconstitute_RejectsUndefinedStatus()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CalendarEvent.Reconstitute(
                SyncableEntityMetadata.Reconstitute(EventId, OrganizationId, CreatorId, CreatedAt, CreatorId, CreatedAt, 1, EntityLifecycleState.Active, null, null, null, null),
                null, "X", null, TimedTiming, (CalendarEventStatus)42));
    }

    [Fact]
    public void Reconstitute_RejectsArchivedLifecycleMetadata()
    {
        var metadata = SyncableEntityMetadata.Reconstitute(
            EventId, OrganizationId, CreatorId, CreatedAt, ActorId, OccurredAt, 2, EntityLifecycleState.Archived,
            null, null, null, OccurredAt);

        Assert.Throws<InvalidOperationException>(
            () => CalendarEvent.Reconstitute(metadata, null, "X", null, TimedTiming, CalendarEventStatus.Scheduled));
    }

    [Fact]
    public void Reconstitute_RejectsTrashedLifecycleMetadata()
    {
        var metadata = SyncableEntityMetadata.Reconstitute(
            EventId, OrganizationId, CreatorId, CreatedAt, ActorId, OccurredAt, 3, EntityLifecycleState.Trashed,
            EntityLifecycleState.Active, OccurredAt, ActorId, null);

        Assert.Throws<InvalidOperationException>(
            () => CalendarEvent.Reconstitute(metadata, null, "X", null, TimedTiming, CalendarEventStatus.Scheduled));
    }

    [Fact]
    public void UpdateDetails_UpdatesASingleScalarField()
    {
        var updated = CreateScheduled().UpdateDetails(ActorId, OccurredAt, ProjectId, "Sprint planning", "Agenda", TimedTiming);

        Assert.Equal("Sprint planning", updated.Title);
        Assert.Equal(ProjectId, updated.ProjectId);
        Assert.Equal("Agenda", updated.Description);
        Assert.Equal(TimedTiming, updated.Timing);
        Assert.Equal(CalendarEventStatus.Scheduled, updated.Status);
        Assert.Equal(2, updated.Metadata.Version);
        Assert.Equal(ActorId, updated.Metadata.UpdatedBy);
        Assert.Equal(OccurredAt, updated.Metadata.UpdatedAtUtc);
    }

    [Fact]
    public void UpdateDetails_UpdatesAllScalarFields()
    {
        var newProjectId = Guid.NewGuid();
        var newTiming = CalendarEventTiming.Create(
            new DateOnly(2026, 8, 18),
            isAllDay: false,
            new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 18, 11, 0, 0, TimeSpan.Zero),
            "Europe/Berlin");

        var updated = CreateScheduled().UpdateDetails(ActorId, OccurredAt, newProjectId, "Release review", "New agenda", newTiming);

        Assert.Equal(newProjectId, updated.ProjectId);
        Assert.Equal("Release review", updated.Title);
        Assert.Equal("New agenda", updated.Description);
        Assert.Equal(newTiming, updated.Timing);
    }

    [Fact]
    public void UpdateDetails_NoOpReturnsSameInstanceWithoutVersionBump()
    {
        var e = CreateScheduled();

        var result = e.UpdateDetails(ActorId, OccurredAt, ProjectId, "Status meeting", "Agenda", TimedTiming);

        Assert.Same(e, result);
        Assert.Equal(1, result.Metadata.Version);
    }

    [Fact]
    public void UpdateDetails_IncrementsVersionExactlyByOne()
    {
        var e = CreateScheduled();

        var updated = e.UpdateDetails(ActorId, OccurredAt, ProjectId, "Sprint planning", "Agenda", TimedTiming);

        Assert.Equal(e.Metadata.Version + 1, updated.Metadata.Version);
        Assert.Equal(2, updated.Metadata.Version);
    }

    [Fact]
    public void UpdateDetails_RejectsNullTiming()
    {
        Assert.Throws<ArgumentNullException>(
            () => CreateScheduled().UpdateDetails(ActorId, OccurredAt, ProjectId, "X", null, null!));
    }

    [Fact]
    public void UpdateDetails_RejectsEmptyProjectId()
    {
        Assert.Throws<ArgumentException>(
            () => CreateScheduled().UpdateDetails(ActorId, OccurredAt, Guid.Empty, "X", null, TimedTiming));
    }

    [Fact]
    public void UpdateDetails_RejectsEmptyTitle()
    {
        Assert.Throws<ArgumentException>(
            () => CreateScheduled().UpdateDetails(ActorId, OccurredAt, ProjectId, " ", null, TimedTiming));
    }

    [Fact]
    public void UpdateDetails_RejectsDescriptionOver20000Characters()
    {
        Assert.Throws<ArgumentException>(
            () => CreateScheduled().UpdateDetails(ActorId, OccurredAt, ProjectId, "X", new string('x', 20001), TimedTiming));
    }

    [Fact]
    public void Cancel_TransitionsToCancelledWithVersionBump()
    {
        var e = CreateScheduled();

        var cancelled = e.Cancel(ActorId, OccurredAt);

        Assert.Equal(CalendarEventStatus.Cancelled, cancelled.Status);
        Assert.Equal(e.Metadata.Version + 1, cancelled.Metadata.Version);
        Assert.Equal(ActorId, cancelled.Metadata.UpdatedBy);
        Assert.Equal(OccurredAt, cancelled.Metadata.UpdatedAtUtc);
    }

    [Fact]
    public void Cancel_FromCancelled_IsRejected()
    {
        var cancelled = CreateScheduled().Cancel(ActorId, OccurredAt);

        Assert.Throws<InvalidOperationException>(() => cancelled.Cancel(ActorId, OccurredAt.AddMinutes(1)));
    }

    [Fact]
    public void Reschedule_TransitionsBackToScheduledWithVersionBump()
    {
        var cancelled = CreateScheduled().Cancel(ActorId, OccurredAt);

        var rescheduled = cancelled.Reschedule(ActorId, OccurredAt.AddMinutes(5));

        Assert.Equal(CalendarEventStatus.Scheduled, rescheduled.Status);
        Assert.Equal(cancelled.Metadata.Version + 1, rescheduled.Metadata.Version);
        Assert.Equal(3, rescheduled.Metadata.Version);
    }

    [Fact]
    public void Reschedule_FromScheduled_IsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => CreateScheduled().Reschedule(ActorId, OccurredAt));
    }

    [Fact]
    public void Reschedule_FromReconstitutedCancelledEvent_IsAllowed()
    {
        var cancelled = CalendarEvent.Reconstitute(
            SyncableEntityMetadata.Reconstitute(
                EventId, OrganizationId, CreatorId, CreatedAt, ActorId, OccurredAt, 4, EntityLifecycleState.Active,
                null, null, null, null),
            ProjectId, "Status meeting", "Agenda", TimedTiming, CalendarEventStatus.Cancelled);

        var rescheduled = cancelled.Reschedule(ActorId, OccurredAt.AddMinutes(1));

        Assert.Equal(CalendarEventStatus.Scheduled, rescheduled.Status);
        Assert.Equal(5, rescheduled.Metadata.Version);
    }

    [Fact]
    public void NonActiveEvent_IsRejectedAtTheReconstitutionBoundary()
    {
        // This packet allows only an Active lifecycle ("lifecycle metadata допускает
        // only Active"). A non-active event cannot be obtained through any public
        // entry point, so Update/Cancel/Reschedule can never run on a non-active
        // instance; the guarantee is enforced at the reconstitution boundary.
        var archivedMetadata = SyncableEntityMetadata.Reconstitute(
            EventId, OrganizationId, CreatorId, CreatedAt, ActorId, OccurredAt, 2, EntityLifecycleState.Archived,
            null, null, null, OccurredAt);
        var trashedMetadata = SyncableEntityMetadata.Reconstitute(
            EventId, OrganizationId, CreatorId, CreatedAt, ActorId, OccurredAt, 3, EntityLifecycleState.Trashed,
            EntityLifecycleState.Active, OccurredAt, ActorId, null);

        Assert.Throws<InvalidOperationException>(
            () => CalendarEvent.Reconstitute(archivedMetadata, null, "X", null, TimedTiming, CalendarEventStatus.Scheduled));
        Assert.Throws<InvalidOperationException>(
            () => CalendarEvent.Reconstitute(trashedMetadata, null, "X", null, TimedTiming, CalendarEventStatus.Scheduled));
    }

    [Fact]
    public void Timing_IsPreservedAsImmutableValueObjectAcrossTransitions()
    {
        var e = CreateScheduled();
        var cancelled = e.Cancel(ActorId, OccurredAt);
        var rescheduled = cancelled.Reschedule(ActorId, OccurredAt.AddMinutes(5));

        Assert.Same(TimedTiming, e.Timing);
        Assert.Same(TimedTiming, cancelled.Timing);
        Assert.Same(TimedTiming, rescheduled.Timing);
        Assert.Equal(CalendarEventStatus.Scheduled, rescheduled.Status);

        var newTiming = CalendarEventTiming.Create(
            new DateOnly(2026, 8, 19),
            isAllDay: true,
            null,
            null,
            "Europe/Berlin");
        var updated = rescheduled.UpdateDetails(ActorId, OccurredAt.AddMinutes(10), ProjectId, "Status meeting", "Agenda", newTiming);

        Assert.Equal(newTiming, updated.Timing);
        Assert.Equal(TimedTiming, rescheduled.Timing);
    }

    [Fact]
    public void UpdateDetails_AppliesToActiveEventRegardlessOfStatus()
    {
        var cancelled = CreateScheduled().Cancel(ActorId, OccurredAt);

        var updated = cancelled.UpdateDetails(ActorId, OccurredAt.AddMinutes(1), ProjectId, "Moved notes", "Notes", TimedTiming);

        Assert.Equal(CalendarEventStatus.Cancelled, updated.Status);
        Assert.Equal("Moved notes", updated.Title);
        Assert.Equal(3, updated.Metadata.Version);
    }

    [Fact]
    public void Create_WithoutAttendees_ExposesEmptyCollections()
    {
        var e = CreateScheduled();

        Assert.Empty(e.UserAttendees);
        Assert.Empty(e.ContactAttendees);
    }

    [Fact]
    public void Reconstitute_WithoutAttendees_ExposesEmptyCollections()
    {
        var e = ReconstituteScheduled();

        Assert.Empty(e.UserAttendees);
        Assert.Empty(e.ContactAttendees);
    }

    [Fact]
    public void Create_WithAttendees_PreservesValuesAndOrder()
    {
        var userAttendees = new[]
        {
            MakeUserAttendee(UserId2, CalendarAttendeeRole.Observer, CalendarAttendeeResponseStatus.Declined),
            MakeUserAttendee(UserId1),
        };
        var contactAttendees = new[]
        {
            MakeContactAttendee(ContactId2, CalendarAttendeeRole.Required, CalendarAttendeeResponseStatus.Tentative),
            MakeContactAttendee(ContactId1),
        };

        var e = CalendarEvent.Create(
            EventId, OrganizationId, CreatorId, ProjectId, "Status meeting", "Agenda", TimedTiming, CreatedAt,
            userAttendees, contactAttendees);

        Assert.Equal(userAttendees, e.UserAttendees);
        Assert.Equal(contactAttendees, e.ContactAttendees);
    }

    [Fact]
    public void Reconstitute_WithAttendees_PreservesValuesAndOrder()
    {
        var userAttendees = new[]
        {
            MakeUserAttendee(UserId1),
            MakeUserAttendee(UserId2, CalendarAttendeeRole.Observer, CalendarAttendeeResponseStatus.Tentative),
        };
        var contactAttendees = new[]
        {
            MakeContactAttendee(ContactId1),
            MakeContactAttendee(ContactId2, CalendarAttendeeRole.Required, CalendarAttendeeResponseStatus.Declined),
        };

        var e = CalendarEvent.Reconstitute(
            ActiveMetadata(version: 4), ProjectId, "Status meeting", "Agenda", TimedTiming, CalendarEventStatus.Cancelled,
            userAttendees, contactAttendees);

        Assert.Equal(userAttendees, e.UserAttendees);
        Assert.Equal(contactAttendees, e.ContactAttendees);
    }

    [Fact]
    public void Create_RejectsNullUserAttendees()
    {
        Assert.Throws<ArgumentNullException>(
            () => CalendarEvent.Create(
                EventId, OrganizationId, CreatorId, null, "X", null, TimedTiming, CreatedAt, null!, Array.Empty<ContactAttendee>()));
    }

    [Fact]
    public void Create_RejectsNullContactAttendees()
    {
        Assert.Throws<ArgumentNullException>(
            () => CalendarEvent.Create(
                EventId, OrganizationId, CreatorId, null, "X", null, TimedTiming, CreatedAt, Array.Empty<EventAttendee>(), null!));
    }

    [Fact]
    public void Create_RejectsNullUserAttendeeElement()
    {
        Assert.Throws<ArgumentException>(
            () => CalendarEvent.Create(
                EventId, OrganizationId, CreatorId, null, "X", null, TimedTiming, CreatedAt,
                new EventAttendee[] { null! }, Array.Empty<ContactAttendee>()));
    }

    [Fact]
    public void Create_RejectsNullContactAttendeeElement()
    {
        Assert.Throws<ArgumentException>(
            () => CalendarEvent.Create(
                EventId, OrganizationId, CreatorId, null, "X", null, TimedTiming, CreatedAt,
                Array.Empty<EventAttendee>(), new ContactAttendee[] { null! }));
    }

    [Fact]
    public void Reconstitute_RejectsNullUserAttendees()
    {
        Assert.Throws<ArgumentNullException>(
            () => CalendarEvent.Reconstitute(
                ActiveMetadata(), null, "X", null, TimedTiming, CalendarEventStatus.Scheduled,
                null!, Array.Empty<ContactAttendee>()));
    }

    [Fact]
    public void Reconstitute_RejectsNullContactAttendees()
    {
        Assert.Throws<ArgumentNullException>(
            () => CalendarEvent.Reconstitute(
                ActiveMetadata(), null, "X", null, TimedTiming, CalendarEventStatus.Scheduled,
                Array.Empty<EventAttendee>(), null!));
    }

    [Fact]
    public void Create_AcceptsExactly500UserAttendees()
    {
        var userAttendees = Enumerable.Range(0, 500).Select(_ => MakeUserAttendee(Guid.NewGuid())).ToArray();

        var e = CalendarEvent.Create(
            EventId, OrganizationId, CreatorId, null, "X", null, TimedTiming, CreatedAt,
            userAttendees, Array.Empty<ContactAttendee>());

        Assert.Equal(500, e.UserAttendees.Count);
    }

    [Fact]
    public void Create_Rejects501UserAttendees()
    {
        var userAttendees = Enumerable.Range(0, 501).Select(_ => MakeUserAttendee(Guid.NewGuid())).ToArray();

        Assert.Throws<ArgumentException>(
            () => CalendarEvent.Create(
                EventId, OrganizationId, CreatorId, null, "X", null, TimedTiming, CreatedAt,
                userAttendees, Array.Empty<ContactAttendee>()));
    }

    [Fact]
    public void Create_AcceptsExactly500ContactAttendees()
    {
        var contactAttendees = Enumerable.Range(0, 500).Select(_ => MakeContactAttendee(Guid.NewGuid())).ToArray();

        var e = CalendarEvent.Create(
            EventId, OrganizationId, CreatorId, null, "X", null, TimedTiming, CreatedAt,
            Array.Empty<EventAttendee>(), contactAttendees);

        Assert.Equal(500, e.ContactAttendees.Count);
    }

    [Fact]
    public void Create_Rejects501ContactAttendees()
    {
        var contactAttendees = Enumerable.Range(0, 501).Select(_ => MakeContactAttendee(Guid.NewGuid())).ToArray();

        Assert.Throws<ArgumentException>(
            () => CalendarEvent.Create(
                EventId, OrganizationId, CreatorId, null, "X", null, TimedTiming, CreatedAt,
                Array.Empty<EventAttendee>(), contactAttendees));
    }

    [Fact]
    public void ReplaceAttendees_ChangesBothCollectionsAndBumpsVersionByExactlyOne()
    {
        var e = CreateScheduled();
        var userAttendees = new[]
        {
            MakeUserAttendee(UserId2, CalendarAttendeeRole.Observer, CalendarAttendeeResponseStatus.Declined),
            MakeUserAttendee(UserId1),
        };
        var contactAttendees = new[]
        {
            MakeContactAttendee(ContactId2, CalendarAttendeeRole.Required, CalendarAttendeeResponseStatus.Tentative),
            MakeContactAttendee(ContactId1),
        };

        var replaced = e.ReplaceAttendees(ActorId, OccurredAt, userAttendees, contactAttendees);

        Assert.Equal(userAttendees, replaced.UserAttendees);
        Assert.Equal(contactAttendees, replaced.ContactAttendees);
        Assert.Equal(e.Metadata.Version + 1, replaced.Metadata.Version);
        Assert.Equal(ActorId, replaced.Metadata.UpdatedBy);
        Assert.Equal(OccurredAt, replaced.Metadata.UpdatedAtUtc);
    }

    [Fact]
    public void ReplaceAttendees_OnCancelledEvent_IsAllowed()
    {
        var cancelled = CreateScheduled().Cancel(ActorId, OccurredAt);

        var replaced = cancelled.ReplaceAttendees(
            ActorId, OccurredAt.AddMinutes(1), new[] { MakeUserAttendee(UserId1) }, Array.Empty<ContactAttendee>());

        Assert.Equal(CalendarEventStatus.Cancelled, replaced.Status);
        Assert.Equal(3, replaced.Metadata.Version);
        Assert.Single(replaced.UserAttendees);
    }

    [Fact]
    public void ReplaceAttendees_SequenceEqual_ReturnsSameInstanceWithoutBump()
    {
        var e = CreateScheduled();
        var userAttendees = new[] { MakeUserAttendee(UserId1) };
        var contactAttendees = new[] { MakeContactAttendee(ContactId1) };
        e = e.ReplaceAttendees(ActorId, OccurredAt, userAttendees, contactAttendees);

        var result = e.ReplaceAttendees(ActorId, OccurredAt.AddMinutes(1), userAttendees, contactAttendees);

        Assert.Same(e, result);
        Assert.Equal(2, result.Metadata.Version);
    }

    [Fact]
    public void ReplaceAttendees_RejectsNullCollections()
    {
        var e = CreateScheduled();

        Assert.Throws<ArgumentNullException>(() => e.ReplaceAttendees(ActorId, OccurredAt, null!, Array.Empty<ContactAttendee>()));
        Assert.Throws<ArgumentNullException>(() => e.ReplaceAttendees(ActorId, OccurredAt, Array.Empty<EventAttendee>(), null!));
    }

    [Fact]
    public void ReplaceAttendees_RejectsNullElements()
    {
        var e = CreateScheduled();

        Assert.Throws<ArgumentException>(
            () => e.ReplaceAttendees(ActorId, OccurredAt, new EventAttendee[] { null! }, Array.Empty<ContactAttendee>()));
        Assert.Throws<ArgumentException>(
            () => e.ReplaceAttendees(ActorId, OccurredAt, Array.Empty<EventAttendee>(), new ContactAttendee[] { null! }));
    }

    [Fact]
    public void ReplaceAttendees_RejectsOver500UserAttendees()
    {
        var e = CreateScheduled();
        var userAttendees = Enumerable.Range(0, 501).Select(_ => MakeUserAttendee(Guid.NewGuid())).ToArray();

        Assert.Throws<ArgumentException>(() => e.ReplaceAttendees(ActorId, OccurredAt, userAttendees, Array.Empty<ContactAttendee>()));
    }

    [Fact]
    public void CallerMutationOfSourceLists_DoesNotAffectAggregateAfterConstruction()
    {
        var user1 = MakeUserAttendee(UserId1);
        var contact1 = MakeContactAttendee(ContactId1);
        var users = new List<EventAttendee> { user1 };
        var contacts = new List<ContactAttendee> { contact1 };

        var e = CalendarEvent.Create(
            EventId, OrganizationId, CreatorId, null, "X", null, TimedTiming, CreatedAt, users, contacts);

        users.Add(MakeUserAttendee(UserId2));
        contacts.RemoveAt(0);

        Assert.Equal(new[] { user1 }, e.UserAttendees);
        Assert.Equal(new[] { contact1 }, e.ContactAttendees);
    }

    [Fact]
    public void CallerMutationOfSourceLists_DoesNotAffectAggregateAfterReplacement()
    {
        var e = CreateScheduled();
        var user1 = MakeUserAttendee(UserId1);
        var contact1 = MakeContactAttendee(ContactId1);
        var users = new List<EventAttendee> { user1 };
        var contacts = new List<ContactAttendee> { contact1 };

        var replaced = e.ReplaceAttendees(ActorId, OccurredAt, users, contacts);

        users.Clear();
        contacts.Clear();

        Assert.Equal(new[] { user1 }, replaced.UserAttendees);
        Assert.Equal(new[] { contact1 }, replaced.ContactAttendees);
    }

    [Fact]
    public void ExposedAttendeeCollections_CannotBeMutatedByCaller()
    {
        var e = CalendarEvent.Create(
            EventId, OrganizationId, CreatorId, null, "X", null, TimedTiming, CreatedAt,
            new[] { MakeUserAttendee(UserId1) }, new[] { MakeContactAttendee(ContactId1) });

        Assert.Throws<NotSupportedException>(
            () => ((IList<EventAttendee>)e.UserAttendees).Add(MakeUserAttendee(UserId2)));
        Assert.Throws<NotSupportedException>(
            () => ((IList<ContactAttendee>)e.ContactAttendees).Add(MakeContactAttendee(ContactId2)));
    }

    [Fact]
    public void UpdateDetails_PreservesAttendeeCollections()
    {
        var e = CalendarEvent.Create(
            EventId, OrganizationId, CreatorId, ProjectId, "Status meeting", "Agenda", TimedTiming, CreatedAt,
            new[] { MakeUserAttendee(UserId1) }, new[] { MakeContactAttendee(ContactId1) });

        var updated = e.UpdateDetails(ActorId, OccurredAt, ProjectId, "Sprint planning", "Agenda", TimedTiming);

        Assert.Equal(e.UserAttendees, updated.UserAttendees);
        Assert.Equal(e.ContactAttendees, updated.ContactAttendees);
    }

    [Fact]
    public void Cancel_PreservesAttendeeCollections()
    {
        var e = CalendarEvent.Create(
            EventId, OrganizationId, CreatorId, ProjectId, "Status meeting", "Agenda", TimedTiming, CreatedAt,
            new[] { MakeUserAttendee(UserId1) }, new[] { MakeContactAttendee(ContactId1) });

        var cancelled = e.Cancel(ActorId, OccurredAt);

        Assert.Equal(e.UserAttendees, cancelled.UserAttendees);
        Assert.Equal(e.ContactAttendees, cancelled.ContactAttendees);
    }

    [Fact]
    public void Reschedule_PreservesAttendeeCollections()
    {
        var e = CalendarEvent.Create(
            EventId, OrganizationId, CreatorId, ProjectId, "Status meeting", "Agenda", TimedTiming, CreatedAt,
            new[] { MakeUserAttendee(UserId1) }, new[] { MakeContactAttendee(ContactId1) });
        var cancelled = e.Cancel(ActorId, OccurredAt);

        var rescheduled = cancelled.Reschedule(ActorId, OccurredAt.AddMinutes(5));

        Assert.Equal(e.UserAttendees, rescheduled.UserAttendees);
        Assert.Equal(e.ContactAttendees, rescheduled.ContactAttendees);
    }
}
