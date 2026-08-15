using Task.Infrastructure.Persistence;

namespace Task.ServiceHosts.Tests;

public sealed class DatabaseMigratorCommandTests
{
    [Theory]
    [InlineData("status")]
    [InlineData("STATUS")]
    public async global::System.Threading.Tasks.Task Status_Current_ReturnsReady(string command)
    {
        var result = await RunAsync([command], new FakeOperations(Current()));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("TASK_DB_MIGRATOR code=Ready expectedVersion=1 actualVersion=1", result.Stdout.Trim());
        Assert.Empty(result.Stderr);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public async global::System.Threading.Tasks.Task Help_ReturnsUsage(string argument)
    {
        var result = await RunAsync([argument], operations: null);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(DatabaseMigratorCommand.Usage, result.Stdout.Trim());
        Assert.Empty(result.Stderr);
    }

    [Theory]
    [InlineData()]
    [InlineData("unknown")]
    [InlineData("status", "extra")]
    public async global::System.Threading.Tasks.Task InvalidArguments_ReturnUsageError(params string[] arguments)
    {
        var result = await RunAsync(arguments, new FakeOperations(Current()));

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("TASK_DB_MIGRATOR code=InvalidArguments", result.Stderr.Trim());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task MissingConfiguration_ReturnsThree()
    {
        var result = await RunAsync(["status"], operations: null);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("TASK_DB_MIGRATOR code=NotConfigured", result.Stderr.Trim());
    }

    [Theory]
    [InlineData(TaskPersistenceMigrationStatus.HistoryMissing, 6, "MigrationsRequired")]
    [InlineData(TaskPersistenceMigrationStatus.Pending, 6, "MigrationsRequired")]
    [InlineData(TaskPersistenceMigrationStatus.UnsupportedServerVersion, 5, "UnsupportedServerVersion")]
    [InlineData(TaskPersistenceMigrationStatus.SchemaObjectsMissing, 7, "SchemaObjectsMissing")]
    [InlineData(TaskPersistenceMigrationStatus.HistoryMismatch, 7, "HistoryMismatch")]
    public async global::System.Threading.Tasks.Task Status_MapsEveryInspectionState(
        TaskPersistenceMigrationStatus status,
        int expectedExitCode,
        string expectedCode)
    {
        var inspection = new TaskPersistenceMigrationInspection(status, 160000, 1, null, 0);
        var result = await RunAsync(["status"], new FakeOperations(inspection));

        Assert.Equal(expectedExitCode, result.ExitCode);
        Assert.Contains($"code={expectedCode}", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Apply_Current_DoesNotInvokeApply()
    {
        var operations = new FakeOperations(Current());

        var result = await RunAsync(["apply"], operations);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("code=AlreadyCurrent", result.Stdout, StringComparison.Ordinal);
        Assert.Equal(0, operations.ApplyCalls);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Apply_Pending_PerformsMandatoryPostCheck()
    {
        var pending = new TaskPersistenceMigrationInspection(
            TaskPersistenceMigrationStatus.Pending, 160000, 1, null, 0);
        var operations = new FakeOperations(pending, Current());

        var result = await RunAsync(["APPLY"], operations);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("code=Applied", result.Stdout, StringComparison.Ordinal);
        Assert.Equal(1, operations.ApplyCalls);
        Assert.Equal(2, operations.InspectCalls);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Apply_FailedPostCheck_ReturnsNine()
    {
        var pending = new TaskPersistenceMigrationInspection(
            TaskPersistenceMigrationStatus.Pending, 160000, 1, null, 0);
        var operations = new FakeOperations(pending, pending);

        var result = await RunAsync(["apply"], operations);

        Assert.Equal(9, result.ExitCode);
        Assert.Contains("code=PostCheckFailed", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Cancellation_ReturnsOneThirty()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var operations = new FakeOperations(new OperationCanceledException(cancellation.Token));

        var result = await RunAsync(["status"], operations, cancellation.Token);

        Assert.Equal(130, result.ExitCode);
        Assert.Contains("code=Cancelled", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task UnexpectedException_DoesNotLeakCredentialsOrRawText()
    {
        const string secret = "Host=secret;Username=admin;Password=hunter2";
        var operations = new FakeOperations(new InvalidOperationException(secret));

        var result = await RunAsync(["status"], operations);

        Assert.Equal(9, result.ExitCode);
        Assert.DoesNotContain(secret, result.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task LockUnavailable_ReturnsEight()
    {
        var operations = new FakeOperations(
            new TaskPersistenceMigrationException(TaskPersistenceMigrationError.LockUnavailable));

        var result = await RunAsync(["status"], operations);

        Assert.Equal(8, result.ExitCode);
        Assert.Contains("code=LockUnavailable", result.Stderr, StringComparison.Ordinal);
    }

    private static TaskPersistenceMigrationInspection Current() =>
        new(TaskPersistenceMigrationStatus.Current, 160000, 1, 1, 1);

    private static async Task<CommandResult> RunAsync(
        string[] args,
        IDatabaseMigrationOperations? operations,
        CancellationToken cancellationToken = default)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var exitCode = await DatabaseMigratorCommand.RunAsync(
            args, operations, stdout, stderr, cancellationToken);
        return new(exitCode, stdout.ToString(), stderr.ToString());
    }

    private sealed class FakeOperations : IDatabaseMigrationOperations
    {
        private readonly Queue<TaskPersistenceMigrationInspection> _inspections = new();
        private readonly Exception? _inspectionException;

        public FakeOperations(params TaskPersistenceMigrationInspection[] inspections)
        {
            foreach (var inspection in inspections)
            {
                _inspections.Enqueue(inspection);
            }
        }

        public FakeOperations(Exception inspectionException)
        {
            _inspectionException = inspectionException;
        }

        public int InspectCalls { get; private set; }

        public int ApplyCalls { get; private set; }

        public Task<TaskPersistenceMigrationInspection> InspectAsync(CancellationToken cancellationToken)
        {
            InspectCalls++;
            if (_inspectionException is not null)
            {
                return global::System.Threading.Tasks.Task.FromException<TaskPersistenceMigrationInspection>(
                    _inspectionException);
            }

            return global::System.Threading.Tasks.Task.FromResult(_inspections.Dequeue());
        }

        public global::System.Threading.Tasks.Task ApplyPendingAsync(CancellationToken cancellationToken)
        {
            ApplyCalls++;
            return global::System.Threading.Tasks.Task.CompletedTask;
        }
    }

    private sealed record CommandResult(int ExitCode, string Stdout, string Stderr);
}
