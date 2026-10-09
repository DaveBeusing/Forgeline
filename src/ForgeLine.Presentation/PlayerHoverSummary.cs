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
    IdentifiedContact
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
    string Requirement = "");

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

        if (owned)
        {
            var details = PlayerExperienceSnapshotFactory.CaptureSelection(entities, extraction.Player,
                new[] { entity }, scenario.Inventories, scenario.Production, scenario.UnitProduction,
                scenario.Services.UnitDefinitions, scenario.Services.BuildingDefinitions);
            if (details.Count != 1) return null;
            var category = details.Kind switch
            {
                PlayerSelectionKind.Unit => PlayerHoverCategory.Unit,
                PlayerSelectionKind.Construction => PlayerHoverCategory.Construction,
                _ => PlayerHoverCategory.Building
            };
            return new(entity, scenario.Simulation.SessionId, context.Tick, category,
                details.DisplayName, SelectionInspectorHudModel.ResolveRoleIcon(details), details);
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
