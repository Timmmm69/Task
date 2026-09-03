using Task.BackupAgent;

namespace Task.ServiceHosts.Tests;

public sealed class BackupRestoreAgentLifecycleTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async global::System.Threading.Tasks.Task Starts_ReceivesCancellation_AndStopsWithinTwoSeconds()
    {
        var logger = new RecordingLogger<BackupRestoreAgent>();
        using var service = CreateService(logger);

        await service.StartAsync(CancellationToken.None);

        var started = await logger.WaitUntilAsync(
            messages => messages.Any(m => m.Contains("hosting loop started")), Timeout);
        Assert.Contains(started, m => m.Contains("Backup/restore agent hosting loop started"));

        await service.StopAsync(CancellationToken.None).WaitAsync(Timeout);

        var messages = logger.Messages;
        Assert.Contains(messages, m => m.Contains("cancellation received"));
        Assert.Contains(messages, m => m.Contains("Backup/restore agent hosting loop stopped"));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task TwoConsecutiveStopCalls_CompleteWithinTwoSeconds_AndLogExactlyOnce()
    {
        var logger = new RecordingLogger<BackupRestoreAgent>();
        using var service = CreateService(logger);

        await service.StartAsync(CancellationToken.None);

        await logger.WaitUntilAsync(
            messages => messages.Any(m => m.Contains("hosting loop started")), Timeout);

        await service.StopAsync(CancellationToken.None).WaitAsync(Timeout);
        await service.StopAsync(CancellationToken.None).WaitAsync(Timeout);

        var messages = logger.Messages;
        Assert.Equal(1, messages.Count(m => m.Contains("hosting loop started")));
        Assert.Equal(1, messages.Count(m => m.Contains("cancellation received")));
        Assert.Equal(1, messages.Count(m => m.Contains("hosting loop stopped")));
    }

    [Fact]
    public void ServiceName_IsTaskBackupAgent()
    {
        Assert.Equal("Task.BackupAgent", BackupRestoreAgent.ServiceName);
    }

    private static BackupRestoreAgent CreateService(RecordingLogger<BackupRestoreAgent> logger)
    {
        var options = new BackupOptions();
        return new BackupRestoreAgent(logger, options,
            new BackupSchedule(options, new BackupCommandRunner(options), TimeProvider.System));
    }
}
