using Task.Worker;

namespace Task.ServiceHosts.Tests;

public sealed class TaskBackgroundWorkerLifecycleTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async global::System.Threading.Tasks.Task Starts_ReceivesCancellation_AndStopsWithinTwoSeconds()
    {
        var logger = new RecordingLogger<TaskBackgroundWorker>();
        using var service = new TaskBackgroundWorker(logger);

        await service.StartAsync(CancellationToken.None);

        var started = await logger.WaitUntilAsync(
            messages => messages.Any(m => m.Contains("hosting loop started")), Timeout);
        Assert.Contains(started, m => m.Contains("Task background worker hosting loop started"));

        await service.StopAsync(CancellationToken.None).WaitAsync(Timeout);

        var messages = logger.Messages;
        Assert.Contains(messages, m => m.Contains("cancellation received"));
        Assert.Contains(messages, m => m.Contains("Task background worker hosting loop stopped"));
    }

    [Fact]
    public void ServiceName_IsTaskBackgroundWorker()
    {
        Assert.Equal("Task.BackgroundWorker", TaskBackgroundWorker.ServiceName);
    }
}
