using Microsoft.Extensions.Logging;

namespace Task.ServiceHosts.Tests;

public sealed class RecordingLoggerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(500);

    [Fact]
    public void LogInformation_StoresFormattedMessage()
    {
        var logger = new RecordingLogger<string>();

        logger.LogInformation("Task {Id} started by {Owner}", 42, "admin");

        var message = Assert.Single(logger.Messages);
        Assert.Equal("Task 42 started by admin", message);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task WaitUntilAsync_ReturnsSnapshotImmediately_WhenPredicateAlreadySatisfied()
    {
        var logger = new RecordingLogger<string>();
        logger.LogInformation("ready");

        var snapshot = await logger.WaitUntilAsync(
            messages => messages.Contains("ready"), Timeout);

        Assert.Equal(new[] { "ready" }, snapshot);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task WaitUntilAsync_Completes_WhenMatchingMessageArrivesDuringWait()
    {
        var logger = new RecordingLogger<string>();
        logger.LogInformation("first");

        var wait = logger.WaitUntilAsync(messages => messages.Contains("second"), Timeout);
        logger.LogInformation("second");

        var snapshot = await wait;

        Assert.Equal(new[] { "first", "second" }, snapshot);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task WaitUntilAsync_WithoutMessages_ReturnsEmptySnapshotOnTimeout()
    {
        var logger = new RecordingLogger<string>();

        var snapshot = await logger.WaitUntilAsync(
            messages => messages.Contains("never"), Timeout);

        Assert.Empty(snapshot);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task WaitUntilAsync_OnTimeout_ReturnsMessagesLoggedSoFar()
    {
        var logger = new RecordingLogger<string>();
        logger.LogInformation("only");

        var snapshot = await logger.WaitUntilAsync(
            messages => messages.Contains("never"), Timeout);

        Assert.Equal(new[] { "only" }, snapshot);
    }
}