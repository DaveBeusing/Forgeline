using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game;

public enum StrategicInfrastructureOperationalState : byte
{
    Operational = 1,
    Disabled = 2,
    Restoring = 3
}

public readonly record struct StrategicInfrastructure
{
    public StrategicInfrastructure(
        string key,
        LogisticsEdgeId logisticsEdge,
        AxisAlignedBounds navigationBlocker,
        bool restorable,
        uint restorationTicks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!logisticsEdge.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(logisticsEdge));
        }

        if (restorable && restorationTicks == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(restorationTicks));
        }

        Key = key;
        LogisticsEdge = logisticsEdge;
        NavigationBlocker = navigationBlocker;
        Restorable = restorable;
        RestorationTicks = restorationTicks;
    }

    public string Key { get; }

    public LogisticsEdgeId LogisticsEdge { get; }

    public AxisAlignedBounds NavigationBlocker { get; }

    public bool Restorable { get; }

    public uint RestorationTicks { get; }
}

public readonly record struct StrategicInfrastructureState(
    StrategicInfrastructureOperationalState State,
    uint RestorationProgressTicks)
{
    public bool IsOperational =>
        State == StrategicInfrastructureOperationalState.Operational;

    public static StrategicInfrastructureState Operational =>
        new(
            StrategicInfrastructureOperationalState.Operational,
            0);

    public static StrategicInfrastructureState Disabled =>
        new(
            StrategicInfrastructureOperationalState.Disabled,
            0);
}

internal enum StrategicInfrastructureRequestKind : byte
{
    Disable = 1,
    Restore = 2
}

internal readonly record struct StrategicInfrastructureRequest(
    EntityId Infrastructure,
    StrategicInfrastructureRequestKind Kind,
    SimulationTick SubmittedAtTick);

public readonly record struct StrategicInfrastructureMetrics(
    long DisableCount,
    long RestoreStartedCount,
    long RestoreCompletedCount,
    long RejectedRequestCount,
    int OperationalCount,
    int DisabledCount,
    int RestoringCount,
    NavigationVersion NavigationVersion,
    LogisticsNetworkVersion LogisticsVersion);

public sealed class DisableStrategicInfrastructureCommand
    : ISimulationCommand
{
    public DisableStrategicInfrastructureCommand(
        EntityId infrastructure,
        SimulationTick submittedAtTick)
    {
        if (!infrastructure.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(infrastructure));
        }

        Infrastructure = infrastructure;
        SubmittedAtTick = submittedAtTick;
    }

    public EntityId Infrastructure { get; }

    public SimulationTick SubmittedAtTick { get; }

    public bool Accepted { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Entities.IsAlive(Infrastructure) ||
            !context.Entities.HasComponent<StrategicInfrastructure>(
                Infrastructure))
        {
            Accepted = false;
            return;
        }

        EntityId request =
            context.Entities.CreateEntity();
        context.Entities.AddComponent(
            request,
            new StrategicInfrastructureRequest(
                Infrastructure,
                StrategicInfrastructureRequestKind.Disable,
                SubmittedAtTick));
        Accepted = true;
    }
}

public sealed class RestoreStrategicInfrastructureCommand
    : ISimulationCommand
{
    public RestoreStrategicInfrastructureCommand(
        EntityId infrastructure,
        SimulationTick submittedAtTick)
    {
        if (!infrastructure.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(infrastructure));
        }

        Infrastructure = infrastructure;
        SubmittedAtTick = submittedAtTick;
    }

    public EntityId Infrastructure { get; }

    public SimulationTick SubmittedAtTick { get; }

    public bool Accepted { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Entities.IsAlive(Infrastructure) ||
            !context.Entities.TryGetComponent(
                Infrastructure,
                out StrategicInfrastructure infrastructure) ||
            !infrastructure.Restorable)
        {
            Accepted = false;
            return;
        }

        EntityId request =
            context.Entities.CreateEntity();
        context.Entities.AddComponent(
            request,
            new StrategicInfrastructureRequest(
                Infrastructure,
                StrategicInfrastructureRequestKind.Restore,
                SubmittedAtTick));
        Accepted = true;
    }
}

