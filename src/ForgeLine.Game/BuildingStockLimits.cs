using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;

namespace ForgeLine.Game;

/// <summary>Physical inventory and enabled building policies jointly bound logistics admission.</summary>
internal static class BuildingStockLimits
{
    /// <summary>Return the smallest configured maximum for a real inbound inventory and material.</summary>
    internal static double GetEffectiveMaximum(
        EntityRegistry entities, InventoryStore stock, InventoryId inventory, ResourceId resource)
    {
        if (!stock.Contains(inventory))
            return 0;

        double maximum = stock.GetResourceCapacity(inventory, resource);
        foreach (EntityId entity in entities.Query<LogisticsStockPolicy>(QueryIterationOrder.StableByEntityIndex))
        {
            var policy = entities.GetComponent<LogisticsStockPolicy>(entity);
            if (!policy.Enabled || policy.ResourceId != resource ||
                ConveyorSimulationSystem.ResolveInventory(entities, policy.TargetEntity, input: true) != inventory)
                continue;

            maximum = Math.Min(maximum, policy.DesiredMaximum);
        }

        return maximum;
    }

    /// <summary>Source stock floor for optional transport; reservations and recipes remain authoritative.</summary>
    internal static double GetOutboundMinimum(EntityRegistry entities, EntityId source, ResourceId resource)
    {
        double minimum = 0;
        foreach (EntityId entity in entities.Query<LogisticsStockPolicy>(QueryIterationOrder.StableByEntityIndex))
        {
            var policy = entities.GetComponent<LogisticsStockPolicy>(entity);
            if (policy.Enabled && policy.TargetEntity == source && policy.ResourceId == resource)
                minimum = Math.Max(minimum, policy.DesiredMinimum);
        }

        return minimum;
    }

    /// <summary>Applies the policy ceiling without changing inventory ownership or recipe production.</summary>
    internal static double GetAddable(
        EntityRegistry entities, InventoryStore stock, InventoryId destination,
        ResourceId resource, double requested, CargoInboundCapacity? committed = null)
    {
        if (!stock.Contains(destination))
            return 0;

        double physical = committed is null
            ? stock.GetAddableQuantity(destination, resource, requested)
            : committed.GetAddable(stock, destination, resource, requested);
        double policyRoom = Math.Max(0,
            GetEffectiveMaximum(entities, stock, destination, resource) -
            stock.GetQuantity(destination, resource) -
            (committed?.GetCommittedResource(destination, resource) ?? 0));

        return Math.Min(physical, policyRoom);
    }
}
