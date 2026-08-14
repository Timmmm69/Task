namespace Task.BackupAgent;

public class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async global::System.Threading.Tasks.Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }
            await global::System.Threading.Tasks.Task.Delay(1000, stoppingToken);
        }
    }
}
