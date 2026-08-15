using Task.Infrastructure.Persistence;

namespace Task.Tests;

public sealed class TaskPersistenceRuntimeTests
{
    [Fact]
    public async global::System.Threading.Tasks.Task MissingConfiguration_IsNotReadyWithoutConnecting()
    {
        await using var runtime = new TaskPersistenceRuntime(connectionString: null);

        var result = await runtime.CheckReadinessAsync();

        Assert.False(runtime.IsConfigured);
        Assert.False(result.Ready);
        Assert.Equal(TaskPersistenceReadinessCode.NotConfigured, result.Code);
        Assert.Equal(TaskPersistenceRuntime.ExpectedMigrationVersion, result.ExpectedMigrationVersion);
        Assert.Null(result.ActualMigrationVersion);
        Assert.DoesNotContain("Host=", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task InvalidConfiguration_FailsClosedWithoutLeakingConfiguration()
    {
        const string invalidConnection = "this is not a PostgreSQL connection string";
        await using var runtime = new TaskPersistenceRuntime(invalidConnection);

        var result = await runtime.CheckReadinessAsync();

        Assert.False(runtime.IsConfigured);
        Assert.False(result.Ready);
        Assert.Equal(TaskPersistenceReadinessCode.InvalidConfiguration, result.Code);
        Assert.DoesNotContain(invalidConnection, result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateStore_WithoutConfiguration_ThrowsSafeException()
    {
        using var runtime = new TaskPersistenceRuntime(connectionString: null);

        var exception = Assert.Throws<InvalidOperationException>(runtime.CreateTaskStore);

        Assert.DoesNotContain("Password", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonPositiveReadinessTimeout_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TaskPersistenceRuntime(connectionString: null, TimeSpan.Zero));
    }
}
