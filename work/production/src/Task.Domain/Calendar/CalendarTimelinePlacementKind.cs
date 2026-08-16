namespace Task.Domain.Calendar;

/// <summary>
/// Kind of a calendar day/range placement. Timeline items occupy a
/// half-open UTC interval on the time axis; DateOnly items live on a
/// calendar date without a timeline interval; None has no calendar
/// temporal placement at all (BR-048, AC-048).
/// </summary>
public enum CalendarTimelinePlacementKind
{
    /// <summary>No calendar temporal placement (e.g. a task without a start).</summary>
    None = 0,

    /// <summary>Placed on the timeline as a half-open UTC interval.</summary>
    Timeline = 1,

    /// <summary>Date-based placement without a timeline interval.</summary>
    DateOnly = 2,
}