using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Npgsql;
using Task.Application;
using Task.Domain;
using Task.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace Task.Tests;

public sealed class PostgresTaskAggregateStoreTests
{
    private const string ConnectionEnvironmentVariable = "TASK_POSTGRES_TEST_ADMIN_CONNECTION";
    private readonly ITestOutputHelper _output;

    public PostgresTaskAggregateStoreTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RealPostgres_MigrationRoundTripTenantBoundaryAndConcurrency()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            _output.WriteLine(
                $"NOT RUN: set {ConnectionEnvironmentVariable} to execute the real PostgreSQL integration gate.");
            return;
        }

        var databaseName = $"task_persistence_{Guid.NewGuid():N}";
        using var adminDataSource = NpgsqlDataSource.Create(adminConnectionString);
        CreateDatabase(adminDataSource, databaseName);

        try
        {
            var databaseConnection = new NpgsqlConnectionStringBuilder(adminConnectionString)
            {
                Database = databaseName,
            }.ConnectionString;

            using var dataSource = NpgsqlDataSource.Create(databaseConnection);
            await using var runtime = new TaskPersistenceRuntime(databaseConnection, TimeSpan.FromSeconds(10));

            var beforeMigration = await runtime.CheckReadinessAsync();
            Assert.False(beforeMigration.Ready);
            Assert.Equal(TaskPersistenceReadinessCode.MigrationsNotApplied, beforeMigration.Code);

            var migrator = new TaskPersistenceMigrator(dataSource);
            migrator.ApplyPending();
            migrator.ApplyPending();

            var compatible = await runtime.CheckReadinessAsync();
            Assert.True(compatible.Ready);
            Assert.Equal(TaskPersistenceReadinessCode.Ready, compatible.Code);
            Assert.Equal(TaskPersistenceRuntime.ExpectedMigrationVersion, compatible.ActualMigrationVersion);
            Assert.NotNull(compatible.ServerVersionNumber);
            Assert.True(compatible.ServerVersionNumber >= 160000);

            await AssertApiReportsReady(databaseConnection);

            var organizationId = Guid.NewGuid();
            var otherOrganizationId = Guid.NewGuid();
            SeedOrganization(dataSource, organizationId);

            var taskId = Guid.NewGuid();
            var creatorId = Guid.NewGuid();
            var editorId = Guid.NewGuid();
            var createdAt = new DateTimeOffset(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);
            var store = new PostgresTaskAggregateStore(dataSource);
            var original = TaskAggregate.Create(taskId, organizationId, creatorId, "Persist me", createdAt);

            var invalidVersionOne = TaskAggregate.Reconstitute(
                SyncableEntityMetadata.Reconstitute(
                    Guid.NewGuid(),
                    organizationId,
                    creatorId,
                    createdAt,
                    creatorId,
                    createdAt,
                    1,
                    EntityLifecycleState.Active,
                    null,
                    null,
                    null,
                    null),
                "Invalid initial state",
                TaskWorkStatus.InProgress,
                null,
                null,
                TaskPriority.Normal,
                TaskSchedule.Create(null, null));
            Assert.Throws<ArgumentException>(() => store.Add(invalidVersionOne));

            store.Add(original);

            Assert.Null(store.Get(taskId, otherOrganizationId));
            var loaded = store.Get(taskId, organizationId);
            Assert.NotNull(loaded);
            Assert.Equal(original.Title, loaded.Title);
            Assert.Equal(1, loaded.Metadata.Version);

            var scheduled = loaded.Reschedule(
                editorId,
                TaskSchedule.Create(createdAt.AddHours(1), createdAt.AddHours(2)),
                createdAt.AddMinutes(1));
            store.Save(scheduled, 1);

            var reprioritized = scheduled.ChangePriority(editorId, TaskPriority.High, createdAt.AddMinutes(2));
            store.Save(reprioritized, 2);

            var stale = original.Start(editorId, createdAt.AddMinutes(3));
            var conflict = Assert.Throws<TaskLifecycleConcurrencyException>(() => store.Save(stale, 1));
            Assert.Equal(3, conflict.ActualVersion);

            var started = reprioritized.Start(editorId, createdAt.AddMinutes(3));
            store.Save(started, 3);
            var completed = started.Complete(editorId, createdAt.AddMinutes(4));
            store.Save(completed, 4);
            var trashed = completed.MoveToTrash(editorId, createdAt.AddMinutes(5));
            store.Save(trashed, 5);

            var roundTripped = store.Get(taskId, organizationId);
            Assert.NotNull(roundTripped);
            Assert.Equal(6, roundTripped.Metadata.Version);
            Assert.Equal(EntityLifecycleState.Trashed, roundTripped.Metadata.LifecycleState);
            Assert.Equal(EntityLifecycleState.Active, roundTripped.Metadata.LifecycleStateBeforeTrash);
            Assert.Equal(TaskWorkStatus.Completed, roundTripped.WorkStatus);
            Assert.Equal(TaskPriority.High, roundTripped.Priority);
            Assert.Equal(scheduled.Schedule, roundTripped.Schedule);
            Assert.Equal(completed.CompletedAtUtc, roundTripped.CompletedAtUtc);
            Assert.Equal(editorId, roundTripped.CompletedBy);
            Assert.Equal(createdAt.AddMinutes(5), roundTripped.Metadata.DeletedAtUtc);
            Assert.Equal(editorId, roundTripped.Metadata.DeletedBy);

            using var checksumCommand = dataSource.CreateCommand(
                "SELECT count(*), min(length(sha256)) FROM infrastructure.schema_migrations;");
            using var checksumReader = checksumCommand.ExecuteReader();
            checksumReader.Read();
            Assert.Equal(1, checksumReader.GetInt64(0));
            Assert.Equal(64, checksumReader.GetInt32(1));
            checksumReader.Close();

            using var corruptChecksum = dataSource.CreateCommand(
                "UPDATE infrastructure.schema_migrations SET sha256 = repeat('0', 64) WHERE version = 1;");
            Assert.Equal(1, corruptChecksum.ExecuteNonQuery());

            var incompatible = await runtime.CheckReadinessAsync();
            Assert.False(incompatible.Ready);
            Assert.Equal(TaskPersistenceReadinessCode.SchemaVersionMismatch, incompatible.Code);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            DropDatabase(adminDataSource, databaseName);
        }
    }

    private static void CreateDatabase(NpgsqlDataSource adminDataSource, string databaseName)
    {
        using var command = adminDataSource.CreateCommand($"CREATE DATABASE {databaseName};");
        command.ExecuteNonQuery();
    }

    private static void DropDatabase(NpgsqlDataSource adminDataSource, string databaseName)
    {
        using var command = adminDataSource.CreateCommand($"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE);");
        command.ExecuteNonQuery();
    }

    private static void SeedOrganization(NpgsqlDataSource dataSource, Guid organizationId)
    {
        using var command = dataSource.CreateCommand(
            """
            INSERT INTO core.organizations (id, code, name, default_time_zone)
            VALUES ($1, $2, $3, $4);
            """);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = $"org-{organizationId:N}" });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = "Integration Test Organization" });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = "Europe/Minsk" });
        command.ExecuteNonQuery();
    }

    private static async global::System.Threading.Tasks.Task AssertApiReportsReady(string connectionString)
    {
        var apiDll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Task.Api", "bin", "Debug", "net10.0", "Task.Api.dll"));
        Assert.True(File.Exists(apiDll), $"Build Task.Api before the real PostgreSQL gate. Missing: {apiDll}");

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var baseAddress = $"http://127.0.0.1:{port}";
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(apiDll);
        startInfo.Environment["ASPNETCORE_URLS"] = baseAddress;
        startInfo.Environment["ConnectionStrings__TaskDatabase"] = connectionString;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Task.Api process could not be started.");
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(baseAddress) };
            HttpResponseMessage? liveResponse = null;
            for (var attempt = 0; attempt < 50 && !process.HasExited; attempt++)
            {
                try
                {
                    liveResponse = await client.GetAsync("/health/live");
                    if (liveResponse.IsSuccessStatusCode)
                    {
                        break;
                    }
                }
                catch (HttpRequestException)
                {
                }

                liveResponse?.Dispose();
                liveResponse = null;
                await global::System.Threading.Tasks.Task.Delay(100);
            }

            Assert.NotNull(liveResponse);
            using (liveResponse)
            {
                Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
            }

            using var readinessResponse = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.OK, readinessResponse.StatusCode);
            using var document = JsonDocument.Parse(await readinessResponse.Content.ReadAsStringAsync());
            Assert.Equal("Ready", document.RootElement.GetProperty("status").GetString());
            var details = document.RootElement.GetProperty("details");
            Assert.True(details.GetProperty("ready").GetBoolean());
            Assert.Equal("Ready", details.GetProperty("persistenceCode").GetString());
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync();
        }
    }
}
