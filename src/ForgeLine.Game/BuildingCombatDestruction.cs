using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

/// <summary>Closes completed-building ownership before the combat lifecycle removes its entity.</summary>
public sealed class BuildingCombatDestruction
{
    private readonly InventoryStore _inventories;
    private readonly BuildingConstructionSystem _construction;
    private readonly ProductionSystem _production;
    private readonly UnitProductionSystem _unitProduction;
    private readonly TechnologyResearchSystem _research;
    private readonly CargoTransportSystem _cargo;
    private readonly AutomatedDistributionSystem _distribution;
    private readonly List<InventoryId> _ownedInventories = new(4);
    private readonly List<EntityId> _policies = new();

    public BuildingCombatDestruction(
        InventoryStore inventories,
        BuildingConstructionSystem construction,
        ProductionSystem production,
        UnitProductionSystem unitProduction,
        TechnologyResearchSystem research,
        CargoTransportSystem cargo,
        AutomatedDistributionSystem distribution)
    {
        _inventories = inventories ?? throw new ArgumentNullException(nameof(inventories));
        _construction = construction ?? throw new ArgumentNullException(nameof(construction));
        _production = production ?? throw new ArgumentNullException(nameof(production));
        _unitProduction = unitProduction ?? throw new ArgumentNullException(nameof(unitProduction));
        _research = research ?? throw new ArgumentNullException(nameof(research));
        _cargo = cargo ?? throw new ArgumentNullException(nameof(cargo));
        _distribution = distribution ?? throw new ArgumentNullException(nameof(distribution));
    }

    internal void RemoveBeforeEntityDestruction(SimulationContext context, EntityId building)
    {
        EntityRegistry entities = context.Entities;
        if (!entities.HasComponent<CompletedBuilding>(building)) return;

        _ownedInventories.Clear();
        if (entities.TryGetComponent(building, out InventoryStorage storage))
            AddInventory(storage.InventoryId);
        if (entities.TryGetComponent(building, out ProductionFacility production))
        {
            AddInventory(production.InputInventory);
            AddInventory(production.OutputInventory);
        }
        if (entities.TryGetComponent(building, out UnitProductionFacility units))
            AddInventory(units.InputInventory);
        _ownedInventories.Sort();

        // Owners release their reservations while the original inventory still exists.
        _production.CancelForFacility(entities, building);
        _unitProduction.CancelForFacility(entities, building);
        _research.CancelForDestroyedBuilding(entities, building, _ownedInventories);
        _construction.CancelForSourceInventories(context, _ownedInventories);
        _cargo.InvalidateBuilding(context, building);
        _distribution.InvalidateBuilding(context, building);

        _policies.Clear();
        foreach (EntityId policy in entities.Query<LogisticsStockPolicy>(QueryIterationOrder.StableByEntityIndex))
            if (entities.GetComponent<LogisticsStockPolicy>(policy).TargetEntity == building)
                _policies.Add(policy);
        foreach (EntityId policy in _policies)
            entities.DestroyEntity(policy);

        // All stock still held by the building is lost once. Wrecks are visual-only and demolition
        // receipts are never credited here. Independent truck/conveyor inventories are preserved.
        foreach (InventoryId inventory in _ownedInventories)
            _inventories.DestroyInventory(inventory);
    }

    private void AddInventory(InventoryId inventory)
    {
        if (inventory.IsSpecified && !_ownedInventories.Contains(inventory))
            _ownedInventories.Add(inventory);
    }
}
