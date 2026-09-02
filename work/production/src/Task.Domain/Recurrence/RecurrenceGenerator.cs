namespace Task.Domain.Recurrence;

/// <summary>
/// Deterministic occurrence-date generator. Given the same rule and window it
/// always produces the same dates, so regenerating an identical set never
/// creates duplicates (BR-041, AC-041). A window that would materialize more
/// occurrences than the complexity limit is rejected instead of exploding
/// (architecture §13.5, "recurrence complexity/occurrence limit").
/// </summary>
public static class RecurrenceGenerator
{
    /// <summary>
    /// Upper bound of occurrences a single generation window may produce.
    /// Aligns with the OpenAPI preview limit (50) x 10, the
    /// <c>RecurrencePreviewRequest.limit</c> maximum of 500, and the
    /// <c>RecurrenceChangeResult.changedTaskIds</c> bound of 500.
    /// </summary>
    public const int MaxOccurrencesPerWindow = 500;

    /// <summary>Hard bound on a single rule scan, roughly a 100-year daily horizon.</summary>
    public const int MaxScanDays = 36_500;

    /// <summary>
    /// Computes the deterministic sequence of occurrence dates of a rule over
    /// the window <paramref name="windowStart"/>..<paramref name="throughDate"/>.
    /// Occurrences are counted from the rule start date, so a capped rule
    /// (<see cref="RecurrenceRule.MaxOccurrences"/>) yields the same prefix
    /// regardless of the window size.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The window would produce more than <see cref="MaxOccurrencesPerWindow"/>
    /// occurrences or scan more than <see cref="MaxScanDays"/> days.
    /// </exception>
    public static IReadOnlyList<DateOnly> GenerateDates(RecurrenceRule rule, DateOnly windowStart, DateOnly throughDate)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (windowStart < rule.OccurrenceStartDate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(windowStart),
                "The window start must not be earlier than the occurrence start date.");
        }

        if (throughDate < windowStart)
        {
            throw new ArgumentOutOfRangeException(nameof(throughDate), "The horizon must not be earlier than the window start.");
        }

        var scanEnd = rule.UntilDate is not null && rule.UntilDate.Value < throughDate ? rule.UntilDate.Value : throughDate;
        if (scanEnd < rule.OccurrenceStartDate)
        {
            return [];
        }

        var spanDays = scanEnd.DayNumber - rule.OccurrenceStartDate.DayNumber;
        if (spanDays > MaxScanDays)
        {
            throw new InvalidOperationException(
                $"Recurrence complexity limit exceeded: the rule scans more than {MaxScanDays} days.");
        }

        var result = new List<DateOnly>();
        var occurrenceCount = 0;
        var capped = false;

        switch (rule.Frequency)
        {
            case RecurrenceFrequency.Daily:
                for (var day = rule.OccurrenceStartDate; day <= scanEnd; day = day.AddDays(1))
                {
                    if (!MatchesDaily(rule, day))
                    {
                        continue;
                    }

                    if (!Record(day, windowStart, rule, ref occurrenceCount, ref capped, result))
                    {
                        break;
                    }
                }

                break;
            case RecurrenceFrequency.Weekly:
                for (var day = rule.OccurrenceStartDate; day <= scanEnd; day = day.AddDays(1))
                {
                    var weekIndex = (day.DayNumber - rule.OccurrenceStartDate.DayNumber) / 7;
                    if (weekIndex % rule.Interval != 0 || !rule.Weekdays.Contains(IsoWeekday(day)))
                    {
                        continue;
                    }

                    if (!Record(day, windowStart, rule, ref occurrenceCount, ref capped, result))
                    {
                        break;
                    }
                }

                break;
            case RecurrenceFrequency.Monthly:
                for (var month = rule.OccurrenceStartDate; month <= scanEnd; month = month.AddMonths(1))
                {
                    var monthIndex = (month.Year - rule.OccurrenceStartDate.Year) * 12
                        + month.Month - rule.OccurrenceStartDate.Month;
                    if (monthIndex % rule.Interval != 0)
                    {
                        continue;
                    }

                    foreach (var monthDay in rule.MonthDays)
                    {
                        var resolved = ResolveMonthDay(monthDay, month);
                        if (resolved is null || resolved.Value < rule.OccurrenceStartDate)
                        {
                            continue;
                        }

                        if (resolved.Value > scanEnd)
                        {
                            continue;
                        }

                        if (!Record(resolved.Value, windowStart, rule, ref occurrenceCount, ref capped, result))
                        {
                            capped = true;
                            break;
                        }
                    }

                    if (capped)
                    {
                        break;
                    }
                }

                break;
            case RecurrenceFrequency.Yearly:
                for (var year = rule.OccurrenceStartDate.Year; year <= scanEnd.Year; year++)
                {
                    if ((year - rule.OccurrenceStartDate.Year) % rule.Interval != 0)
                    {
                        continue;
                    }

                    foreach (var monthDay in rule.MonthDays)
                    {
                        var resolved = ResolveMonthDay(monthDay, new DateOnly(year, rule.MonthOfYear!.Value, 1));
                        if (resolved is null || resolved.Value < rule.OccurrenceStartDate || resolved.Value > scanEnd)
                        {
                            continue;
                        }

                        if (!Record(resolved.Value, windowStart, rule, ref occurrenceCount, ref capped, result))
                        {
                            capped = true;
                            break;
                        }
                    }

                    if (capped)
                    {
                        break;
                    }
                }

                break;
        }

        return result;
    }

    /// <summary>
    /// Idempotent variant of <see cref="GenerateDates"/>: dates whose
    /// occurrence key is already present in <paramref name="existingKeys"/>
    /// are skipped, so re-running generation over the same set never creates
    /// duplicates (BR-041, AC-041).
    /// </summary>
    public static IReadOnlyList<DateOnly> GenerateMissing(
        RecurrenceRule rule,
        DateOnly windowStart,
        DateOnly throughDate,
        IReadOnlyCollection<OccurrenceKey> existingKeys)
    {
        ArgumentNullException.ThrowIfNull(existingKeys);
        var dates = GenerateDates(rule, windowStart, throughDate);
        if (dates.Count == 0 || existingKeys.Count == 0)
        {
            return dates;
        }

        var existingDates = existingKeys.Select(key => key.LocalDate).ToHashSet();
        return dates.Where(date => !existingDates.Contains(date)).ToArray();
    }

    private static bool MatchesDaily(RecurrenceRule rule, DateOnly day)
    {
        var offset = day.DayNumber - rule.OccurrenceStartDate.DayNumber;
        if (offset % rule.Interval != 0)
        {
            return false;
        }

        return rule.Weekdays.Count == 0 || rule.Weekdays.Contains(IsoWeekday(day));
    }

    private static int IsoWeekday(DateOnly day) => ((int)day.DayOfWeek + 6) % 7 + 1;

    private static bool Record(
        DateOnly day,
        DateOnly windowStart,
        RecurrenceRule rule,
        ref int occurrenceCount,
        ref bool capped,
        List<DateOnly> result)
    {
        if (rule.MaxOccurrences is not null && occurrenceCount >= rule.MaxOccurrences.Value)
        {
            capped = true;
            return false;
        }

        if (day >= windowStart && result.Count >= MaxOccurrencesPerWindow)
        {
            throw new InvalidOperationException(
                $"Recurrence complexity limit exceeded: the window produces more than {MaxOccurrencesPerWindow} occurrences.");
        }

        if (day >= windowStart)
        {
            result.Add(day);
        }

        occurrenceCount++;
        return true;
    }

    private static DateOnly? ResolveMonthDay(int monthDay, DateOnly month)
    {
        var daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);
        if (monthDay > daysInMonth || -monthDay > daysInMonth)
        {
            return null;
        }

        var day = monthDay > 0 ? monthDay : daysInMonth + monthDay + 1;
        return new DateOnly(month.Year, month.Month, day);
    }
}
