namespace Task.Domain.Reminders;

public static class DeadlineReminderTime
{
    public static DateTimeOffset Anchor(DateOnly day, TimeOnly time, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(time, DateTimeKind.Unspecified);
        // Recurrence policy: move a gap forward and choose the earliest overlap instant.
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        return zone.IsAmbiguousTime(local)
            ? new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Max()).ToUniversalTime()
            : new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    public static DateTimeOffset? Due(DateTimeOffset? deadline, DateOnly? day, string? zone, string time, int offset)
        => offset < 0 ? null : (deadline ?? (day is { } date
            ? Anchor(date, TimeOnly.Parse(time), TimeZoneInfo.FindSystemTimeZoneById(zone!)) : (DateTimeOffset?)null))?.AddMinutes(-offset);
}
