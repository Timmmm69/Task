using Task.Domain.Recurrence;

namespace Task.Tests.Recurrence;

public sealed class RecurrenceRuleTests
{
    private static readonly DateOnly Start = new(2026, 8, 3);

    [Fact]
    public void Create_AcceptsAllSupportedCombinations()
    {
        Assert.NotNull(Daily());
        Assert.NotNull(Daily(weekdays: [1, 2, 3, 4, 5]));
        Assert.NotNull(Weekly(weekdays: [1, 3, 5]));
        Assert.NotNull(Monthly(monthDays: [1, 15]));
        Assert.NotNull(Monthly(monthDays: [-1]));
        Assert.NotNull(Yearly());
        Assert.NotNull(Daily(untilDate: new DateOnly(2026, 8, 31)));
        Assert.NotNull(Daily(maxOccurrences: 5));
    }

    [Fact]
    public void Create_RejectsUnknownFrequency()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecurrenceRule.Create((RecurrenceFrequency)99, 1, null, null, null, Start, null, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecurrenceRule.Create((RecurrenceFrequency)(-1), 1, null, null, null, Start, null, null, null));
    }

    [Fact]
    public void Create_RejectsIntervalOutsideOneToNineHundredNinetyNine()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecurrenceRule.Create(RecurrenceFrequency.Daily, 0, null, null, null, Start, null, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecurrenceRule.Create(RecurrenceFrequency.Daily, -1, null, null, null, Start, null, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecurrenceRule.Create(RecurrenceFrequency.Daily, 1000, null, null, null, Start, null, null, null));
        Assert.Equal(999, Daily(interval: 999).Interval);
    }

    [Fact]
    public void Create_RejectsOutOfRangeWeekdays()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RecurrenceRule.Create(RecurrenceFrequency.Weekly, 1, [0], null, null, Start, null, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => RecurrenceRule.Create(RecurrenceFrequency.Weekly, 1, [8], null, null, Start, null, null, null));
    }

    [Fact]
    public void Create_RejectsDuplicateOrExcessiveWeekdays()
    {
        Assert.Throws<ArgumentException>(() => RecurrenceRule.Create(RecurrenceFrequency.Weekly, 1, [1, 1], null, null, Start, null, null, null));
        Assert.Throws<ArgumentException>(() =>
            RecurrenceRule.Create(RecurrenceFrequency.Weekly, 1, [1, 2, 3, 4, 5, 6, 7, 1], null, null, Start, null, null, null));
    }

    [Fact]
    public void Create_RejectsZeroOutOfRangeAndDuplicateMonthDays()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RecurrenceRule.Create(RecurrenceFrequency.Monthly, 1, null, [0], null, Start, null, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => RecurrenceRule.Create(RecurrenceFrequency.Monthly, 1, null, [32], null, Start, null, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => RecurrenceRule.Create(RecurrenceFrequency.Monthly, 1, null, [-32], null, Start, null, null, null));
        Assert.Throws<ArgumentException>(() => RecurrenceRule.Create(RecurrenceFrequency.Monthly, 1, null, [15, 15], null, Start, null, null, null));
        Assert.Throws<ArgumentException>(() =>
            RecurrenceRule.Create(RecurrenceFrequency.Monthly, 1, null, Enumerable.Range(-31, 63).ToArray(), null, Start, null, null, null));
    }

    [Fact]
    public void Create_RejectsOutOfRangeMonthOfYear()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RecurrenceRule.Create(RecurrenceFrequency.Yearly, 1, null, [1], 0, Start, null, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => RecurrenceRule.Create(RecurrenceFrequency.Yearly, 1, null, [1], 13, Start, null, null, null));
    }

    [Fact]
    public void Create_RejectsTwoTerminationModes()
    {
        Assert.Throws<ArgumentException>(() =>
            RecurrenceRule.Create(
                RecurrenceFrequency.Daily,
                1,
                null,
                null,
                null,
                Start,
                null,
                new DateOnly(2026, 8, 31),
                5));
    }

    [Fact]
    public void Create_RejectsUntilDateBeforeStart()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecurrenceRule.Create(RecurrenceFrequency.Daily, 1, null, null, null, Start, null, Start.AddDays(-1), null));
        var rule = RecurrenceRule.Create(RecurrenceFrequency.Daily, 1, null, null, null, Start, null, Start, null);
        Assert.Equal(Start, rule.UntilDate);
    }

    [Fact]
    public void Create_RejectsNonPositiveMaximumOccurrences()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecurrenceRule.Create(RecurrenceFrequency.Daily, 1, null, null, null, Start, null, null, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecurrenceRule.Create(RecurrenceFrequency.Daily, 1, null, null, null, Start, null, null, -3));
    }

    [Fact]
    public void Create_RejectsCrossFrequencyDaySelectors()
    {
        Assert.Throws<ArgumentException>(() =>
            RecurrenceRule.Create(RecurrenceFrequency.Weekly, 1, [], null, null, Start, null, null, null));
        Assert.Throws<ArgumentException>(() =>
            RecurrenceRule.Create(RecurrenceFrequency.Weekly, 1, [1], [15], null, Start, null, null, null));
        Assert.Throws<ArgumentException>(() =>
            RecurrenceRule.Create(RecurrenceFrequency.Monthly, 1, null, [], null, Start, null, null, null));
        Assert.Throws<ArgumentException>(() =>
            RecurrenceRule.Create(RecurrenceFrequency.Monthly, 1, [1], [15], null, Start, null, null, null));
        Assert.Throws<ArgumentException>(() =>
            RecurrenceRule.Create(RecurrenceFrequency.Yearly, 1, null, [], null, Start, null, null, null));
        Assert.Throws<ArgumentException>(() =>
            RecurrenceRule.Create(RecurrenceFrequency.Yearly, 1, null, [15], null, Start, null, null, null));
        Assert.Throws<ArgumentException>(() =>
            RecurrenceRule.Create(RecurrenceFrequency.Daily, 1, null, [15], null, Start, null, null, null));
        Assert.Throws<ArgumentException>(() =>
            RecurrenceRule.Create(RecurrenceFrequency.Daily, 1, null, null, 3, Start, null, null, null));
        Assert.Throws<ArgumentException>(() =>
            RecurrenceRule.Create(RecurrenceFrequency.Weekly, 1, [1], null, 3, Start, null, null, null));
        Assert.Throws<ArgumentException>(() =>
            RecurrenceRule.Create(RecurrenceFrequency.Monthly, 1, null, [15], 3, Start, null, null, null));
    }

    [Fact]
    public void Create_NormalizesDaySelectorOrder()
    {
        var unsorted = RecurrenceRule.Create(RecurrenceFrequency.Weekly, 1, [5, 1, 3], null, null, Start, null, null, null);
        var sorted = RecurrenceRule.Create(RecurrenceFrequency.Weekly, 1, [1, 3, 5], null, null, Start, null, null, null);

        Assert.Equal(sorted, unsorted);
        Assert.Equal(new[] { 1, 3, 5 }, unsorted.Weekdays);
    }

    [Fact]
    public void Create_ValueEqualityIgnoresSelectorOrder()
    {
        var first = RecurrenceRule.Create(RecurrenceFrequency.Weekly, 2, [1, 5], null, null, Start, null, null, 10);
        var second = RecurrenceRule.Create(RecurrenceFrequency.Weekly, 2, [5, 1], null, null, Start, null, null, 10);

        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Create_EqualitySensitiveToChangedFields()
    {
        var baseRule = Monthly();
        Assert.NotEqual(baseRule, Monthly(interval: 2));
        Assert.NotEqual(baseRule, Monthly(monthDays: [1, 16]));
        Assert.NotEqual(baseRule, Monthly(start: Start.AddDays(1)));
        Assert.NotEqual(baseRule, Daily());
    }

    private static RecurrenceRule Daily(int interval = 1, IReadOnlyList<int>? weekdays = null, DateOnly? untilDate = null, int? maxOccurrences = null) =>
        RecurrenceRule.Create(RecurrenceFrequency.Daily, interval, weekdays, null, null, Start, null, untilDate, maxOccurrences);

    private static RecurrenceRule Weekly(IReadOnlyList<int> weekdays, int interval = 1) =>
        RecurrenceRule.Create(RecurrenceFrequency.Weekly, interval, weekdays, null, null, Start, null, null, null);

    private static RecurrenceRule Monthly(int interval = 1, IReadOnlyList<int>? monthDays = null, DateOnly? start = null) =>
        RecurrenceRule.Create(RecurrenceFrequency.Monthly, interval, null, monthDays ?? [1], null, start ?? Start, null, null, null);

    private static RecurrenceRule Yearly(int interval = 1) =>
        RecurrenceRule.Create(RecurrenceFrequency.Yearly, interval, null, [15], 3, Start, null, null, null);
}