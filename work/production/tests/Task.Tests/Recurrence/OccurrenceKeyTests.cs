using Task.Domain.Recurrence;

namespace Task.Tests.Recurrence;

public sealed class OccurrenceKeyTests
{
    [Fact]
    public void FromLocalDate_IsDeterministic()
    {
        var date = new DateOnly(2026, 8, 3);

        var first = OccurrenceKey.FromLocalDate(date);
        var second = OccurrenceKey.FromLocalDate(date);

        Assert.Equal(first, second);
        Assert.Equal("2026-08-03", first.Value);
        Assert.Equal(date, first.LocalDate);
    }

    [Fact]
    public void FromValue_RoundTripsTheCanonicalForm()
    {
        var key = OccurrenceKey.FromValue("2026-12-31");

        Assert.Equal(new DateOnly(2026, 12, 31), key.LocalDate);
        Assert.Equal("2026-12-31", key.Value);
    }

    [Fact]
    public void FromValue_RejectsNonCalendarDates()
    {
        Assert.Throws<ArgumentException>(() => OccurrenceKey.FromValue(""));
        Assert.Throws<ArgumentException>(() => OccurrenceKey.FromValue("   "));
        Assert.Throws<ArgumentException>(() => OccurrenceKey.FromValue("20260803"));
        Assert.Throws<ArgumentException>(() => OccurrenceKey.FromValue("2026-13-01"));
        Assert.Throws<ArgumentException>(() => OccurrenceKey.FromValue("2026-02-30"));
        Assert.Throws<ArgumentException>(() => OccurrenceKey.FromValue("2026-8-3"));
        Assert.Throws<ArgumentException>(() => OccurrenceKey.FromValue(new string('1', 65)));
    }

    [Fact]
    public void FromValue_NormalizesLeapDay()
    {
        var key = OccurrenceKey.FromValue("2028-02-29");

        Assert.Equal(new DateOnly(2028, 2, 29), key.LocalDate);
    }

    [Fact]
    public void ToString_ReturnsTheKeyValue()
    {
        Assert.Equal("2026-08-03", OccurrenceKey.FromLocalDate(new DateOnly(2026, 8, 3)).ToString());
    }
}