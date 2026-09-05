using Task.Infrastructure.Persistence;

namespace Task.Tests;

public sealed class TaskPersistenceMigrationHistoryTests
{
    [Fact]
    public void Catalog_ExpectsUserReadPermissionVersionTwelve()
    {
        Assert.Equal(13, TaskPersistenceRuntime.ExpectedMigrationVersion);
        Assert.Equal("object_authorization", TaskPersistenceMigrationCatalog.All[^1].Name);
    }

    [Fact]
    public void EmptyExistingHistory_IsPending()
    {
        var status = TaskPersistenceMigrator.EvaluateHistory([]);

        Assert.Equal(TaskPersistenceMigrationStatus.Pending, status);
    }

    [Fact]
    public void ExactCatalogPrefixAtLatestVersion_IsCurrent()
    {
        var applied = TaskPersistenceMigrationCatalog.All
            .Select(migration => new TaskPersistenceAppliedMigration(
                migration.Version,
                migration.Name,
                migration.Sha256))
            .ToArray();

        var status = TaskPersistenceMigrator.EvaluateHistory(applied);

        Assert.Equal(TaskPersistenceMigrationStatus.Current, status);
    }

    [Fact]
    public void WrongName_IsHistoryMismatch()
    {
        var migration = TaskPersistenceMigrationCatalog.All[0];
        var applied = new TaskPersistenceAppliedMigration(
            migration.Version,
            "changed_name",
            migration.Sha256);

        var status = TaskPersistenceMigrator.EvaluateHistory([applied]);

        Assert.Equal(TaskPersistenceMigrationStatus.HistoryMismatch, status);
    }

    [Fact]
    public void WrongChecksum_IsHistoryMismatch()
    {
        var migration = TaskPersistenceMigrationCatalog.All[0];
        var applied = new TaskPersistenceAppliedMigration(
            migration.Version,
            migration.Name,
            new string('0', 64));

        var status = TaskPersistenceMigrator.EvaluateHistory([applied]);

        Assert.Equal(TaskPersistenceMigrationStatus.HistoryMismatch, status);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(100)]
    public void GapOrUnknownFutureVersion_IsHistoryMismatch(int version)
    {
        var migration = TaskPersistenceMigrationCatalog.All[0];
        var applied = new TaskPersistenceAppliedMigration(
            version,
            migration.Name,
            migration.Sha256);

        var status = TaskPersistenceMigrator.EvaluateHistory([applied]);

        Assert.Equal(TaskPersistenceMigrationStatus.HistoryMismatch, status);
    }

    [Fact]
    public void ExtraHistoryRow_IsHistoryMismatch()
    {
        var migration = TaskPersistenceMigrationCatalog.All[0];
        TaskPersistenceAppliedMigration[] applied =
        [
            new(migration.Version, migration.Name, migration.Sha256),
            new(2, "future", new string('A', 64)),
        ];

        var status = TaskPersistenceMigrator.EvaluateHistory(applied);

        Assert.Equal(TaskPersistenceMigrationStatus.HistoryMismatch, status);
    }
}
