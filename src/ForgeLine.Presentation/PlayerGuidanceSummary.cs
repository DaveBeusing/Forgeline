using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

[Flags]
public enum PlayerGuidanceMilestone : uint
{
    None = 0,
    CommandCore = 1,
    Power = 2,
    FerrousExtraction = 4,
    SteelProcessing = 8,
    VehicleFactory = 16,
    Scout = 32,
    Supply = 64,
    OpponentContact = 128,
    Smelter = 256, OrePolicy = 512, OreDelivered = 1024, FactoryInputs = 2048,
    SupplyDepot = 4096, DepotFuelDelivered = 8192, DepotAmmunitionDelivered = 16384,
    SupplyTruckProduced = 32768, SupplyTruckLoaded = 65536, FieldSupplied = 131072, DisruptionResolved = 262144, ConveyorDelivery = 524288, ProductionOutput = 1048576, ResearchCompleted = 2097152
}

public readonly record struct PlayerGuidanceSummary(
    SimulationSessionId SessionId, SimulationTick Tick, PlayerId Player, PlayerGuidanceMilestone Observed,
    IReadOnlyList<GameplayProgressEvent>? Evidence = null)
{
    public PracticeSnapshot? Practice { get; init; }
}

internal static class PlayerGuidanceSummaryFactory
{
    public static PlayerGuidanceSummary Capture(SimulationContext context, PresentationExtractionContext extraction,
        FactionIntelligenceSnapshot? intelligence)
    {
        var observed = PlayerGuidanceMilestone.None;
        var entities = context.Entities;
        // Ownership is checked before reading operational components or inventories.
        foreach (var entity in entities.Query<ControllableEntity>())
        {
            if (entities.GetComponent<ControllableEntity>(entity).Owner != extraction.Player) continue;
            if (entities.TryGetComponent(entity, out CompletedBuilding building) && building.Owner == extraction.Player)
            {
                if (building.BuildingId == BuildingIds.Smelter && entities.HasComponent<BuildingLoadingDock>(entity)) observed |= PlayerGuidanceMilestone.Smelter;
                if (building.BuildingId == BuildingIds.SupplyDepot) observed |= PlayerGuidanceMilestone.SupplyDepot;
                if (building.BuildingId == BuildingIds.VehicleFactory && entities.TryGetComponent(entity, out UnitProductionFacility localFactory))
                {
                    var definition = extraction.Scenario.Services.UnitDefinitions[UnitIds.ScoutVehicle];
                    bool ready = extraction.Scenario.Inventories.Contains(localFactory.InputInventory);
                    if (ready) foreach (var cost in definition.Costs)
                        ready &= extraction.Scenario.Inventories.GetAvailableQuantity(localFactory.InputInventory, cost.ResourceId) >= cost.Quantity;
                    if (ready) observed |= PlayerGuidanceMilestone.FactoryInputs;
                }
                if (building.BuildingId == BuildingIds.CommandCore) observed |= PlayerGuidanceMilestone.CommandCore;
                if (building.BuildingId == BuildingIds.PowerPlant && entities.TryGetComponent(entity, out PowerGenerator producer) &&
                    producer.Enabled && producer.State == PowerGeneratorState.Generating) observed |= PlayerGuidanceMilestone.Power;
                if (building.BuildingId == BuildingIds.VehicleFactory) observed |= PlayerGuidanceMilestone.VehicleFactory;
                // The authored Smelter exclusively processes Steel. Starting inventory is not production evidence.
                if (building.BuildingId == BuildingIds.Smelter && entities.TryGetComponent(entity, out ProductionFacility facility) &&
                    facility.Capabilities == ProductionCapability.SteelProcessing && facility.CompletedCycles > 0)
                    observed |= PlayerGuidanceMilestone.SteelProcessing;
            }
            if (entities.TryGetComponent(entity, out SupplyProvider provider) && provider.Owner == extraction.Player && provider.Enabled &&
                entities.HasComponent<WorldTransform>(entity) &&
                (!entities.TryGetComponent(entity, out SupplyDepot depot) || depot.State == SupplyDepotState.Operational) &&
                extraction.Scenario.Inventories.Contains(provider.InventoryId) &&
                extraction.Scenario.Inventories.GetAvailableQuantity(provider.InventoryId, ResourceIds.Fuel) > 0 &&
                extraction.Scenario.Inventories.GetAvailableQuantity(provider.InventoryId, ResourceIds.Ammunition) > 0)
                observed |= PlayerGuidanceMilestone.Supply;
        }
        foreach (var policyEntity in entities.Query<LogisticsStockPolicy>())
        {
            var policy = entities.GetComponent<LogisticsStockPolicy>(policyEntity);
            if (policy.ResourceId == ResourceIds.FerrousOre && policy.Enabled &&
                entities.TryGetComponent(policy.TargetEntity, out CompletedBuilding target) &&
                target.Owner == extraction.Player && target.BuildingId == BuildingIds.Smelter)
                observed |= PlayerGuidanceMilestone.OrePolicy;
        }
        var evidence = extraction.Scenario.Progress.Capture(extraction.Player, context.Tick);
        foreach (var item in evidence)
        {
            if (item.Player != extraction.Player || item.Tick.Value > context.Tick.Value || item.Quantity <= 0) continue;
            observed |= item.Kind switch
            {
                GameplayProgressKind.ConveyorDelivered => PlayerGuidanceMilestone.ConveyorDelivery |
                    (item.DefinitionId == BuildingIds.Smelter.Value && item.Resource == ResourceIds.FerrousOre ? PlayerGuidanceMilestone.OreDelivered : 0) |
                    (item.DefinitionId == BuildingIds.SupplyDepot.Value && item.Resource == ResourceIds.Ammunition ? PlayerGuidanceMilestone.DepotAmmunitionDelivered : 0),
                GameplayProgressKind.ProductionCompleted => PlayerGuidanceMilestone.ProductionOutput,
                GameplayProgressKind.ResearchCompleted => PlayerGuidanceMilestone.ResearchCompleted,
                GameplayProgressKind.Extraction when item.Resource == ResourceIds.FerrousOre => PlayerGuidanceMilestone.FerrousExtraction,
                GameplayProgressKind.SteelCompleted => PlayerGuidanceMilestone.SteelProcessing,
                GameplayProgressKind.ScoutProduced => PlayerGuidanceMilestone.Scout | PlayerGuidanceMilestone.FactoryInputs,
                GameplayProgressKind.OrePolicyApplied => PlayerGuidanceMilestone.OrePolicy,
                GameplayProgressKind.SupplyTruckProduced => PlayerGuidanceMilestone.SupplyTruckProduced,
                GameplayProgressKind.CargoUnloaded when item.DefinitionId == BuildingIds.Smelter.Value && item.Resource == ResourceIds.FerrousOre => PlayerGuidanceMilestone.OreDelivered,
                GameplayProgressKind.CargoUnloaded when item.DefinitionId == BuildingIds.SupplyDepot.Value && item.Resource == ResourceIds.Fuel => PlayerGuidanceMilestone.DepotFuelDelivered,
                GameplayProgressKind.CargoUnloaded when item.DefinitionId == BuildingIds.SupplyDepot.Value && item.Resource == ResourceIds.Ammunition => PlayerGuidanceMilestone.DepotAmmunitionDelivered,
                GameplayProgressKind.FieldFuelTransferred or GameplayProgressKind.FieldAmmunitionTransferred => PlayerGuidanceMilestone.FieldSupplied,
                GameplayProgressKind.SupplyLoadCompleted when item.DefinitionId == BuildingIds.SupplyDepot.Value => PlayerGuidanceMilestone.SupplyTruckLoaded,
                GameplayProgressKind.DisruptionResolved => PlayerGuidanceMilestone.DisruptionResolved,
                _ => 0
            };
        }
        // Opaque current contacts are sufficient evidence of scouting; no enemy ECS lookup is needed.
        if (intelligence is not null && intelligence.Tick == context.Tick &&
            intelligence.Faction == new FactionId(checked((uint)extraction.Player.Value)))
            for (int i = 0; i < intelligence.Contacts.Count; i++)
                if (intelligence.Contacts[i].IsCurrent && intelligence.Contacts[i].State is IntelligenceState.Detected or IntelligenceState.Identified)
                { observed |= PlayerGuidanceMilestone.OpponentContact; break; }
        return new(extraction.Scenario.Simulation.SessionId, context.Tick, extraction.Player, observed, evidence)
            { Practice=extraction.Scenario.Practice?.Capture(extraction.Player,context.Tick) };
    }
}
