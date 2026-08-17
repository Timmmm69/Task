namespace Task.Application.Calendar;

/// <summary>
/// Read-only application service for the unified calendar schedule. Never
/// mutates any aggregate: it loads raw rows through <see cref="IScheduleStore"/>
/// and projects them into <see cref="ScheduleItem"/> / <see cref="ScheduleConflict"/>
/// without lifecycle transitions. The service applies the exact window
/// semantics (all-day events use the day boundaries in their own time zone),
/// resolves task local dates in the requested time zone, sorts the page and
/// computes schedule overlaps.
/// </summary>
public sealed class ScheduleQueryService
{
    private readonly IScheduleStore _store;

    public ScheduleQueryService(IScheduleStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>
    /// Returns the unified schedule page for the window
    /// <c>[fromUtc, toUtc)</c>. Timed events and tasks are pre-filtered by the
    /// store; the service additionally applies the strict window rule to
    /// all-day events (day boundaries in the event time zone) and to
    /// zero-duration task intervals. Items are sorted by
    /// <c>(intervalStart, itemType, objectId)</c> and carry the local date in
    /// the requested time zone.
    /// </summary>
    /// <param name="organizationId">Tenant identity; must not be empty.</param>
    /// <param name="fromUtc">Window start with the UTC offset.</param>
    /// <param name="toUtc">Window end with the UTC offset; must be later than
    /// <paramref name="fromUtc"/> and no more than 366 days apart.</param>
    /// <param name="timezoneId">System time-zone identifier for task local
    /// dates; null or whitespace means UTC.</param>
    /// <param name="users">Optional attendee filter for calendar events.</param>
    /// <param name="projects">Optional project filter for calendar events.</param>
    /// <param name="status">Optional exact status filter for both tables.</param>
    public SchedulePage GetSchedule(
        Guid organizationId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        string? timezoneId,
        IReadOnlyList<Guid>? users,
        IReadOnlyList<Guid>? projects,
        string? status)
    {
        ValidateRange(organizationId, fromUtc, toUtc);
        var timeZone = ResolveTimeZone(timezoneId);
        ValidateIdentifiers(users, nameof(users));
        ValidateIdentifiers(projects, nameof(projects));

        var rows = _store.QuerySchedule(organizationId, fromUtc, toUtc, users, projects, status);
        var projections = new List<(DateTimeOffset IntervalStart, ScheduleItem Item)>(rows.Count);
        foreach (var row in rows)
        {
            var timeline = BuildTimeline(row, timeZone);
            if (timeline is null || !IsIncluded(timeline, fromUtc, toUtc))
            {
                continue;
            }

            var localDate = timeline.LocalDate ?? DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(row.StartAtUtc ?? row.EndAtUtc!.Value, timeZone).DateTime);
            projections.Add((
                timeline.IntervalStart,
                new ScheduleItem(
                    row.ObjectId,
                    row.ItemType,
                    row.Title,
                    localDate,
                    row.StartAtUtc,
                    row.EndAtUtc,
                    row.IsAllDay,
                    row.ProjectId,
                    row.Status,
                    row.Priority)));
        }

        projections.Sort(static (a, b) =>
        {
            var byStart = a.IntervalStart.CompareTo(b.IntervalStart);
            if (byStart != 0)
            {
                return byStart;
            }

            var byType = a.Item.ItemType.CompareTo(b.Item.ItemType);
            if (byType != 0)
            {
                return byType;
            }

            return a.Item.ObjectId.CompareTo(b.Item.ObjectId);
        });

        return new SchedulePage(projections.Select(p => p.Item).ToList(), null, fromUtc, toUtc);
    }

    /// <summary>
    /// Computes overlaps between positive-duration schedule items in the
    /// window <c>[fromUtc, toUtc)</c>. Point items (start-only or
    /// deadline-only tasks) and zero-duration intervals never participate.
    /// Overlaps of at least 30 minutes are blocking, shorter ones are
    /// warnings; <see cref="ScheduleConflictSeverity.Info"/> is not produced.
    /// </summary>
    /// <param name="organizationId">Tenant identity; must not be empty.</param>
    /// <param name="fromUtc">Window start with the UTC offset.</param>
    /// <param name="toUtc">Window end with the UTC offset.</param>
    /// <param name="timezoneId">System time-zone identifier for all-day event
    /// day boundaries; null or whitespace means UTC.</param>
    /// <param name="users">Optional attendee filter for calendar events.</param>
    /// <param name="excludeObjectId">Optional object identity whose pairs must
    /// be omitted; must not be empty.</param>
    public IReadOnlyList<ScheduleConflict> GetConflicts(
        Guid organizationId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        string? timezoneId,
        IReadOnlyList<Guid>? users,
        Guid? excludeObjectId)
    {
        ValidateRange(organizationId, fromUtc, toUtc);
        var timeZone = ResolveTimeZone(timezoneId);
        ValidateIdentifiers(users, nameof(users));
        if (excludeObjectId == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", nameof(excludeObjectId));
        }

        var rows = _store.QuerySchedule(organizationId, fromUtc, toUtc, users, projects: null, status: null);
        var candidates = new List<(ScheduleItemRow Row, Timeline Timeline)>(rows.Count);
        foreach (var row in rows)
        {
            var timeline = BuildTimeline(row, timeZone);
            if (timeline is null ||
                timeline.IsPoint ||
                timeline.Start >= timeline.End ||
                !IsIncluded(timeline, fromUtc, toUtc))
            {
                continue;
            }

            candidates.Add((row, timeline));
        }

        var conflicts = new List<ScheduleConflict>();
        for (var i = 0; i < candidates.Count; i++)
        {
            for (var j = i + 1; j < candidates.Count; j++)
            {
                var first = candidates[i];
                var second = candidates[j];
                var leftIsFirst = !IsLater(first.Row, first.Timeline, second.Row, second.Timeline);
                var left = leftIsFirst ? first : second;
                var right = leftIsFirst ? second : first;

                if (excludeObjectId is not null &&
                    (left.Row.ObjectId == excludeObjectId.Value || right.Row.ObjectId == excludeObjectId.Value))
                {
                    continue;
                }

                var overlapStart = left.Timeline.Start > right.Timeline.Start
                    ? left.Timeline.Start
                    : right.Timeline.Start;
                var overlapEnd = left.Timeline.End < right.Timeline.End
                    ? left.Timeline.End
                    : right.Timeline.End;
                if (overlapStart >= overlapEnd)
                {
                    continue;
                }

                conflicts.Add(new ScheduleConflict(
                    left.Row.ObjectId,
                    right.Row.ObjectId,
                    overlapStart,
                    overlapEnd,
                    overlapEnd - overlapStart >= TimeSpan.FromMinutes(30)
                        ? ScheduleConflictSeverity.Blocking
                        : ScheduleConflictSeverity.Warning));
            }
        }

        return conflicts
            .OrderBy(c => c.OverlapStart)
            .ThenBy(c => c.LeftObjectId)
            .ThenBy(c => c.RightObjectId)
            .ToList();
    }

