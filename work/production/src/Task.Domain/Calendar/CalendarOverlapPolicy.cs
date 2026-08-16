namespace Task.Domain.Calendar;

/// <summary>
/// Pure overlap policy (BR-047, AC-047). Overlap is allowed and only
/// warned about: the policy never throws for valid placements and never acts
/// as a creation/placement veto. Only Timeline placements participate;
/// all-day, date-only and none placements never cause a timeline overlap.
/// Intervals are half-open: <c>[10:00,11:00)</c> and <c>[11:00,12:00)</c> do
/// not overlap. The result is UI/API-ready without depending on UI or
/// carrying its own DTO contract.
/// </summary>
public static class CalendarOverlapPolicy
{
    /// <summary>
    /// Evaluates a pair of placements. Any non-timeline participant yields
    /// no overlap.
    /// </summary>
    public static CalendarOverlapResult Evaluate(CalendarTimelinePlacement first, CalendarTimelinePlacement second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        if (!first.IsTimeline || !second.IsTimeline)
        {
            return CalendarOverlapResult.None;
        }

        var start = later(first.StartUtc!.Value, second.StartUtc!.Value);
        var end = earlier(first.EndUtc, second.EndUtc);
        var hasOverlap = end is null || start < end.Value;

        return hasOverlap
            ? new CalendarOverlapResult(hasOverlap: true, start, end)
            : CalendarOverlapResult.None;
    }

    /// <summary>
    /// Finds all overlapping pairs in a collection. Only Timeline placements
    /// participate; date-only and none placements are ignored. The policy
    /// does not throw on overlap.
    /// </summary>
    public static IReadOnlyList<CalendarOverlapPair> FindOverlaps(IReadOnlyCollection<CalendarTimelinePlacement> placements)
    {
        ArgumentNullException.ThrowIfNull(placements);
        var timed = placements.Where(placement => placement.IsTimeline).ToArray();
        if (timed.Length < 2)
        {
            return Array.Empty<CalendarOverlapPair>();
        }

        var overlaps = new List<CalendarOverlapPair>();
        for (var i = 0; i < timed.Length - 1; i++)
        {
            for (var j = i + 1; j < timed.Length; j++)
            {
                var result = Evaluate(timed[i], timed[j]);
                if (result.HasOverlap)
                {
                    overlaps.Add(new CalendarOverlapPair(timed[i], timed[j], result));
                }
            }
        }

        return overlaps;
    }

    private static DateTimeOffset later(DateTimeOffset left, DateTimeOffset right) =>
        left >= right ? left : right;

    private static DateTimeOffset? earlier(DateTimeOffset? left, DateTimeOffset? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return left.Value <= right.Value ? left : right;
    }
}

/// <summary>
/// Result of an overlap evaluation: whether an overlap was found and the
/// half-open intersection interval. Suitable for UI/API warning rendering.
/// </summary>
public readonly record struct CalendarOverlapResult
{
    public CalendarOverlapResult(bool hasOverlap, DateTimeOffset? overlapStartUtc, DateTimeOffset? overlapEndUtc)
    {
        HasOverlap = hasOverlap;
        OverlapStartUtc = overlapStartUtc;
        OverlapEndUtc = overlapEndUtc;
    }

    public bool HasOverlap { get; }

    /// <summary>Start of the half-open intersection; null when no overlap.</summary>
    public DateTimeOffset? OverlapStartUtc { get; }

    /// <summary>Exclusive end of the intersection; null when no overlap or open-ended.</summary>
    public DateTimeOffset? OverlapEndUtc { get; }

    public static CalendarOverlapResult None => new(hasOverlap: false, null, null);
}

/// <summary>An overlapping pair found by <see cref="CalendarOverlapPolicy.FindOverlaps"/>.</summary>
public sealed record CalendarOverlapPair(
    CalendarTimelinePlacement First,
    CalendarTimelinePlacement Second,
    CalendarOverlapResult Overlap);