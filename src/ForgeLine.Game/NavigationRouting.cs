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
    MovementOrder OriginalOrder, Quaternion StartRotation = default,
    NavigationBoundSearch? BoundSearch = null, ulong RootGeneration = 0, bool VehicleRouting = false);

public readonly record struct NavigationRouteState(
    MovementOrder OriginalOrder,
    NavigationPath Path,
    int NextWaypointIndex,
    SimulationTick ActiveWaypointTick, NavigationPathRequest? SourceRequest = null, Quaternion StartRotation = default, NavigationBoundSearch? BoundSearch = null)
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

public sealed partial class HierarchicalNavigationSystem : ISimulationSystem, IDisposable
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
        if (VehicleRoutingConnected)
            world.GetSectorGraph(NavigationCapabilities.For(NavigationMovementClass.Wheeled));
    }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        AdvanceVehicleSearches(context);
        ApplyCompletedResults(context);
        AdmitWaitingVehicleSearches(context);

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

        if (UsesVehicleKernel(context, entity) &&
            NavigationReadAdapter.TryCaptureVehicle(context.Entities, entity, _routingClearance, out var precise) &&
            NavigationPrecisionOwnership.IsPrecise(precise))
        {
            if (precise.Pending is { } precisePending && precise.DirectOrder is null)
            {
                if (NavigationPrecisionOwnership.TryBeginOwnedQueueApproach(
                        precise with { DirectOrder = precisePending.OriginalOrder }, out _))
                    context.Entities.AddComponent(entity, precisePending.OriginalOrder);
                else SetFailure(context, entity, precisePending.OriginalOrder, NavigationFailureReason.Canceled);
            }
            if (precise.DirectOrder is { } preciseOrder &&
                context.Entities.TryGetComponent(entity, out NavigationRouteState previousRoute) &&
                !IsManagedWaypoint(preciseOrder, previousRoute))
                RemoveRoute(context, entity, removeManagedOrder: false);
            CancelPending(context, entity);
            // The normal GroundMovement queue/dock handoff runs in Movement.
            return;
        }

        bool hasRoute =
            context.Entities.TryGetComponent(
                entity,
                out NavigationRouteState route);

        if (hasRoute &&
            route.Path.Version != World.Version &&
            (!context.Entities.TryGetComponent(entity, out MovementOrder currentOrder) ||
             IsManagedWaypoint(currentOrder, route)))
        {
            CancelPending(context, entity);
            RemoveRoute(context, entity, removeManagedOrder: true);

            var replacement = new MovementOrder(
                route.OriginalOrder.Issuer,
                route.OriginalOrder.WorldTarget,
                route.OriginalOrder.SubmittedAtTick,
                context.Tick);
            SupplyRescueTravel.RedirectOwnedOrder(
                context, entity, route.OriginalOrder, replacement);

            ScheduleRequest(
                context,
                entity,
                transform,
                agent,
                replacement, route.BoundSearch?.Binding.CommandGeneration ?? 0);
            return;
        }

        bool hasMovementOrder =
            context.Entities.TryGetComponent(
                entity,
                out MovementOrder movementOrder);

        if (hasMovementOrder)
        {
            if (movementOrder.Kind is MovementOrderKind.FormationLocal or MovementOrderKind.LocalApproach)
            {
                CancelPending(context, entity);

                if (hasRoute)
                {
                    RemoveRoute(
                        context,
                        entity,
                        removeManagedOrder: false);
                }

                RemoveFailure(context, entity);
                return;
            }

            if (hasRoute &&
                IsManagedWaypoint(movementOrder, route))
            {
                if (TryAdvanceStuckManagedWaypoint(
                        context,
                        entity,
                        movementOrder,
                        route))
                {
                    return;
                }

                if (context.Entities.TryGetComponent(entity, out GroundMovementState movementState) &&
                    movementState.Status == GroundMovementStatus.Stuck &&
                    movementState.ObservedOrderTick == movementOrder.AcceptedAtTick)
                {
                    RemoveRoute(context, entity, removeManagedOrder: true);
                    var recoveryOrder = new MovementOrder(
                        route.OriginalOrder.Issuer, route.OriginalOrder.WorldTarget,
                        route.OriginalOrder.SubmittedAtTick, context.Tick);
                    SupplyRescueTravel.RedirectOwnedOrder(
                        context, entity, route.OriginalOrder, recoveryOrder);
                    ScheduleRequest(context, entity, transform, agent, recoveryOrder, route.BoundSearch?.Binding.CommandGeneration ?? 0);
                }

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

        if (context.Entities.TryGetComponent(entity, out NavigationPendingPath waiting) && waiting.VehicleRouting)
            TryStartVehicleSearch(context, entity, waiting);

        if (hasRoute)
        {
            IssueNextWaypointOrComplete(
                context,
                entity,
                route);
        }
    }

    /// <summary>Normal replan admission for the current route owner, never a replacement player target.</summary>
    internal bool TryRequestTrafficReplan(SimulationContext context, in NavigationOrderBinding binding)
    {
        EntityId entity = binding.Requester;
        if (!context.Entities.IsAlive(entity) || binding.CommandGeneration == 0 ||
            !NavigationReadAdapter.TryCaptureVehicle(context.Entities, entity, 0, out var observation) ||
            NavigationPrecisionOwnership.IsPrecise(observation) || !NavigationPrecisionOwnership.Matches(observation, binding) ||
            !context.Entities.TryGetComponent(entity, out NavigationRouteState route) ||
            route.OriginalOrder != binding.OriginalOrder || route.SourceRequest?.RequestId != binding.RequestId ||
            !context.Entities.TryGetComponent(entity, out MovementOrder current) || !IsManagedWaypoint(current, route) ||
            !context.Entities.TryGetComponent(entity, out WorldTransform transform) ||
            !context.Entities.TryGetComponent(entity, out NavigationAgent agent)) return false;
        CancelPending(context, entity);
        RemoveRoute(context, entity, removeManagedOrder: true);
        var replacement = route.OriginalOrder with { AcceptedAtTick = context.Tick };
        SupplyRescueTravel.RedirectOwnedOrder(context, entity, route.OriginalOrder, replacement);
        ScheduleRequest(context, entity, transform, agent, replacement, route.BoundSearch?.Binding.CommandGeneration ?? 0);
        return true;
    }

    private void ScheduleRequest(
        SimulationContext context,
        EntityId entity,
        in WorldTransform transform,
        in NavigationAgent agent,
        in MovementOrder order, ulong commandGeneration = 0)
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

        bool vehicleRouting = UsesVehicleKernel(context, entity);
        if (vehicleRouting && commandGeneration == 0)
        {
            commandGeneration = _nextCommandGeneration;
            _nextCommandGeneration = checked(_nextCommandGeneration + 1);
        }
        var pending = new NavigationPendingPath(request, order, transform.Rotation,
            RootGeneration: commandGeneration, VehicleRouting: vehicleRouting);

        if (context.Entities.HasComponent<NavigationPendingPath>(entity))
        {
            context.Entities.SetComponent(entity, pending);
        }
        else
        {
            context.Entities.AddComponent(entity, pending);
        }

        _queuedPathCount++;

        if (vehicleRouting)
        {
            TryStartVehicleSearch(context, entity, pending);
            return;
        }

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
                request.Capabilities,
                projectBlockedEndpoints: true);
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

            // Scheduling removes the previous MovementOrder. Any order now
            // present supersedes it, including another command in the same tick.
            if (context.Entities.HasComponent<MovementOrder>(entity))
            {
                context.Entities.RemoveComponent<
                    NavigationPendingPath>(entity);
                _staleResultCount++;
                continue;
            }

            context.Entities.RemoveComponent<
                NavigationPendingPath>(entity);

            if (result.Request.NavigationVersion != World.Version ||
                result.Search.Version != World.Version ||
                result.Search.FailureReason == NavigationFailureReason.StaleNavigationVersion)
            {
                _staleResultCount++;
                // The request was validly owned, but its immutable world expired.
                // Restore intent for ProcessAgent below instead of orphaning the
                // truck or recursively draining newly scheduled results here.
                var replacement = pending.OriginalOrder with { AcceptedAtTick = context.Tick };
                SupplyRescueTravel.RedirectOwnedOrder(
                    context, entity, pending.OriginalOrder, replacement);
                if (pending.VehicleRouting &&
                    context.Entities.TryGetComponent(entity, out WorldTransform replanTransform) &&
                    context.Entities.TryGetComponent(entity, out NavigationAgent replanAgent))
                    ScheduleRequest(context, entity, replanTransform, replanAgent, replacement, pending.RootGeneration);
                else context.Entities.AddComponent(entity, replacement);
                continue;
            }

            if (!result.Succeeded ||
                result.Search.Path is null)
            {
                _failedPathCount++;
                SupplyRescueTravel.RecordNavigationFailure(
                    context, entity, pending.OriginalOrder, result.Search.FailureReason);
                SetFailure(
                    context,
                    entity,
                    pending.OriginalOrder,
                    result.Search.FailureReason);
                continue;
            }

            if (pending.BoundSearch is { } bound &&
                (!NavigationReadAdapter.TryCaptureVehicle(context.Entities, entity, bound.Search.Vehicle.ClearanceMeters, out var currentVehicle) ||
                 !NavigationPrecisionOwnership.Matches(currentVehicle, bound.Binding) ||
                 currentVehicle.Profile != bound.Search.Vehicle || currentVehicle.Pose != bound.Search.Start))
            {
                _staleResultCount++;
                context.Entities.AddComponent(entity, pending.OriginalOrder);
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

            // A completed geometric search is not proof of affordable travel.
            // Reuse its exact path before exposing any movement waypoint.
            if (!SupplyRescueTravel.ValidateCompletedRoute(
                    context, entity, pending.OriginalOrder, result.Search.Path))
            {
                continue;
            }

            if (!CargoDeliveryFuelPolicy.ValidateCompletedRoute(
                    context, entity, pending.OriginalOrder, result.Search.Path))
            {
                continue;
            }

            var route = new NavigationRouteState(
                pending.OriginalOrder,
                result.Search.Path,
                NextWaypointIndex: 0,
                ActiveWaypointTick: SimulationTick.Zero,
                SourceRequest: result.Request, StartRotation: pending.StartRotation, BoundSearch: pending.BoundSearch);

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

            if (route.BoundSearch is { } bound)
            {
                // A projected endpoint is never permission for an unchecked tail.
                // Only the existing precise queue owner can take over its exact pose.
                if (bound.Search.TargetSemantics == NavigationTargetSemantics.DockAccess &&
                    NavigationReadAdapter.TryCaptureVehicle(context.Entities, entity, bound.Search.Vehicle.ClearanceMeters, out var observation) &&
                    NavigationPrecisionOwnership.Matches(observation, bound.Binding) &&
                    NavigationPrecisionOwnership.TryBeginOwnedQueueApproach(
                        observation with { RouteOrder = route.OriginalOrder }, out var serviceApproach))
                {
                    context.Entities.AddComponent(entity, serviceApproach);
                    SupplyRescueTravel.RedirectOwnedOrder(context, entity, route.OriginalOrder, serviceApproach);
                }
                return;
            }

            if (route.Path.Waypoints.Count > 0 &&
                Vector3.DistanceSquared(route.Path.Waypoints[^1], route.OriginalOrder.WorldTarget) > 0.0001f)
            {
                // Coarse cells can overlap a building while the requested point
                // is physically clear. Finish from the grid edge using collision-aware movement.
                var approach = new MovementOrder(
                    route.OriginalOrder.Issuer,
                    route.OriginalOrder.WorldTarget,
                    route.OriginalOrder.SubmittedAtTick,
                    context.Tick,
                    MovementOrderKind.LocalApproach);
                context.Entities.AddComponent(entity, approach);
                SupplyRescueTravel.RedirectOwnedOrder(
                    context, entity, route.OriginalOrder, approach);
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
        if (_vehicleSearches.Remove(entity, out var search))
        { _fleetScheduler?.Cancel(search.Bound.Binding); }
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

    private static bool TryAdvanceStuckManagedWaypoint(
        SimulationContext context,
        EntityId entity,
        in MovementOrder movementOrder,
        in NavigationRouteState route)
    {
        // Skipping a nearby intermediate corner is recovery, not arrival.
        // The requested final destination must still satisfy normal stop tolerance.
        if (route.NextWaypointIndex >= route.Path.Waypoints.Count ||
            !context.Entities.TryGetComponent(
                entity,
                out GroundMovementState movementState) ||
            movementState.Status != GroundMovementStatus.Stuck ||
            movementState.ObservedOrderTick != movementOrder.AcceptedAtTick ||
            !context.Entities.TryGetComponent(
                entity,
                out GroundMovement movement) ||
            !context.Entities.TryGetComponent(
                entity,
                out WorldTransform transform))
        {
            return false;
        }

        Vector3 delta =
            movementOrder.WorldTarget - transform.Position;
        delta.Y = 0.0f;

        float recoveryRadius =
            movement.StopRadius +
            (movement.Radius * 2.0f);

        if (delta.LengthSquared() >
            recoveryRadius * recoveryRadius)
        {
            return false;
        }

        context.Entities.RemoveComponent<MovementOrder>(entity);
        IssueNextWaypointOrComplete(
            context,
            entity,
            route);
        return true;
    }

    internal static bool IsManagedWaypoint(
        in MovementOrder movementOrder,
        in NavigationRouteState route)
    {
        if (!route.HasActiveWaypoint ||
            movementOrder.Kind != MovementOrderKind.Strategic ||
            movementOrder.TargetHeadingRadians.HasValue ||
            movementOrder.Issuer != route.OriginalOrder.Issuer ||
            movementOrder.SubmittedAtTick != route.OriginalOrder.SubmittedAtTick ||
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
