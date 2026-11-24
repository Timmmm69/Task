namespace Task.Application.Inventory;

public static class InventoryAccounting
{
    public static InventoryLedger Calculate(InventoryData data)
    {
        Require(data.Version == 1 && data.Materials is not null && data.Suppliers is not null &&
            data.Units is not null && data.Movements is not null, "Неизвестный формат данных склада.");

        var units = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var unit in data.Units!)
            Require(Text(unit, 30) && units.Add(unit), "Проверьте единицы измерения.");
        var materials = new Dictionary<string, InventoryMaterial>();
        var materialNames = new HashSet<(string Name, string Unit)>();
        foreach (var material in data.Materials!)
        {
            Require(material is not null && Text(material.Id, 100) && Text(material.Name, 120) &&
                Text(material.Unit, 30) && units.Contains(material.Unit) && IsQuantity(material.Threshold, allowZero: true),
                "Проверьте название, единицу и минимум материала.");
            Require(materials.TryAdd(material!.Id, material) &&
                materialNames.Add((material.Name.ToUpperInvariant(), material.Unit.ToUpperInvariant())),
                "Такой материал уже есть.");
        }

        var suppliers = new HashSet<string>();
        var supplierNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var supplier in data.Suppliers!)
            Require(supplier is not null && Text(supplier.Id, 100) && Text(supplier.Name, 120) &&
                suppliers.Add(supplier.Id) && supplierNames.Add(supplier.Name), "Проверьте названия поставщиков.");

        var lots = new Dictionary<string, List<Lot>>();
        var total = new Dictionary<string, decimal>();
        var allocations = new Dictionary<string, InventoryAllocation[]>();
        Require(data.Movements!.All(move => move is not null), "Операция отсутствует.");
        foreach (var move in data.Movements!.OrderBy(move => move.Date).ThenBy(move => move.CreatedAt))
        {
            Require(Text(move.Id, 100) && total.TryAdd(move.Id, 0) && move.Date != default &&
                (move.Note?.Length ?? 0) <= 250 && (move.Invoice?.Length ?? 0) <= 80 &&
                move.Lines is { Length: > 0 } && move.Type is "receipt" or "issue", "Проверьте данные операции.");
            var receipt = move.Type == "receipt";
            Require(!receipt || move.SupplierId is not null && suppliers.Contains(move.SupplierId), "Выберите поставщика.");
            var distributed = new List<InventoryAllocation>();
            var lineIds = new HashSet<string>();
            foreach (var line in move.Lines!)
            {
                Require(line is not null && Text(line.Id, 100) && lineIds.Add(line.Id) &&
                    Text(line.MaterialId, 100) && materials.ContainsKey(line.MaterialId) &&
                    IsQuantity(line.Quantity), "Проверьте материал и количество в операции.");
                if (!lots.TryGetValue(line!.MaterialId, out var materialLots))
                    lots[line.MaterialId] = materialLots = [];

                if (receipt)
                {
                    Require(line.UnitPrice is >= 0 and <= 10_000_000_000m, "Укажите корректную цену закупки.");
                    var price = line.UnitPrice!.Value;
                    materialLots.Add(new(move.Id, move.SupplierId!, move.Date, price, line.Quantity));
                    total[move.Id] += line.Quantity * price;
                    continue;
                }

                Require(line.UnitPrice is null, "Стоимость списания рассчитывается по партиям.");
                var chosenSupplier = string.IsNullOrEmpty(line.SupplierId) ? null : line.SupplierId;
                Require(chosenSupplier is null || suppliers.Contains(chosenSupplier), "Поставщик не найден.");
                var remaining = line.Quantity;
                foreach (var lot in materialLots)
                {
                    if (remaining == 0) break;
                    if (lot.Quantity == 0 || chosenSupplier is not null && lot.SupplierId != chosenSupplier) continue;
                    var used = Math.Min(remaining, lot.Quantity);
                    lot.Quantity -= used;
                    remaining -= used;
                    total[move.Id] += used * lot.Price;
                    distributed.Add(new(line.Id, lot.SupplierId, lot.ReceiptId, used, lot.Price));
                }
                Require(remaining == 0, $"Недостаточно материала «{materials[line.MaterialId].Name}» на дату списания.");
            }
            if (!receipt) allocations.Add(move.Id, distributed.ToArray());
        }

        InventoryStock[] stock = [.. data.Materials!.Select(material =>
        {
            var materialLots = lots.GetValueOrDefault(material.Id) ?? [];
            return new InventoryStock(material, materialLots.Sum(lot => lot.Quantity), materialLots.Sum(lot => lot.Quantity * lot.Price));
        })];
        InventorySupplierStock[] bySupplier = [.. lots.SelectMany(pair => pair.Value.GroupBy(lot => lot.SupplierId).Select(group =>
        {
            var last = group.Last();
            return new InventorySupplierStock(pair.Key, group.Key, group.Sum(lot => lot.Quantity),
                group.Sum(lot => lot.Quantity * lot.Price), last.Price, last.Date);
        }))];
        return new(stock, bySupplier, total, allocations);
    }

    public static StoredInventory Apply(StoredInventory current, InventoryCommand command, DateOnly today, long timestamp)
    {
        if (current.Revision != command.Revision) throw new InventoryConflictException();
        var data = current.Data;
        var manager = current.ManagerId;
        switch (command)
        {
            case { Action: "material", Material: { } material }:
                var old = data.Materials.FirstOrDefault(item => item.Id == material.Id);
                Require(old is null || old.Unit == material.Unit || !data.Movements.Any(move =>
                    move.Lines.Any(line => line.MaterialId == old.Id)), "Единица материала с историей уже зафиксирована.");
                var units = data.Units.Contains(material.Unit, StringComparer.OrdinalIgnoreCase)
                    ? data.Units : [.. data.Units, material.Unit];
                data = data with { Materials = Save(data.Materials, material, item => item.Id), Units = units };
                break;
            case { Action: "supplier", Supplier: { } supplier }:
                data = data with { Suppliers = Save(data.Suppliers, supplier, item => item.Id) };
                break;
            case { Action: "unit", Unit: { } unit }:
                data = data with { Units = [.. data.Units, unit] };
                break;
            case { Action: "move", Movement: { } movement }:
                Require(data.Movements.All(move => move.Id != movement.Id), "Операция уже проведена.");
                Require(movement.Lines is not null && movement.Lines.All(line => line is not null), "Добавьте позиции.");
                // An issue's value always comes from its receipt lots, never from the request.
                var lines = movement.Lines!.Select(line => movement.Type == "issue"
                    ? line with { UnitPrice = null } : line with { SupplierId = null }).ToArray();
                data = data with
                {
                    Movements = [.. data.Movements, movement with
                {
                    CreatedAt = timestamp, Lines = lines,
                    SupplierId = movement.Type == "issue" ? null : movement.SupplierId,
                    Invoice = movement.Type == "issue" ? null : movement.Invoice,
                }]
                };
                break;
            case { Action: "cancel", MovementId: { } id }:
                Require(data.Movements.Any(move => move.Id == id), "Операция не найдена.");
                data = data with { Movements = data.Movements.Where(move => move.Id != id).ToArray() };
                break;
            case { Action: "import", Backup: { } backup }:
                data = backup;
                break;
            case { Action: "demo" }:
                data = InventoryDemo.Create(today);
                break;
            case { Action: "manager" }:
                manager = command.ManagerId;
                break;
            default:
                throw new InventoryValidationException("Недопустимая операция склада.");
        }

        Calculate(data);
        return new(checked(current.Revision + 1), data, manager);
    }

    private static T[] Save<T>(T[] all, T next, Func<T, string> key)
        => all.Any(item => key(item) == key(next))
            ? all.Select(item => key(item) == key(next) ? next : item).ToArray()
            : [.. all, next];

    private static bool Text(string? value, int max)
        => value is { Length: > 0 } && value.Length <= max && value.Trim() == value && !value.Any(char.IsControl);
    private static bool IsQuantity(decimal value, bool allowZero = false)
        => (value > 0 || allowZero && value == 0) && value <= 1_000_000_000m && decimal.Round(value, 6) == value;

    private static void Require(bool valid, string message)
    {
        if (!valid) throw new InventoryValidationException(message);
    }

    private sealed class Lot(string receiptId, string supplierId, DateOnly date, decimal price, decimal quantity)
    {
        public string ReceiptId { get; } = receiptId;
        public string SupplierId { get; } = supplierId;
        public DateOnly Date { get; } = date;
        public decimal Price { get; } = price;
        public decimal Quantity { get; set; } = quantity;
    }
}
