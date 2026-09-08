using Npgsql;
using Task.Application.Background;

namespace Task.Worker;

public sealed class OutboxPublisherWorker(
    ILogger<OutboxPublisherWorker> logger,
    IBackgroundDeliveryStore? store = null,
    TimeSpan? runPeriod = null,
    TimeProvider? timeProvider = null) : BackgroundService
{
    public const int PeriodSeconds = 1;
    public const int LeaseSeconds = 30;
    public const int BatchSize = 100;
    public const string WorkerName = "outbox.publish";

    private readonly TimeSpan _runPeriod = NormalizePeriod(runPeriod);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    protected override async global::System.Threading.Tasks.Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox publisher worker hosting loop started");
        if (store is null)
            logger.LogWarning("IBackgroundDeliveryStore is not registered; outbox publishing passes will be skipped");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPassAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (NpgsqlException exception)
            {
                logger.LogWarning(exception, "Outbox publishing pass could not reach PostgreSQL; the next pass will retry");
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox publishing pass failed; the next pass will retry");
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

        logger.LogInformation("Outbox publisher worker hosting loop stopped");
    }

    internal async global::System.Threading.Tasks.Task RunPassAsync(CancellationToken cancellationToken)
    {
        if (store is null) return;
        var leaseToken = Guid.NewGuid();
        var deliveries = await store.ClaimOutboxAsync(
            WorkerName, leaseToken, TimeSpan.FromSeconds(LeaseSeconds), BatchSize, cancellationToken);

        foreach (var delivery in deliveries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await store.RenewOutboxLeaseAsync(
                    delivery, TimeSpan.FromSeconds(LeaseSeconds), cancellationToken);
                await store.DeliverOutboxAsync(delivery, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Outbox message {MessageId} delivery failed; scheduling a safe retry", delivery.Id);
                try
                {
                    await store.FailOutboxAsync(
                        delivery, "OUTBOX_DELIVERY_FAILED", "Outbox delivery failed.", cancellationToken);
                }
                catch (Exception failureException)
                {
                    logger.LogError(failureException, "Outbox message {MessageId} failure state could not be persisted", delivery.Id);
                }
            }
        }

        if (deliveries.Count > 0)
            logger.LogInformation("Outbox publisher pass processed {Count} messages", deliveries.Count);
    }

    private static TimeSpan NormalizePeriod(TimeSpan? runPeriod)
    {
        var value = runPeriod ?? TimeSpan.FromSeconds(PeriodSeconds);
        return value > TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(nameof(runPeriod));
    }
}
