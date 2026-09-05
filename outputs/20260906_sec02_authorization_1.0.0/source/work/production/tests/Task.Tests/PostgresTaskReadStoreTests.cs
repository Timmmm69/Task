using Npgsql;
using Task.Application;
using Task.Domain;
using Task.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace Task.Tests;

public sealed class PostgresTaskReadStoreTests
{
    private const string ConnectionEnvironmentVariable = "TASK_POSTGRES_TEST_ADMIN_CONNECTION";
    private readonly ITestOutputHelper _output;

    public PostgresTaskReadStoreTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RealPostgres_ActiveProjectionIsolationAndCursorPagination()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            _output.WriteLine(
                $"NOT RUN: set {ConnectionEnvironmentVariable} to execute the real PostgreSQL Task read gate.");
            return;
        }

        var databaseName = $"task_read_{Guid.NewGuid():N}";
        using var adminDataSource = NpgsqlDataSource.Create(adminConnectionString);
        CreateDatabase(adminDataSource, databaseName);

        try
        {
            var databaseConnection = new NpgsqlConnectionStringBuilder(adminConnectionString)
            {
                Database = databaseName,
            }.ConnectionString;

            await using (var dataSource = NpgsqlDataSource.Create(databaseConnection))
            {
                await new TaskPersistenceMigrator(dataSource).ApplyPendingAsync();

                var organizationId = Guid.NewGuid();
                var otherOrganizationId = Guid.NewGuid();
                var emptyOrganizationId = Guid.NewGuid();
                await SeedOrganizationAsync(dataSource, organizationId);
                await SeedOrganizationAsync(dataSource, otherOrganizationId);
                await SeedOrganizationAsync(dataSource, emptyOrganizationId);

                var creatorId = Guid.NewGuid();
                var userAccountId = creatorId;
                var aggregateStore = new PostgresTaskAggregateStore(dataSource);
                var active = new List<TaskAggregate>();
                var baseTime = new DateTimeOffset(2025, 8, 20, 8, 0, 0, TimeSpan.Zero);

                for (var index = 0; index < 55; index++)
                {
                    var createdAt = baseTime.AddMinutes(index);
                    var aggregate = TaskAggregate.Create(
                        Guid.NewGuid(),
                        organizationId,
                        creatorId,
                        $"Active task {index:00}",
                        createdAt);
                    aggregateStore.Add(aggregate);

                    if (index == 54)
                    {
                        var scheduled = aggregate.Reschedule(
                            creatorId,
                            TaskSchedule.Create(createdAt.AddHours(1), createdAt.AddHours(2)),
                            createdAt.AddSeconds(10));
                        aggregateStore.Save(scheduled, expectedVersion: 1);
                        var prioritized = scheduled.ChangePriority(
                            creatorId,
                            TaskPriority.High,
                            createdAt.AddSeconds(20));
                        aggregateStore.Save(prioritized, expectedVersion: 2);
                        aggregate = prioritized.Start(creatorId, createdAt.AddSeconds(30));
                        aggregateStore.Save(aggregate, expectedVersion: 3);
                    }

                    active.Add(aggregate);
                }

                var archived = TaskAggregate.Create(
                    Guid.NewGuid(), organizationId, creatorId, "Archived task", baseTime.AddDays(1));
                aggregateStore.Add(archived);
                var cancelledForArchive = archived.Cancel(
                    creatorId,
                    baseTime.AddDays(1).AddSeconds(30));
                aggregateStore.Save(cancelledForArchive, expectedVersion: 1);
                aggregateStore.Save(
                    cancelledForArchive.Archive(creatorId, baseTime.AddDays(1).AddMinutes(1)),
                    expectedVersion: 2);

                var trashed = TaskAggregate.Create(
                    Guid.NewGuid(), organizationId, creatorId, "Trashed task", baseTime.AddDays(1));
                aggregateStore.Add(trashed);
                var cancelledForTrash = trashed.Cancel(
                    creatorId,
                    baseTime.AddDays(1).AddSeconds(30));
                aggregateStore.Save(cancelledForTrash, expectedVersion: 1);
                aggregateStore.Save(
                    cancelledForTrash.MoveToTrash(creatorId, baseTime.AddDays(1).AddMinutes(1)),
                    expectedVersion: 2);

                var foreign = TaskAggregate.Create(
                    Guid.NewGuid(), otherOrganizationId, creatorId, "Foreign task", baseTime.AddDays(2));
                aggregateStore.Add(foreign);

                var readStore = new PostgresTaskReadStore(dataSource);
                var detailSource = active[^1];
                var detail = await readStore.GetByIdAsync(organizationId, detailSource.Metadata.Id);

                Assert.NotNull(detail);
                Assert.Equal(detailSource.Metadata.Id, detail.Id);
                Assert.Equal(organizationId, detail.OrganizationId);
                Assert.Equal(4, detail.Version);
                Assert.Equal(detailSource.Metadata.CreatedAtUtc, detail.CreatedAtUtc);
                Assert.Equal(detailSource.Metadata.UpdatedAtUtc, detail.UpdatedAtUtc);
                Assert.Equal(TimeSpan.Zero, detail.CreatedAtUtc.Offset);
                Assert.Equal(TimeSpan.Zero, detail.UpdatedAtUtc.Offset);
                Assert.Equal(detailSource.Title, detail.Title);
                Assert.Equal(creatorId, detail.AuthorUserId);
                Assert.Equal(TaskWorkStatus.InProgress, detail.Status);
                Assert.Equal(TaskPriority.High, detail.Priority);
                Assert.Equal(detailSource.Schedule.StartsAtUtc, detail.StartAtUtc);
                Assert.Equal(detailSource.Schedule.DeadlineUtc, detail.DeadlineAtUtc);

                Assert.Null(await readStore.GetByIdAsync(otherOrganizationId, detailSource.Metadata.Id));
                Assert.Null(await readStore.GetByIdAsync(organizationId, archived.Metadata.Id));
                Assert.Null(await readStore.GetByIdAsync(organizationId, trashed.Metadata.Id));

                var request = new TaskReadPageRequest(
                    organizationId,
                    userAccountId,
                    AuthorizationScopeVersion: 7);
                var firstPage = await readStore.GetPageAsync(request);

                Assert.Equal(PostgresTaskReadStore.PageSize, firstPage.Items.Count);
                Assert.NotNull(firstPage.NextCursor);
                Assert.Null(firstPage.Total);
                Assert.All(firstPage.Items, item =>
                {
                    Assert.Equal(organizationId, item.OrganizationId);
                    Assert.Equal(TimeSpan.Zero, item.CreatedAtUtc.Offset);
                    Assert.Equal(TimeSpan.Zero, item.UpdatedAtUtc.Offset);
                });
                Assert.DoesNotContain(firstPage.Items, item =>
                    item.Id == archived.Metadata.Id ||
                    item.Id == trashed.Metadata.Id ||
                    item.Id == foreign.Metadata.Id);
                AssertStableDescendingOrder(firstPage.Items);

                var secondPage = await readStore.GetPageAsync(request with
                {
                    Cursor = firstPage.NextCursor,
                });
                Assert.Equal(5, secondPage.Items.Count);
                Assert.Null(secondPage.NextCursor);
                AssertStableDescendingOrder(secondPage.Items);

                var combined = firstPage.Items.Concat(secondPage.Items).ToArray();
                Assert.Equal(55, combined.Length);
                Assert.Equal(55, combined.Select(item => item.Id).Distinct().Count());
                Assert.Equal(
                    active.Select(item => item.Metadata.Id).OrderBy(id => id),
                    combined.Select(item => item.Id).OrderBy(id => id));

                var emptyPage = await readStore.GetPageAsync(new TaskReadPageRequest(
                    emptyOrganizationId,
                    userAccountId,
                    AuthorizationScopeVersion: 7));
                Assert.Empty(emptyPage.Items);
                Assert.Null(emptyPage.NextCursor);

                var foreignPage = await readStore.GetPageAsync(new TaskReadPageRequest(
                    otherOrganizationId,
                    userAccountId,
                    AuthorizationScopeVersion: 7));
                Assert.Equal(foreign.Metadata.Id, Assert.Single(foreignPage.Items).Id);

                await Assert.ThrowsAsync<TaskReadCursorException>(() => readStore.GetPageAsync(
                    request with { Cursor = "%%%" }));
                await Assert.ThrowsAsync<TaskReadCursorException>(() => readStore.GetPageAsync(
                    request with
                    {
                        OrganizationId = otherOrganizationId,
                        Cursor = firstPage.NextCursor,
                    }));
                await Assert.ThrowsAsync<TaskReadCursorException>(() => readStore.GetPageAsync(
                    request with
                    {
                        UserAccountId = Guid.NewGuid(),
                        Cursor = firstPage.NextCursor,
                    }));
                await Assert.ThrowsAsync<TaskReadCursorException>(() => readStore.GetPageAsync(
                    request with
                    {
                        AuthorizationScopeVersion = 8,
                        Cursor = firstPage.NextCursor,
                    }));

                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    readStore.GetPageAsync(request, cancelled.Token));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    readStore.GetByIdAsync(organizationId, detailSource.Metadata.Id, cancelled.Token));
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            DropDatabase(adminDataSource, databaseName);
        }
    }

    private static void AssertStableDescendingOrder(IReadOnlyList<TaskReadProjection> items)
    {
        for (var index = 1; index < items.Count; index++)
        {
            var previous = items[index - 1];
            var current = items[index];
            Assert.True(
                previous.UpdatedAtUtc > current.UpdatedAtUtc ||
                (previous.UpdatedAtUtc == current.UpdatedAtUtc &&
                 string.CompareOrdinal(previous.Id.ToString("D"), current.Id.ToString("D")) > 0),
                $"Unexpected order between {previous.Id} and {current.Id}.");
        }
    }

    private static async global::System.Threading.Tasks.Task SeedOrganizationAsync(
        NpgsqlDataSource dataSource,
        Guid organizationId)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO core.organizations (id, code, name, default_time_zone)
            VALUES ($1, $2, $3, $4);
            """);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = $"org-{organizationId:N}" });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = "Task Read Test Organization" });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = "Europe/Minsk" });
        await command.ExecuteNonQueryAsync();
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
}
