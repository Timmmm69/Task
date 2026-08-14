namespace Task.BackupAgent;

public sealed class BackupRestoreAgent(ILogger<BackupRestoreAgent> logger) : BackgroundService
{
    public const string ServiceName = "Task.BackupAgent";

    protected override async global::System.Threading.Tasks.Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Backup/restore agent hosting loop started");

        try
        {
            await global::System.Threading.Tasks.Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Backup/restore agent cancellation received, stopping hosting loop");
        }

        logger.LogInformation("Backup/restore agent hosting loop stopped");
    }
}