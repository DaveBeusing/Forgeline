using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public enum PlayerHoverCategory : byte
{
    Unit,
    Building,
    Construction,
    Deposit,
    IdentifiedContact,
    Conveyor,
    Bridge
}

// Facts are authorized before publication. Unavailable numeric facts are nullable.
public readonly record struct PlayerHoverSummary(
    EntityId Entity,
    SimulationSessionId SessionId,
    SimulationTick Tick,
    PlayerHoverCategory Category,
    string DisplayName,
    RtsUiIcon RoleIcon,
    PlayerSelectionSummary OwnedDetails,
    double? RemainingQuantity = null,
    string ExtractionState = "",
    string Requirement = "",
    ProductionInputInformation? ProductionInput = null);

internal static class PlayerHoverSummaryFactory
{
    public static PlayerHoverSummary? Capture(
        SimulationContext context, PresentationExtractionContext extraction, EntityId entity)
    {
        var entities = context.Entities;
        if (!entity.IsValid || !entities.IsAlive(entity) ||
            !entities.TryGetComponent(entity, out WorldTransform transform) ||
            !entities.TryGetComponent(entity, out VisualIdentity visual) ||
            ((uint)visual.Visibility & (uint)RenderVisibilityMask.World) == 0)
            return null;

        var scenario = extraction.Scenario;
        var faction = new FactionId(checked((uint)extraction.Player.Value));
        bool owned = entities.TryGetComponent(entity, out ControllableEntity control) &&
            control.Owner == extraction.Player;

        if (owned && entities.TryGetComponent(entity,out ConveyorPiece conveyor))
        {
            var detail=PlayerExperienceSnapshotFactory.CaptureSelection(entities,extraction.Player,new[]{entity},
                scenario.Inventories,scenario.Production,scenario.UnitProduction,
                scenario.Services.UnitDefinitions,scenario.Services.BuildingDefinitions);
            var row=ConveyorConstructionService.Capture(entities,extraction.Player,scenario.Inventories).First(p=>p.Entity==entity);
            string info=row.Flow is {} flow?FormattableString.Invariant(
                $"IN {flow.InputPerSecond:0.##}/s; OUT {flow.OutputPerSecond:0.##}/s; BUFFER {conveyor.BufferedQuantity:0.##}/{flow.Capacity:0}; NETWORK {flow.Network}; IN [{flow.InputA}, {flow.InputB}] OUT [{flow.OutputA}, {flow.OutputB}]. {flow.Cause}."):
                "STRUCTURAL PART; NO CARGO";
            if(row.Cargo is {} cargo && cargo.Count>0)info+=" CARGO: "+string.Join(", ",cargo.Select(c=>FormattableString.Invariant($"{c.ResourceId}: {c.Quantity:0.##}")));
            return new(entity,scenario.Simulation.SessionId,context.Tick,PlayerHoverCategory.Conveyor,
                "Conveyor / "+ConveyorGeometry.Slug(conveyor.Placement.Module),RtsUiIcon.CommandBuild,detail,
                ExtractionState:row.Status.ToString(),
                Requirement:info+" BUILD MENU / V TO REPAIR, REMOVE OR REPLACE. OCCUPIED PARTS MUST DRAIN; REPAIR WRECKS TO RECOVER CARGO.");
        }
        if (entities.HasComponent<BridgeCombat>(entity) &&
            scenario.Intelligence.IsEntityCurrentlyIdentified(faction, entity))
        {
            var infrastructure = entities.GetComponent<StrategicInfrastructure>(entity);
            var state = entities.GetComponent<StrategicInfrastructureState>(entity);
            var health = entities.GetComponent<ForgeLine.Combat.HealthState>(entity);
            string status = FormattableString.Invariant(
                $"{state.State.ToString().ToUpperInvariant()} / HP {health.Current:0}/{health.Maximum:0} / WORK {state.RestorationProgressTicks}/{infrastructure.RestorationTicks}");
            bool paid = entities.TryGetComponent(entity, out BridgeRepairWork work);
            if (paid && work.Block != BridgeRepairFailure.None) status += " / " + BridgeRepairText.Describe(work.Block);
            return new(entity, scenario.Simulation.SessionId, context.Tick, PlayerHoverCategory.Bridge,
                infrastructure.Key, RtsUiIcon.UnitLogistics, PlayerSelectionSummary.Empty,
                ExtractionState: status, Requirement: paid ? "KIT PAID / PAYING PLAYER'S ENGINEER TO RESUME" :
                    "COMBAT ENGINEER WITHIN 32m / OWN STOCK WITHIN 64m / 120 STEEL + 20 ELECTRONICS");
        }
        if (owned)
        {
            var details = PlayerExperienceSnapshotFactory.CaptureSelection(entities, extraction.Player,
                new[] { entity }, scenario.Inventories, scenario.Production, scenario.UnitProduction,
                scenario.Services.UnitDefinitions, scenario.Services.BuildingDefinitions);
            if (details.Count != 1) return null;
            details = PlayerTacticalProtection.Attach(entities, extraction.Player, details, scenario.Battlefield.TacticalAreas);
            var category = details.Kind switch
            {
                PlayerSelectionKind.Unit => PlayerHoverCategory.Unit,
                PlayerSelectionKind.Construction => PlayerHoverCategory.Construction,
                _ => PlayerHoverCategory.Building
            };
            return new(entity, scenario.Simulation.SessionId, context.Tick, category,
                details.DisplayName, SelectionInspectorHudModel.ResolveRoleIcon(details), details,
                ProductionInput: ProductionInputInformationCapture.Capture(context, extraction, entity));
        }

        bool positionVisible = scenario.Intelligence.GetTerrainState(faction,
            scenario.Intelligence.WorldToCell(transform.Position)) == IntelligenceState.Visible;
        if (entities.TryGetComponent(entity, out ResourceDeposit deposit))
        {
            if (!positionVisible || !entities.TryGetComponent(entity, out WorldPresentationIdentity world) ||
                !world.Inspectable || !scenario.Services.Resources.TryGet(deposit.ResourceId, out var resource))
                return null;

            // Only a locally owned extractor may publish its operational state.
            string state = deposit.IsDepleted ? "DEPLETED" : "AVAILABLE";
            foreach (EntityId extractorEntity in entities.Query<ResourceExtractor>())
            {
                var extractor = entities.GetComponent<ResourceExtractor>(extractorEntity);
                if (extractor.Deposit != entity || extractor.Owner != faction) continue;
                state = extractor.State.ToString();
                break;
            }
            string requirement = scenario.Services.BuildingDefinitions.TryGet(BuildingIds.Extractor, out var mine)
                ? mine.DisplayName : string.Empty;
            return new(entity, scenario.Simulation.SessionId, context.Tick, PlayerHoverCategory.Deposit,
                resource.DisplayName, RtsUiIcon.CommandBuild, PlayerSelectionSummary.Empty,
                deposit.RemainingQuantity, state, requirement);
        }

        // Hostile metrics, inventory, work, supply and orders never cross this boundary.
        if (!entities.TryGetComponent(entity, out IntelligenceSignature signature) ||
            signature.Faction == faction || !scenario.Intelligence.IsEntityCurrentlyIdentified(faction, entity))
            return null;
        string name = "IDENTIFIED CONTACT";
        RtsUiIcon icon = RtsUiIcon.MinimapVisibleEnemy;
        if (entities.TryGetComponent(entity, out UnitIdentity unit) &&
            scenario.Services.UnitDefinitions.TryGet(unit.UnitId, out var unitDefinition))
        {
            name = unitDefinition.DisplayName;
            icon = RtsUiIconCatalog.ResolveUnitRole(unit.UnitId);
        }
        else if (entities.TryGetComponent(entity, out CompletedBuilding building) &&
            scenario.Services.BuildingDefinitions.TryGet(building.BuildingId, out var buildingDefinition))
        {
            name = buildingDefinition.DisplayName;
            icon = RtsUiIconCatalog.ResolveBuildingRole(building.BuildingId);
        }
        return new(entity, scenario.Simulation.SessionId, context.Tick, PlayerHoverCategory.IdentifiedContact,
            name, icon, PlayerSelectionSummary.Empty);
    }
}
