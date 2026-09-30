using System.Collections.ObjectModel;
using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public enum PlacementPreviewFreshness : byte
{
    Unavailable = 0,
    Current = 1,
    Stale = 2
}

public readonly record struct BuildingPlacementPreviewRequest(
    ulong RequestId,
    PlayerId Issuer,
    BuildingId BuildingId,
    Vector3 RequestedPosition,
    BuildingOrientation Orientation);

public readonly record struct BuildingPlacementPreviewReadModel(
    ulong RequestId,
    SimulationTick CompletedTick,
    BuildingPlacementPreview Preview);

internal readonly record struct PresentationInteractionRequestSnapshot(
    IReadOnlyList<EntityId> SelectedEntities,
    BuildingPlacementPreviewRequest? PlacementRequest,
    bool DebugEnabled,
    float DebugPlaneHeight,
    StrategicOverlayMode StrategicOverlay);

public sealed class PresentationInteractionState
{
    private readonly object _gate = new();
    private EntityId[] _selectedEntities = [];
    private BuildingPlacementPreviewRequest? _placementRequest;
    private bool _debugEnabled;
    private float _debugPlaneHeight;
    private StrategicOverlayMode _strategicOverlay;
    private ulong _nextPlacementRequestId = 1;

    public void SetSelection(
        IReadOnlyCollection<EntityId> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        lock (_gate)
        {
            _selectedEntities =
                entities.ToArray();
        }
    }

    public ulong RequestPlacementPreview(
        PlayerId issuer,
        BuildingId buildingId,
        Vector3 requestedPosition,
        BuildingOrientation orientation)
    {
        if (!issuer.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(issuer));
        }

        if (!buildingId.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(buildingId));
        }

        if (!float.IsFinite(requestedPosition.X) ||
            !float.IsFinite(requestedPosition.Y) ||
            !float.IsFinite(requestedPosition.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(requestedPosition));
        }

        if (!Enum.IsDefined(orientation))
        {
            throw new ArgumentOutOfRangeException(nameof(orientation));
        }

        lock (_gate)
        {
            ulong requestId = _nextPlacementRequestId++;

            if (requestId == 0)
            {
                throw new OverflowException(
                    "Placement preview request identifier space has been exhausted.");
            }

            _placementRequest =
                new BuildingPlacementPreviewRequest(
                    requestId,
                    issuer,
                    buildingId,
                    requestedPosition,
                    orientation);

            return requestId;
        }
    }

    public void ClearPlacementPreview()
    {
        lock (_gate)
        {
            _placementRequest = null;
        }
    }

    public void SetDebugState(
        bool enabled,
        float planeHeight)
    {
        if (!float.IsFinite(planeHeight))
        {
            throw new ArgumentOutOfRangeException(nameof(planeHeight));
        }

        lock (_gate)
        {
            _debugEnabled = enabled;
            _debugPlaneHeight = planeHeight;
        }
    }

    public void SetStrategicOverlay(
        StrategicOverlayMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        lock (_gate)
        {
            _strategicOverlay = mode;
        }
    }

    internal PresentationInteractionRequestSnapshot Capture()
    {
        lock (_gate)
        {
            return new PresentationInteractionRequestSnapshot(
                Array.AsReadOnly(
                    _selectedEntities.ToArray()),
                _placementRequest,
                _debugEnabled,
                _debugPlaneHeight,
                _strategicOverlay);
        }
    }
}

public sealed class PresentationDebugSnapshot
{
    private readonly IReadOnlyList<TacticalCombatDebugEntry> _tacticalEntries;
    private readonly IReadOnlyList<IntelligenceSensorDebugEntry> _intelligenceSensors;
    private readonly IReadOnlyList<SkirmishOpponentDebugReadModel> _opponents;
    private readonly IReadOnlyDictionary<
        string,
        StrategicInfrastructureOperationalState> _crossingStates;

