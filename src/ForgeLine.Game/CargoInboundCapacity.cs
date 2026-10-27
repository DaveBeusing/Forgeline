using ForgeLine.Core;
using ForgeLine.Economy;

namespace ForgeLine.Game;

/// <summary>One phase-local view of accepted cargo commitments; never persisted or used as inventory.</summary>
internal sealed class CargoInboundCapacity
{
    private readonly Dictionary<InventoryId, double> _totals = [];
    private readonly Dictionary<(InventoryId, ResourceId), double> _resources = [];

    internal void Add(InventoryId inventory, ResourceId resource, double quantity)
    {
        if (quantity <= 0) return;
        _totals[inventory] = _totals.GetValueOrDefault(inventory) + quantity;
        var key = (inventory, resource);
        _resources[key] = _resources.GetValueOrDefault(key) + quantity;
    }

    internal double GetAddable(InventoryStore stock, InventoryId inventory, ResourceId resource, double requested)
    {
        double physical = stock.GetAddableQuantity(inventory, resource, requested);
        double total = Math.Max(0, stock.GetRemainingCapacity(inventory) - _totals.GetValueOrDefault(inventory));
        double material = Math.Max(0, stock.GetResourceCapacity(inventory, resource) -
            stock.GetQuantity(inventory, resource) - _resources.GetValueOrDefault((inventory, resource)));
        return Math.Min(physical, Math.Min(total, material));
    }
}
