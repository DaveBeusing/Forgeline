using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;

namespace ForgeLine.Game;

internal static class SkirmishConstructionFunding
{
    internal static double UnpaidProductionInput(EntityRegistry entities, UnitDefinitionCatalog units,
        EntityId source, InventoryId inventory, ResourceId resource)
    {
        // Available inventory excludes paid reservations already. Protect only
        // the live active request's unpaid inputs, including a power-blocked one.
        if (!entities.TryGetComponent(source, out UnitProductionFacility facility) ||
            facility.InputInventory != inventory || facility.InputsReserved ||
            !facility.ActiveRequest.IsValid ||
            !entities.TryGetComponent(facility.ActiveRequest, out UnitProductionRequest request) ||
            request.Facility != source || request.UnitId != facility.ActiveUnit || request.Paused ||
            entities.HasComponent<UnitProductionCancellationRequest>(facility.ActiveRequest) ||
            !units.TryGet(facility.ActiveUnit, out var unit)) return 0;
        foreach (var cost in unit.Costs)
            if (cost.ResourceId == resource) return cost.Quantity;
        return 0;
    }

    internal static bool CanPay(EntityRegistry entities, InventoryStore inventories, UnitDefinitionCatalog units,
        EntityId source, InventoryId inventory, BuildingDefinition building)
    {
        foreach (var cost in building.Costs)
            if (inventories.GetAvailableQuantity(inventory, cost.ResourceId) + 1e-9 <
                cost.Quantity + UnpaidProductionInput(entities, units, source, inventory, cost.ResourceId)) return false;
        return true;
    }
}
