using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

/// <summary>Owned, copied input facts from the same tick as the surrounding snapshot.</summary>
public sealed record ProductionInputInformation(EntityId Entity, InventoryId Inventory,
    Vector3? Position, bool MissingInput, bool Automatic,
    IReadOnlyList<PlayerActionResourceAmount> MissingMaterials, bool MoreMaterials);

internal static class ProductionInputInformationCapture
{
    public static ProductionInputInformation? Capture(SimulationContext context,
        PresentationExtractionContext extraction, EntityId entity)
    {
        var entities = context.Entities;
        if (!entity.IsValid || !entities.IsAlive(entity) ||
            !entities.TryGetComponent(entity, out ControllableEntity control) ||
            control.Owner != extraction.Player) return null;
        var scenario = extraction.Scenario;
        InventoryId inventory;
        bool missing, automatic = false;
        var amounts = new List<PlayerActionResourceAmount>(7);
        bool more = false;
        if (entities.TryGetComponent(entity, out ProductionFacility processing))
        {
            inventory = processing.InputInventory;
            missing = processing.BlockReason == ProductionBlockReason.NoInput;
            var recipe = processing.ActiveRecipe;
            var automaticRecipe = AutomaticProcessing.RecipeFor(processing.Capabilities);
            automatic = automaticRecipe.IsSpecified;
            if (!recipe.IsSpecified && automatic) recipe = automaticRecipe;
            if (missing && scenario.Services.ProductionRecipes.TryGet(recipe, out var definition))
                foreach (var input in definition.Inputs) Add(input.ResourceId, input.Quantity);
        }
        else if (entities.TryGetComponent(entity, out UnitProductionFacility factory))
        {
            inventory = factory.InputInventory;
            missing = factory.BlockReason == UnitProductionBlockReason.NoInput;
            if (missing && scenario.Services.UnitDefinitions.TryGet(factory.ActiveUnit, out var definition))
                foreach (var cost in definition.Costs) Add(cost.ResourceId, cost.Quantity);
        }
        else return null;

        return new(entity, inventory,
            entities.TryGetComponent(entity, out WorldTransform transform) ? transform.Position : null,
            missing, automatic, Array.AsReadOnly(amounts.ToArray()), more);

        void Add(ResourceId resource, double required)
        {
            double available = scenario.Inventories.Contains(inventory)
                ? scenario.Inventories.GetAvailableQuantity(inventory, resource) : 0;
            if (available >= required) return;
            if (amounts.Count >= 7) { more = true; return; }
            amounts.Add(new(resource, scenario.Services.Resources.TryGet(resource, out var definition)
                ? definition.DisplayName : resource.ToString(), required, available));
        }
    }
}
