using Task.BackupAgent;

namespace Task.ServiceHosts.Tests;

public sealed class BackupRestoreAgentLifecycleTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async global::System.Threading.Tasks.Task Starts_ReceivesCancellation_AndStopsWithinTwoSeconds()
    {
        var logger = new RecordingLogger<BackupRestoreAgent>();
        using var service = new BackupRestoreAgent(logger);

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
    public void ServiceName_IsTaskBackupAgent()
    {
        Assert.Equal("Task.BackupAgent", BackupRestoreAgent.ServiceName);
    }
}
