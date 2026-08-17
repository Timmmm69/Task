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
}
