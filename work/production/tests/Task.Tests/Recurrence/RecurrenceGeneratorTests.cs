using Task.Domain.Recurrence;

namespace Task.Tests.Recurrence;

public sealed class RecurrenceGeneratorTests
{
    private static readonly DateOnly Start = new(2026, 8, 3);

    [Fact]
    public void GenerateDates_DailyIntervalAdvancesByIntervalDays()
    {
        var rule = Daily(interval: 2);

        var dates = RecurrenceGenerator.GenerateDates(rule, Start, new DateOnly(2026, 8, 9));

        Assert.Equal(
            new[] { new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 5), new DateOnly(2026, 8, 7), new DateOnly(2026, 8, 9) },
            dates);
    }

    [Fact]
    public void GenerateDates_DailyWorkdaysSkipsWeekends()
    {
        var rule = Daily(weekdays: [1, 2, 3, 4, 5]);

        var dates = RecurrenceGenerator.GenerateDates(rule, Start, new DateOnly(2026, 8, 9));

        Assert.Equal(
            new[]
            {
                new DateOnly(2026, 8, 3),
                new DateOnly(2026, 8, 4),
                new DateOnly(2026, 8, 5),
                new DateOnly(2026, 8, 6),
                new DateOnly(2026, 8, 7),
            },
            dates);
    }

    [Fact]
    public void GenerateDates_WeeklyEmitsSelectedWeekdaysPerIntervalWeeks()
    {
        var everyWeek = Weekly([1]);
        var fortnightly = Weekly([1, 5], interval: 2);

        Assert.Equal(
            new[]
            {
                new DateOnly(2026, 8, 3),
                new DateOnly(2026, 8, 10),
                new DateOnly(2026, 8, 17),
                new DateOnly(2026, 8, 24),
                new DateOnly(2026, 8, 31),
            },
            RecurrenceGenerator.GenerateDates(everyWeek, Start, new DateOnly(2026, 8, 31)));
        Assert.Equal(
            new[]
            {
                new DateOnly(2026, 8, 3),
                new DateOnly(2026, 8, 7),
                new DateOnly(2026, 8, 17),
                new DateOnly(2026, 8, 21),
                new DateOnly(2026, 8, 31),
            },
            RecurrenceGenerator.GenerateDates(fortnightly, Start, new DateOnly(2026, 8, 31)));
    }

    [Fact]
    public void GenerateDates_MonthlyResolvesPositiveMonthDays()
    {
        var rule = Monthly([1, 15]);

        var dates = RecurrenceGenerator.GenerateDates(rule, Start, new DateOnly(2026, 10, 31));

        Assert.Equal(
            new[]
            {
                new DateOnly(2026, 8, 15),
                new DateOnly(2026, 9, 1),
                new DateOnly(2026, 9, 15),
                new DateOnly(2026, 10, 1),
                new DateOnly(2026, 10, 15),
            },
            dates);
    }

    [Fact]
    public void GenerateDates_MonthlyResolvesNegativeMonthDaysFromMonthEnd()
    {
        var rule = Monthly([-1]);

        var dates = RecurrenceGenerator.GenerateDates(rule, Start, new DateOnly(2026, 12, 31));

        Assert.Equal(
            new[]
            {
                new DateOnly(2026, 8, 31),
                new DateOnly(2026, 9, 30),
                new DateOnly(2026, 10, 31),
                new DateOnly(2026, 11, 30),
                new DateOnly(2026, 12, 31),
            },
            dates);
    }

    [Fact]
    public void GenerateDates_MonthlySkipsInexistentDays()
    {
        var rule = Monthly([31]);
        var window = new DateOnly(2027, 1, 1);

        var dates = RecurrenceGenerator.GenerateDates(rule, window, new DateOnly(2027, 3, 31));

        Assert.Equal(new[] { new DateOnly(2027, 1, 31), new DateOnly(2027, 3, 31) }, dates);
    }

    [Fact]
    public void GenerateDates_YearlyRespectsMonthOfYearAndInterval()
    {
        var rule = Yearly(interval: 2);

        var dates = RecurrenceGenerator.GenerateDates(rule, Start, new DateOnly(2030, 12, 31));

        Assert.Equal(
            new[] { new DateOnly(2028, 3, 15), new DateOnly(2030, 3, 15) },
            dates);
    }

    [Fact]
    public void GenerateDates_HonorsUntilDateInclusive()
    {
        var rule = Daily(untilDate: new DateOnly(2026, 8, 12));

        var dates = RecurrenceGenerator.GenerateDates(rule, Start, new DateOnly(2026, 8, 31));

        Assert.Equal(new DateOnly(2026, 8, 12), dates.Last());
        Assert.Equal(10, dates.Count);
    }

    [Fact]
    public void GenerateDates_CapsAtMaximumOccurrencesCountingFromSeriesStart()
    {
        var rule = Daily(maxOccurrences: 3);

        var withinWindow = RecurrenceGenerator.GenerateDates(rule, Start, new DateOnly(2026, 8, 31));
        Assert.Equal(
            new[]
            {
                new DateOnly(2026, 8, 3),
                new DateOnly(2026, 8, 4),
                new DateOnly(2026, 8, 5),
            },
            withinWindow);

        var lateWindow = RecurrenceGenerator.GenerateDates(rule, Start.AddDays(5), new DateOnly(2026, 8, 31));
        Assert.Empty(lateWindow);
    }

    [Fact]
    public void GenerateDates_RespectsGenerationWindowStart()
    {
        var rule = Daily();

        var dates = RecurrenceGenerator.GenerateDates(rule, Start.AddDays(3), new DateOnly(2026, 8, 10));

        Assert.Equal(
            new[] { new DateOnly(2026, 8, 6), new DateOnly(2026, 8, 7), new DateOnly(2026, 8, 8), new DateOnly(2026, 8, 9), new DateOnly(2026, 8, 10) },
            dates);
    }

    [Fact]
    public void GenerateDates_IsDeterministic()
    {
        var rule = Weekly([1, 3, 5], interval: 2);

        var first = RecurrenceGenerator.GenerateDates(rule, Start, new DateOnly(2027, 1, 31));
        var second = RecurrenceGenerator.GenerateDates(rule, Start, new DateOnly(2027, 1, 31));

        Assert.Equal(first, second);
        Assert.All(first, date => Assert.True(date >= Start));
    }

    [Fact]
    public void GenerateDates_RejectsInvalidWindows()
    {
        var rule = Daily();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecurrenceGenerator.GenerateDates(rule, Start.AddDays(-1), Start));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecurrenceGenerator.GenerateDates(rule, Start, Start.AddDays(-1)));
        Assert.Throws<ArgumentNullException>(() => RecurrenceGenerator.GenerateDates(null!, Start, Start));
    }

    [Fact]
    public void GenerateDates_EnforcesOccurrenceComplexityLimit()
    {
        var rule = Daily();

        Assert.Throws<InvalidOperationException>(() =>
            RecurrenceGenerator.GenerateDates(rule, Start, new DateOnly(2029, 8, 3)));
    }

    [Fact]
    public void GenerateDates_EnforcesScanDayLimitForSparseRules()
    {
        var rule = Daily(interval: 999);

        Assert.Throws<InvalidOperationException>(() =>
            RecurrenceGenerator.GenerateDates(rule, Start, new DateOnly(2126, 8, 3)));
    }

    [Fact]
    public void GenerateMissing_DeduplicatesExistingOccurrenceKeys()
    {
        var rule = Daily();
        var existing = new[]
        {
            OccurrenceKey.FromLocalDate(new DateOnly(2026, 8, 4)),
            OccurrenceKey.FromLocalDate(new DateOnly(2026, 8, 5)),
        };

        var missing = RecurrenceGenerator.GenerateMissing(rule, Start, new DateOnly(2026, 8, 7), existing);

        Assert.Equal(
            new[] { new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 6), new DateOnly(2026, 8, 7) },
            missing);
    }

    [Fact]
    public void GenerateMissing_IsIdempotent()
    {
        var rule = Daily();
        var windowStart = Start.AddDays(3);
        var through = Start.AddDays(9);

        var first = RecurrenceGenerator.GenerateMissing(rule, windowStart, through, []);
        var merged = existing(first);

        var second = RecurrenceGenerator.GenerateMissing(rule, windowStart, through, merged);

        Assert.Empty(second);
        Assert.Equal(
            RecurrenceGenerator.GenerateMissing(rule, windowStart, through, []),
            first);
    }

    private static OccurrenceKey[] existing(IReadOnlyList<DateOnly> dates) =>
        dates.Select(OccurrenceKey.FromLocalDate).ToArray();

    private static RecurrenceRule Daily(int interval = 1, IReadOnlyList<int>? weekdays = null, int? maxOccurrences = null, DateOnly? untilDate = null) =>
        RecurrenceRule.Create(RecurrenceFrequency.Daily, interval, weekdays, null, null, Start, null, untilDate, maxOccurrences);

    private static RecurrenceRule Weekly(IReadOnlyList<int> weekdays, int interval = 1) =>
        RecurrenceRule.Create(RecurrenceFrequency.Weekly, interval, weekdays, null, null, Start, null, null, null);

    private static RecurrenceRule Monthly(IReadOnlyList<int> monthDays) =>
        RecurrenceRule.Create(RecurrenceFrequency.Monthly, 1, null, monthDays, null, Start, null, null, null);

    private static RecurrenceRule Yearly(int interval = 1) =>
        RecurrenceRule.Create(RecurrenceFrequency.Yearly, interval, null, [15], 3, Start, null, null, null);
}