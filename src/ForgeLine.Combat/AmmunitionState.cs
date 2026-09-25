using ForgeLine.Economy;

namespace ForgeLine.Combat;

public readonly record struct AmmunitionState
{
    public AmmunitionState(
        InventoryId inventoryId,
        double capacity)
    {
        if (!inventoryId.IsSpecified)
        {
            throw new ArgumentException(
                "Ammunition state requires a valid inventory.",
                nameof(inventoryId));
        }

        if (!double.IsFinite(capacity) || capacity <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        InventoryId = inventoryId;
        Capacity = capacity;
    }

    public InventoryId InventoryId { get; }

    public double Capacity { get; }
}

public static class AmmunitionConsumption
{
    public static bool TryConsume(
        InventoryStore inventories,
        in AmmunitionState ammunition,
        double quantity)
    {
        ArgumentNullException.ThrowIfNull(inventories);

        if (!double.IsFinite(quantity) || quantity <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        if (!inventories.Contains(ammunition.InventoryId) ||
            inventories.GetAvailableQuantity(
                ammunition.InventoryId,
                ForgeLine.Core.ResourceIds.Ammunition) < quantity)
        {
            return false;
        }

        return inventories.Remove(
            ammunition.InventoryId,
            ForgeLine.Core.ResourceIds.Ammunition,
            quantity).Succeeded;
    }
}