public sealed class StrategicInfrastructureSystem
    : ISimulationSystem
{
    private readonly LogisticsNetwork _logistics;
    private readonly TerrainWorld _terrain;
    private readonly HierarchicalNavigationSystem _navigation;
    private readonly AxisAlignedBounds[] _persistentObstacles;
    private readonly NavigationGridSettings _gridSettings;
    private readonly NavigationSectorSettings _sectorSettings;
    private readonly NavigationVersionTracker _navigationVersions = new();
    private readonly List<EntityId> _requests = new();
    private readonly List<EntityId> _infrastructure = new();
    private bool _initialized;
    private long _disableCount;
    private long _restoreStartedCount;
    private long _restoreCompletedCount;
    private long _rejectedRequestCount;

    public StrategicInfrastructureSystem(
        LogisticsNetwork logistics,
        TerrainWorld terrain,
        HierarchicalNavigationSystem navigation,
        IEnumerable<AxisAlignedBounds> persistentObstacles,
        NavigationGridSettings? gridSettings = null,
        NavigationSectorSettings? sectorSettings = null)
    {
        _logistics = logistics ??
            throw new ArgumentNullException(nameof(logistics));
        _terrain = terrain ??
            throw new ArgumentNullException(nameof(terrain));
        _navigation = navigation ??
            throw new ArgumentNullException(nameof(navigation));
        ArgumentNullException.ThrowIfNull(persistentObstacles);

        _persistentObstacles =
            persistentObstacles.ToArray();
        _gridSettings =
            gridSettings ?? new NavigationGridSettings();
        _sectorSettings =
            sectorSettings ?? new NavigationSectorSettings();
    }

    public SimulationPhase Phase =>
        SimulationPhase.OrderProcessing;

    public StrategicInfrastructureMetrics Metrics { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        bool topologyChanged =
            ProcessRequests(context);

        topologyChanged |=
            AdvanceRestoration(context);

        if (!_initialized ||
            topologyChanged)
        {
            SynchronizeAuthoritativeTopology(context);
            _initialized = true;
        }

        UpdateMetrics(context);
    }

    private bool ProcessRequests(
        SimulationContext context)
    {
        _requests.Clear();

        foreach (EntityId entity in
                 context.Entities.Query<StrategicInfrastructureRequest>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            _requests.Add(entity);
        }

        bool changed = false;

        for (int index = 0; index < _requests.Count; index++)
        {
            EntityId requestEntity =
                _requests[index];

            if (!context.Entities.TryGetComponent(
                    requestEntity,
                    out StrategicInfrastructureRequest request) ||
                !context.Entities.IsAlive(
                    request.Infrastructure) ||
                !context.Entities.TryGetComponent(
                    request.Infrastructure,
                    out StrategicInfrastructure infrastructure) ||
                !context.Entities.TryGetComponent(
                    request.Infrastructure,
                    out StrategicInfrastructureState state))
            {
                _rejectedRequestCount++;
                context.Entities.DestroyEntity(requestEntity);
                continue;
            }

            switch (request.Kind)
            {
                case StrategicInfrastructureRequestKind.Disable:
                    if (state.State !=
                        StrategicInfrastructureOperationalState.Disabled)
                    {
                        context.Entities.SetComponent(
                            request.Infrastructure,
                            StrategicInfrastructureState.Disabled);
                        _disableCount++;
                        changed = true;
                    }

                    break;

                case StrategicInfrastructureRequestKind.Restore:
                    if (!infrastructure.Restorable ||
                        state.State ==
                        StrategicInfrastructureOperationalState.Operational)
                    {
                        _rejectedRequestCount++;
                        break;
                    }

                    if (state.State !=
                        StrategicInfrastructureOperationalState.Restoring)
                    {
                        context.Entities.SetComponent(
                            request.Infrastructure,
                            new StrategicInfrastructureState(
                                StrategicInfrastructureOperationalState.Restoring,
                                0));
                        _restoreStartedCount++;
                    }

                    break;

                default:
                    _rejectedRequestCount++;
                    break;
            }

            context.Entities.DestroyEntity(requestEntity);
        }

        return changed;
    }

    private bool AdvanceRestoration(
        SimulationContext context)
    {
        _infrastructure.Clear();

        foreach (EntityId entity in
                 context.Entities.Query<
                     StrategicInfrastructure,
                     StrategicInfrastructureState>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            _infrastructure.Add(entity);
        }

        bool changed = false;

        for (int index = 0; index < _infrastructure.Count; index++)
        {
            EntityId entity =
                _infrastructure[index];
            StrategicInfrastructure infrastructure =
                context.Entities.GetComponent<StrategicInfrastructure>(
                    entity);
            StrategicInfrastructureState state =
                context.Entities.GetComponent<StrategicInfrastructureState>(
                    entity);

            if (state.State !=
                StrategicInfrastructureOperationalState.Restoring)
            {
                continue;
            }

            uint progress =
                checked(
                    state.RestorationProgressTicks + 1);

            if (progress >= infrastructure.RestorationTicks)
            {
                context.Entities.SetComponent(
                    entity,
                    StrategicInfrastructureState.Operational);
                _restoreCompletedCount++;
                changed = true;
            }
            else
            {
                context.Entities.SetComponent(
                    entity,
                    new StrategicInfrastructureState(
                        StrategicInfrastructureOperationalState.Restoring,
                        progress));
            }
        }

        return changed;
    }

    private void SynchronizeAuthoritativeTopology(
        SimulationContext context)
    {
        var blockers =
            new List<AxisAlignedBounds>(
                _persistentObstacles.Length +
                context.Entities.GetComponentCount<
                    StrategicInfrastructure>());
        blockers.AddRange(
            _persistentObstacles);

        foreach (EntityId entity in
                 context.Entities.Query<
                     StrategicInfrastructure,
                     StrategicInfrastructureState>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            StrategicInfrastructure infrastructure =
                context.Entities.GetComponent<StrategicInfrastructure>(
                    entity);
            StrategicInfrastructureState state =
                context.Entities.GetComponent<StrategicInfrastructureState>(
                    entity);

            bool operational =
                state.State ==
                StrategicInfrastructureOperationalState.Operational;

            if (_logistics.TryGetEdge(
                    infrastructure.LogisticsEdge,
                    out LogisticsEdge edge) &&
                edge.Enabled != operational)
            {
                _logistics.SetEdgeEnabled(
                    infrastructure.LogisticsEdge,
                    operational);
            }

            if (!operational)
            {
                blockers.Add(
                    infrastructure.NavigationBlocker);
            }
        }

        NavigationVersion version =
            _navigationVersions.Invalidate();
        NavigationWorld world =
            NavigationWorld.Build(
                _terrain,
                blockers,
                _gridSettings,
                _sectorSettings,
                version);
        _navigation.UpdateWorld(world);
    }

    private void UpdateMetrics(
        SimulationContext context)
    {
        int operational = 0;
        int disabled = 0;
        int restoring = 0;

        foreach (EntityId entity in
                 context.Entities.Query<StrategicInfrastructureState>())
        {
            StrategicInfrastructureState state =
                context.Entities.GetComponent<StrategicInfrastructureState>(
                    entity);

            switch (state.State)
            {
                case StrategicInfrastructureOperationalState.Operational:
                    operational++;
                    break;
                case StrategicInfrastructureOperationalState.Disabled:
                    disabled++;
                    break;
                case StrategicInfrastructureOperationalState.Restoring:
                    restoring++;
                    break;
            }
        }

        Metrics =
            new StrategicInfrastructureMetrics(
                _disableCount,
                _restoreStartedCount,
                _restoreCompletedCount,
                _rejectedRequestCount,
                operational,
                disabled,
                restoring,
                _navigation.World.Version,
                _logistics.Version);
    }
}