    private static Timeline? BuildTimeline(ScheduleItemRow row, TimeZoneInfo timeZone)
    {
        if (row.ItemType == ScheduleItemType.CalendarEvent)
        {
            if (row.IsAllDay)
            {
                var dayStart = TimeZoneInfo.ConvertTimeToUtc(
                    new DateTime(
                        row.EventDate!.Value.Year,
                        row.EventDate.Value.Month,
                        row.EventDate.Value.Day,
                        0,
                        0,
                        0,
                        DateTimeKind.Unspecified),
                    TimeZoneInfo.FindSystemTimeZoneById(row.TimeZoneId!));
                return new Timeline(dayStart, dayStart.AddDays(1), dayStart, row.EventDate, IsPoint: false);
            }

            if (row.StartAtUtc is null || row.EndAtUtc is null)
            {
                return null;
            }

            return new Timeline(
                row.StartAtUtc.Value,
                row.EndAtUtc.Value,
                row.StartAtUtc.Value,
                row.EventDate,
                IsPoint: false);
        }

        if (row.StartAtUtc is not null && row.EndAtUtc is not null)
        {
            return new Timeline(
                row.StartAtUtc.Value,
                row.EndAtUtc.Value,
                row.StartAtUtc.Value,
                LocalDate: null,
                IsPoint: false);
        }

        if (row.StartAtUtc is not null)
        {
            return new Timeline(
                row.StartAtUtc.Value,
                row.StartAtUtc.Value,
                row.StartAtUtc.Value,
                LocalDate: null,
                IsPoint: true);
        }

        if (row.EndAtUtc is not null)
        {
            return new Timeline(
                row.EndAtUtc.Value,
                row.EndAtUtc.Value,
                row.EndAtUtc.Value,
                LocalDate: null,
                IsPoint: true);
        }

        return null;
    }

    private static bool IsIncluded(Timeline timeline, DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        if (timeline.IsPoint)
        {
            return fromUtc <= timeline.Start && timeline.Start < toUtc;
        }

        var overlapStart = timeline.Start > fromUtc ? timeline.Start : fromUtc;
        var overlapEnd = timeline.End < toUtc ? timeline.End : toUtc;
        return overlapStart < overlapEnd;
    }

    private static bool IsLater(
        ScheduleItemRow row,
        Timeline timeline,
        ScheduleItemRow otherRow,
        Timeline otherTimeline)
    {
        var byStart = timeline.IntervalStart.CompareTo(otherTimeline.IntervalStart);
        if (byStart != 0)
        {
            return byStart > 0;
        }

        return row.ObjectId.CompareTo(otherRow.ObjectId) > 0;
    }

    private static TimeZoneInfo ResolveTimeZone(string? timezoneId)
    {
        if (string.IsNullOrWhiteSpace(timezoneId))
        {
            return TimeZoneInfo.Utc;
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timezoneId, out var timeZone))
        {
            throw new ArgumentException(
                "The time zone identifier must resolve to a known time zone.",
                nameof(timezoneId));
        }

        return timeZone;
    }

    private static void ValidateRange(Guid organizationId, DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", nameof(organizationId));
        }

        if (fromUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamps must use the UTC offset.", nameof(fromUtc));
        }

        if (toUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamps must use the UTC offset.", nameof(toUtc));
        }

        if (fromUtc >= toUtc)
        {
            throw new ArgumentException("The schedule range must be non-empty.", nameof(toUtc));
        }

        if (toUtc - fromUtc > TimeSpan.FromDays(366))
        {
            throw new ArgumentException("The schedule range must not exceed 366 days.", nameof(toUtc));
        }
    }

    private static void ValidateIdentifiers(IReadOnlyList<Guid>? identifiers, string parameterName)
    {
        if (identifiers is null || identifiers.Count == 0)
        {
            return;
        }

        foreach (var identifier in identifiers)
        {
            if (identifier == Guid.Empty)
            {
                throw new ArgumentException("Identifier must not be empty.", parameterName);
            }
        }
    }

    private sealed record Timeline(
        DateTimeOffset Start,
        DateTimeOffset End,
        DateTimeOffset IntervalStart,
        DateOnly? LocalDate,
        bool IsPoint);
}
