using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Navigation;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public readonly record struct NavigationAgent(
    NavigationMovementClass MovementClass)
{
    public NavigationCapabilities Capabilities =>
        NavigationCapabilities.For(MovementClass);
}

public readonly record struct NavigationPendingPath(
    NavigationPathRequest Request,
    MovementOrder OriginalOrder);

public readonly record struct NavigationRouteState(
    MovementOrder OriginalOrder,
    NavigationPath Path,
    int NextWaypointIndex,
    SimulationTick ActiveWaypointTick)
{
    public bool HasActiveWaypoint =>
        ActiveWaypointTick != SimulationTick.Zero &&
        NextWaypointIndex > 0 &&
        NextWaypointIndex <= Path.Waypoints.Count;
}

public readonly record struct NavigationFailureState(
    MovementOrder OriginalOrder,
    NavigationFailureReason FailureReason,
    NavigationVersion NavigationVersion,
    SimulationTick FailedAtTick);

public readonly record struct HierarchicalNavigationDiagnosticsSnapshot(
    ulong QueuedPathCount,
    ulong CompletedPathCount,
    ulong FailedPathCount,
    ulong CanceledPathCount,
    ulong StaleResultCount,
    int PendingPathCount,
    int ActiveRouteCount,
    int LastExpandedHighLevelNodes,
    int LastExpandedLocalNodes,
    float LastRouteLengthMeters,
    TimeSpan LastLatency);

public sealed class HierarchicalNavigationSystem : ISimulationSystem
{
    private readonly HierarchicalPathfinder _pathfinder;
    private readonly ConcurrentQueue<NavigationPathResult> _completed = new();
    private readonly List<EntityId> _agents = new();
    private ulong _nextRequestId = 1;
    private ulong _queuedPathCount;
    private ulong _completedPathCount;
    private ulong _failedPathCount;
    private ulong _canceledPathCount;
    private ulong _staleResultCount;
    private int _lastExpandedHighLevelNodes;
    private int _lastExpandedLocalNodes;
    private float _lastRouteLengthMeters;
    private TimeSpan _lastLatency;

    public HierarchicalNavigationSystem(
        HierarchicalPathfinder pathfinder)
    {
        _pathfinder = pathfinder ??
            throw new ArgumentNullException(nameof(pathfinder));
    }

    public SimulationPhase Phase =>
        SimulationPhase.NavigationRequests;

    public NavigationWorld World => _pathfinder.World;

    public HierarchicalNavigationDiagnosticsSnapshot LastDiagnostics
    {
        get;
        private set;
    }

    public NavigationPath? LastCompletedPath { get; private set; }

    public void UpdateWorld(NavigationWorld world)
    {
        _pathfinder.UpdateWorld(world);
    }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ApplyCompletedResults(context);

        _agents.Clear();
        foreach (EntityId entity in context.Entities.Query<
                     WorldTransform,
                     NavigationAgent>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            _agents.Add(entity);
        }

        for (int index = 0; index < _agents.Count; index++)
        {
            ProcessAgent(context, _agents[index]);
        }

