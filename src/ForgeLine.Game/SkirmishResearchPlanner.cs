using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum SkirmishResearchReason : byte
{
    NotNeeded, Priority, Active, Requirements, ProductionReserve, Started
}

public readonly record struct SkirmishResearchReadModel(TechnologyId Technology,
    SkirmishResearchReason Reason, TechnologyResearchBlockReason Block, SimulationTick DecisionTick);

public readonly record struct SkirmishResearchInfrastructure(bool SupplyEstablished);

/// <summary>At most three technology candidates and 32 copied owned buildings per decision.</summary>
public sealed class SkirmishResearchPlanner(TechnologyDefinitionCatalog technologies,
    UnitDefinitionCatalog units, InventoryStore inventories)
{
    private readonly Dictionary<EntityId, SkirmishResearchReadModel> _diagnostics = new();

    public SkirmishResearchReadModel Capture(EntityId controller) =>
        _diagnostics.GetValueOrDefault(controller);

    public void Forget(EntityId controller) => _diagnostics.Remove(controller);

    public void Evaluate(SimulationContext context, EntityId controllerEntity,
        SkirmishOpponentController controller, IReadOnlyList<EntityId> buildings,
        bool priority, bool needsArtillery)
    {
        if (priority) { Record(default, SkirmishResearchReason.Priority); return; }
        if (TechnologyStateQueries.TryGetActiveResearch(context.Entities, controller.Player, out _, out var active))
        {
            Record(active.TechnologyId, SkirmishResearchReason.Active, active.BlockReason);
            return;
        }

        // Logistics Coordination is deliberately excluded until an integrated priority-routing consumer exists.
        foreach (TechnologyId id in new[] { TechnologyIds.IndustrialStandardization,
                     TechnologyIds.MechanizedSystems, TechnologyIds.SensorFusion })
        {
            if (!technologies.TryGet(id, out var definition) ||
                TechnologyStateQueries.IsCompleted(context.Entities, controller.Player, id) ||
                (id == TechnologyIds.MechanizedSystems && !needsArtillery)) continue;
            EntityId facility = EntityId.Invalid;
            for (int i = 0; i < Math.Min(32, buildings.Count); i++)
                if (context.Entities.TryGetComponent(buildings[i], out CompletedBuilding building) &&
                    building.Owner == controller.Player && building.BuildingId == definition.RequiredFacility)
                { facility = buildings[i]; break; }

            var block = TechnologyResearchBlockReason.MissingFacility;
            if (facility.IsValid)
                for (int i = -1; i < Math.Min(32, buildings.Count); i++)
                {
                    EntityId source = i < 0 ? controller.PreferredConstructionSource : buildings[i];
                    if (!context.Entities.TryGetComponent(source, out CompletedBuilding building) ||
                        building.Owner != controller.Player ||
                        !context.Entities.TryGetComponent(source, out InventoryStorage storage)) continue;
                    block = TechnologyResearchPolicy.Evaluate(context.Entities, inventories, technologies,
                        controller.Player, definition, facility, storage.InventoryId, false);
                    if (block != TechnologyResearchBlockReason.None) continue;
                    bool reserved = false;
                    foreach (var cost in definition.Costs)
                    {
                        double reserve = 0;
                        foreach (UnitId unit in new[] { UnitIds.SupplyTruck, UnitIds.CargoTruck })
                            foreach (var productionCost in units[unit].Costs)
                                if (productionCost.ResourceId == cost.ResourceId) reserve += productionCost.Quantity;
                        if (AvailableBudget(cost.ResourceId) + 1e-9 < cost.Quantity + reserve)
                        { reserved = true; break; }
                    }
                    if (reserved) { Record(id, SkirmishResearchReason.ProductionReserve); continue; }
                    var command = PlayerTechnologyActionCommand.Start(controller.Player, id, facility,
                        source, context.Tick, technologies);
                    command.Execute(context);
                    if (command.Accepted) { Record(id, SkirmishResearchReason.Started); return; }
                }
            if (Capture(controllerEntity).DecisionTick != context.Tick)
                Record(id, SkirmishResearchReason.Requirements, block);
            // Foundation completion is mandatory before either dependent investment.
            if (id == TechnologyIds.IndustrialStandardization) return;
        }
        if (Capture(controllerEntity).DecisionTick != context.Tick) Record(default, SkirmishResearchReason.NotNeeded);

        void Record(TechnologyId id, SkirmishResearchReason reason,
            TechnologyResearchBlockReason block = TechnologyResearchBlockReason.None) =>
            _diagnostics[controllerEntity] = new(id, reason, block, context.Tick);

        double AvailableBudget(ResourceId resource)
        {
            double available = 0;
            var counted = new HashSet<InventoryId>();
            for (int index = 0; index < Math.Min(32, buildings.Count); index++)
            {
                EntityId entity = buildings[index];
                if (!context.Entities.TryGetComponent(entity, out CompletedBuilding building) ||
                    building.Owner != controller.Player) continue;
                if (context.Entities.TryGetComponent(entity, out InventoryStorage storage)) Add(storage.InventoryId);
                if (context.Entities.TryGetComponent(entity, out UnitProductionFacility factory)) Add(factory.InputInventory);
                if (context.Entities.TryGetComponent(entity, out ProductionFacility processing)) Add(processing.OutputInventory);
            }
            return available;

            void Add(InventoryId inventory)
            {
                if (counted.Add(inventory) && inventories.Contains(inventory))
                    available += inventories.GetAvailableQuantity(inventory, resource);
            }
        }
    }
}
