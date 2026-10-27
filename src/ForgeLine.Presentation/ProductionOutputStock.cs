using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public readonly record struct ProductionOutputAmount(ResourceId Resource, string Name, double Quantity, double Reserved);
public sealed record ProductionOutputStock(EntityId Entity, InventoryId Inventory,
    IReadOnlyList<ProductionOutputAmount> Materials, bool MoreMaterials);

internal static class ProductionOutputStockCapture
{
    public static ProductionOutputStock? Capture(SimulationContext context,
        PresentationExtractionContext extraction, EntityId entity)
    {
        var entities = context.Entities;
        var scenario = extraction.Scenario;
        if (!entity.IsValid || !entities.IsAlive(entity) ||
            !entities.TryGetComponent(entity, out ControllableEntity control) || control.Owner != extraction.Player ||
            !entities.TryGetComponent(entity, out ProductionFacility facility) ||
            !scenario.Inventories.Contains(facility.OutputInventory)) return null;
        var materials = new List<ProductionOutputAmount>(7);
        bool more = false;
        foreach (var recipe in scenario.Services.ProductionRecipes.Definitions)
        {
            if (!facility.Supports(recipe.RequiredCapability)) continue;
            foreach (var output in recipe.Outputs)
            {
                bool known = false;
                foreach (var material in materials) known |= material.Resource == output.ResourceId;
                if (known) continue;
                if (materials.Count == 7) { more = true; continue; }
                materials.Add(new(output.ResourceId,
                    scenario.Services.Resources.TryGet(output.ResourceId, out var resource) ? resource.DisplayName : output.ResourceId.ToString(),
                    scenario.Inventories.GetQuantity(facility.OutputInventory, output.ResourceId),
                    scenario.Inventories.GetReservedQuantity(facility.OutputInventory, output.ResourceId)));
            }
        }
        return new(entity, facility.OutputInventory, Array.AsReadOnly(materials.ToArray()), more);
    }
}