        UpdateDiagnostics(context);
    }

    private void ProcessAgent(
        SimulationContext context,
        EntityId entity)
    {
        if (!context.Entities.TryGetComponent(
                entity,
                out WorldTransform transform) ||
            !context.Entities.TryGetComponent(
                entity,
                out NavigationAgent agent))
        {
            return;
        }

        bool hasRoute =
            context.Entities.TryGetComponent(
                entity,
                out NavigationRouteState route);

        if (hasRoute &&
            route.Path.Version != World.Version)
        {
            CancelPending(context, entity);
            RemoveRoute(context, entity, removeManagedOrder: true);

            var replacement = new MovementOrder(
                route.OriginalOrder.Issuer,
                route.OriginalOrder.WorldTarget,
                route.OriginalOrder.SubmittedAtTick,
                context.Tick);

            ScheduleRequest(
                context,
                entity,
                transform,
                agent,
                replacement);
            return;
        }

        bool hasMovementOrder =
            context.Entities.TryGetComponent(
                entity,
                out MovementOrder movementOrder);

        if (hasMovementOrder)
        {
            if (hasRoute &&
                IsManagedWaypoint(movementOrder, route))
            {
                return;
            }

            CancelPending(context, entity);

            if (hasRoute)
            {
                RemoveRoute(
                    context,
                    entity,
                    removeManagedOrder: false);
            }

            RemoveFailure(context, entity);

            ScheduleRequest(
                context,
                entity,
                transform,
                agent,
                movementOrder);
            return;
        }

        if (hasRoute)
        {
            IssueNextWaypointOrComplete(
                context,
                entity,
                route);
        }
    }

    private void ScheduleRequest(
        SimulationContext context,
        EntityId entity,
        in WorldTransform transform,
        in NavigationAgent agent,
        in MovementOrder order)
    {
        if (context.Entities.HasComponent<MovementOrder>(entity))
        {
            context.Entities.RemoveComponent<MovementOrder>(entity);
        }

        ulong requestId = _nextRequestId++;
        if (requestId == 0)
        {
            throw new OverflowException(
                "Navigation request identifier space has been exhausted.");
        }

        var request = new NavigationPathRequest(
            requestId,
            entity,
            transform.Position,
            order.WorldTarget,
            agent.Capabilities,
            World.Version);

        var pending =
            new NavigationPendingPath(request, order);

        if (context.Entities.HasComponent<NavigationPendingPath>(entity))
        {
            context.Entities.SetComponent(entity, pending);
        }
        else
        {
            context.Entities.AddComponent(entity, pending);
        }

        _queuedPathCount++;

        if (context.Jobs.IsAvailable)
        {
            context.Jobs.Schedule(
                cancellationToken =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _completed.Enqueue(
                            new NavigationPathResult(
                                request,
                                new NavigationSearchResult(
                                    request.NavigationVersion,
                                    NavigationFailureReason.Canceled,
                                    Path: null),
                                TimeSpan.Zero));
                        return;
                    }

                    _completed.Enqueue(Compute(request));
                });
        }
        else
        {
            _completed.Enqueue(Compute(request));
        }
    }

    private NavigationPathResult Compute(
        in NavigationPathRequest request)
    {
        long started = Stopwatch.GetTimestamp();

        NavigationSearchResult search;

        if (request.NavigationVersion != World.Version)
        {
            search = new NavigationSearchResult(
                request.NavigationVersion,
                NavigationFailureReason.StaleNavigationVersion,
                Path: null);
        }
        else
        {
            search = _pathfinder.FindPath(
                request.Start,
                request.Destination,
                request.Capabilities);
        }

        TimeSpan latency = Stopwatch.GetElapsedTime(started);
        return new NavigationPathResult(
            request,
            search,
            latency);
    }

    private void ApplyCompletedResults(
        SimulationContext context)
    {
        while (_completed.TryDequeue(
                   out NavigationPathResult result))
        {
            _lastLatency = result.Latency;

            EntityId entity = result.Request.Requester;

            if (!context.Entities.IsAlive(entity) ||
                !context.Entities.TryGetComponent(
                    entity,
                    out NavigationPendingPath pending) ||
                pending.Request.RequestId != result.Request.RequestId)
            {
                _staleResultCount++;
                continue;
            }

            if (context.Entities.TryGetComponent(
                    entity,
                    out MovementOrder newerOrder) &&
                newerOrder.AcceptedAtTick >
                pending.OriginalOrder.AcceptedAtTick)
            {
                context.Entities.RemoveComponent<
                    NavigationPendingPath>(entity);
                _staleResultCount++;
                continue;
            }

            context.Entities.RemoveComponent<
                NavigationPendingPath>(entity);

            if (result.Request.NavigationVersion != World.Version ||
                result.Search.Version != World.Version)
            {
                _staleResultCount++;
                continue;
            }

            if (!result.Succeeded ||
                result.Search.Path is null)
            {
                _failedPathCount++;
                SetFailure(
                    context,
                    entity,
                    pending.OriginalOrder,
                    result.Search.FailureReason);
                continue;
            }

            _completedPathCount++;
            LastCompletedPath = result.Search.Path;

            NavigationPathDiagnostics diagnostics =
                result.Search.Path.Diagnostics;
            _lastExpandedHighLevelNodes =
                diagnostics.ExpandedHighLevelNodes;
            _lastExpandedLocalNodes =
                diagnostics.ExpandedLocalNodes;
            _lastRouteLengthMeters =
                diagnostics.RouteLengthMeters;

            var route = new NavigationRouteState(
                pending.OriginalOrder,
                result.Search.Path,
                NextWaypointIndex: 0,
                ActiveWaypointTick: SimulationTick.Zero);

            if (context.Entities.HasComponent<
                    NavigationRouteState>(entity))
            {
                context.Entities.SetComponent(entity, route);
            }
            else
            {
                context.Entities.AddComponent(entity, route);
            }

            RemoveFailure(context, entity);

            IssueNextWaypointOrComplete(
                context,
                entity,
                route);
        }
    }

    private static void IssueNextWaypointOrComplete(
        SimulationContext context,
        EntityId entity,
        in NavigationRouteState route)
    {
        if (route.NextWaypointIndex >= route.Path.Waypoints.Count)
        {
            if (context.Entities.HasComponent<
                    NavigationRouteState>(entity))
            {
                context.Entities.RemoveComponent<
                    NavigationRouteState>(entity);
            }

            return;
        }

        Vector3 waypoint =
            route.Path.Waypoints[route.NextWaypointIndex];

        var localOrder = new MovementOrder(
            route.OriginalOrder.Issuer,
            waypoint,
            route.OriginalOrder.SubmittedAtTick,
            context.Tick);

        if (context.Entities.HasComponent<MovementOrder>(entity))
        {
            context.Entities.SetComponent(entity, localOrder);
        }
        else
        {
            context.Entities.AddComponent(entity, localOrder);
        }

        var updatedRoute = route with
        {
            NextWaypointIndex = route.NextWaypointIndex + 1,
            ActiveWaypointTick = context.Tick
        };

        context.Entities.SetComponent(entity, updatedRoute);
    }

    private void CancelPending(
        SimulationContext context,
        EntityId entity)
    {
        if (context.Entities.HasComponent<
                NavigationPendingPath>(entity))
        {
            context.Entities.RemoveComponent<
                NavigationPendingPath>(entity);
            _canceledPathCount++;
        }
    }

    private static void RemoveRoute(
        SimulationContext context,
        EntityId entity,
        bool removeManagedOrder)
    {
        if (!context.Entities.TryGetComponent(
                entity,
                out NavigationRouteState route))
        {
            return;
        }

        if (removeManagedOrder &&
            context.Entities.TryGetComponent(
                entity,
                out MovementOrder movementOrder) &&
            IsManagedWaypoint(movementOrder, route))
        {
            context.Entities.RemoveComponent<
                MovementOrder>(entity);
        }

        context.Entities.RemoveComponent<
            NavigationRouteState>(entity);
    }

    private static bool IsManagedWaypoint(
        in MovementOrder movementOrder,
        in NavigationRouteState route)
    {
        if (!route.HasActiveWaypoint ||
            movementOrder.AcceptedAtTick != route.ActiveWaypointTick)
        {
            return false;
        }

        Vector3 expected =
            route.Path.Waypoints[route.NextWaypointIndex - 1];

        return Vector3.DistanceSquared(
                   movementOrder.WorldTarget,
                   expected) <= 0.0001f;
    }

    private void SetFailure(
        SimulationContext context,
        EntityId entity,
        in MovementOrder originalOrder,
        NavigationFailureReason failureReason)
    {
        var failure = new NavigationFailureState(
            originalOrder,
            failureReason,
            World.Version,
            context.Tick);

        if (context.Entities.HasComponent<
                NavigationFailureState>(entity))
        {
            context.Entities.SetComponent(entity, failure);
        }
        else
        {
            context.Entities.AddComponent(entity, failure);
        }
    }

    private static void RemoveFailure(
        SimulationContext context,
        EntityId entity)
    {
        if (context.Entities.HasComponent<
                NavigationFailureState>(entity))
        {
            context.Entities.RemoveComponent<
                NavigationFailureState>(entity);
        }
    }

    private void UpdateDiagnostics(
        SimulationContext context)
    {
        LastDiagnostics =
            new HierarchicalNavigationDiagnosticsSnapshot(
                _queuedPathCount,
                _completedPathCount,
                _failedPathCount,
                _canceledPathCount,
                _staleResultCount,
                context.Entities.GetComponentCount<
                    NavigationPendingPath>(),
                context.Entities.GetComponentCount<
                    NavigationRouteState>(),
                _lastExpandedHighLevelNodes,
                _lastExpandedLocalNodes,
                _lastRouteLengthMeters,
                _lastLatency);
    }
}
