using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;

namespace ForgeLine.Game;

internal static class SkirmishConstructionFunding
{
    internal static bool CanPay(EntityRegistry entities, InventoryStore inventories, UnitDefinitionCatalog units,
        EntityId source, InventoryId inventory, BuildingDefinition building)
    {
        // A delivery into a live production input is not unclaimed construction stock.
        // Preserve one active unit's unpaid inputs; existing inventory reservations
        // already protect paid work and must not be counted twice.
        UnitDefinition? protectedUnit = null;
        if (entities.TryGetComponent(source, out UnitProductionFacility facility) &&
            facility.InputInventory == inventory && !facility.InputsReserved &&
            facility.Status == UnitProductionStatus.NoInput && facility.ActiveRequest.IsValid &&
            entities.TryGetComponent(facility.ActiveRequest, out UnitProductionRequest request) &&
            request.Facility == source && request.UnitId == facility.ActiveUnit && !request.Paused &&
            !entities.HasComponent<UnitProductionCancellationRequest>(facility.ActiveRequest) &&
            units.TryGet(facility.ActiveUnit, out var unit)) protectedUnit = unit;
        foreach (var cost in building.Costs)
        {
            double productionNeed = 0;
            if (protectedUnit is not null)
                foreach (var input in protectedUnit.Costs)
                    if (input.ResourceId == cost.ResourceId) productionNeed += input.Quantity;
            if (inventories.GetAvailableQuantity(inventory, cost.ResourceId) < cost.Quantity + productionNeed) return false;
        }
        return true;
    }
}
