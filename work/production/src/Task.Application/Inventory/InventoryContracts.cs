using System.Text.Json;
using System.Text.Json.Serialization;

namespace Task.Application.Inventory;

// The backup shape also accepts the version 1 files exported by the HTML demo.
public sealed record InventoryData(int Version, InventoryMaterial[] Materials, InventorySupplier[] Suppliers,
    string[] Units, InventoryMovement[] Movements, bool Demo)
{
    public static InventoryData Empty() => new(1, [], [], ["шт.", "уп.", "г", "кг", "мл", "л", "пар", "набор"], [], false);
}

public sealed record InventoryMaterial(string Id, string Name, string Unit, decimal Threshold);
public sealed record InventorySupplier(string Id, string Name);
public sealed record InventoryLine(string Id, string MaterialId, decimal Quantity, decimal? UnitPrice = null, string? SupplierId = null);
public sealed record InventoryMovement(string Id, string Type, DateOnly Date, long CreatedAt, InventoryLine[] Lines,
    string? SupplierId = null, string? Invoice = null, string? Note = null);

public sealed record InventoryCommand(long Revision, string Action, InventoryMaterial? Material = null,
    InventorySupplier? Supplier = null, string? Unit = null, InventoryMovement? Movement = null,
    string? MovementId = null, InventoryData? Backup = null, Guid? ManagerId = null)
{
    public bool RequiresAdministrator => Action is "cancel" or "import" or "demo" or "manager";
}

public sealed record InventoryStock(InventoryMaterial Material, decimal Quantity, decimal? Value = null)
{
    public bool IsOut => Quantity == 0;
    public bool NeedsAttention => IsOut || Material.Threshold > 0 && Quantity <= Material.Threshold * 1.5m;
    public string Status => IsOut ? "Нет в наличии" : Quantity <= Material.Threshold ? "Критично" : NeedsAttention ? "Мало" : "В наличии";
    public decimal? AveragePrice => Quantity > 0 ? Value / Quantity : null;
}

public sealed record InventorySnapshot(long Revision, bool CanManage, bool CanAdminister, bool Demo,
    InventoryStock[] Stock, InventoryData? Details = null, Guid? ManagerId = null);

public sealed record InventoryManager(Guid Id, string Name);
public sealed record InventoryAllocation(string LineId, string SupplierId, string ReceiptId, decimal Quantity, decimal UnitPrice);
public sealed record InventorySupplierStock(string MaterialId, string SupplierId, decimal Quantity, decimal Value, decimal LastPrice, DateOnly LastDate);
public sealed record InventoryLedger(InventoryStock[] Stock, InventorySupplierStock[] Suppliers,
    IReadOnlyDictionary<string, decimal> MovementTotals, IReadOnlyDictionary<string, InventoryAllocation[]> Allocations);

public sealed record StoredInventory(long Revision, InventoryData Data, Guid? ManagerId = null)
{
    public InventorySnapshot Project(Guid actor, bool administrator)
    {
        var manage = administrator || ManagerId == actor;
        var stock = InventoryAccounting.Calculate(Data).Stock;
        return new(Revision, manage, administrator, Data.Demo,
            manage ? stock : stock.Select(item => item with { Value = null }).ToArray(),
            manage ? Data : null, administrator ? ManagerId : null);
    }
}

public interface IInventoryStore
{
    Task<StoredInventory> ReadAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<bool> TrySaveAsync(Guid organizationId, StoredInventory next, CancellationToken cancellationToken);
}

public sealed class InventoryValidationException(string message) : Exception(message);
public sealed class InventoryConflictException() : Exception("Склад изменился. Обновите остатки и повторите операцию.");

public static class InventoryJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        RespectRequiredConstructorParameters = true,
    };

    public static InventoryData Read(string json)
        => JsonSerializer.Deserialize<InventoryData>(json, Options)
            ?? throw new InventoryValidationException("Резервная копия склада пуста.");
}
