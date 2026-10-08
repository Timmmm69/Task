using System.Globalization;
using Task.Desktop.Personal;
using Task.Desktop.Work;
using Task.Domain.Calendar;

namespace Task.Desktop.Calendar;

public sealed record FreeTimeSlot(DateTimeOffset StartUtc, DateTimeOffset EndUtc, TimeZoneInfo Zone)
{
    public DateOnly Date => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(StartUtc, Zone).DateTime);
    public int DurationMinutes => (int)(EndUtc - StartUtc).TotalMinutes;
    public string Label => $"{TimeZoneInfo.ConvertTime(StartUtc, Zone).ToString("dddd, d MMMM yyyy HH:mm zzz", CultureInfo.GetCultureInfo("ru-RU"))} — {TimeZoneInfo.ConvertTime(EndUtc, Zone):HH:mm zzz} · {DurationMinutes} мин";
    public double Top => TimeZoneInfo.ConvertTime(StartUtc, Zone).TimeOfDay.TotalHours * 69;
    public double Height => Math.Max(24, (TimeZoneInfo.ConvertTime(EndUtc, Zone).DateTime - TimeZoneInfo.ConvertTime(StartUtc, Zone).DateTime).TotalHours * 69);
}

/// <summary>Client-only gaps over complete, permission-filtered Calendar read models.</summary>
public static class FreeTimeCalculator
{
    // The application already chooses the earlier instant in an ambiguous hour.
    // An ending boundary includes the second occurrence of that hour.
    public static DateTimeOffset Boundary(DateOnly date, TimeOnly time, TimeZoneInfo zone, bool ending = false)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        for (var i = 0; zone.IsInvalidTime(local) && i < 1440; i++) local = local.AddMinutes(1);
        if (zone.IsInvalidTime(local)) throw new ArgumentException("Рабочая граница отсутствует в этом часовом поясе.");
        return ending && zone.IsAmbiguousTime(local)
            ? new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Min()).ToUniversalTime()
            : PersonalTimePolicy.ToUtc(local, zone);
    }

    public static IReadOnlyList<FreeTimeSlot> Find(IReadOnlyList<DesktopScheduleItem> items,
        DesktopUserSettings settings, TimeZoneInfo zone, DateOnly first, DateOnly lastExclusive,
        DateTimeOffset now, int minutes, bool personal = false)
    {
        if (minutes is not (15 or 30 or 60) || lastExclusive <= first || lastExclusive.DayNumber - first.DayNumber > 7
            || !TimeOnly.TryParse(settings.WorkdayStart, out var startTime)
            || !TimeOnly.TryParse(settings.WorkdayEnd, out var endTime) || endTime <= startTime
            || settings.WeekendDays.Any(d => d is < 1 or > 7))
            throw new ArgumentException("Некорректные рабочие часы или диапазон поиска.");
        var busy = items.Where(i => !i.IsAllDay && i.Status != "cancelled" && (!personal || i.Status != "completed")
                && i.StartAtUtc.HasValue && i.EndAtUtc > i.StartAtUtc)
            .Select(i => CalendarTimelinePlacement.Timeline(i.StartAtUtc!.Value.ToUniversalTime(), i.EndAtUtc!.Value.ToUniversalTime()))
            .OrderBy(i => i.StartUtc).ToArray();
        var slots = new List<FreeTimeSlot>(3);
        // Minute precision matches the existing create editor, never rounds into the past.
        var earliest = new DateTimeOffset(((now.UtcTicks + TimeSpan.TicksPerMinute - 1) / TimeSpan.TicksPerMinute) * TimeSpan.TicksPerMinute, TimeSpan.Zero);
        var index = 0;
        for (var day = first; day < lastExclusive && slots.Count < 3; day = day.AddDays(1))
        {
            if (settings.WeekendDays.Contains((int)day.DayOfWeek == 0 ? 7 : (int)day.DayOfWeek)) continue;
            var start = Boundary(day, startTime, zone);
            var end = Boundary(day, endTime, zone, ending: true);
            if (end <= start || end <= earliest) continue;
            var period = CalendarTimelinePlacement.Timeline(start, end);
            var cursor = start > earliest ? start : earliest;
            while (index < busy.Length && busy[index].EndUtc <= cursor) index++;
            for (var i = index; i < busy.Length && busy[i].StartUtc < end && slots.Count < 3; i++)
            {
                // Reuse the canonical intersection; do not implement a second overlap policy.
                var overlap = CalendarOverlapPolicy.Evaluate(period, busy[i]);
                if (!overlap.HasOverlap) continue;
                AddGap(overlap.OverlapStartUtc!.Value);
                if (overlap.OverlapEndUtc > cursor) cursor = overlap.OverlapEndUtc.Value;
            }
            AddGap(end);
            void AddGap(DateTimeOffset gapEnd)
            {
                while (slots.Count < 3 && gapEnd - cursor >= TimeSpan.FromMinutes(minutes))
                {
                    var slotEnd = cursor.AddMinutes(minutes);
                    slots.Add(new(cursor, slotEnd, zone)); cursor = slotEnd;
                }
            }
        }
        return slots;
    }
}

