using System.Text.Json;
using Task.Application.Inventory;
using Task.Desktop.Inventory;
using Async = System.Threading.Tasks;

namespace Task.Desktop.Personal;

internal sealed class PersonalInventoryClient(PersonalTaskStore store, TimeProvider clock) : IDesktopInventoryClient
{
    public bool IsCorporate => false;

    public Async.Task<InventorySnapshot> ReadAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Async.Task.FromResult(store.ReadInventory().Project(store.LocalActorId, administrator: true));
    }

    public Async.Task<InventorySnapshot> ApplyAsync(InventoryCommand command, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var now = clock.GetLocalNow();
        var next = store.ApplyInventory(command, DateOnly.FromDateTime(now.DateTime), now.ToUnixTimeMilliseconds());
        return Async.Task.FromResult(next.Project(store.LocalActorId, administrator: true));
    }

    public Async.Task<InventoryManager[]> ManagersAsync(CancellationToken token) => Async.Task.FromResult(Array.Empty<InventoryManager>());
}

public sealed partial class PersonalTaskStore
{
    private const string InventoryMetadataKey = "MaterialInventory";

    internal StoredInventory ReadInventory() => Locked(() =>
    {
        using var read = Command("SELECT value FROM personal_metadata WHERE key=$key;", null, ("$key", InventoryMetadataKey));
        return read.ExecuteScalar() is string json
            ? JsonSerializer.Deserialize<StoredInventory>(json, InventoryJson.Options)
                ?? throw new InventoryValidationException("Личные данные склада повреждены.")
            : new(0, InventoryData.Empty(), null);
    });

    internal StoredInventory ApplyInventory(InventoryCommand command, DateOnly today, long timestamp) => Locked(() =>
    {
        var next = InventoryAccounting.Apply(ReadInventory(), command, today, timestamp);
        using var write = Command("""
            INSERT INTO personal_metadata(key,value) VALUES($key,$value)
            ON CONFLICT(key) DO UPDATE SET value=excluded.value;
            """, null, ("$key", InventoryMetadataKey), ("$value", JsonSerializer.Serialize(next, InventoryJson.Options)));
        write.ExecuteNonQuery();
        return next;
    });
}
