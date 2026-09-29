using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Logistics;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public sealed class PresentationExtractor : ISimulationTickObserver
{
    private readonly PresentationSnapshotBuffer _buffer;
    private readonly FactionIntelligenceStore? _intelligence;
    private readonly FactionId _viewingFaction;
    private readonly AxisAlignedBounds? _intelligenceWorldBounds;
    private readonly PresentationExtractionContext? _extraction;

    public PresentationExtractor(
        PresentationSnapshotBuffer buffer,
        FactionIntelligenceStore? intelligence = null,
        FactionId viewingFaction = default,
        AxisAlignedBounds? intelligenceWorldBounds = null)
    {
        _buffer =
            buffer ??
            throw new ArgumentNullException(nameof(buffer));
        _intelligence = intelligence;
        _viewingFaction = viewingFaction;
        _intelligenceWorldBounds = intelligenceWorldBounds;

        if (_intelligence is not null &&
            !_viewingFaction.IsSpecified)
        {
            throw new ArgumentException(
                "Faction-specific intelligence extraction requires a viewing faction.",
                nameof(viewingFaction));
        }
    }

    public PresentationExtractor(
        PresentationSnapshotBuffer buffer,
        PresentationExtractionContext extraction)
        : this(
            buffer,
            extraction?.Scenario.Intelligence ??
                throw new ArgumentNullException(nameof(extraction)),
            new FactionId(
                checked((uint)extraction.Player.Value)),
            extraction.Scenario.Terrain.WorldBounds)
    {
        _extraction = extraction;
    }

    public void OnTickCompleted(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        PresentationInteractionRequestSnapshot interaction =
            _extraction?.Interaction.Capture() ??
            default;

        if (_extraction is not null)
        {
            ApplyDebugCaptureState(
                _extraction.Scenario,
                interaction.DebugEnabled);
        }

        FactionIntelligenceSnapshot? intelligenceSnapshot =
            CaptureIntelligence();

        RenderInstance[] instances =
            CaptureRenderInstances(
                context);

        PlayerExperienceSnapshot? playerExperience =
            _extraction is null
                ? null
                : CapturePlayerExperience(
                    context,
                    interaction);

        BuildingPlacementPreviewReadModel? placementPreview =
            _extraction is null
                ? null
                : CapturePlacementPreview(
                    context,
                    interaction);

        BuildingConstructionDebugSnapshot? construction =
            _extraction is null
                ? null
                : CaptureConstruction(
                    context,
                    interaction.DebugEnabled);

        PresentationDebugSnapshot? debug =
            _extraction is not null &&
            interaction.DebugEnabled
                ? CaptureDebug(
                    context,
                    interaction,
                    construction)
                : null;

        _buffer.Publish(
            new PresentationSnapshot(
                context.Tick,
                context.TickDuration,
                context.Entities.EntityCount,
                instances,
                intelligenceSnapshot,
                _extraction?.Scenario.Simulation.SessionId ??
                    SimulationSessionId.None,
                playerExperience,
                placementPreview,
                debug,
                construction));
    }

    private RenderInstance[] CaptureRenderInstances(
        SimulationContext context)
    {
        int count =
            context.Entities.GetComponentCount<WorldTransform>();

        if (count == 0 ||
            context.Entities.GetComponentCount<VisualIdentity>() == 0)
        {
            return [];
        }

        var instances = new RenderInstance[
            Math.Min(
                count,
                context.Entities.GetComponentCount<VisualIdentity>())];
        int index = 0;

        foreach (EntityId entity in context.Entities.Query<
                     WorldTransform,
                     VisualIdentity>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            if (!IsVisibleToViewer(
                    context.Entities,
                    entity))
            {
                continue;
            }

            WorldTransform transform =
                context.Entities.GetComponent<WorldTransform>(
                    entity);
            VisualIdentity visual =
                context.Entities.GetComponent<VisualIdentity>(
                    entity);

            RenderVisibilityMask visibility =
                (RenderVisibilityMask)(uint)visual.Visibility;

            SelectablePresentationMetadata selectable =
                context.Entities.TryGetComponent(
                    entity,
                    out ControllableEntity controllable) &&
                controllable.IsControllable
                    ? new SelectablePresentationMetadata(
                        controllable.Owner,
                        controllable.Category)
                    : SelectablePresentationMetadata.None;

            instances[index++] =
                new RenderInstance(
                    entity,
                    new RenderTransform(
                        transform.Position,
                        transform.Rotation,
                        transform.Scale),
                    new RenderMeshHandle(
                        visual.VisualId),
                    RenderMaterialHandle.Default,
                    visibility,
                    entity.Index,
                    selectable);
        }

        if (index == instances.Length)
        {
            return instances;
        }

        return instances.AsSpan(
            0,
            index).ToArray();
    }

    private PlayerExperienceSnapshot CapturePlayerExperience(
        SimulationContext context,
        in PresentationInteractionRequestSnapshot interaction)
    {
        PresentationExtractionContext extraction =
            _extraction!;
        VerticalSliceScenario scenario =
            extraction.Scenario;

        return PlayerExperienceSnapshotFactory.Capture(
            context.Entities,
            extraction.Player,
            extraction.Side.CommandCore,
            extraction.Side.StartingInventory,
            scenario.BattlefieldRuntime.MatchStateEntity,
            interaction.SelectedEntities,
            scenario.Inventories,
            scenario.Power,
            scenario.Production,
            scenario.UnitProduction,
            extraction.Commands.LatestFeedback,
            scenario.Intelligence,
            scenario.Services.UnitDefinitions,
            scenario.Services.BuildingDefinitions,
            context.Tick,
            scenario.Simulation.Clock.TicksPerSecond);
    }

    private BuildingPlacementPreviewReadModel? CapturePlacementPreview(
        SimulationContext context,
        in PresentationInteractionRequestSnapshot interaction)
    {
        if (interaction.PlacementRequest is not
            BuildingPlacementPreviewRequest request ||
            request.Issuer !=
            _extraction!.Player)
        {
            return null;
        }

        BuildingPlacementPreview preview =
            _extraction.Scenario.Services.BuildingPlacement.CreatePreview(
                context.Entities,
                request.Issuer,
                request.BuildingId,
                request.RequestedPosition,
                request.Orientation);

        return new BuildingPlacementPreviewReadModel(
            request.RequestId,
            context.Tick,
            preview);
    }

    private BuildingConstructionDebugSnapshot? CaptureConstruction(
        SimulationContext context,
        bool debugEnabled)
    {
        VerticalSliceScenario scenario =
            _extraction!.Scenario;

        if (!debugEnabled &&
            scenario.Services.BuildingConstruction.Metrics.ActiveSites <= 0)
        {
            return null;
        }

        return BuildingConstructionDebugSnapshot.Capture(
            context.Entities,
            scenario.Services.BuildingDefinitions);
    }

    private PresentationDebugSnapshot CaptureDebug(
        SimulationContext context,
        in PresentationInteractionRequestSnapshot interaction,
        BuildingConstructionDebugSnapshot? construction)
    {
        VerticalSliceScenario scenario =
            _extraction!.Scenario;

        ResourceExtractionDebugSnapshot resources =
            ResourceExtractionDebugSnapshot.Capture(
                context.Entities,
                scenario.Extraction.Metrics,
                scenario.Services.Resources);
        LogisticsNetworkDebugSnapshot logistics =
            LogisticsNetworkDebugSnapshot.Capture(
                scenario.Logistics);

        return new PresentationDebugSnapshot(
            scenario.Services.GroundMovement.CaptureDebugSnapshot(),
            scenario.Services.FormationMovement.CaptureDebugSnapshot(),
            scenario.Services.Navigation.World,
            scenario.Services.Navigation.LastCompletedPath,
            scenario.Services.SpatialIndex.CaptureDebugSnapshot(
                interaction.DebugPlaneHeight + 0.1f),
            construction,
            resources,
            logistics,
            scenario.CargoTransport.LastDebugSnapshot,
            scenario.AutomatedDistribution.LastDebugSnapshot,
            scenario.AutomatedDistribution.LastCapacityDebugSnapshot,
            scenario.BattlefieldSupply.LastDebugSnapshot,
            scenario.Services.CombatDebugSnapshots.LastDebugSnapshot,
            scenario.Artillery.LastDebugSnapshot,
            scenario.Readiness.LastDebugSnapshot,
            scenario.Services.TacticalCombat.DebugEntries,
            scenario.Services.TacticalCombat.Metrics,
            scenario.Services.AutomaticResupply.Metrics,
            scenario.Services.BattlefieldIntelligence.DebugSensors,
            scenario.Services.BattlefieldIntelligence.Metrics,
            scenario.Opponents.DebugSnapshot,
            CaptureCrossingStates(
                context.Entities,
                scenario.BattlefieldRuntime));
    }

    private static void ApplyDebugCaptureState(
        VerticalSliceScenario scenario,
        bool enabled)
    {
        scenario.Services.GroundMovement.DebugCaptureEnabled =
            enabled;
        scenario.Services.FormationMovement.DebugCaptureEnabled =
            enabled;
        scenario.BattlefieldSupply.DebugCaptureEnabled =
            enabled;
        scenario.Services.BattlefieldIntelligence.TimingEnabled =
            enabled;
        scenario.Services.BattlefieldIntelligence.DebugCaptureEnabled =
            enabled;
        scenario.Artillery.DebugCaptureEnabled =
            enabled;
        scenario.Services.TargetAcquisition.DebugCaptureEnabled =
            enabled;
        scenario.Services.TacticalCombat.DebugCaptureEnabled =
            enabled;
        scenario.Readiness.DebugCaptureEnabled =
            enabled;
        scenario.Services.CombatDebugSnapshots.DebugCaptureEnabled =
            enabled;
        scenario.Opponents.DebugCaptureEnabled =
            enabled;
    }

    private bool IsVisibleToViewer(
        EntityRegistry entities,
        EntityId entity)
    {
        if (_intelligence is null ||
            !_viewingFaction.IsSpecified ||
            !entities.TryGetComponent(
                entity,
                out IntelligenceSignature signature) ||
            signature.Faction == _viewingFaction)
        {
            return true;
        }

        return _intelligence.IsEntityCurrentlyIdentified(
            _viewingFaction,
            entity);
    }

    private FactionIntelligenceSnapshot? CaptureIntelligence()
    {
        if (_intelligence is null ||
            !_viewingFaction.IsSpecified)
        {
            return null;
        }

        return _intelligenceWorldBounds is AxisAlignedBounds bounds
            ? _intelligence.Capture(
                _viewingFaction,
                bounds)
            : _intelligence.Capture(
                _viewingFaction);
    }

    private static IReadOnlyDictionary<
        string,
        StrategicInfrastructureOperationalState> CaptureCrossingStates(
        EntityRegistry entities,
        PrototypeBattlefieldRuntime runtime)
    {
        var states =
            new Dictionary<
                string,
                StrategicInfrastructureOperationalState>(
                    StringComparer.Ordinal);

        foreach (KeyValuePair<string, EntityId> pair in
                 runtime.CrossingEntities)
        {
            if (entities.TryGetComponent(
                    pair.Value,
                    out StrategicInfrastructureState state))
            {
                states[pair.Key] =
                    state.State;
            }
        }

        return states;
    }
}
