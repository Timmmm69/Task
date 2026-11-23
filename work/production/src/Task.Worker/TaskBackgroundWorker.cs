using Task.Infrastructure.Persistence;

namespace Task.Worker;

public sealed class TaskBackgroundWorker(
    ILogger<TaskBackgroundWorker> logger,
    TaskPersistenceRuntime? persistenceRuntime = null,
    WorkerOperationalState? operationalState = null) : BackgroundService
{
    public const string ServiceName = "Task.BackgroundWorker";
    private const string SearchSnapshotWorkerName = "search.snapshots-expired-purge";

    private static readonly TimeSpan SnapshotCleanupPeriod = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SnapshotBacklogDelay = TimeSpan.FromSeconds(1);

    protected override async global::System.Threading.Tasks.Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Task background worker hosting loop started");

        var configured = persistenceRuntime?.IsConfigured == true;
        operationalState?.Register(SearchSnapshotWorkerName, TimeSpan.FromMinutes(2), configured);
        try
        {
            if (!configured)
            {
                await global::System.Threading.Tasks.Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = SnapshotCleanupPeriod;
                operationalState?.MarkAttempt(SearchSnapshotWorkerName);
                try
                {
                    var deleted = await persistenceRuntime!.PurgeExpiredSearchSnapshotsAsync(stoppingToken);
                    operationalState?.MarkSuccess(SearchSnapshotWorkerName);
                    logger.LogDebug("Purged {Deleted} expired search snapshots", deleted);
                    if (deleted == TaskPersistenceRuntime.SearchSnapshotCleanupBatchSize)
                    {
                        delay = SnapshotBacklogDelay;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    operationalState?.MarkFailure(SearchSnapshotWorkerName);
                    logger.LogWarning(ex, "Expired search snapshot cleanup failed; the next pass will retry");
                }

                await global::System.Threading.Tasks.Task.Delay(delay, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Task background worker cancellation received, stopping hosting loop");
        }
        finally
        {
            logger.LogInformation("Task background worker hosting loop stopped");
        }
    }
}
