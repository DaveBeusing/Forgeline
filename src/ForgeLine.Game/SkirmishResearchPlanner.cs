using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum SkirmishResearchReason : byte
{
    NotNeeded, Priority, Active, Requirements, ProductionReserve, Started, ProductionInputs
}

public readonly record struct SkirmishResearchReadModel(TechnologyId Technology,
    SkirmishResearchReason Reason, TechnologyResearchBlockReason Block, SimulationTick DecisionTick)
{
    public EntityId Source { get; init; }
    public bool MaterialsConsumed { get; init; }
    public uint ProgressTicks { get; init; }
    public ResourceId Resource { get; init; }
    public double Required { get; init; }
    public double Available { get; init; }
    public double Reserved { get; init; }
    public double UnpaidProduction { get; init; }
    public double ReplacementReserve { get; init; }
    public double AvailableBudget { get; init; }
}

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
        if (TechnologyStateQueries.TryGetActiveResearch(context.Entities, controller.Player, out _, out var active))
        {
            Record(active.TechnologyId, SkirmishResearchReason.Active, active.BlockReason);
            _diagnostics[controllerEntity] = Capture(controllerEntity) with
            { MaterialsConsumed = active.MaterialsConsumed, ProgressTicks = active.ProgressTicks };
            if (inventories.Contains(active.SourceInventory) && technologies.TryGet(active.TechnologyId, out var activeDefinition))
            {
                EntityId source = EntityId.Invalid;
                for (int i = -1; i < Math.Min(32, buildings.Count); i++)
                {
                    EntityId candidate = i < 0 ? controller.PreferredConstructionSource : buildings[i];
                    if (context.Entities.TryGetComponent(candidate, out CompletedBuilding building) &&
                        building.Owner == controller.Player &&
                        context.Entities.TryGetComponent(candidate, out InventoryStorage storage) &&
                        storage.InventoryId == active.SourceInventory) { source = candidate; break; }
                }
                Describe(source, active.SourceInventory, activeDefinition);
            }
            return;
        }

        if (priority) { Record(default, SkirmishResearchReason.Priority); return; }

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

            var block = facility.IsValid ? TechnologyResearchBlockReason.MissingInventory :
                TechnologyResearchBlockReason.MissingFacility;
            if (facility.IsValid)
                for (int i = -1; i < Math.Min(32, buildings.Count); i++)
                {
                    EntityId source = i < 0 ? controller.PreferredConstructionSource : buildings[i];
                    if (!context.Entities.TryGetComponent(source, out CompletedBuilding building) ||
                        building.Owner != controller.Player ||
                        !context.Entities.TryGetComponent(source, out InventoryStorage storage) ||
                        !inventories.Contains(storage.InventoryId)) continue;
                    block = TechnologyResearchPolicy.Evaluate(context.Entities, inventories, technologies,
                        controller.Player, definition, facility, storage.InventoryId, false);
                    if (block != TechnologyResearchBlockReason.None)
                    {
                        // Keep a real source and material shortage rather than
                        // overwriting it with an unrelated later storage failure.
                        if (Capture(controllerEntity).DecisionTick != context.Tick)
                        {
                            Record(id, SkirmishResearchReason.Requirements, block);
                            Describe(source, storage.InventoryId, definition);
                        }
                        continue;
                    }
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
                    if (reserved) { Record(id, SkirmishResearchReason.ProductionReserve);
                        Describe(source, storage.InventoryId, definition); continue; }
                    bool productionInputs = false;
                    foreach (var cost in definition.Costs)
                        if (inventories.GetAvailableQuantity(storage.InventoryId, cost.ResourceId) + 1e-9 <
                            cost.Quantity + SkirmishConstructionFunding.UnpaidProductionInput(context.Entities,
                                units, source, storage.InventoryId, cost.ResourceId))
                        { productionInputs = true; break; }
                    if (productionInputs)
                    {
                        Record(id, SkirmishResearchReason.ProductionInputs);
                        Describe(source, storage.InventoryId, definition);
                        continue;
                    }
                    var command = PlayerTechnologyActionCommand.Start(controller.Player, id, facility,
                        source, context.Tick, technologies);
                    command.Execute(context);
                    if (command.Accepted) { Record(id, SkirmishResearchReason.Started);
                        Describe(source, storage.InventoryId, definition); return; }
                }
            if (Capture(controllerEntity).DecisionTick != context.Tick)
                Record(id, SkirmishResearchReason.Requirements, block);
            // Foundation completion is mandatory before either dependent investment.
            if (id == TechnologyIds.IndustrialStandardization ||
                id == TechnologyIds.MechanizedSystems && needsArtillery) return;
        }
        if (Capture(controllerEntity).DecisionTick != context.Tick) Record(default, SkirmishResearchReason.NotNeeded);

        void Record(TechnologyId id, SkirmishResearchReason reason,
            TechnologyResearchBlockReason block = TechnologyResearchBlockReason.None) =>
            _diagnostics[controllerEntity] = new(id, reason, block, context.Tick);

        void Describe(EntityId source, InventoryId inventory, TechnologyDefinition definition)
        {
            foreach (var cost in definition.Costs)
            {
                double available = inventories.GetAvailableQuantity(inventory, cost.ResourceId);
                double unpaid = SkirmishConstructionFunding.UnpaidProductionInput(context.Entities,
                    units, source, inventory, cost.ResourceId);
                double reserve = 0;
                foreach (UnitId unit in new[] { UnitIds.SupplyTruck, UnitIds.CargoTruck })
                    foreach (var input in units[unit].Costs)
                        if (input.ResourceId == cost.ResourceId) reserve += input.Quantity;
                double budget = AvailableBudget(cost.ResourceId);
                _diagnostics[controllerEntity] = Capture(controllerEntity) with
                {
                    Source = source, Resource = cost.ResourceId, Required = cost.Quantity,
                    Available = available, Reserved = inventories.GetReservedQuantity(inventory, cost.ResourceId),
                    UnpaidProduction = unpaid, ReplacementReserve = reserve, AvailableBudget = budget
                };
                if (available + 1e-9 < cost.Quantity + unpaid ||
                    budget + 1e-9 < cost.Quantity + reserve) break;
            }
        }

        double AvailableBudget(ResourceId resource)
        {
            double available = 0;
            var counted = new HashSet<InventoryId>();
            for (int index = 0; index < Math.Min(32, buildings.Count); index++)
            {
                EntityId entity = buildings[index];
                if (!context.Entities.TryGetComponent(entity, out CompletedBuilding building) ||
                    building.Owner != controller.Player) continue;
                if (context.Entities.TryGetComponent(entity, out InventoryStorage storage)) Add(entity, storage.InventoryId);
                if (context.Entities.TryGetComponent(entity, out UnitProductionFacility factory)) Add(entity, factory.InputInventory);
                if (context.Entities.TryGetComponent(entity, out ProductionFacility processing)) Add(entity, processing.OutputInventory);
            }
            return available;

            void Add(EntityId source, InventoryId inventory)
            {
                if (counted.Add(inventory) && inventories.Contains(inventory))
                    available += Math.Max(0, inventories.GetAvailableQuantity(inventory, resource) -
                        SkirmishConstructionFunding.UnpaidProductionInput(context.Entities, units, source, inventory, resource));
            }
        }
    }
}
