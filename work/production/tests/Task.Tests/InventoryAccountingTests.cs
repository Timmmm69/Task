using System.Text.Json;
using Task.Application.Inventory;

namespace Task.Tests;

public sealed class InventoryAccountingTests
{
    private static readonly DateOnly Date = new(2026, 11, 22);
    private static readonly InventoryData Seed = InventoryData.Empty() with
    {
        Materials = [new("gloves", "Перчатки", "уп.", 2)],
        Suppliers = [new("first", "Медснаб"), new("second", "Стомснаб")],
        Movements =
        [
            new("r1", "receipt", Date.AddDays(-2), 1, [new("l1", "gloves", 5, 4)], "first"),
            new("r2", "receipt", Date.AddDays(-1), 2, [new("l2", "gloves", 5, 6)], "second"),
        ],
    };

    [Fact]
    public void FifoAndSupplierSelectionUseTheActualPurchasePrices()
    {
        var fifo = InventoryAccounting.Calculate(Seed with
        {
            Movements = [.. Seed.Movements, new("issue", "issue", Date, 3, [new("l3", "gloves", 6)])],
        });
        Assert.Equal(26m, fifo.MovementTotals["issue"]);
        Assert.Equal([5m, 1m], fifo.Allocations["issue"].Select(item => item.Quantity));
        Assert.Equal(24m, Assert.Single(fifo.Stock).Value);
        var chosen = InventoryAccounting.Calculate(Seed with
        {
            Movements = [.. Seed.Movements, new("issue", "issue", Date, 3, [new("l3", "gloves", 3, SupplierId: "second")])],
        });
        Assert.Equal(18m, chosen.MovementTotals["issue"]);
        Assert.Equal(32m, Assert.Single(chosen.Stock).Value);
    }

    [Fact]
    public void BackdatingAndCancellingCannotCreateNegativeHistoricalStock()
    {
        var stored = new StoredInventory(7, Seed, null);
        var past = new InventoryMovement("issue", "issue", Date.AddDays(-2), 3, [new("l3", "gloves", 6)]);
        Assert.Throws<InventoryValidationException>(() => InventoryAccounting.Apply(stored, new(7, "move", Movement: past), Date, 3));

        var applied = InventoryAccounting.Apply(stored, new(7, "move", Movement: past with { Date = Date }), Date, 3);
        Assert.Throws<InventoryValidationException>(() => InventoryAccounting.Apply(applied, new(8, "cancel", MovementId: "r1"), Date, 4));
        Assert.Throws<InventoryConflictException>(() => InventoryAccounting.Apply(applied, new(7, "cancel", MovementId: "issue"), Date, 4));
        var undone = InventoryAccounting.Apply(applied, new(8, "cancel", MovementId: "issue"), Date, 4);
        Assert.Equal(10m, Assert.Single(InventoryAccounting.Calculate(undone.Data).Stock).Quantity);
    }

    [Fact]
    public void DemoUsesTheHtmlDataAndCanRoundTripThroughTheBackupFormat()
    {
        var demo = InventoryAccounting.Apply(new(0, InventoryData.Empty(), null), new(0, "demo"), Date, 0).Data;
        var restored = InventoryJson.Read(JsonSerializer.Serialize(demo, InventoryJson.Options));
        Assert.True(restored.Demo);
        Assert.Equal(27, restored.Materials.Length);
        Assert.Equal(6, restored.Suppliers.Length);
        Assert.Equal(25, restored.Movements.Length);
        Assert.Equal(Date, restored.Movements.Max(move => move.Date));
        Assert.Contains(InventoryAccounting.Calculate(restored).Stock, stock => stock.IsOut);
    }
}
