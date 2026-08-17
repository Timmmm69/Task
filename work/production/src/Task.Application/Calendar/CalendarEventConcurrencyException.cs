namespace Task.Application.Calendar;

/// <summary>
/// Signals an optimistic concurrency conflict: the calendar event was
/// modified by another writer between the version the caller expected and
/// the version actually stored.
/// </summary>
public sealed class CalendarEventConcurrencyException : InvalidOperationException
{
    public CalendarEventConcurrencyException(Guid eventId, int expectedVersion, int actualVersion)
        : base(
            $"Optimistic concurrency conflict for calendar event '{eventId}': " +
            $"expected version {expectedVersion} but actual version is {actualVersion}.")
    {
        EventId = eventId;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    public Guid EventId { get; }

    public int ExpectedVersion { get; }

    public int ActualVersion { get; }
}
