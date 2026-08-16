using Task.Domain;
using Task.Domain.Recurrence;

namespace Task.Tests.Recurrence;

public sealed class RecurrenceSeriesTests
{
    private static readonly Guid SeriesId = Guid.Parse("b64fbeec-f0f4-4f5f-9967-ea2ce57be461");
    private static readonly Guid OrganizationId = Guid.Parse("751fa8ce-5cc3-4d98-8574-1108080b2ff4");
    private static readonly Guid CreatorId = Guid.Parse("3077f0f8-536f-4988-bd73-6f26265d0b92");
    private static readonly Guid EditorId = Guid.Parse("957c3a11-d6f2-4a2a-bb1b-0945a0f6a820");
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 15, 8, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Start = new(2026, 8, 3);

    [Fact]
    public void Create_SetsAuditMetadataRulesAndDefaultHorizon()
    {
        var series = Series();

        Assert.Equal(SeriesId, series.Metadata.Id);
        Assert.Equal(OrganizationId, series.Metadata.OrganizationId);
        Assert.Equal(CreatorId, series.Metadata.CreatedBy);
        Assert.Equal(1, series.Metadata.Version);
        Assert.Equal(RecurrenceSeriesStatus.Active, series.Status);
        Assert.Equal("Europe/Moscow", series.TimeZoneId);
        Assert.Equal(Start, series.NextGenerationDate);
        Assert.True(series.IsGenerating);
    }

    [Fact]
    public void Create_AllowsPausedStatus()
    {
        var series = Series(status: RecurrenceSeriesStatus.Paused);

        Assert.Equal(RecurrenceSeriesStatus.Paused, series.Status);
        Assert.False(series.IsGenerating);
    }

    [Fact]
    public void Create_RejectsTerminalStatuses()
    {
        Assert.Throws<ArgumentException>(() => Series(status: RecurrenceSeriesStatus.Completed));
        Assert.Throws<ArgumentException>(() => Series(status: RecurrenceSeriesStatus.Cancelled));
        Assert.Throws<ArgumentException>(() => Series(status: (RecurrenceSeriesStatus)99));
    }

    [Fact]
    public void TimeZone_ValidationAcceptsKnownZonesAndTrims()
    {
        Assert.Equal("UTC", Series(timeZoneId: "  UTC  ").TimeZoneId);
        Assert.Equal("Europe/Moscow", Series(timeZoneId: "Europe/Moscow").TimeZoneId);
    }

    [Fact]
    public void TimeZone_ValidationRejectsUnknownOrMalformedZones()
    {
        Assert.Throws<ArgumentException>(() => Series(timeZoneId: ""));
        Assert.Throws<ArgumentException>(() => Series(timeZoneId: "   "));
        Assert.Throws<ArgumentException>(() => Series(timeZoneId: new string('x', 65)));
        Assert.Throws<ArgumentException>(() => Series(timeZoneId: "Not/AZone"));
    }

    [Fact]
    public void Reconstitute_AcceptsAllStatusesWithTerminationGuard()
    {
        var metadata = SyncableEntityMetadata.Create(SeriesId, OrganizationId, CreatorId, CreatedAt);
        var completed = RecurrenceSeries.Reconstitute(
            metadata,
            RecurrenceSeriesStatus.Completed,
            "UTC",
            Rule(maxOccurrences: 10),
            Start,
            Template());

        Assert.Equal(RecurrenceSeriesStatus.Completed, completed.Status);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecurrenceSeries.Reconstitute(metadata, RecurrenceSeriesStatus.Cancelled, "UTC", Rule(), Start.AddDays(-1), Template()));
        Assert.Throws<ArgumentException>(() =>
            RecurrenceSeries.Reconstitute(metadata, RecurrenceSeriesStatus.Completed, "UTC", Rule(), Start, Template()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecurrenceSeries.Reconstitute(metadata, RecurrenceSeriesStatus.Active, "UTC", Rule(), Start.AddDays(-1), Template()));
    }