public sealed record FreeTimeSearchResult(IReadOnlyList<FreeTimeSlot> Slots, bool Stale, string? Error = null,
    DesktopCalendarResult<DesktopSchedulePage>? Failure = null);

public sealed class FreeTimeSearch(IDesktopCalendarApiClient client, TimeZoneInfo zone, bool personal = false)
{
    private readonly List<DesktopSchedulePage> _cache = [];
    public void Clear() => _cache.Clear();
    public DesktopSchedulePage? CachedRange(DateTimeOffset from, DateTimeOffset to)
    {
        var covered = from;
        var pages = new List<DesktopSchedulePage>();
        foreach (var page in _cache.OrderBy(p => p.RangeStartUtc))
        {
            if (page.RangeStartUtc > covered) break;
            if (page.RangeEndUtc <= covered) continue;
            pages.Add(page); covered = page.RangeEndUtc;
            if (covered >= to) return new(pages.SelectMany(p => p.Items).DistinctBy(i => (i.ItemType, i.ObjectId)).ToArray(), from, to);
        }
        return null;
    }
    public void Remember(DesktopSchedulePage page)
    {
        // Never mix an older version of an overlapping range with a fresh page.
        _cache.RemoveAll(p => p.RangeStartUtc < page.RangeEndUtc && p.RangeEndUtc > page.RangeStartUtc);
        _cache.Add(page);
        if (_cache.Count > 32) _cache.RemoveAt(0);
    }

    public async System.Threading.Tasks.Task<FreeTimeSearchResult> SearchAsync(DesktopUserSettings settings,
        DateOnly first, DateOnly last, DateTimeOffset now, int minutes, bool online, CancellationToken token)
    {
        var from = FreeTimeCalculator.Boundary(first, TimeOnly.MinValue, zone);
        var to = FreeTimeCalculator.Boundary(last, TimeOnly.MinValue, zone);
        var pages = new List<DesktopSchedulePage>();
        var stale = !online;
        var requests = 0;
        var failure = await Read(from, to);
        if (failure is not null) return new([], stale, failure is DesktopCalendarResult<DesktopSchedulePage>.RangeTooLarge
            ? "Календарь превышает лимит даже после дробления диапазона. Свободное время определить нельзя."
            : "Недостаточно подтверждённых данных для всего диапазона. Свободное время определить нельзя.", failure);
        var slots = await System.Threading.Tasks.Task.Run(() =>
        {
            var items = pages.SelectMany(p => p.Items).DistinctBy(i => (i.ItemType, i.ObjectId)).ToArray();
            return FreeTimeCalculator.Find(items, settings, zone, first, last, now, minutes, personal);
        }, token);
        return new(slots, stale);

        async System.Threading.Tasks.Task<DesktopCalendarResult<DesktopSchedulePage>?> Read(DateTimeOffset a, DateTimeOffset b)
        {
            token.ThrowIfCancellationRequested();
            DesktopCalendarResult<DesktopSchedulePage> result = new DesktopCalendarResult<DesktopSchedulePage>.ServerUnavailable();
            // Personal reads SQLite synchronously before returning its Task; keep those reads off the UI too.
            if (online)
            {
                if (requests >= 64) return new DesktopCalendarResult<DesktopSchedulePage>.RangeTooLarge();
                requests++;
                result = await System.Threading.Tasks.Task.Run(() => client.GetScheduleAsync(a, b, zone.Id, token), token);
            }
            token.ThrowIfCancellationRequested();
            if (result is DesktopCalendarResult<DesktopSchedulePage>.Succeeded success)
            {
                if (success.Value.RangeStartUtc != a || success.Value.RangeEndUtc != b)
                    return new DesktopCalendarResult<DesktopSchedulePage>.MalformedResponse();
                Remember(success.Value); pages.Add(success.Value); return null;
            }
            // Current API uses VALIDATION_FAILED for >500 items; also accept the explicit range code.
            if (result is DesktopCalendarResult<DesktopSchedulePage>.RangeTooLarge && b - a > TimeSpan.FromHours(1))
            {
                var middle = a.AddTicks((b - a).Ticks / 2);
                return await Read(a, middle) ?? await Read(middle, b);
            }
            if (result is not DesktopCalendarResult<DesktopSchedulePage>.ServerUnavailable) return result;
            stale = true;
            if (CachedRange(a, b) is { } cached) { pages.Add(cached); return null; }
            return result;
        }
    }
}
