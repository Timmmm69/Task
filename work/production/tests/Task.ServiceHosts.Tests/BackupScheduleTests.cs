using Task.BackupAgent;

namespace Task.ServiceHosts.Tests;

public sealed class BackupScheduleTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "task-backup-test-" + Guid.NewGuid());
    private readonly TestClock clock = new();
    private readonly RecordingRunner runner = new();

    [Fact]
    public async global::System.Threading.Tasks.Task FirstBackup_IsDurable_AndNextDayRunsOnce()
    {
        var schedule = Create();
        await schedule.TickAsync(default);
        Assert.Equal(new[] { "backup" }, runner.Calls);
        var restarted = Create();
        await restarted.TickAsync(default);
        Assert.Equal(new[] { "backup", "verify" }, runner.Calls);
        clock.Advance(TimeSpan.FromDays(1));
        await restarted.TickAsync(default);
        await restarted.TickAsync(default);
        Assert.Equal(new[] { "backup", "verify", "backup" }, runner.Calls);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Failure_PreservesLastSuccess_AndRetriesAfterDelay()
    {
        var schedule = Create();
        await schedule.TickAsync(default);
        var success = schedule.State.LastBackup;
        clock.Advance(TimeSpan.FromDays(1));
        runner.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => schedule.TickAsync(default));
        Assert.Equal(success, schedule.State.LastBackup);
        Assert.Equal("backup", Create().State.FailedOperation);
        await schedule.TickAsync(default);
        Assert.Equal(2, runner.Calls.Count);
        clock.Advance(TimeSpan.FromMinutes(5));
        runner.Fail = false;
        await schedule.TickAsync(default);
        Assert.Null(schedule.State.FailedOperation);
        Assert.Equal(3, runner.Calls.Count);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task CheckRunsEveryFiveMinutes_WithoutExtraBackup()
    {
        var schedule = Create();
        await schedule.TickAsync(default);
        await schedule.TickAsync(default);
        clock.Advance(TimeSpan.FromMinutes(5));
        await schedule.TickAsync(default);
        Assert.Equal(new[] { "backup", "verify", "check" }, runner.Calls);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Cancellation_DoesNotPublishSuccess()
    {
        var schedule = Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => schedule.TickAsync(cancellation.Token));
        Assert.Null(schedule.State.LastBackup);
        Assert.False(File.Exists(Path.Combine(directory, "status.json")));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task CorruptState_IsPreservedAndTriggersFreshBackup()
    {
        Create();
        File.WriteAllText(Path.Combine(directory, "status.json"), "broken");
        var recovered = Create();
        Assert.True(recovered.RecoveredInvalidState);
        Assert.Single(Directory.GetFiles(directory, "invalid-status-*.json"));
        await recovered.TickAsync(default);
        Assert.Equal(new[] { "backup" }, runner.Calls);
    }

    [Theory]
    [InlineData("LastBackup")]
    [InlineData("LastRestoreTest")]
    [InlineData("LastCheck")]
    public async global::System.Threading.Tasks.Task FutureSuccess_IsRejectedAndCannotSuppressProtection(string field)
    {
        Create();
        File.WriteAllText(Path.Combine(directory, "status.json"),
            System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, DateTimeOffset>
            {
                [field] = clock.GetUtcNow().AddDays(1)
            }));
        var recovered = Create();
        Assert.True(recovered.RecoveredInvalidState);
        await recovered.TickAsync(default);
        Assert.Equal(new[] { "backup" }, runner.Calls);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task WeeklyRestoreTest_RunsEvenWithDailyBackups()
    {
        var schedule = Create();
        await schedule.TickAsync(default);
        await schedule.TickAsync(default);
        for (var day = 1; day <= 7; day++)
        {
            clock.Advance(TimeSpan.FromDays(1));
            await schedule.TickAsync(default);
            await schedule.TickAsync(default);
        }

        Assert.Equal(8, runner.Calls.Count(call => call == "backup"));
        Assert.Equal(2, runner.Calls.Count(call => call == "verify"));
        Assert.Equal(clock.GetUtcNow(), schedule.State.LastRestoreTest);
    }

    [Theory]
    [InlineData(0, 300, 7)]
    [InlineData(24, 300, 7)]
    [InlineData(1, 901, 7)]
    [InlineData(1, 300, 8)]
    public void UnsafeConfiguration_IsRejected(int hour, int check, int days)
    {
        var options = new BackupOptions
        {
            RunnerPath = Path.GetFullPath("runner"),
            StateDirectory = directory,
            BackupHourUtc = hour == 0 ? -1 : hour,
            CheckIntervalSeconds = check,
            RestoreTestDays = days
        };
        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    private BackupSchedule Create()
    {
        var result = new BackupSchedule(new BackupOptions { StateDirectory = directory }, runner, clock);
        result.Load();
        return result;
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 9, 3, 2, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }

    private sealed class RecordingRunner : IBackupCommandRunner
    {
        public List<string> Calls { get; } = [];
        public bool Fail { get; set; }
        public global::System.Threading.Tasks.Task RunAsync(string operation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(operation);
            if (Fail) throw new IOException("simulated unavailable repository");
            return global::System.Threading.Tasks.Task.CompletedTask;
        }
    }
}
