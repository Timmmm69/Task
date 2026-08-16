using Task.Domain;
using Task.Domain.Recurrence;

namespace Task.Tests.Recurrence;

public sealed class RecurrenceScopeFilterTests
{
    private static readonly Guid OccurrenceIdBase = Guid.Parse("5a3c81d7-2e4b-4f6a-9a2d-1c4e6f8a0b2d");
    private static readonly Guid SeriesId = Guid.Parse("b64fbeec-f0f4-4f5f-9967-ea2ce57be461");
    private static readonly Guid OrganizationId = Guid.Parse("751fa8ce-5cc3-4d98-8574-1108080b2ff4");
    private static readonly Guid ActorId = Guid.Parse("3077f0f8-536f-4988-bd73-6f26265d0b92");
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 15, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Select_RejectsUnexplicitDefaultScope()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecurrenceScopeFilter.Select((RecurrenceChangeScope)0, Key("2026-08-04"), Occurrences()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecurrenceScopeFilter.Select((RecurrenceChangeScope)99, Key("2026-08-04"), Occurrences()));
    }

    [Fact]
    public void Select_ThisOccurrenceReturnsOnlyTheTarget()
    {
        var occurrences = Occurrences();
        var target = Key("2026-08-04");

        var selected = RecurrenceScopeFilter.Select(RecurrenceChangeScope.ThisOccurrence, target, occurrences);

        Assert.Single(selected);
        Assert.Equal("2026-08-04", selected[0].OccurrenceKey.Value);
        Assert.Same(occurrences.First(occurrence => occurrence.OccurrenceKey.Value == "2026-08-04"), selected[0]);
    }

    [Fact]
    public void Select_ThisAndFutureReturnsTargetAndLaterOccurrencesSortedByDate()
    {
        var selected = RecurrenceScopeFilter.Select(RecurrenceChangeScope.ThisAndFuture, Key("2026-08-04"), Occurrences());

        Assert.Equal(
            new[] { "2026-08-04", "2026-08-07", "2026-08-15" },
            selected.Select(occurrence => occurrence.OccurrenceKey.Value));
    }

    [Fact]
    public void Select_EntireSeriesReturnsAllOccurrencesSortedByDate()
    {
        var selected = RecurrenceScopeFilter.Select(RecurrenceChangeScope.EntireSeries, Key("2026-08-04"), Occurrences());

        Assert.Equal(
            new[] { "2026-08-03", "2026-08-04", "2026-08-07", "2026-08-15" },
            selected.Select(occurrence => occurrence.OccurrenceKey.Value));
    }

    [Fact]
    public void Select_RejectsTargetOutsideTheOccurrenceList()
    {
        var occurrences = Occurrences();

        Assert.Throws<ArgumentException>(() =>
            RecurrenceScopeFilter.Select(RecurrenceChangeScope.ThisOccurrence, Key("2026-09-01"), occurrences));
        Assert.Throws<ArgumentException>(() =>
            RecurrenceScopeFilter.Select(RecurrenceChangeScope.ThisAndFuture, Key("2026-09-01"), occurrences));
        Assert.Throws<ArgumentException>(() =>
            RecurrenceScopeFilter.Select(RecurrenceChangeScope.EntireSeries, Key("2026-09-01"), occurrences));
    }

    [Fact]
    public void Select_RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() =>
            RecurrenceScopeFilter.Select(RecurrenceChangeScope.ThisOccurrence, null!, Occurrences()));
        Assert.Throws<ArgumentNullException>(() =>
            RecurrenceScopeFilter.Select(RecurrenceChangeScope.ThisOccurrence, Key("2026-08-04"), null!));
    }

    private static OccurrenceKey Key(string value) => OccurrenceKey.FromValue(value);

    private static IReadOnlyList<RecurrenceOccurrence> Occurrences() =>
        new[]
        {
            ("2026-08-07", "5a3c81d7-2e4b-4f6a-9a2d-1c4e6f8a0b21"),
            ("2026-08-03", "5a3c81d7-2e4b-4f6a-9a2d-1c4e6f8a0b22"),
            ("2026-08-15", "5a3c81d7-2e4b-4f6a-9a2d-1c4e6f8a0b23"),
            ("2026-08-04", "5a3c81d7-2e4b-4f6a-9a2d-1c4e6f8a0b24"),
        }
            .Select(pair => RecurrenceOccurrence.Create(
                Guid.Parse(pair.Item2),
                OrganizationId,
                SeriesId,
                ActorId,
                OccurrenceKey.FromValue(pair.Item1),
                CreatedAt))
            .ToArray();
}