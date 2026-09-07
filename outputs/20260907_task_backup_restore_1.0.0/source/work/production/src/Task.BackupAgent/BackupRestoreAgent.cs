namespace Task.BackupAgent;

public sealed class BackupRestoreAgent(
    ILogger<BackupRestoreAgent> logger,
    BackupOptions options,
    BackupSchedule schedule) : BackgroundService
{
    public const string ServiceName = "Task.BackupAgent";

    protected override async global::System.Threading.Tasks.Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Backup/restore agent hosting loop started");

        try
        {
            if (!options.Enabled)
            {
                logger.LogWarning("Backup protection is DISABLED; this host is not protecting company data");
                await global::System.Threading.Tasks.Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            else
            {
                options.Validate();
                Directory.CreateDirectory(options.StateDirectory);
                // An OS-held lock survives neither process death nor reboot; it cannot become a stale lease.
                using var singleton = new FileStream(Path.Combine(options.StateDirectory, "agent.lock"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                schedule.Load();
                if (schedule.RecoveredInvalidState)
                    logger.LogCritical("Backup scheduler state was invalid and preserved for inspection; an immediate backup is required");
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await schedule.TickAsync(stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogCritical("Backup protection failed ({FailureType}); operation={Operation}. Check protected operator journal; RPO is at risk",
                            ex.GetType().Name, schedule.State.FailedOperation);
                    }

                    await global::System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Backup/restore agent cancellation received, stopping hosting loop");
        }

        logger.LogInformation("Backup/restore agent hosting loop stopped");
    }
}