    [Fact]
    public void PauseAndResume_TransitionOnlyBetweenActiveAndPaused()
    {
        var paused = Series().Pause(CreatorId, CreatedAt.AddMinutes(1));
        Assert.Equal(RecurrenceSeriesStatus.Paused, paused.Status);
        Assert.Equal(2, paused.Metadata.Version);

        var resumed = paused.Resume(CreatorId, CreatedAt.AddMinutes(2));
        Assert.Equal(RecurrenceSeriesStatus.Active, resumed.Status);
        Assert.Equal(3, resumed.Metadata.Version);
    }

    [Fact]
    public void PauseAndResume_RejectInvalidTransitions()
    {
        var active = Series();
        var paused = active.Pause(CreatorId, CreatedAt.AddMinutes(1));
        var cancelled = active.Cancel(CreatorId, CreatedAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => paused.Pause(CreatorId, CreatedAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => active.Resume(CreatorId, CreatedAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => cancelled.Resume(CreatorId, CreatedAt.AddMinutes(2)));
    }

    [Fact]
    public void Cancel_MarksStatusWithoutMovingSeriesToTrash()
    {
        var cancelled = Series().Cancel(EditorId, CreatedAt.AddMinutes(1));

        Assert.Equal(RecurrenceSeriesStatus.Cancelled, cancelled.Status);
        Assert.Equal(EntityLifecycleState.Active, cancelled.Metadata.LifecycleState);
        Assert.Null(cancelled.Metadata.DeletedAtUtc);
        Assert.Null(cancelled.Metadata.DeletedBy);
        Assert.Null(cancelled.Metadata.ArchivedAtUtc);
        Assert.Equal(2, cancelled.Metadata.Version);
        Assert.False(cancelled.IsGenerating);
    }

    [Fact]
    public void Cancel_AcceptsPausedSeriesAndRejectsTerminalOnes()
    {
        var paused = Series().Pause(CreatorId, CreatedAt.AddMinutes(1));
        Assert.Equal(RecurrenceSeriesStatus.Cancelled, paused.Cancel(EditorId, CreatedAt.AddMinutes(2)).Status);

        var cancelled = Series().Cancel(EditorId, CreatedAt.AddMinutes(1));
        var completed = Series(rule: Rule(maxOccurrences: 10)).Complete(EditorId, CreatedAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => cancelled.Cancel(EditorId, CreatedAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => completed.Cancel(EditorId, CreatedAt.AddMinutes(2)));
    }

    [Fact]
    public void Complete_RequiresTerminationMode()
    {
        Assert.Throws<InvalidOperationException>(() => Series().Complete(EditorId, CreatedAt.AddMinutes(1)));

        var completed = Series(rule: Rule(maxOccurrences: 10)).Complete(EditorId, CreatedAt.AddMinutes(1));
        Assert.Equal(RecurrenceSeriesStatus.Completed, completed.Status);
        Assert.False(completed.IsGenerating);
    }

    [Fact]
    public void Complete_RejectsTerminalSeries()
    {
        var completed = Series(rule: Rule(maxOccurrences: 10)).Complete(EditorId, CreatedAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() =>
            completed.Complete(EditorId, CreatedAt.AddMinutes(2)));
    }

    [Fact]
    public void IsTerminationReached_DependsOnHorizonOrGeneratedCount()
    {
        var untilLimited = Series(rule: Rule(untilDate: new DateOnly(2026, 8, 31)));
        Assert.True(untilLimited.IsTerminationReached(new DateOnly(2026, 8, 31), 0));
        Assert.True(untilLimited.IsTerminationReached(new DateOnly(2026, 9, 1), 0));
        Assert.False(untilLimited.IsTerminationReached(new DateOnly(2026, 8, 30), 0));

        var countLimited = Series(rule: Rule(maxOccurrences: 5));
        Assert.True(countLimited.IsTerminationReached(new DateOnly(2026, 12, 31), 5));
        Assert.True(countLimited.IsTerminationReached(new DateOnly(2026, 12, 31), 6));
        Assert.False(countLimited.IsTerminationReached(new DateOnly(2026, 12, 31), 4));
        Assert.False(Series().IsTerminationReached(new DateOnly(2030, 1, 1), 1000));
    }

    [Fact]
    public void UpdateRule_ChangesRuleAndKeepsHorizonValid()
    {
        var series = Series();
        var earlierRule = Rule(start: Start.AddDays(-2));
        var laterRule = Rule(start: Start.AddDays(10));

        var backDated = series.UpdateRule(EditorId, CreatedAt.AddMinutes(1), earlierRule);
        Assert.Equal(Start, backDated.NextGenerationDate);

        var forward = series.UpdateRule(EditorId, CreatedAt.AddMinutes(1), laterRule);
        Assert.Equal(Start.AddDays(10), forward.NextGenerationDate);
        Assert.Equal(laterRule, forward.Rule);
        Assert.Equal(2, forward.Metadata.Version);
    }

    [Fact]
    public void UpdateRule_IsNoOpForEqualRule()
    {
        var series = Series();

        var result = series.UpdateRule(EditorId, CreatedAt.AddMinutes(1), Rule());

        Assert.Same(series, result);
        Assert.Equal(1, result.Metadata.Version);
    }

    [Fact]
    public void UpdateRuleAndTemplate_RejectedForTerminalSeries()
    {
        var cancelled = Series().Cancel(EditorId, CreatedAt.AddMinutes(1));
        var completed = Series(rule: Rule(maxOccurrences: 10)).Complete(EditorId, CreatedAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => cancelled.UpdateRule(EditorId, CreatedAt.AddMinutes(2), Rule()));
        Assert.Throws<InvalidOperationException>(() => completed.UpdateTemplate(EditorId, CreatedAt.AddMinutes(2), Template()));
    }

    [Fact]
    public void UpdateTemplate_AppliesNewTemplateOrStaysNoOp()
    {
        var series = Series();
        var newTemplate = Template(title: "Renamed");

        var updated = series.UpdateTemplate(EditorId, CreatedAt.AddMinutes(1), newTemplate);
        Assert.Equal("Renamed", updated.Template.Title);
        Assert.Equal(2, updated.Metadata.Version);

        Assert.Same(updated, updated.UpdateTemplate(EditorId, CreatedAt.AddMinutes(2), newTemplate));
        Assert.Equal(2, updated.Metadata.Version);
    }

    [Fact]
    public void AdvanceHorizon_MovesForwardOnlyWhileMutable()
    {
        var series = Series();
        var advanced = series.AdvanceHorizon(EditorId, CreatedAt.AddMinutes(1), Start.AddMonths(1));

        Assert.Equal(Start.AddMonths(1), advanced.NextGenerationDate);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            advanced.AdvanceHorizon(EditorId, CreatedAt.AddMinutes(2), Start));
        Assert.Throws<InvalidOperationException>(() =>
            Series().Cancel(EditorId, CreatedAt.AddMinutes(1)).AdvanceHorizon(EditorId, CreatedAt.AddMinutes(2), Start.AddDays(1)));
    }

    [Fact]
    public void TimestampRules_MatchTheAggregateConventions()
    {
        var nonUtcAt = new DateTimeOffset(2026, 8, 15, 14, 0, 0, TimeSpan.FromHours(5));

        Assert.Throws<ArgumentException>(() => Series().Pause(CreatorId, nonUtcAt));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Series().Pause(CreatorId, CreatedAt.AddMinutes(1)).Resume(CreatorId, CreatedAt));
    }

    private static RecurrenceRule Rule(
        DateOnly? start = null,
        int maxOccurrences = 0,
        DateOnly? untilDate = null) =>
        RecurrenceRule.Create(
            RecurrenceFrequency.Daily,
            1,
            null,
            null,
            null,
            start ?? Start,
            null,
            untilDate,
            maxOccurrences > 0 ? maxOccurrences : null);

    private static RecurrenceTaskTemplate Template(string? title = null) =>
        RecurrenceTaskTemplate.Create(
            null,
            title ?? "Weekly sync report",
            null,
            CreatorId,
            null,
            null,
            TaskPriority.Normal,
            null,
            null,
            [],
            [],
            [],
            [],
            templateVersion: 1);

    private static RecurrenceSeries Series(
        RecurrenceSeriesStatus status = RecurrenceSeriesStatus.Active,
        string timeZoneId = "Europe/Moscow",
        RecurrenceRule? rule = null) =>
        RecurrenceSeries.Create(
            SeriesId,
            OrganizationId,
            CreatorId,
            CreatedAt,
            status,
            timeZoneId,
            rule ?? Rule(),
            Template());
}