using Task.Application.Inventory;
using Task.Infrastructure.Persistence;
using Async = System.Threading.Tasks;

namespace Task.Tests;

public sealed partial class PostgresProductApiTests
{
    [RequiresPostgresFact]
    public async Async.Task UpgradeWindowsDeliveryRestoresCanonicalHistoryWithoutResettingInventory()
    {
        const string deliveredDeadlineChecksum = "7316049939299BF05212C67FE9B8FA4A1DC33D8E4193C399B5D45C8AB9660211";
        using var db = Database.Create(17)!;
        var inventory = TaskPersistenceMigrationCatalog.All.Single(migration => migration.Name == "material_inventory");

        // The delivery used the same warehouse table and the prior implementation of deadline reconciliation.
        db.Sql(TaskPersistenceMigrationCatalog.All.Single(migration => migration.Name == "preserve_at_deadline_delivery").Sql);
        db.Sql(inventory.Sql);
        db.Sql("""
            INSERT INTO infrastructure.schema_migrations (version, name, sha256)
            VALUES (18, 'at_deadline_reminder_delivery', $1), (19, 'material_inventory', $2);
            """, deliveredDeadlineChecksum, inventory.Sha256);
        var store = db.Runtime.CreateInventoryStore();
        var delivered = InventoryAccounting.Apply(new(0, InventoryData.Empty()), new(0, "demo"), new(2026, 11, 22), 0)
            with
        { ManagerId = db.OtherUser };
        Assert.True(await store.TrySaveAsync(db.Organization, delivered, default));

        var migrator = db.Runtime.CreateMigrator();
        Assert.Equal(TaskPersistenceMigrationStatus.Pending, (await migrator.InspectAsync()).Status);
        await migrator.ApplyPendingAsync();
        await migrator.ApplyPendingAsync();
        Assert.Equal(TaskPersistenceMigrationStatus.Current, (await migrator.InspectAsync()).Status);

        db.ApplyRuntimeGrants();
        var retained = await db.ApiRuntime.CreateInventoryStore().ReadAsync(db.Organization, default);
        Assert.Equal(delivered.Revision, retained.Revision);
        Assert.Equal(delivered.ManagerId, retained.ManagerId);
        Assert.Equal(InventoryAccounting.Calculate(delivered.Data).Stock, InventoryAccounting.Calculate(retained.Data).Stock);
        Assert.Equal(delivered.Data.Movements.Select(movement => movement.Id), retained.Data.Movements.Select(movement => movement.Id));

        await using var archive = db.Source.CreateCommand("""
            SELECT count(*)
            FROM infrastructure.windows_delivery_schema_migrations AS archived
            WHERE (version = 18 AND name = 'at_deadline_reminder_delivery' AND sha256 = $1)
               OR (version = 19 AND name = 'material_inventory' AND sha256 = $2
                   AND applied_at = (SELECT applied_at FROM infrastructure.schema_migrations WHERE version = 21));
            """);
        archive.Parameters.AddWithValue(deliveredDeadlineChecksum);
        archive.Parameters.AddWithValue(inventory.Sha256);
        Assert.Equal(2L, await archive.ExecuteScalarAsync());
    }

    [RequiresPostgresFact]
    public async Async.Task InventoryMigrationAndRuntimeStorePreserveOrgBoundariesAndConcurrentUpdates()
    {
        using var db = Database.Create(20)!;
        db.Runtime.CreateMigrator().ApplyPending();
        db.ApplyRuntimeGrants();

        var store = db.ApiRuntime.CreateInventoryStore();
        var initial = await store.ReadAsync(db.Organization, default);
        Assert.Equal(0, initial.Revision);
        Assert.Empty(initial.Data.Materials);
        var next = new StoredInventory(1, InventoryData.Empty() with
        {
            Materials = [new("m1", "Маски", "уп.", 5)],
        }, db.OtherUser);

        var writes = await Async.Task.WhenAll(store.TrySaveAsync(db.Organization, next, default),
            store.TrySaveAsync(db.Organization, next with { ManagerId = null }, default));
        Assert.Single(writes, written => written);
        var current = await db.ApiRuntime.CreateInventoryStore().ReadAsync(db.Organization, default);
        Assert.Equal("Маски", Assert.Single(current.Data.Materials).Name);
        Assert.Equal(writes[0] ? db.OtherUser : (Guid?)null, current.ManagerId);
        Assert.True(await store.TrySaveAsync(db.Organization, current with { Revision = 2, ManagerId = db.OtherUser }, default));
        Assert.False(await store.TrySaveAsync(db.Organization, current with { Revision = 2 }, default));
        Assert.Equal(db.OtherUser, (await store.ReadAsync(db.Organization, default)).ManagerId);
        Assert.Empty((await store.ReadAsync(Guid.NewGuid(), default)).Data.Materials);
    }
}
