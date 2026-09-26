using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Logistics;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public sealed class AutomatedDistributionSystem
    : ISimulationSystem,
      ICargoTransportReservationObserver
{
    private const double QuantityEpsilon = 0.000000001;
    private const ulong TerminalRetentionTicks = 20;

    private readonly LogisticsNetwork _network;
    private readonly InventoryStore _inventories;
    private readonly CargoTransportSystem _cargoTransportSystem;
    private readonly LogisticsCapacityTracker _capacityTracker;
    private readonly LogisticsRouteCostPolicy _routePolicy;
    private readonly ulong _retryDelayTicks;
    private readonly uint _maximumTransportAttempts;
    private readonly ulong _fairnessAgingTicks;
    private readonly List<RequestState> _requests = new();
    private readonly List<RequestState> _dispatchCandidates = new();
    private readonly List<PolicyState> _policies = new();
    private ulong _nextRequestId;
    private long _completedRequestCount;
    private long _failedRequestCount;
    private ulong _totalDeliveryLatencyTicks;
    private ulong _maximumDeliveryLatencyTicks;
    private int _unservedDeficitCount;

    public AutomatedDistributionSystem(
        LogisticsNetwork network,
        InventoryStore inventories,
        CargoTransportSystem cargoTransportSystem,
        LogisticsRouteCostPolicy? routePolicy = null,
        ulong retryDelayTicks = 20,
        uint maximumTransportAttempts = 4,
        ulong fairnessAgingTicks = 200,
        LogisticsCapacityTracker? capacityTracker = null)
    {
        _network = network ??
            throw new ArgumentNullException(nameof(network));
        _inventories = inventories ??
            throw new ArgumentNullException(nameof(inventories));
        _cargoTransportSystem = cargoTransportSystem ??
            throw new ArgumentNullException(nameof(cargoTransportSystem));
        _capacityTracker =
            capacityTracker ??
            new LogisticsCapacityTracker();

        ArgumentOutOfRangeException.ThrowIfZero(retryDelayTicks);
        ArgumentOutOfRangeException.ThrowIfZero(maximumTransportAttempts);
        ArgumentOutOfRangeException.ThrowIfZero(fairnessAgingTicks);

        _routePolicy = routePolicy ?? LogisticsRouteCostPolicy.Default;
        _cargoTransportSystem.SetReservationObserver(this);
        _retryDelayTicks = retryDelayTicks;
        _maximumTransportAttempts = maximumTransportAttempts;
        _fairnessAgingTicks = fairnessAgingTicks;
    }

    public SimulationPhase Phase =>
        SimulationPhase.Logistics;

    public AutomatedDistributionMetrics Metrics { get; private set; }

    public LogisticsCapacityTracker CapacityTracker =>
        _capacityTracker;

    public LogisticsCapacityDebugSnapshot LastCapacityDebugSnapshot
    {
        get;
        private set;
    } = LogisticsCapacityDebugSnapshot.Empty;

    public AutomatedDistributionDebugSnapshot LastDebugSnapshot
    {
        get;
        private set;
    } = AutomatedDistributionDebugSnapshot.Empty;

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _capacityTracker.Advance(
            _network,
            context.Tick);
        ReconcileRequests(context);
        CapturePolicies(context.Entities);
        GenerateAndCoalesceRequests(context);
        DispatchPendingRequests(context);
        UpdateMetricsAndDebugSnapshot(context);
        PruneTerminalRequests(context.Tick);
    }

    private void ReconcileRequests(SimulationContext context)
    {
        for (int index = 0; index < _requests.Count; index++)
        {
            RequestState request = _requests[index];

            if (request.CapacityReservationId.IsSpecified &&
                !_capacityTracker.Contains(request.CapacityReservationId))
            {
                request.CapacityReservationId =
                    LogisticsThroughputReservationId.None;
            }

            if (request.State == LogisticsTransportRequestState.RetryPending &&
                context.Tick >= request.NextAttemptTick)
            {
                request.State = LogisticsTransportRequestState.Pending;
                request.StateChangedAtTick = context.Tick;
                request.FailureReason =
                    LogisticsTransportRequestFailureReason.None;
                request.AssignedTruck = EntityId.Invalid;
                request.Origin = LogisticsNodeId.None;
            }

            if (request.State != LogisticsTransportRequestState.Assigned &&
                request.State != LogisticsTransportRequestState.InTransit)
            {
                continue;
            }

            if (!request.AssignedTruck.IsValid ||
                !context.Entities.IsAlive(request.AssignedTruck))
            {
                ReleaseReservationIfPresent(request);
                ReleaseCapacityReservationIfPresent(request);
                RetryOrFail(
                    request,
                    LogisticsTransportRequestFailureReason.TransportFailed,
                    context.Tick);
                continue;
            }

            bool hasReservation =
                context.Entities.TryGetComponent(
                    request.AssignedTruck,
                    out CargoTransportReservation reservation) &&
                reservation.RequestId == request.Id;

            if (!hasReservation && request.ReservedQuantity > QuantityEpsilon)
            {
                request.ReservedQuantity = 0.0;
                request.ReservedSourceInventory = InventoryId.None;

                if (context.Entities.HasComponent<CargoTransportOrder>(
                        request.AssignedTruck))
                {
                    request.State = LogisticsTransportRequestState.InTransit;
                    request.StateChangedAtTick = context.Tick;
                }
            }

            if (!context.Entities.TryGetComponent(
                    request.AssignedTruck,
                    out CargoTransportRuntimeState transportState))
            {
                continue;
            }

            if (transportState.Lifecycle == CargoTransportLifecycleState.Failed)
            {
                ReleaseReservationIfPresent(request);
                ReleaseCapacityReservationIfPresent(request);

                double cargoQuantity = GetTruckCargoQuantity(
                    context.Entities,
                    request.AssignedTruck,
                    request.ResourceId);

                if (cargoQuantity > QuantityEpsilon)
                {
                    MarkFailed(
                        request,
                        LogisticsTransportRequestFailureReason.TransportFailed,
                        context.Tick);
                }
                else
                {
                    RetryOrFail(
                        request,
                        LogisticsTransportRequestFailureReason.TransportFailed,
                        context.Tick);
                }

                continue;
            }

            if (transportState.Lifecycle == CargoTransportLifecycleState.Idle &&
                !context.Entities.HasComponent<CargoTransportOrder>(
                    request.AssignedTruck))
            {
                ReleaseReservationIfPresent(request);
                ReleaseCapacityReservationIfPresent(request);
                CompleteOrContinueRequest(
                    context,
                    request);
            }
        }
    }

    void ICargoTransportReservationObserver.OnCargoReservationConsumed(
        LogisticsTransportRequestId requestId)
    {
        for (int index = 0; index < _requests.Count; index++)
        {
            RequestState request = _requests[index];
            if (request.Id != requestId)
            {
                continue;
            }

            request.ReservedQuantity = 0.0;
            request.ReservedSourceInventory = InventoryId.None;
            return;
        }
    }

    private void CapturePolicies(EntityRegistry entities)
    {
        _policies.Clear();

        foreach (EntityId policyEntity in
                 entities.Query<LogisticsStockPolicy>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            LogisticsStockPolicy policy =
                entities.GetComponent<LogisticsStockPolicy>(policyEntity);

            _policies.Add(new PolicyState(policyEntity, policy));
        }
    }

    private void GenerateAndCoalesceRequests(SimulationContext context)
    {
        _unservedDeficitCount = 0;

        for (int policyIndex = 0;
             policyIndex < _policies.Count;
             policyIndex++)
        {
            PolicyState policyState = _policies[policyIndex];
            LogisticsStockPolicy policy = policyState.Policy;

            if (!policy.Enabled ||
                !context.Entities.IsAlive(policy.TargetEntity) ||
                !_network.TryGetNodeForEntity(
                    policy.TargetEntity,
                    out LogisticsNodeId destinationNodeId) ||
                !_network.TryGetNode(
                    destinationNodeId,
                    out LogisticsNode destinationNode) ||
                !destinationNode.Enabled ||
                !destinationNode.Capabilities.HasFlag(
                    LogisticsNodeCapabilities.CargoDestination) ||
                !TryResolveDestinationInventory(
                    context.Entities,
                    destinationNode,
                    out InventoryId destinationInventory))
            {
                continue;
            }

            double currentQuantity =
                _inventories.GetQuantity(
                    destinationInventory,
                    policy.ResourceId);
            double outstandingQuantity =
                GetOutstandingQuantity(policyState.PolicyEntity);
            double projectedQuantity =
                currentQuantity + outstandingQuantity;

            if (projectedQuantity + QuantityEpsilon >=
                policy.DesiredMinimum)
            {
                continue;
            }

            double requestedQuantity = Math.Max(
                0.0,
                policy.DesiredTarget - projectedQuantity);

            if (requestedQuantity <= QuantityEpsilon)
            {
                continue;
            }

            RequestState? existing =
                FindActiveRequest(policyState.PolicyEntity);

            if (existing is not null)
            {
                if (existing.State == LogisticsTransportRequestState.Pending ||
                    existing.State == LogisticsTransportRequestState.RetryPending)
                {
                    existing.RequestedQuantity = Math.Max(
                        existing.RequestedQuantity,
                        requestedQuantity);
                    existing.Destination = destinationNodeId;
                    existing.Priority = policy.Priority;
                }

                if (existing.State != LogisticsTransportRequestState.Assigned &&
                    existing.State != LogisticsTransportRequestState.InTransit)
                {
                    _unservedDeficitCount++;
                }

                continue;
            }

            var request = new RequestState(
                AllocateRequestId(),
                policyState.PolicyEntity,
                destinationNodeId,
                policy.ResourceId,
                requestedQuantity,
                policy.Priority,
                context.Tick);

            _requests.Add(request);
            _unservedDeficitCount++;
        }
    }

    private void DispatchPendingRequests(SimulationContext context)
    {
        _dispatchCandidates.Clear();

        for (int index = 0; index < _requests.Count; index++)
        {
            RequestState request = _requests[index];
            if (request.State == LogisticsTransportRequestState.Pending)
            {
                _dispatchCandidates.Add(request);
            }
        }

        _dispatchCandidates.Sort(
            (left, right) =>
            {
                int priorityComparison =
                    GetEffectivePriority(left, context.Tick).CompareTo(
                        GetEffectivePriority(right, context.Tick));

                if (priorityComparison != 0)
                {
                    return priorityComparison;
                }

                int ageComparison =
                    left.CreatedAtTick.CompareTo(right.CreatedAtTick);

                return ageComparison != 0
                    ? ageComparison
                    : left.Id.CompareTo(right.Id);
            });

        for (int index = 0;
             index < _dispatchCandidates.Count;
             index++)
        {
            TryDispatch(
                context,
                _dispatchCandidates[index]);
        }
    }

    private void TryDispatch(
        SimulationContext context,
        RequestState request)
    {
        if (!_network.TryGetNode(
                request.Destination,
                out LogisticsNode destination) ||
            !destination.Enabled ||
            !TryResolveDestinationInventory(
                context.Entities,
                destination,
                out InventoryId destinationInventory))
        {
            DelayRequest(
                request,
                LogisticsTransportRequestFailureReason.DestinationUnavailable,
                context.Tick);
            return;
        }

        double destinationCapacity =
            _inventories.GetAddableQuantity(
                destinationInventory,
                request.ResourceId,
                request.RequestedQuantity);

        if (destinationCapacity <= QuantityEpsilon)
        {
            DelayRequest(
                request,
                LogisticsTransportRequestFailureReason.DestinationFull,
                context.Tick);
            return;
        }

        DispatchSelection selection =
            SelectDispatchCandidate(
                context.Entities,
                request,
                destination,
                destinationCapacity);

        if (!selection.Succeeded)
        {
            DelayRequest(
                request,
                selection.FailureReason,
                context.Tick);
            return;
        }

        if (!_capacityTracker.TryReserveRoute(
                _network,
                selection.Route!,
                selection.Quantity,
                context.Tick,
                out LogisticsThroughputReservationId throughputReservation,
                out _))
        {
            DelayRequest(
                request,
                LogisticsTransportRequestFailureReason.CapacitySaturated,
                context.Tick);
            return;
        }

        request.CapacityReservationId =
            throughputReservation;

        InventoryOperationResult reservationResult =
            _inventories.Reserve(
                selection.SourceInventory,
                request.ResourceId,
                selection.Quantity);

        if (!reservationResult.Succeeded)
        {
            DelayRequest(
                request,
                LogisticsTransportRequestFailureReason.ReservationFailed,
                context.Tick);
            return;
        }

        var reservation = new CargoTransportReservation(
            request.Id,
            selection.SourceInventory,
            request.ResourceId,
            selection.Quantity);

        context.Entities.AddComponent(
            selection.TruckEntity,
            reservation);

        var order = new CargoTransportOrder(
            selection.SourceNode.Id,
            request.Destination,
            request.ResourceId,
            selection.Quantity,
            context.Tick,
            CargoPartialLoadPolicy.RequireRequestedQuantity);

        if (!_cargoTransportSystem.TryAssignOrder(
                context.Entities,
                selection.TruckEntity,
                order,
                context.Tick))
        {
            context.Entities.RemoveComponent<CargoTransportReservation>(
                selection.TruckEntity);
            _ = _inventories.ReleaseReservation(
                selection.SourceInventory,
                request.ResourceId,
                selection.Quantity);

            DelayRequest(
                request,
                LogisticsTransportRequestFailureReason.AssignmentFailed,
                context.Tick);
            return;
        }

        request.Origin = selection.SourceNode.Id;
        request.AssignedTruck = selection.TruckEntity;
        request.ReservedSourceInventory = selection.SourceInventory;
        request.ReservedQuantity = selection.Quantity;
        request.AttemptCount++;
        request.State = LogisticsTransportRequestState.Assigned;
        request.FailureReason = LogisticsTransportRequestFailureReason.None;
        request.StateChangedAtTick = context.Tick;
    }

    private DispatchSelection SelectDispatchCandidate(
        EntityRegistry entities,
        RequestState request,
        in LogisticsNode destination,
        double destinationCapacity)
    {
        IReadOnlyList<LogisticsNode> nodes = _network.GetNodes();
        DispatchSelection best = default;
        bool destinationHasOwner =
            TryResolveOwner(
                entities,
                destination.Entity,
                out PlayerId destinationOwner);
        bool hadSurplus = false;
        bool hadStructuralRoute = false;
        bool hadAvailableTruck = false;
        bool hadCapacityBlockedRoute = false;

        for (int index = 0; index < nodes.Count; index++)
        {
            LogisticsNode node = nodes[index];

            if (node.Id == destination.Id ||
                !node.Enabled ||
                !node.Capabilities.HasFlag(
                    LogisticsNodeCapabilities.CargoSource) ||
                !TryResolveSourceInventory(
                    entities,
                    node,
                    out InventoryId sourceInventory))
            {
                continue;
            }

            if (destinationHasOwner &&
                (!TryResolveOwner(
                     entities,
                     node.Entity,
                     out PlayerId sourceOwner) ||
                 sourceOwner != destinationOwner))
            {
                continue;
            }

            double available =
                _inventories.GetAvailableQuantity(
                    sourceInventory,
                    request.ResourceId);
            double retainedTarget =
                GetRetainedSourceTarget(
                    node.Entity,
                    request.ResourceId);
            double surplus = Math.Max(
                0.0,
                available - retainedTarget);

            if (surplus <= QuantityEpsilon)
            {
                continue;
            }

            hadSurplus = true;

            LogisticsRouteSearchResult structuralRoute =
                _network.FindRoute(
                    node.Id,
                    destination.Id,
                    _routePolicy);

            if (!structuralRoute.Succeeded ||
                structuralRoute.Route is null)
            {
                continue;
            }

            hadStructuralRoute = true;

            if (!TrySelectAvailableTruck(
                    entities,
                    destination,
                    node,
                    out EntityId truckEntity,
                    out CargoTransport truck))
            {
                continue;
            }

            hadAvailableTruck = true;

            double quantity = Math.Min(
                request.RequestedQuantity,
                Math.Min(
                    surplus,
                    Math.Min(
                        truck.Capacity,
                        destinationCapacity)));

            if (quantity <= QuantityEpsilon)
            {
                continue;
            }

            LogisticsRouteSearchResult capacityRoute =
                _network.FindCapacityAwareRoute(
                    node.Id,
                    destination.Id,
                    _routePolicy,
                    _capacityTracker,
                    quantity);

            if (!capacityRoute.Succeeded ||
                capacityRoute.Route is null)
            {
                hadCapacityBlockedRoute = true;
                continue;
            }

            if (!best.Succeeded ||
                capacityRoute.Route.TotalCost < best.Route!.TotalCost ||
                (capacityRoute.Route.TotalCost == best.Route!.TotalCost &&
                 node.Id < best.SourceNode.Id))
            {
                best =
                    new DispatchSelection(
                        node,
                        sourceInventory,
                        truckEntity,
                        quantity,
                        capacityRoute.Route,
                        LogisticsTransportRequestFailureReason.None);
            }
        }

        if (best.Succeeded)
        {
            return best;
        }

        LogisticsTransportRequestFailureReason failureReason =
            !hadSurplus
                ? LogisticsTransportRequestFailureReason.NoSourceSurplus
                : !hadStructuralRoute
                    ? LogisticsTransportRequestFailureReason.NoRoute
                    : !hadAvailableTruck
                        ? LogisticsTransportRequestFailureReason.NoTruckAvailable
                        : hadCapacityBlockedRoute
                            ? LogisticsTransportRequestFailureReason.CapacitySaturated
                            : LogisticsTransportRequestFailureReason.AssignmentFailed;

        return DispatchSelection.Failed(failureReason);
    }

    private bool TrySelectAvailableTruck(
        EntityRegistry entities,
        in LogisticsNode destination,
        in LogisticsNode source,
        out EntityId selectedEntity,
        out CargoTransport selectedTransport)
    {
        selectedEntity = EntityId.Invalid;
        selectedTransport = default;

        bool hasOwner =
            TryResolveOwner(
                entities,
                destination.Entity,
                out PlayerId destinationOwner);

        double bestDistanceSquared = double.PositiveInfinity;

        foreach (EntityId entity in
                 entities.Query<CargoTransport>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            CargoTransport transport =
                entities.GetComponent<CargoTransport>(entity);

            if (hasOwner &&
                transport.Owner != destinationOwner)
            {
                continue;
            }

            if (entities.HasComponent<SupplyTruck>(entity))
            {
                continue;
            }

            if (entities.HasComponent<CargoTransportOrder>(entity) ||
                entities.HasComponent<CargoTransportReservation>(entity) ||
                !entities.TryGetComponent(
                    entity,
                    out CargoTransportRuntimeState state) ||
                state.Lifecycle != CargoTransportLifecycleState.Idle ||
                !_inventories.Contains(transport.CargoInventory) ||
                _inventories.GetTotalQuantity(
                    transport.CargoInventory) > QuantityEpsilon)
            {
                continue;
            }

            double distanceSquared = 0.0;
            if (entities.TryGetComponent(
                    entity,
                    out WorldTransform transform))
            {
                distanceSquared =
                    Vector3.DistanceSquared(
                        transform.Position,
                        source.WorldPosition);
            }

            if (!selectedEntity.IsValid ||
                distanceSquared < bestDistanceSquared)
            {
                selectedEntity = entity;
                selectedTransport = transport;
                bestDistanceSquared = distanceSquared;
            }
        }

        return selectedEntity.IsValid;
    }

    private double GetRetainedSourceTarget(
        EntityId sourceEntity,
        ResourceId resourceId)
    {
        double retainedTarget = 0.0;

        for (int index = 0; index < _policies.Count; index++)
        {
            LogisticsStockPolicy policy = _policies[index].Policy;
            if (policy.Enabled &&
                policy.TargetEntity == sourceEntity &&
                policy.ResourceId == resourceId)
            {
                retainedTarget = Math.Max(
                    retainedTarget,
                    policy.DesiredTarget);
            }
        }

        return retainedTarget;
    }

    private double GetOutstandingQuantity(EntityId policyEntity)
    {
        double quantity = 0.0;

        for (int index = 0; index < _requests.Count; index++)
        {
            RequestState request = _requests[index];

            if (request.PolicyEntity == policyEntity &&
                !IsTerminal(request.State))
            {
                quantity += request.RequestedQuantity;
            }
        }

        return quantity;
    }

    private RequestState? FindActiveRequest(EntityId policyEntity)
    {
        for (int index = 0; index < _requests.Count; index++)
        {
            RequestState request = _requests[index];

            if (request.PolicyEntity == policyEntity &&
                !IsTerminal(request.State))
            {
                return request;
            }
        }

        return null;
    }

    private int GetEffectivePriority(
        RequestState request,
        SimulationTick tick)
    {
        int basePriority = (int)request.Priority;
        ulong age = tick.Value >= request.CreatedAtTick.Value
            ? tick.Value - request.CreatedAtTick.Value
            : 0;
        ulong agingSteps = age / _fairnessAgingTicks;
        int boundedAging = (int)Math.Min(
            (ulong)basePriority,
            agingSteps);

        return basePriority - boundedAging;
    }

    private void DelayRequest(
        RequestState request,
        LogisticsTransportRequestFailureReason reason,
        SimulationTick tick)
    {
        ReleaseCapacityReservationIfPresent(request);
        request.State = LogisticsTransportRequestState.RetryPending;
        request.FailureReason = reason;
        request.StateChangedAtTick = tick;
        request.NextAttemptTick =
            new SimulationTick(checked(tick.Value + _retryDelayTicks));
        request.AssignedTruck = EntityId.Invalid;
        request.Origin = LogisticsNodeId.None;
    }

    private void RetryOrFail(
        RequestState request,
        LogisticsTransportRequestFailureReason reason,
        SimulationTick tick)
    {
        ReleaseCapacityReservationIfPresent(request);
        request.AssignedTruck = EntityId.Invalid;
        request.Origin = LogisticsNodeId.None;
        request.TransportFailureCount++;

        if (request.TransportFailureCount >= _maximumTransportAttempts)
        {
            MarkFailed(
                request,
                LogisticsTransportRequestFailureReason.RetryLimitReached,
                tick);
            return;
        }

        DelayRequest(request, reason, tick);
    }

    private void CompleteOrContinueRequest(
        SimulationContext context,
        RequestState request)
    {
        ReleaseCapacityReservationIfPresent(request);
        request.AssignedTruck = EntityId.Invalid;
        request.Origin = LogisticsNodeId.None;
        request.TransportFailureCount = 0;

        if (!context.Entities.TryGetComponent(
                request.PolicyEntity,
                out LogisticsStockPolicy policy) ||
            !policy.Enabled ||
            !_network.TryGetNode(
                request.Destination,
                out LogisticsNode destination) ||
            !TryResolveDestinationInventory(
                context.Entities,
                destination,
                out InventoryId destinationInventory))
        {
            MarkCompleted(request, context.Tick);
            return;
        }

        double currentQuantity =
            _inventories.GetQuantity(
                destinationInventory,
                request.ResourceId);
        double remaining =
            Math.Max(
                0.0,
                policy.DesiredTarget - currentQuantity);

        if (remaining <= QuantityEpsilon)
        {
            MarkCompleted(request, context.Tick);
            return;
        }

        request.RequestedQuantity = remaining;
        request.Priority = policy.Priority;
        request.State = LogisticsTransportRequestState.Pending;
        request.FailureReason =
            LogisticsTransportRequestFailureReason.None;
        request.StateChangedAtTick = context.Tick;
        request.NextAttemptTick = context.Tick;
    }

    private void MarkCompleted(
        RequestState request,
        SimulationTick tick)
    {
        if (IsTerminal(request.State))
        {
            return;
        }

        ReleaseCapacityReservationIfPresent(request);
        request.State = LogisticsTransportRequestState.Completed;
        request.FailureReason = LogisticsTransportRequestFailureReason.None;
        request.StateChangedAtTick = tick;
        request.AssignedTruck = EntityId.Invalid;

        ulong latency = tick.Value >= request.CreatedAtTick.Value
            ? tick.Value - request.CreatedAtTick.Value
            : 0;

        _completedRequestCount++;
        _totalDeliveryLatencyTicks =
            checked(_totalDeliveryLatencyTicks + latency);
        _maximumDeliveryLatencyTicks = Math.Max(
            _maximumDeliveryLatencyTicks,
            latency);
    }

    private void MarkFailed(
        RequestState request,
        LogisticsTransportRequestFailureReason reason,
        SimulationTick tick)
    {
        if (IsTerminal(request.State))
        {
            return;
        }

        ReleaseCapacityReservationIfPresent(request);
        request.State = LogisticsTransportRequestState.Failed;
        request.FailureReason = reason;
        request.StateChangedAtTick = tick;
        request.AssignedTruck = EntityId.Invalid;
        _failedRequestCount++;
    }

    private void ReleaseReservationIfPresent(RequestState request)
    {
        if (request.ReservedQuantity <= QuantityEpsilon ||
            !request.ReservedSourceInventory.IsSpecified ||
            !_inventories.Contains(request.ReservedSourceInventory))
        {
            request.ReservedQuantity = 0.0;
            request.ReservedSourceInventory = InventoryId.None;
            return;
        }

        double reserved =
            _inventories.GetReservedQuantity(
                request.ReservedSourceInventory,
                request.ResourceId);

        if (reserved + QuantityEpsilon >= request.ReservedQuantity)
        {
            _ = _inventories.ReleaseReservation(
                request.ReservedSourceInventory,
                request.ResourceId,
                request.ReservedQuantity);
        }

        request.ReservedQuantity = 0.0;
        request.ReservedSourceInventory = InventoryId.None;
    }

    private void ReleaseCapacityReservationIfPresent(
        RequestState request)
    {
        if (!request.CapacityReservationId.IsSpecified)
        {
            return;
        }

        _capacityTracker.Release(
            request.CapacityReservationId);
        request.CapacityReservationId =
            LogisticsThroughputReservationId.None;
    }

    private static LogisticsBottleneckReason ResolveBottleneckReason(
        LogisticsTransportRequestFailureReason reason) =>
        reason switch
        {
            LogisticsTransportRequestFailureReason.NoSourceSurplus =>
                LogisticsBottleneckReason.InsufficientSourceStock,
            LogisticsTransportRequestFailureReason.NoTruckAvailable =>
                LogisticsBottleneckReason.InsufficientTruckCapacity,
            LogisticsTransportRequestFailureReason.CapacitySaturated =>
                LogisticsBottleneckReason.SaturatedLinkOrHub,
            LogisticsTransportRequestFailureReason.NoRoute =>
                LogisticsBottleneckReason.DisconnectedRoute,
            LogisticsTransportRequestFailureReason.DestinationFull =>
                LogisticsBottleneckReason.DestinationFull,
            LogisticsTransportRequestFailureReason.DestinationUnavailable =>
                LogisticsBottleneckReason.DestinationUnavailable,
            LogisticsTransportRequestFailureReason.TransportFailed or
            LogisticsTransportRequestFailureReason.RetryLimitReached =>
                LogisticsBottleneckReason.TransportFailure,
            _ => LogisticsBottleneckReason.None
        };

    private double GetTruckCargoQuantity(
        EntityRegistry entities,
        EntityId truckEntity,
        ResourceId resourceId)
    {
        if (!entities.TryGetComponent(
                truckEntity,
                out CargoTransport transport) ||
            !_inventories.Contains(transport.CargoInventory))
        {
            return 0.0;
        }

        return _inventories.GetQuantity(
            transport.CargoInventory,
            resourceId);
    }

    private void UpdateMetricsAndDebugSnapshot(SimulationContext context)
    {
        int pending = 0;
        int assigned = 0;
        int inTransit = 0;
        int retryPending = 0;
        int backlogRequests = 0;
        int capacityBlockedRequests = 0;
        double backlogQuantity = 0.0;
        double reservedCargo = 0.0;

        var readModels =
            new LogisticsTransportRequestReadModel[_requests.Count];

        for (int index = 0; index < _requests.Count; index++)
        {
            RequestState request = _requests[index];

            switch (request.State)
            {
                case LogisticsTransportRequestState.Pending:
                    pending++;
                    backlogRequests++;
                    backlogQuantity += request.RequestedQuantity;
                    break;
                case LogisticsTransportRequestState.Assigned:
                    assigned++;
                    break;
                case LogisticsTransportRequestState.InTransit:
                    inTransit++;
                    break;
                case LogisticsTransportRequestState.RetryPending:
                    retryPending++;
                    backlogRequests++;
                    backlogQuantity += request.RequestedQuantity;
                    if (request.FailureReason ==
                        LogisticsTransportRequestFailureReason.CapacitySaturated)
                    {
                        capacityBlockedRequests++;
                    }
                    break;
            }

            reservedCargo += request.ReservedQuantity;

            Vector3 originPosition =
                _network.TryGetNode(
                    request.Origin,
                    out LogisticsNode originNode)
                    ? originNode.WorldPosition
                    : Vector3.Zero;
            Vector3 destinationPosition =
                _network.TryGetNode(
                    request.Destination,
                    out LogisticsNode destinationNode)
                    ? destinationNode.WorldPosition
                    : Vector3.Zero;

            readModels[index] =
                new LogisticsTransportRequestReadModel(
                    request.Id,
                    request.PolicyEntity,
                    request.State,
                    request.FailureReason,
                    request.Priority,
                    request.ResourceId,
                    request.Origin,
                    request.Destination,
                    originPosition,
                    destinationPosition,
                    request.RequestedQuantity,
                    request.ReservedQuantity,
                    request.AssignedTruck,
                    request.AttemptCount,
                    request.CreatedAtTick,
                    request.StateChangedAtTick,
                    ResolveBottleneckReason(request.FailureReason),
                    request.CapacityReservationId);
        }

        int idleTrucks = 0;
        int activeTrucks = 0;

        foreach (EntityId entity in
                 context.Entities.Query<CargoTransport>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            CargoTransport transport =
                context.Entities.GetComponent<CargoTransport>(entity);
            bool idle =
                context.Entities.TryGetComponent(
                    entity,
                    out CargoTransportRuntimeState state) &&
                state.Lifecycle == CargoTransportLifecycleState.Idle &&
                !context.Entities.HasComponent<CargoTransportOrder>(entity) &&
                _inventories.Contains(transport.CargoInventory) &&
                _inventories.GetTotalQuantity(
                    transport.CargoInventory) <= QuantityEpsilon;

            if (idle)
            {
                idleTrucks++;
            }
            else
            {
                activeTrucks++;
            }
        }

        double averageLatency =
            _completedRequestCount == 0
                ? 0.0
                : (double)_totalDeliveryLatencyTicks /
                  _completedRequestCount;

        Metrics = new AutomatedDistributionMetrics(
            _policies.Count,
            pending,
            assigned,
            inTransit,
            retryPending,
            _unservedDeficitCount,
            idleTrucks,
            activeTrucks,
            reservedCargo,
            _completedRequestCount,
            _failedRequestCount,
            averageLatency,
            _maximumDeliveryLatencyTicks,
            backlogRequests,
            backlogQuantity,
            capacityBlockedRequests);

        LastCapacityDebugSnapshot =
            _capacityTracker.CaptureDebugSnapshot(
                _network,
                context.Tick,
                backlogRequests,
                backlogQuantity);

        LastDebugSnapshot =
            new AutomatedDistributionDebugSnapshot(
                Metrics,
                readModels);
    }

    private void PruneTerminalRequests(SimulationTick tick)
    {
        for (int index = _requests.Count - 1;
             index >= 0;
             index--)
        {
            RequestState request = _requests[index];
            if (!IsTerminal(request.State))
            {
                continue;
            }

            ulong age = tick.Value >= request.StateChangedAtTick.Value
                ? tick.Value - request.StateChangedAtTick.Value
                : 0;

            if (age >= TerminalRetentionTicks)
            {
                _requests.RemoveAt(index);
            }
        }
    }

    private bool TryResolveSourceInventory(
        EntityRegistry entities,
        in LogisticsNode node,
        out InventoryId inventoryId)
    {
        if (!entities.IsAlive(node.Entity))
        {
            inventoryId = InventoryId.None;
            return false;
        }

        if (entities.TryGetComponent(
                node.Entity,
                out ProductionFacility production) &&
            _inventories.Contains(production.OutputInventory))
        {
            inventoryId = production.OutputInventory;
            return true;
        }

        if (entities.TryGetComponent(
                node.Entity,
                out InventoryStorage storage) &&
            _inventories.Contains(storage.InventoryId))
        {
            inventoryId = storage.InventoryId;
            return true;
        }

        if (entities.TryGetComponent(
                node.Entity,
                out LogisticsHub hub) &&
            _inventories.Contains(hub.InventoryId))
        {
            inventoryId = hub.InventoryId;
            return true;
        }

        if (entities.TryGetComponent(
                node.Entity,
                out ResourceExtractor extractor) &&
            extractor.OutputInventory.IsValid &&
            entities.TryGetComponent(
                extractor.OutputInventory,
                out InventoryStorage outputStorage) &&
            _inventories.Contains(outputStorage.InventoryId))
        {
            inventoryId = outputStorage.InventoryId;
            return true;
        }

        inventoryId = InventoryId.None;
        return false;
    }

    private bool TryResolveDestinationInventory(
        EntityRegistry entities,
        in LogisticsNode node,
        out InventoryId inventoryId)
    {
        if (!entities.IsAlive(node.Entity))
        {
            inventoryId = InventoryId.None;
            return false;
        }

        if (entities.TryGetComponent(
                node.Entity,
                out ProductionFacility production) &&
            _inventories.Contains(production.InputInventory))
        {
            inventoryId = production.InputInventory;
            return true;
        }

        if (entities.TryGetComponent(
                node.Entity,
                out InventoryStorage storage) &&
            _inventories.Contains(storage.InventoryId))
        {
            inventoryId = storage.InventoryId;
            return true;
        }

        if (entities.TryGetComponent(
                node.Entity,
                out LogisticsHub hub) &&
            _inventories.Contains(hub.InventoryId))
        {
            inventoryId = hub.InventoryId;
            return true;
        }

        inventoryId = InventoryId.None;
        return false;
    }

    private static bool TryResolveOwner(
        EntityRegistry entities,
        EntityId entity,
        out PlayerId owner)
    {
        if (entities.TryGetComponent(
                entity,
                out CompletedBuilding completed) &&
            completed.Owner.IsSpecified)
        {
            owner = completed.Owner;
            return true;
        }

        if (entities.TryGetComponent(
                entity,
                out StorageDepot storage) &&
            storage.Owner.IsSpecified)
        {
            owner = new PlayerId(storage.Owner.Value);
            return true;
        }

        if (entities.TryGetComponent(
                entity,
                out LogisticsHub hub) &&
            hub.Owner.IsSpecified)
        {
            owner = new PlayerId(hub.Owner.Value);
            return true;
        }

        owner = PlayerId.None;
        return false;
    }

    private LogisticsTransportRequestId AllocateRequestId() =>
        new(checked(++_nextRequestId));

    private static bool IsTerminal(
        LogisticsTransportRequestState state) =>
        state == LogisticsTransportRequestState.Completed ||
        state == LogisticsTransportRequestState.Failed;

    private sealed class RequestState
    {
        public RequestState(
            LogisticsTransportRequestId id,
            EntityId policyEntity,
            LogisticsNodeId destination,
            ResourceId resourceId,
            double requestedQuantity,
            LogisticsStockPriority priority,
            SimulationTick createdAtTick)
        {
            Id = id;
            PolicyEntity = policyEntity;
            Destination = destination;
            ResourceId = resourceId;
            RequestedQuantity = requestedQuantity;
            Priority = priority;
            CreatedAtTick = createdAtTick;
            StateChangedAtTick = createdAtTick;
            NextAttemptTick = createdAtTick;
        }

        public LogisticsTransportRequestId Id { get; }

        public EntityId PolicyEntity { get; }

        public LogisticsNodeId Origin { get; set; }

        public LogisticsNodeId Destination { get; set; }

        public ResourceId ResourceId { get; }

        public double RequestedQuantity { get; set; }

        public LogisticsStockPriority Priority { get; set; }

        public SimulationTick CreatedAtTick { get; }

        public SimulationTick StateChangedAtTick { get; set; }

        public SimulationTick NextAttemptTick { get; set; }

        public LogisticsTransportRequestState State { get; set; }

        public LogisticsTransportRequestFailureReason FailureReason { get; set; }

        public EntityId AssignedTruck { get; set; } = EntityId.Invalid;

        public InventoryId ReservedSourceInventory { get; set; }

        public double ReservedQuantity { get; set; }

        public uint AttemptCount { get; set; }

        public uint TransportFailureCount { get; set; }

        public LogisticsThroughputReservationId CapacityReservationId
        {
            get;
            set;
        }
    }

    private readonly record struct PolicyState(
        EntityId PolicyEntity,
        LogisticsStockPolicy Policy);

    private readonly record struct DispatchSelection(
        LogisticsNode SourceNode,
        InventoryId SourceInventory,
        EntityId TruckEntity,
        double Quantity,
        LogisticsRoute? Route,
        LogisticsTransportRequestFailureReason FailureReason)
    {
        public bool Succeeded =>
            SourceNode.Id.IsSpecified &&
            SourceInventory.IsSpecified &&
            TruckEntity.IsValid &&
            Route is not null &&
            FailureReason == LogisticsTransportRequestFailureReason.None;

        public static DispatchSelection Failed(
            LogisticsTransportRequestFailureReason failureReason) =>
            new(
                default,
                InventoryId.None,
                EntityId.Invalid,
                0.0,
                null,
                failureReason);
    }
}
