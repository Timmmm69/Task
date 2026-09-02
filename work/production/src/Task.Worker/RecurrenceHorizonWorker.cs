using Task.Application.Calendar;

namespace Task.Worker;

/// <summary>Extends active recurrence series ahead of the user-visible calendar horizon.</summary>
public sealed class RecurrenceHorizonWorker(
    ILogger<RecurrenceHorizonWorker> logger,
    IRecurrenceStore? store = null,
    RecurrenceService? recurrenceService = null,
    TimeSpan? runPeriod = null,
    TimeProvider? timeProvider = null) : BackgroundService
{
    public const int PeriodMinutes = 1;
    public const int HorizonDays = 62;
    public const int BatchSize = 50;
    private readonly ILogger<RecurrenceHorizonWorker> _logger = logger;
    private readonly IRecurrenceStore? _store = store;
    private readonly RecurrenceService? _recurrenceService = recurrenceService;
    private readonly TimeSpan _runPeriod = runPeriod ?? TimeSpan.FromMinutes(PeriodMinutes);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    protected override async global::System.Threading.Tasks.Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_runPeriod <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(runPeriod));
        if (_store is null || _recurrenceService is null)
            _logger.LogWarning("Recurrence horizon worker is not configured; passes will be skipped");

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunPassAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { _logger.LogError(exception, "Recurrence horizon pass failed; the next pass will retry"); }

            try { await global::System.Threading.Tasks.Task.Delay(_runPeriod, _timeProvider, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    internal global::System.Threading.Tasks.Task RunPassAsync(CancellationToken cancellationToken)
    {
        if (_store is null || _recurrenceService is null) return global::System.Threading.Tasks.Task.CompletedTask;
        var target = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime).AddDays(HorizonDays);
        foreach (var series in _store.ListDue(target, BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = series.Definition.NextGenerationDate ?? series.Definition.OccurrenceStartDate;
            var through = next.AddDays(365);
            if (through > target) through = target;
            if (through < next) continue;
            var key = $"horizon-{series.Id:N}-{series.Version}-{through:yyyy-MM-dd}";
            try
            {
                _recurrenceService.Generate(series.OrganizationId, series.CreatedBy, series.Id, series.Version, key, through);
            }
            catch (RecurrenceRequestException exception) when (exception.Status == 412)
            {
                _logger.LogDebug("Recurrence horizon generation skipped because series {SeriesId} changed concurrently", series.Id);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Recurrence horizon generation failed for series {SeriesId}; continuing with remaining series", series.Id);
            }
        }
        return global::System.Threading.Tasks.Task.CompletedTask;
    }
}
