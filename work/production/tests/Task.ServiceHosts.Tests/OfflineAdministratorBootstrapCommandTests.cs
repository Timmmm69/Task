using Task.Infrastructure.Identity;
using Task.Infrastructure.Persistence;

namespace Task.ServiceHosts.Tests;

public sealed class OfflineAdministratorBootstrapCommandTests
{
    [Fact]
    public async global::System.Threading.Tasks.Task BootstrapAdmin_DelegatesToOfflineOperationAndDoesNotWriteSecrets()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var bootstrap = new FakeBootstrapOperations(new OfflineAdministratorBootstrapResult(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), "admin"));

        var exitCode = await DatabaseMigratorCommand.RunAsync(
            ["bootstrap-admin"], new FakeMigrationOperations(), output, error, CancellationToken.None, bootstrap);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, bootstrap.CallCount);
        Assert.Contains("code=BootstrapCompleted", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("password", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task BootstrapAdmin_RefusesRepeatedBootstrap()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var bootstrap = new FakeBootstrapOperations(new OfflineAdministratorBootstrapException(OfflineAdministratorBootstrapError.AlreadyCompleted));

        var exitCode = await DatabaseMigratorCommand.RunAsync(
            ["bootstrap-admin"], new FakeMigrationOperations(), output, error, CancellationToken.None, bootstrap);

        Assert.Equal(7, exitCode);
        Assert.Contains("code=BootstrapAlreadyCompleted", error.ToString(), StringComparison.Ordinal);
    }

    private sealed class FakeMigrationOperations : IDatabaseMigrationOperations
    {
        public global::System.Threading.Tasks.Task<TaskPersistenceMigrationInspection> InspectAsync(CancellationToken cancellationToken) =>
            global::System.Threading.Tasks.Task.FromResult(new TaskPersistenceMigrationInspection(TaskPersistenceMigrationStatus.Current, 160000, 2, 2, 2));

        public global::System.Threading.Tasks.Task ApplyPendingAsync(CancellationToken cancellationToken) =>
            global::System.Threading.Tasks.Task.CompletedTask;
    }

    private sealed class FakeBootstrapOperations : IOfflineBootstrapOperations
    {
        private readonly OfflineAdministratorBootstrapResult? _result;
        private readonly Exception? _exception;

        public FakeBootstrapOperations(OfflineAdministratorBootstrapResult result) => _result = result;
        public FakeBootstrapOperations(Exception exception) => _exception = exception;
        public int CallCount { get; private set; }

        public global::System.Threading.Tasks.Task<OfflineAdministratorBootstrapResult> BootstrapAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return _exception is not null
                ? global::System.Threading.Tasks.Task.FromException<OfflineAdministratorBootstrapResult>(_exception)
                : global::System.Threading.Tasks.Task.FromResult(_result!);
        }
    }
}
