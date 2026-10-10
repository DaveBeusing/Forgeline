using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public sealed class PresentationSnapshot
{
    private readonly RenderInstance[] _instances;

    public PresentationSnapshot(
        SimulationTick tick,
        TimeSpan tickDuration,
        int simulationEntityCount,
        ReadOnlySpan<RenderInstance> instances,
        FactionIntelligenceSnapshot? intelligence = null,
        SimulationSessionId sessionId = default,
        PlayerExperienceSnapshot? playerExperience = null,
        BuildingPlacementPreviewReadModel? placementPreview = null,
        PresentationDebugSnapshot? debug = null,
        BuildingConstructionDebugSnapshot? construction = null,
        SimulationDiagnosticsSnapshot? simulationDiagnostics = null,
        PlayerActionSnapshot? playerActions = null,
        VfxPresentationMetrics vfxMetrics = default,
        StrategicOverlaySnapshot? strategicOverlay = null,
        CombatGroupOperationalSnapshot? combatGroups = null,
        PlayerHoverSummary? hover = null,
        PlayerGuidanceSummary? guidance = null,
        OperationsSnapshot? operations = null)
        : this(instances.ToArray(), tick, tickDuration, simulationEntityCount, intelligence, sessionId, playerExperience, placementPreview, debug, construction, simulationDiagnostics, playerActions, vfxMetrics, strategicOverlay, combatGroups, hover, guidance, operations)
    {
    }

    // Only extraction transfers a fresh, unaliased array; readers can retain it indefinitely.
    internal PresentationSnapshot(
        RenderInstance[] ownedInstances,
        SimulationTick tick,
        TimeSpan tickDuration,
        int simulationEntityCount,
        FactionIntelligenceSnapshot? intelligence = null,
        SimulationSessionId sessionId = default,
        PlayerExperienceSnapshot? playerExperience = null,
        BuildingPlacementPreviewReadModel? placementPreview = null,
        PresentationDebugSnapshot? debug = null,
        BuildingConstructionDebugSnapshot? construction = null,
        SimulationDiagnosticsSnapshot? simulationDiagnostics = null,
        PlayerActionSnapshot? playerActions = null,
        VfxPresentationMetrics vfxMetrics = default,
        StrategicOverlaySnapshot? strategicOverlay = null,
        CombatGroupOperationalSnapshot? combatGroups = null,
        PlayerHoverSummary? hover = null,
        PlayerGuidanceSummary? guidance = null,
        OperationsSnapshot? operations = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            tickDuration,
            TimeSpan.Zero);

        ArgumentOutOfRangeException.ThrowIfNegative(simulationEntityCount);

        SessionId = sessionId;
        Tick = tick;
        TickDuration = tickDuration;
        SimulationEntityCount = simulationEntityCount;
        Intelligence = intelligence;
        PlayerExperience = playerExperience;
        PlacementPreview = placementPreview;
        Debug = debug;
        Construction = construction;
        SimulationDiagnostics = simulationDiagnostics;
        PlayerActions = playerActions;
        VfxMetrics = vfxMetrics;
        StrategicOverlay = strategicOverlay;
        CombatGroups = combatGroups;
        Hover = hover;
        Guidance = guidance;
        Operations = operations;
        _instances = ownedInstances;
    }

    public SimulationSessionId SessionId { get; }

    public SimulationTick Tick { get; }

    public TimeSpan TickDuration { get; }

    public int SimulationEntityCount { get; }

    public FactionIntelligenceSnapshot? Intelligence { get; }

    public PlayerExperienceSnapshot? PlayerExperience { get; }

    public BuildingPlacementPreviewReadModel? PlacementPreview { get; }

    public PresentationDebugSnapshot? Debug { get; }

    public BuildingConstructionDebugSnapshot? Construction { get; }

    public SimulationDiagnosticsSnapshot? SimulationDiagnostics { get; }

    public PlayerActionSnapshot? PlayerActions { get; }

    public VfxPresentationMetrics VfxMetrics { get; }

    public StrategicOverlaySnapshot? StrategicOverlay { get; }

    public CombatGroupOperationalSnapshot? CombatGroups { get; }

    public PlayerHoverSummary? Hover { get; }

    public PlayerGuidanceSummary? Guidance { get; }
    public OperationsSnapshot? Operations { get; }

    public int InstanceCount => _instances.Length;

    public ReadOnlySpan<RenderInstance> Instances => _instances;

    internal RenderInstance GetInstance(int index) => _instances[index];
}
