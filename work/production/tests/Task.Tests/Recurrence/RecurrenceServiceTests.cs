using System.Text.Json;
using Task.Application.Calendar;
using Task.Domain;
using Task.Domain.Recurrence;

namespace Task.Tests.Recurrence;

public sealed class RecurrenceServiceTests
{
    [Fact]
    public void Preview_CountLimitedLeapDayRuleScansEmptyYears()
    {
        var definition = Definition(Guid.NewGuid()) with
        {
            Frequency = "yearly",
            MonthOfYear = 2,
            MonthDays = [29],
            OccurrenceStartDate = new DateOnly(2025, 1, 1),
            MaxOccurrences = 2,
        };
        var dates = RecurrenceService.Preview(definition, definition.OccurrenceStartDate, 10);
        Assert.Equal(new[] { new DateOnly(2028, 2, 29), new DateOnly(2032, 2, 29) }, dates.Select(d => d.LocalDate));
    }

    [Fact]
    public void Generate_UsesImmutableTemplateAuthorRatherThanCaller()
    {
        var (service, store, org, author, series) = CreateActiveSeries();
        var caller = Guid.NewGuid();

        service.Generate(org, caller, series.Id, series.Version, "generate-1", series.Definition.OccurrenceStartDate.AddDays(100));

        Assert.All(store.Tasks.Values, task => Assert.Equal(author, task.Metadata.CreatedBy));
    }

    [Fact]
    public void Patch_RejectsTemplateAuthorChangeAndRollsBack()
    {
        var (service, store, org, author, series) = CreateActiveSeries();
        var body = JsonSerializer.Serialize(new { template = Template(Guid.NewGuid()) }, RecurrenceService.JsonOptions);

        Assert.Throws<RecurrenceRequestException>(() => service.Patch(org, author, series.Id, series.Version, "patch-author", body));
        Assert.Equal(series.Version, store.Series!.Version);
        Assert.Equal(author, store.Series.Definition.Template.AuthorUserId);
    }

    [Fact]
    public void InvalidPatch_RollsBackAllTaskAndSeriesChanges()
    {
        var (service, store, org, author, series) = CreateActiveSeries();
        var before = store.Tasks.ToDictionary(pair => pair.Key, pair => pair.Value);

        Assert.Throws<ArgumentOutOfRangeException>(() => service.Patch(org, author, series.Id, series.Version, "invalid-rule", "{\"interval\":0}"));

        Assert.Equal(series.Version, store.Series!.Version);
        Assert.Equal(before, store.Tasks);
    }

    [Fact]
    public void Patch_Twice_RegeneratesPreviouslyUpdatedTasksAndSnapshotsTemplate()
    {
        var (service, store, org, author, series) = CreateActiveSeries();
        var first = store.Occurrences.First(); var task = store.Tasks[first.TaskId];

        var second = service.Patch(org, author, series.Id, series.Version, "patch-bb", PatchTemplate(author, "B"));
        var third = service.Patch(org, author, series.Id, second.Version, "patch-cc", PatchTemplate(author, "C"));
        var occurrence = store.Occurrences.Single(o => o.TaskId == first.TaskId);

        Assert.Equal("C", store.Tasks[first.TaskId].Title);
        Assert.True(store.Tasks[first.TaskId].Metadata.Version > task.Metadata.Version);
        Assert.Equal(store.Tasks[first.TaskId].Metadata.Version, occurrence.GeneratedTaskVersion);
        Assert.Equal("C", occurrence.Template!.Title);
        Assert.Equal(third.Version, store.Series!.Version);
    }

    [Fact]
    public void ThisOccurrence_IsExceptionAndFutureRuleEditsDoNotOverwriteIt()
    {
        var (service, store, org, author, series) = CreateActiveSeries();
        var target = store.Occurrences.First(); var task = store.Tasks[target.TaskId];
        var applied = service.ApplyChange(org, author, series.Id, series.Version, "one-change", target.LocalDate,
            task.Metadata.Version, RecurrenceChangeScope.ThisOccurrence, "Исключение", "high", 30);

        service.Patch(org, author, series.Id, applied.Version, "rule-change", PatchTemplate(author, "Общее"));

        Assert.Equal("Исключение", store.Tasks[target.TaskId].Title);
        Assert.True(store.Occurrences.Single(o => o.TaskId == target.TaskId).IsException);
    }

    [Fact]
    public void StaleSeriesOrTaskVersion_IsRejected()
    {
        var (service, store, org, author, series) = CreateActiveSeries();
        var target = store.Occurrences.First();
        Assert.Throws<RecurrenceRequestException>(() => service.Patch(org, author, series.Id, series.Version + 1, "stale-series", "{\"interval\":2}"));
        Assert.Throws<RecurrenceRequestException>(() => service.ApplyChange(org, author, series.Id, series.Version, "stale-task", target.LocalDate,
            store.Tasks[target.TaskId].Metadata.Version + 1, RecurrenceChangeScope.ThisOccurrence, "X", "normal", null));
    }

