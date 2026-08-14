namespace Task.Worker;

public sealed class TaskBackgroundWorker(ILogger<TaskBackgroundWorker> logger) : BackgroundService
{
    public const string ServiceName = "Task.BackgroundWorker";

    protected override async global::System.Threading.Tasks.Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Task background worker hosting loop started");

        try
        {
            await global::System.Threading.Tasks.Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Task background worker cancellation received, stopping hosting loop");
        }

        logger.LogInformation("Task background worker hosting loop stopped");
    }
}