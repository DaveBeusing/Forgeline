using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

[Flags]
public enum PlayerGuidanceMilestone : ushort
{
    None = 0,
    CommandCore = 1,
    Power = 2,
    FerrousExtraction = 4,
    SteelProcessing = 8,
    VehicleFactory = 16,
    Scout = 32,
    Supply = 64,
    OpponentContact = 128
}

public readonly record struct PlayerGuidanceSummary(
    SimulationSessionId SessionId, SimulationTick Tick, PlayerId Player, PlayerGuidanceMilestone Observed);

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
                if (building.BuildingId == BuildingIds.CommandCore) observed |= PlayerGuidanceMilestone.CommandCore;
                if (building.BuildingId == BuildingIds.PowerPlant && entities.TryGetComponent(entity, out PowerGenerator producer) &&
                    producer.Enabled && producer.State == PowerGeneratorState.Generating) observed |= PlayerGuidanceMilestone.Power;
                if (building.BuildingId == BuildingIds.VehicleFactory) observed |= PlayerGuidanceMilestone.VehicleFactory;
                // The authored Smelter exclusively processes Steel. Starting inventory is not production evidence.
                if (building.BuildingId == BuildingIds.Smelter && entities.TryGetComponent(entity, out ProductionFacility facility) &&
                    facility.Capabilities == ProductionCapability.SteelProcessing && facility.CompletedCycles > 0)
                    observed |= PlayerGuidanceMilestone.SteelProcessing;
            }
            if (entities.TryGetComponent(entity, out ResourceExtractor extractor) &&
                extractor.Owner == new FactionId(checked((uint)extraction.Player.Value)) && extractor.ResourceId == ResourceIds.FerrousOre &&
                extractor.State is ResourceExtractorState.Extracting or ResourceExtractorState.OutputConstrained or ResourceExtractorState.PowerConstrained)
                observed |= PlayerGuidanceMilestone.FerrousExtraction;
            if (entities.TryGetComponent(entity, out UnitIdentity unit) && unit.UnitId == UnitIds.ScoutVehicle)
                observed |= PlayerGuidanceMilestone.Scout;
            if (entities.TryGetComponent(entity, out SupplyProvider provider) && provider.Owner == extraction.Player && provider.Enabled &&
                (!entities.TryGetComponent(entity, out SupplyDepot depot) || depot.State == SupplyDepotState.Operational) &&
                extraction.Scenario.Inventories.Contains(provider.InventoryId) &&
                extraction.Scenario.Inventories.GetAvailableQuantity(provider.InventoryId, ResourceIds.Fuel) > 0 &&
                extraction.Scenario.Inventories.GetAvailableQuantity(provider.InventoryId, ResourceIds.Ammunition) > 0)
                observed |= PlayerGuidanceMilestone.Supply;
        }
        // Opaque current contacts are sufficient evidence of scouting; no enemy ECS lookup is needed.
        if (intelligence is not null && intelligence.Tick == context.Tick)
            for (int i = 0; i < intelligence.Contacts.Count; i++)
                if (intelligence.Contacts[i].IsCurrent && intelligence.Contacts[i].State is IntelligenceState.Detected or IntelligenceState.Identified)
                { observed |= PlayerGuidanceMilestone.OpponentContact; break; }
        return new(extraction.Scenario.Simulation.SessionId, context.Tick, extraction.Player, observed);
    }
}