    [Fact]
    public void Patch_RejectsSeriesWithMoreThanFiveHundredOccurrencesBeforeEdits()
    {
        var (service, store, org, author, series) = CreateActiveSeries();
        var current = series;
        current = ReplySeries(service.Generate(org, author, current.Id, current.Version, "window-1", current.Definition.OccurrenceStartDate.AddDays(366)), store);
        current = ReplySeries(service.Generate(org, author, current.Id, current.Version, "window-2", current.Definition.OccurrenceStartDate.AddDays(732)), store);
        var before = store.Tasks.ToDictionary(pair => pair.Key, pair => pair.Value);

        Assert.Throws<RecurrenceRequestException>(() => service.Patch(org, author, current.Id, current.Version, "too-many", "{\"interval\":2}"));
        Assert.Equal(before, store.Tasks);
    }

    [Fact]
    public void Preview_HandlesLeapMonthDay()
    {
        var definition = Definition(Guid.NewGuid()) with { Frequency = "yearly", MonthDays = [29], MonthOfYear = 2, OccurrenceStartDate = new DateOnly(2024, 2, 29) };

        var items = RecurrenceService.Preview(definition, new DateOnly(2024, 1, 1), 3);

        Assert.Equal([new DateOnly(2024, 2, 29), new DateOnly(2028, 2, 29), new DateOnly(2032, 2, 29)], items.Select(x => x.LocalDate));
    }

    private static (RecurrenceService Service, MemoryStore Store, Guid Org, Guid Author, RecurrenceRecord Series) CreateActiveSeries()
    {
        var store = new MemoryStore(); var service = new RecurrenceService(store); var org = Guid.NewGuid(); var author = Guid.NewGuid();
        service.Create(org, author, "create-series", JsonSerializer.Serialize(Definition(author), RecurrenceService.JsonOptions));
        return (service, store, org, author, store.Series!);
    }
    private static RecurrenceDefinition Definition(Guid author) => new()
    {
        Status = "active",
        Frequency = "daily",
        Interval = 1,
        OccurrenceStartDate = new DateOnly(2024, 1, 1),
        TimeZone = "UTC",
        Template = Template(author),
    };
    private static RecurrenceTemplateData Template(Guid author) => new() { Title = "A", AuthorUserId = author, Priority = "normal" };
    private static string PatchTemplate(Guid author, string title) => JsonSerializer.Serialize(new { template = Template(author) with { Title = title } }, RecurrenceService.JsonOptions);
    private static RecurrenceRecord ReplySeries(RecurrenceReply _, MemoryStore store) => store.Series!;

    private sealed class MemoryStore : IRecurrenceStore
    {
        public RecurrenceRecord? Series { get; private set; }
        public Dictionary<Guid, TaskAggregate> Tasks { get; private set; } = [];
        public List<RecurrenceOccurrenceRecord> Occurrences { get; private set; } = [];
        public IReadOnlyList<RecurrenceRecord> List(Guid organizationId) => Series is { OrganizationId: var org } && org == organizationId ? [Series] : [];
        public RecurrenceRecord? Get(Guid organizationId, Guid id) => Series is { OrganizationId: var org, Id: var seriesId } && org == organizationId && seriesId == id ? Series : null;
        public IReadOnlyList<RecurrenceOccurrenceDetails> GetOccurrences(Guid organizationId, Guid id) => [];
        public RecurrenceReply Execute(Guid organizationId, Guid actorId, Guid id, string operation, string key, string hash, Func<RecurrenceRecord?, IRecurrenceTransaction, RecurrenceReply> action)
        {
            var copy = new MemoryTransaction(Series, new Dictionary<Guid, TaskAggregate>(Tasks), [.. Occurrences]);
            var reply = action(Get(organizationId, id), copy);
            Series = copy.Series; Tasks = copy.Tasks; Occurrences = copy.Occurrences;
            return reply;
        }
    }

    private sealed class MemoryTransaction(RecurrenceRecord? series, Dictionary<Guid, TaskAggregate> tasks, List<RecurrenceOccurrenceRecord> occurrences) : IRecurrenceTransaction
    {
        public RecurrenceRecord? Series { get; private set; } = series;
        public Dictionary<Guid, TaskAggregate> Tasks { get; } = tasks;
        public List<RecurrenceOccurrenceRecord> Occurrences { get; } = occurrences;
        IReadOnlyList<RecurrenceOccurrenceRecord> IRecurrenceTransaction.Occurrences => Occurrences.ToArray();
        public TaskAggregate? GetTask(Guid id) => Tasks.GetValueOrDefault(id);
        public void SaveTask(TaskAggregate task, int? expectedVersion) => Tasks[task.Metadata.Id] = task;
        public void SaveOccurrence(RecurrenceOccurrenceRecord occurrence)
        { var index = Occurrences.FindIndex(o => o.LocalDate == occurrence.LocalDate); if (index < 0) Occurrences.Add(occurrence); else Occurrences[index] = occurrence; }
        public void SaveSeries(RecurrenceRecord series) => Series = series;
    }
}
