using System.Globalization;
using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

internal sealed record WorkdayCalendar(TimeOnly End, DayOfWeek FirstDay, IReadOnlySet<DayOfWeek> Weekends, TimeZoneInfo Zone)
{
    internal static WorkdayCalendar? From(DesktopUserSettings? settings, TimeZoneInfo zone)
    {
        if (settings is null || settings.FirstDayOfWeek is < 1 or > 7 ||
            settings.WeekendDays.Any(d => d is < 1 or > 7) ||
            !TimeOnly.TryParse(settings.WorkdayEnd, CultureInfo.InvariantCulture, out var end)) return null;
        if (settings.TimeZone is { Length: > 0 } id && !TimeZoneInfo.TryFindSystemTimeZoneById(id, out zone!)) return null;
        return new(end, (DayOfWeek)(settings.FirstDayOfWeek % 7),
            settings.WeekendDays.Select(d => (DayOfWeek)(d % 7)).ToHashSet(), zone);
    }

    internal DateOnly LocalDate(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Zone).DateTime);

    internal DateTimeOffset Instant(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        while (Zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        // End a repeated local hour only after its second occurrence.
        return TimeZoneInfo.ConvertTimeToUtc(local, Zone);
    }

    internal DateOnly? WorkDate(DateOnly date)
    {
        for (var i = 0; i < 7; i++, date = date.AddDays(-1))
            if (!Weekends.Contains(date.DayOfWeek)) return date;
        return null;
    }

    internal DateOnly? LastClosedDay(DateTimeOffset now)
    {
        var today = LocalDate(now);
        return WorkDate(now < Instant(today, End) ? today.AddDays(-1) : today);
    }

    internal (DateOnly First, DateOnly Last)? LastClosedWeek(DateTimeOffset now)
    {
        if (LastClosedDay(now) is not { } last) return null;
        var first = last.AddDays(-(((int)last.DayOfWeek - (int)FirstDay + 7) % 7));
        var end = WorkDate(first.AddDays(6))!.Value;
        return end <= last ? (first, end) : (first.AddDays(-7), end.AddDays(-7));
    }
}
