using Task.Application.Calendar;

namespace Task.Tests;

public sealed class ScheduleQueryServiceTests
{
    private static readonly Guid OrganizationId = Guid.Parse("d5c4b3a2-1f0e-4d9c-8b7a-6543210fedcb");
    private static readonly DateTimeOffset From = new(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 8, 21, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly EventDate = new(2026, 8, 20);

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenStoreIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new ScheduleQueryService(null!));
    }

    [Fact]
    public void GetSchedule_Throws_WhenOrganizationIdIsEmpty()
    {
        var service = NewService();

        var exception = Assert.Throws<ArgumentException>(
            () => service.GetSchedule(Guid.Empty, From, To, null, null, null, null));

        Assert.Equal("organizationId", exception.ParamName);
        Assert.Contains("Identifier must not be empty.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSchedule_Throws_WhenFromUtcHasNonUtcOffset()
    {
        var service = NewService();
        var localTimestamp = new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.FromHours(3));

        var exception = Assert.Throws<ArgumentException>(
            () => service.GetSchedule(OrganizationId, localTimestamp, To, null, null, null, null));

        Assert.Equal("fromUtc", exception.ParamName);
        Assert.Contains("Timestamps must use the UTC offset.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSchedule_Throws_WhenToUtcHasNonUtcOffset()
    {
        var service = NewService();
        var localTimestamp = new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.FromHours(3));

        var exception = Assert.Throws<ArgumentException>(
            () => service.GetSchedule(OrganizationId, From, localTimestamp, null, null, null, null));

        Assert.Equal("toUtc", exception.ParamName);
        Assert.Contains("Timestamps must use the UTC offset.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSchedule_Throws_WhenRangeIsEmptyOrReversed()
    {
        var service = NewService();

        var empty = Assert.Throws<ArgumentException>(
            () => service.GetSchedule(OrganizationId, From, From, null, null, null, null));
        Assert.Equal("toUtc", empty.ParamName);
        Assert.Contains("The schedule range must be non-empty.", empty.Message, StringComparison.Ordinal);

        var reversed = Assert.Throws<ArgumentException>(
            () => service.GetSchedule(OrganizationId, To, From, null, null, null, null));
        Assert.Equal("toUtc", reversed.ParamName);
        Assert.Contains("The schedule range must be non-empty.", reversed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSchedule_Throws_WhenRangeExceeds366Days()
    {
        var service = NewService();
        var farEnd = From.AddDays(367);

        var exception = Assert.Throws<ArgumentException>(
            () => service.GetSchedule(OrganizationId, From, farEnd, null, null, null, null));

        Assert.Equal("toUtc", exception.ParamName);
        Assert.Contains("The schedule range must not exceed 366 days.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSchedule_Throws_WhenTimeZoneIsUnknown()
    {
        var service = NewService();

        var exception = Assert.Throws<ArgumentException>(
            () => service.GetSchedule(OrganizationId, From, To, "Mars/Olympus", null, null, null));

        Assert.Equal("timezoneId", exception.ParamName);
        Assert.Contains("The time zone identifier must resolve to a known time zone.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSchedule_Throws_WhenUsersContainsEmptyIdentifier()
    {
        var service = NewService();

        var exception = Assert.Throws<ArgumentException>(
            () => service.GetSchedule(OrganizationId, From, To, null, new[] { Guid.NewGuid(), Guid.Empty }, null, null));

        Assert.Equal("users", exception.ParamName);
        Assert.Contains("Identifier must not be empty.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSchedule_Throws_WhenProjectsContainsEmptyIdentifier()
    {
        var service = NewService();

        var exception = Assert.Throws<ArgumentException>(
            () => service.GetSchedule(OrganizationId, From, To, null, null, new[] { Guid.Empty }, null));

        Assert.Equal("projects", exception.ParamName);
        Assert.Contains("Identifier must not be empty.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetConflicts_Throws_WhenOrganizationIdIsEmpty()
    {
        var service = NewService();

        var exception = Assert.Throws<ArgumentException>(
            () => service.GetConflicts(Guid.Empty, From, To, null, null, Guid.NewGuid()));

        Assert.Equal("organizationId", exception.ParamName);
        Assert.Contains("Identifier must not be empty.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetConflicts_Throws_WhenFromUtcHasNonUtcOffset()
    {
        var service = NewService();
        var localTimestamp = new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.FromHours(3));

        var exception = Assert.Throws<ArgumentException>(
            () => service.GetConflicts(OrganizationId, localTimestamp, To, null, null, Guid.NewGuid()));

        Assert.Equal("fromUtc", exception.ParamName);
        Assert.Contains("Timestamps must use the UTC offset.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetConflicts_Throws_WhenToUtcHasNonUtcOffset()
    {
        var service = NewService();
        var localTimestamp = new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.FromHours(3));

        var exception = Assert.Throws<ArgumentException>(
            () => service.GetConflicts(OrganizationId, From, localTimestamp, null, null, Guid.NewGuid()));

        Assert.Equal("toUtc", exception.ParamName);
        Assert.Contains("Timestamps must use the UTC offset.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetConflicts_Throws_WhenRangeIsEmptyOrExceeds366Days()
    {
        var service = NewService();

        var empty = Assert.Throws<ArgumentException>(
            () => service.GetConflicts(OrganizationId, From, From, null, null, Guid.NewGuid()));
        Assert.Contains("The schedule range must be non-empty.", empty.Message, StringComparison.Ordinal);

        var oversized = Assert.Throws<ArgumentException>(
            () => service.GetConflicts(OrganizationId, From, From.AddDays(367), null, null, Guid.NewGuid()));
        Assert.Contains("The schedule range must not exceed 366 days.", oversized.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetConflicts_Throws_WhenTimeZoneIsUnknown()
    {
        var service = NewService();

        var exception = Assert.Throws<ArgumentException>(
            () => service.GetConflicts(OrganizationId, From, To, "Mars/Olympus", null, Guid.NewGuid()));

        Assert.Equal("timezoneId", exception.ParamName);
        Assert.Contains("The time zone identifier must resolve to a known time zone.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetConflicts_Throws_WhenUsersContainsEmptyIdentifier()
    {
        var service = NewService();

        var exception = Assert.Throws<ArgumentException>(
            () => service.GetConflicts(OrganizationId, From, To, null, new[] { Guid.NewGuid(), Guid.Empty }, Guid.NewGuid()));

        Assert.Equal("users", exception.ParamName);
        Assert.Contains("Identifier must not be empty.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetConflicts_Throws_WhenExcludeObjectIdIsEmpty()
    {
        var service = NewService();

        var exception = Assert.Throws<ArgumentException>(
            () => service.GetConflicts(OrganizationId, From, To, null, null, Guid.Empty));

        Assert.Equal("excludeObjectId", exception.ParamName);
        Assert.Contains("Identifier must not be empty.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSchedule_IncludesAllDayEvent_WhenDayStartsExactlyAtWindowStart()
    {
        var service = NewService(
            AllDayEventRow(Guid.NewGuid(), EventDate, "Pacific/Kiritimati"));

        var page = service.GetSchedule(OrganizationId, From, To, null, null, null, null);

        var item = Assert.Single(page.Items);
        Assert.Equal(ScheduleItemType.CalendarEvent, item.ItemType);
        Assert.Equal(EventDate, item.LocalDate);
        Assert.True(item.IsAllDay);
        Assert.Null(item.StartAtUtc);
        Assert.Null(item.EndAtUtc);
    }

    [Fact]
    public void GetSchedule_ExcludesAllDayEvent_WhenDayStartsAtWindowEnd()
    {
        var service = NewService(
            AllDayEventRow(Guid.NewGuid(), EventDate, "Pacific/Kiritimati"));
        var windowEnd = new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);
        var windowStart = new DateTimeOffset(2026, 8, 18, 0, 0, 0, TimeSpan.Zero);

        var page = service.GetSchedule(OrganizationId, windowStart, windowEnd, null, null, null, null);

        Assert.Empty(page.Items);
    }

    [Fact]
    public void GetSchedule_PassesTimedEventsAndTaskPointsThroughWithoutRefiltering()
    {
        var timedId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var intervalId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var startOnlyId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var deadlineOnlyId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var projectId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var service = NewService(
            TimedEventRow(timedId, new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero), projectId),
            TaskRow(intervalId, new DateTimeOffset(2026, 8, 20, 9, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 20, 11, 0, 0, TimeSpan.Zero), ScheduleItemPriority.High),
            TaskRow(startOnlyId, new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero), null, ScheduleItemPriority.Low, "cancelled"),
            TaskRow(deadlineOnlyId, null, new DateTimeOffset(2026, 8, 20, 13, 0, 0, TimeSpan.Zero)));

        var page = service.GetSchedule(OrganizationId, From, To, null, null, null, null);

        Assert.Equal(4, page.Items.Count);
        var timed = page.Items[0];
        Assert.Equal(timedId, timed.ObjectId);
        Assert.Equal(ScheduleItemType.CalendarEvent, timed.ItemType);
        Assert.Equal(EventDate, timed.LocalDate);
        Assert.Equal(new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero), timed.StartAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero), timed.EndAtUtc);
        Assert.Equal(projectId, timed.ProjectId);
        Assert.Equal("scheduled", timed.Status);
        Assert.Null(timed.Priority);
        Assert.False(timed.IsAllDay);

        var interval = page.Items[1];
        Assert.Equal(intervalId, interval.ObjectId);
        Assert.Equal(ScheduleItemType.Task, interval.ItemType);
        Assert.Equal(ScheduleItemPriority.High, interval.Priority);
        Assert.Equal("new", interval.Status);

        var startOnly = page.Items[2];
        Assert.Equal(startOnlyId, startOnly.ObjectId);
        Assert.Equal("cancelled", startOnly.Status);
        Assert.Equal(ScheduleItemPriority.Low, startOnly.Priority);

        var deadlineOnly = page.Items[3];
        Assert.Equal(deadlineOnlyId, deadlineOnly.ObjectId);
        Assert.Null(deadlineOnly.Priority);
    }

    [Fact]
    public void GetSchedule_ExcludesTaskWithEmptyInterval()
    {
        var zeroId = Guid.NewGuid();
        var service = NewService(
            TaskRow(zeroId, new DateTimeOffset(2026, 8, 20, 9, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 20, 9, 30, 0, TimeSpan.Zero)));

        var page = service.GetSchedule(OrganizationId, From, To, null, null, null, null);

        Assert.Empty(page.Items);
    }

    [Fact]
    public void GetSchedule_ComputesTaskLocalDateInRequestedTimeZone()
    {
        var taskId = Guid.NewGuid();
        var service = NewService(
            TaskRow(taskId, new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero), null));

        var minsk = service.GetSchedule(OrganizationId, From, To, "Europe/Minsk", null, null, null);
        Assert.Equal(new DateOnly(2026, 8, 20), minsk.Items[0].LocalDate);

        var utc = service.GetSchedule(OrganizationId, From, To, null, null, null, null);
        Assert.Equal(new DateOnly(2026, 8, 20), utc.Items[0].LocalDate);

        var whitespace = service.GetSchedule(OrganizationId, From, To, "  ", null, null, null);
        Assert.Equal(new DateOnly(2026, 8, 20), whitespace.Items[0].LocalDate);

        var midway = service.GetSchedule(OrganizationId, From, To, "Pacific/Midway", null, null, null);
        Assert.Equal(new DateOnly(2026, 8, 19), midway.Items[0].LocalDate);
    }

    [Fact]
    public void GetSchedule_SortsByIntervalStartItemTypeAndObjectId()
    {
        var allDayId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var taskAtNineId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var eventBId = Guid.Parse("00000000-0000-0000-0000-000000000000");
        var eventAId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var intervalId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var deadlineOnlyId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var service = NewService(
            TaskRow(deadlineOnlyId, null, new DateTimeOffset(2026, 8, 20, 11, 0, 0, TimeSpan.Zero)),
            TimedEventRow(eventAId, new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 20, 9, 30, 0, TimeSpan.Zero)),
            TaskRow(intervalId, new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 20, 11, 30, 0, TimeSpan.Zero)),
            AllDayEventRow(allDayId, EventDate, "Pacific/Kiritimati"),
            TaskRow(taskAtNineId, new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero), null),
            TimedEventRow(eventBId, new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 20, 9, 30, 0, TimeSpan.Zero)));

        var page = service.GetSchedule(OrganizationId, From, To, null, null, null, null);

        Assert.Equal(
            new[] { allDayId, taskAtNineId, eventBId, eventAId, intervalId, deadlineOnlyId },
            page.Items.Select(i => i.ObjectId).ToArray());
        Assert.Null(page.NextCursor);
        Assert.Equal(From, page.RangeStart);
        Assert.Equal(To, page.RangeEnd);
    }

    [Fact]
    public void GetSchedule_PassesFiltersToStoreUnchanged()
    {
        var eventForUserOneId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var eventForUserTwoId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var unassignedTaskId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var users = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var projects = new[] { Guid.NewGuid() };
        var store = new FakeScheduleStore(
            new[] { TimedEventRow(unassignedTaskId, new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero)) },
            new Dictionary<Guid, IReadOnlyList<ScheduleItemRow>>
            {
                [users[0]] = new[] { TimedEventRow(eventForUserOneId, new DateTimeOffset(2026, 8, 20, 13, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 20, 14, 0, 0, TimeSpan.Zero)) },
                [users[1]] = new[] { TimedEventRow(eventForUserTwoId, new DateTimeOffset(2026, 8, 20, 14, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 20, 15, 0, 0, TimeSpan.Zero)) },
            });
        var service = new ScheduleQueryService(store);

        var page = service.GetSchedule(OrganizationId, From, To, null, users, projects, "scheduled");

        Assert.Equal(new[] { eventForUserOneId, eventForUserTwoId }, page.Items.Select(i => i.ObjectId).ToArray());
        var call = Assert.Single(store.Calls);
        Assert.Equal(OrganizationId, call.OrganizationId);
        Assert.Equal(From, call.FromUtc);
        Assert.Equal(To, call.ToUtc);
        Assert.Same(users, call.Users);
        Assert.Same(projects, call.Projects);
        Assert.Equal("scheduled", call.Status);
    }

    [Fact]
    public void GetSchedule_ReturnsAllRows_WhenFilterListsAreEmpty()
    {
        var taskId = Guid.NewGuid();
        var store = new FakeScheduleStore(
            new[] { TaskRow(taskId, new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero), null) },
            new Dictionary<Guid, IReadOnlyList<ScheduleItemRow>>());
        var service = new ScheduleQueryService(store);

        var page = service.GetSchedule(OrganizationId, From, To, null, Array.Empty<Guid>(), Array.Empty<Guid>(), null);

        Assert.Equal(taskId, Assert.Single(page.Items).ObjectId);
        var call = Assert.Single(store.Calls);
        Assert.Same(Array.Empty<Guid>(), call.Users);
        Assert.Same(Array.Empty<Guid>(), call.Projects);
    }

    [Fact]
    public void GetConflicts_ReturnsWarning_ForTenMinuteOverlap()
    {
        var firstId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var service = NewService(
            TimedEventRow(firstId, new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 20, 9, 40, 0, TimeSpan.Zero)),
            TimedEventRow(secondId, new DateTimeOffset(2026, 8, 20, 9, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero)));

        var conflicts = service.GetConflicts(OrganizationId, ConflictFrom, ConflictTo, null, null, null);

        var conflict = Assert.Single(conflicts);
        Assert.Equal(firstId, conflict.LeftObjectId);
        Assert.Equal(secondId, conflict.RightObjectId);
        Assert.Equal(new DateTimeOffset(2026, 8, 20, 9, 30, 0, TimeSpan.Zero), conflict.OverlapStart);
        Assert.Equal(new DateTimeOffset(2026, 8, 20, 9, 40, 0, TimeSpan.Zero), conflict.OverlapEnd);
        Assert.Equal(ScheduleConflictSeverity.Warning, conflict.Severity);
    }

    [Fact]
    public void GetConflicts_ReturnsBlocking_ForExactlyThirtyMinuteOverlap()
    {
        var service = NewService(
            TimedEventRow(Guid.NewGuid(), new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero)),
            TimedEventRow(Guid.NewGuid(), new DateTimeOffset(2026, 8, 20, 9, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 20, 10, 30, 0, TimeSpan.Zero)));

        var conflicts = service.GetConflicts(OrganizationId, ConflictFrom, ConflictTo, null, null, null);

        var conflict = Assert.Single(conflicts);
        Assert.Equal(new DateTimeOffset(2026, 8, 20, 9, 30, 0, TimeSpan.Zero), conflict.OverlapStart);
        Assert.Equal(new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero), conflict.OverlapEnd);
        Assert.Equal(ScheduleConflictSeverity.Blocking, conflict.Severity);
    }

    [Fact]
    public void GetConflicts_ReturnsBlocking_ForSixtyMinuteOverlap()
    {
        var service = NewService(
            TimedEventRow(Guid.NewGuid(), new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero)),
            TimedEventRow(Guid.NewGuid(), new DateTimeOffset(2026, 8, 20, 9, 15, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 20, 10, 15, 0, TimeSpan.Zero)));

        var conflicts = service.GetConflicts(OrganizationId, ConflictFrom, ConflictTo, null, null, null);

        var conflict = Assert.Single(conflicts);
        Assert.Equal(ScheduleConflictSeverity.Blocking, conflict.Severity);
    }

    [Fact]
    public void GetConflicts_DetectsAllDayVersusTimedOverlap_WithObjectIdTieBreak()
    {
        var timedId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var allDayId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var service = NewService(
            AllDayEventRow(allDayId, EventDate, "Pacific/Kiritimati"),
            TimedEventRow(timedId, new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 19, 11, 0, 0, TimeSpan.Zero)));

        var conflicts = service.GetConflicts(OrganizationId, From, To, null, null, null);

        var conflict = Assert.Single(conflicts);
        Assert.Equal(timedId, conflict.LeftObjectId);
        Assert.Equal(allDayId, conflict.RightObjectId);
        Assert.Equal(new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.Zero), conflict.OverlapStart);
        Assert.Equal(new DateTimeOffset(2026, 8, 19, 11, 0, 0, TimeSpan.Zero), conflict.OverlapEnd);
        Assert.Equal(ScheduleConflictSeverity.Blocking, conflict.Severity);
    }

    [Fact]
    public void GetConflicts_IgnoresPointsAndZeroDurationItems()
    {
        var timedId = Guid.NewGuid();
        var zeroDurationEventId = Guid.NewGuid();
        var service = NewService(
            TimedEventRow(timedId, new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero)),
            TaskRow(Guid.NewGuid(), new DateTimeOffset(2026, 8, 20, 9, 30, 0, TimeSpan.Zero), null),
            TaskRow(Guid.NewGuid(), null, new DateTimeOffset(2026, 8, 20, 9, 15, 0, TimeSpan.Zero)),
            TaskRow(Guid.NewGuid(), new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero)),
            TaskRow(Guid.NewGuid(), new DateTimeOffset(2026, 8, 20, 9, 45, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 20, 9, 45, 0, TimeSpan.Zero)),
            TimedEventRow(zeroDurationEventId, new DateTimeOffset(2026, 8, 20, 9, 20, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 20, 9, 20, 0, TimeSpan.Zero)));

        var conflicts = service.GetConflicts(OrganizationId, ConflictFrom, ConflictTo, null, null, null);

        Assert.Empty(conflicts);
    }

    [Fact]
    public void GetConflicts_ExcludesPairsWithExcludeObjectId()
    {
        var firstId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var thirdId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var rows = new[]
        {
            TimedEventRow(firstId, new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 20, 9, 30, 0, TimeSpan.Zero)),
            TimedEventRow(secondId, new DateTimeOffset(2026, 8, 20, 9, 15, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 20, 9, 45, 0, TimeSpan.Zero)),
            TimedEventRow(thirdId, new DateTimeOffset(2026, 8, 20, 9, 20, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero)),
        };
        var service = NewService(rows);

        var all = service.GetConflicts(OrganizationId, ConflictFrom, ConflictTo, null, null, null);
        Assert.Equal(3, all.Count);

        var excluded = service.GetConflicts(OrganizationId, ConflictFrom, ConflictTo, null, null, secondId);

        var conflict = Assert.Single(excluded);
        Assert.Equal(firstId, conflict.LeftObjectId);
        Assert.Equal(thirdId, conflict.RightObjectId);
    }

    [Fact]
    public void GetConflicts_OrdersResultsByOverlapStart()
    {
        var earlyLeftId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var earlyRightId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var lateLeftId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var lateRightId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var service = NewService(
            TimedEventRow(lateLeftId, new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero)),
            TimedEventRow(earlyLeftId, new DateTimeOffset(2026, 8, 20, 8, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero)),
            TimedEventRow(lateRightId, new DateTimeOffset(2026, 8, 20, 9, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 20, 10, 30, 0, TimeSpan.Zero)),
            TimedEventRow(earlyRightId, new DateTimeOffset(2026, 8, 20, 8, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero)));

        var conflicts = service.GetConflicts(OrganizationId, ConflictFrom, ConflictTo, null, null, null);

        Assert.Equal(2, conflicts.Count);
        Assert.Equal((earlyLeftId, earlyRightId), (conflicts[0].LeftObjectId, conflicts[0].RightObjectId));
        Assert.Equal((lateLeftId, lateRightId), (conflicts[1].LeftObjectId, conflicts[1].RightObjectId));
    }

    [Fact]
    public void GetConflicts_QueriesWithoutProjectOrStatusFilter()
    {
        var store = new FakeScheduleStore(
            new[] { TimedEventRow(Guid.NewGuid(), new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero)) },
            new Dictionary<Guid, IReadOnlyList<ScheduleItemRow>>());
        var service = new ScheduleQueryService(store);
        var users = new[] { Guid.NewGuid() };

        service.GetConflicts(OrganizationId, ConflictFrom, ConflictTo, null, users, null);

        var call = Assert.Single(store.Calls);
        Assert.Same(users, call.Users);
        Assert.Null(call.Projects);
        Assert.Null(call.Status);
    }

    private static readonly DateTimeOffset ConflictFrom = new(2026, 8, 20, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ConflictTo = new(2026, 8, 20, 11, 0, 0, TimeSpan.Zero);

    private static ScheduleQueryService NewService(params ScheduleItemRow[] rows) =>
        new(new FakeScheduleStore(rows, new Dictionary<Guid, IReadOnlyList<ScheduleItemRow>>()));

    private static ScheduleItemRow TimedEventRow(
        Guid id,
        DateTimeOffset start,
        DateTimeOffset end,
        Guid? projectId = null) =>
        new(id, ScheduleItemType.CalendarEvent, "Timed event", EventDate, false, start, end, "Europe/Minsk", projectId, "scheduled", null);

    private static ScheduleItemRow AllDayEventRow(Guid id, DateOnly eventDate, string timeZoneId) =>
        new(id, ScheduleItemType.CalendarEvent, "All day event", eventDate, true, null, null, timeZoneId, null, "scheduled", null);

    private static ScheduleItemRow TaskRow(
        Guid id,
        DateTimeOffset? start,
        DateTimeOffset? deadline,
        ScheduleItemPriority? priority = null,
        string status = "new") =>
        new(id, ScheduleItemType.Task, "Task", null, false, start, deadline, null, null, status, priority);

    private sealed class FakeScheduleStore : IScheduleStore
    {
        private readonly IReadOnlyList<ScheduleItemRow> _rows;
        private readonly IReadOnlyDictionary<Guid, IReadOnlyList<ScheduleItemRow>> _rowsByUser;

        public FakeScheduleStore(
            IReadOnlyList<ScheduleItemRow> rows,
            IReadOnlyDictionary<Guid, IReadOnlyList<ScheduleItemRow>> rowsByUser)
        {
            _rows = rows;
            _rowsByUser = rowsByUser;
        }

        public List<QueryCall> Calls { get; } = new();

        public IReadOnlyList<ScheduleItemRow> QuerySchedule(
            Guid organizationId,
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            IReadOnlyList<Guid>? users,
            IReadOnlyList<Guid>? projects,
            string? status)
        {
            Calls.Add(new QueryCall(organizationId, fromUtc, toUtc, users, projects, status));

            if (users is null || users.Count == 0)
            {
                return _rows;
            }

            var rows = new List<ScheduleItemRow>();
            foreach (var user in users)
            {
                if (_rowsByUser.TryGetValue(user, out var userRows))
                {
                    rows.AddRange(userRows);
                }
            }

            return rows;
        }
    }

    private sealed record QueryCall(
        Guid OrganizationId,
        DateTimeOffset FromUtc,
        DateTimeOffset ToUtc,
        IReadOnlyList<Guid>? Users,
        IReadOnlyList<Guid>? Projects,
        string? Status);
}
