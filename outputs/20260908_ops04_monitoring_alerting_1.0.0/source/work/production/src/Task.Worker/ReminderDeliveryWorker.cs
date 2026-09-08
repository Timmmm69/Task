using Npgsql;
using Task.Application.Background;

namespace Task.Worker;

public sealed class ReminderDeliveryWorker(
    ILogger<ReminderDeliveryWorker> logger,
    IBackgroundDeliveryStore? store = null,
    TimeSpan? runPeriod = null,
    TimeProvider? timeProvider = null,
    WorkerOperationalState? operationalState = null) : BackgroundService
{
    public const int PeriodSeconds = 5;
    public const int MaterializationPeriodMinutes = 5;
    public const int LeaseSeconds = 30;
    public const int BatchSize = 100;
    public const string WorkerName = "reminders.dispatch";

    private readonly TimeSpan _runPeriod = NormalizePeriod(runPeriod);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly WorkerOperationalState? _operationalState = RegisterOperationalState(operationalState, store is not null);
    private DateTimeOffset _nextMaterializationAtUtc = DateTimeOffset.MinValue;

    protected override async global::System.Threading.Tasks.Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Reminder delivery worker hosting loop started");
        if (store is null)
            logger.LogWarning("IBackgroundDeliveryStore is not registered; reminder delivery passes will be skipped");

        while (!stoppingToken.IsCancellationRequested)
        {
            _operationalState?.MarkAttempt(WorkerName);
            try
            {
                await RunPassAsync(stoppingToken);
                _operationalState?.MarkSuccess(WorkerName);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (NpgsqlException exception)
            {
                _operationalState?.MarkFailure(WorkerName);
                logger.LogWarning(exception, "Reminder delivery pass could not reach PostgreSQL; the next pass will retry");
            }
            catch (Exception exception)
            {
                _operationalState?.MarkFailure(WorkerName);
                logger.LogError(exception, "Reminder delivery pass failed; the next pass will retry");
            }

            try
            {
                await global::System.Threading.Tasks.Task.Delay(_runPeriod, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("Reminder delivery worker hosting loop stopped");
    }

    internal async global::System.Threading.Tasks.Task RunPassAsync(CancellationToken cancellationToken)
    {
        if (store is null) return;
        var now = _timeProvider.GetUtcNow();
        if (now >= _nextMaterializationAtUtc)
        {
            var materialized = await store.MaterializeDueRemindersAsync(BatchSize, cancellationToken);
            _nextMaterializationAtUtc = now.AddMinutes(MaterializationPeriodMinutes);
            if (materialized > 0)
                logger.LogInformation("Reminder materialization created {Count} occurrences", materialized);
        }

        var leaseToken = Guid.NewGuid();
        var deliveries = await store.ClaimReminderDeliveriesAsync(
            WorkerName, leaseToken, TimeSpan.FromSeconds(LeaseSeconds), BatchSize, cancellationToken);
        foreach (var delivery in deliveries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await store.RenewReminderLeaseAsync(
                    delivery, TimeSpan.FromSeconds(LeaseSeconds), cancellationToken);
                await store.DeliverReminderAsync(delivery, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _operationalState?.MarkFailure(WorkerName);
                logger.LogWarning(
                    exception,
                    "Reminder occurrence {OccurrenceId} delivery failed; scheduling a safe retry",
                    delivery.OccurrenceId);
                try
                {
                    await store.FailReminderAsync(delivery, "REMINDER_DELIVERY_FAILED", cancellationToken);
                }
                catch (Exception failureException)
                {
                    logger.LogError(
                        failureException,
                        "Reminder occurrence {OccurrenceId} failure state could not be persisted",
                        delivery.OccurrenceId);
                }
            }
        }

        if (deliveries.Count > 0)
            logger.LogInformation("Reminder delivery pass processed {Count} occurrences", deliveries.Count);
    }

    private static TimeSpan NormalizePeriod(TimeSpan? runPeriod)
    {
        var value = runPeriod ?? TimeSpan.FromSeconds(PeriodSeconds);
        return value > TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(nameof(runPeriod));
    }

    private static WorkerOperationalState? RegisterOperationalState(WorkerOperationalState? state, bool configured)
    {
        state?.Register(WorkerName, TimeSpan.FromSeconds(30), configured);
        return state;
    }
}
