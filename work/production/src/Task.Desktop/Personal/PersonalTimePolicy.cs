namespace Task.Desktop.Personal;

public static class PersonalTimePolicy
{
    public static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) throw new ArgumentException("Это время отсутствует из-за перехода часового пояса.");
        // Same overlap policy as RecurrenceService.PreviewDate: earliest instant.
        return zone.IsAmbiguousTime(local)
            ? new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Max()).ToUniversalTime()
            : new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }
}