    public PresentationDebugSnapshot(
        GroundMovementDebugSnapshot movement,
        FormationMovementDebugSnapshot formation,
        NavigationWorld navigationWorld,
        NavigationPath? navigationPath,
        SpatialIndexDebugSnapshot spatial,
        BuildingConstructionDebugSnapshot? construction,
        ResourceExtractionDebugSnapshot? resources,
        LogisticsNetworkDebugSnapshot? logistics,
        CargoTransportDebugSnapshot? cargoTransport,
        AutomatedDistributionDebugSnapshot? distribution,
        LogisticsCapacityDebugSnapshot? logisticsCapacity,
        BattlefieldSupplyDebugSnapshot? battlefieldSupply,
        CombatDebugSnapshot? combat,
        ArtilleryDebugSnapshot? artillery,
        CombatReadinessDebugSnapshot? readiness,
        IReadOnlyList<TacticalCombatDebugEntry> tacticalEntries,
        TacticalCombatMetrics tacticalMetrics,
        AutomaticResupplyDecisionMetrics resupplyDecisionMetrics,
        IReadOnlyList<IntelligenceSensorDebugEntry> intelligenceSensors,
        BattlefieldIntelligenceMetrics intelligenceMetrics,
        IReadOnlyList<SkirmishOpponentDebugReadModel> opponents,
        IReadOnlyDictionary<
            string,
            StrategicInfrastructureOperationalState> crossingStates)
    {
        Movement = movement;
        Formation = formation;
        NavigationWorld =
            navigationWorld ??
            throw new ArgumentNullException(nameof(navigationWorld));
        NavigationPath = navigationPath;
        Spatial = spatial;
        Construction = construction;
        Resources = resources;
        Logistics = logistics;
        CargoTransport = cargoTransport;
        Distribution = distribution;
        LogisticsCapacity = logisticsCapacity;
        BattlefieldSupply = battlefieldSupply;
        Combat = combat;
        Artillery = artillery;
        Readiness = readiness;
        TacticalMetrics = tacticalMetrics;
        ResupplyDecisionMetrics = resupplyDecisionMetrics;
        IntelligenceMetrics = intelligenceMetrics;

        _tacticalEntries =
            Array.AsReadOnly(
                tacticalEntries.ToArray());
        _intelligenceSensors =
            Array.AsReadOnly(
                intelligenceSensors.ToArray());
        _opponents =
            Array.AsReadOnly(
                opponents.ToArray());
        _crossingStates =
            new ReadOnlyDictionary<
                string,
                StrategicInfrastructureOperationalState>(
                    crossingStates.ToDictionary(
                        static pair => pair.Key,
                        static pair => pair.Value,
                        StringComparer.Ordinal));
    }

    public GroundMovementDebugSnapshot Movement { get; }

    public FormationMovementDebugSnapshot Formation { get; }

    public NavigationWorld NavigationWorld { get; }

    public NavigationPath? NavigationPath { get; }

    public SpatialIndexDebugSnapshot Spatial { get; }

    public BuildingConstructionDebugSnapshot? Construction { get; }

    public ResourceExtractionDebugSnapshot? Resources { get; }

    public LogisticsNetworkDebugSnapshot? Logistics { get; }

    public CargoTransportDebugSnapshot? CargoTransport { get; }

    public AutomatedDistributionDebugSnapshot? Distribution { get; }

    public LogisticsCapacityDebugSnapshot? LogisticsCapacity { get; }

    public BattlefieldSupplyDebugSnapshot? BattlefieldSupply { get; }

    public CombatDebugSnapshot? Combat { get; }

    public ArtilleryDebugSnapshot? Artillery { get; }

    public CombatReadinessDebugSnapshot? Readiness { get; }

    public IReadOnlyList<TacticalCombatDebugEntry> TacticalEntries =>
        _tacticalEntries;

    public TacticalCombatMetrics TacticalMetrics { get; }

    public AutomaticResupplyDecisionMetrics ResupplyDecisionMetrics { get; }

    public IReadOnlyList<IntelligenceSensorDebugEntry> IntelligenceSensors =>
        _intelligenceSensors;

    public BattlefieldIntelligenceMetrics IntelligenceMetrics { get; }

    public IReadOnlyList<SkirmishOpponentDebugReadModel> Opponents =>
        _opponents;

    public IReadOnlyDictionary<
        string,
        StrategicInfrastructureOperationalState> CrossingStates =>
        _crossingStates;
}

public sealed class PresentationExtractionContext
{
    public PresentationExtractionContext(
        VerticalSliceScenario scenario,
        PlayerId player,
        PresentationInteractionState interaction,
        PlayerCommandGateway commands)
    {
        Scenario =
            scenario ??
            throw new ArgumentNullException(nameof(scenario));

        if (!player.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(player));
        }

        Player = player;
        Interaction =
            interaction ??
            throw new ArgumentNullException(nameof(interaction));
        Commands =
            commands ??
            throw new ArgumentNullException(nameof(commands));

        if (commands.SessionId !=
            scenario.Simulation.SessionId)
        {
            throw new ArgumentException(
                "Presentation extraction and command delivery must use the same simulation session.",
                nameof(commands));
        }

        Side =
            player == scenario.West.Player
                ? scenario.West
                : player == scenario.East.Player
                    ? scenario.East
                    : throw new ArgumentOutOfRangeException(
                        nameof(player),
                        "Player is not part of the vertical-slice match.");
    }

    public VerticalSliceScenario Scenario { get; }

    public PlayerId Player { get; }

    public SkirmishStartingBase Side { get; }

    public PresentationInteractionState Interaction { get; }

    public PlayerCommandGateway Commands { get; }
}
