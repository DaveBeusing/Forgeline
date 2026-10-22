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
    private readonly List<EntityId> _pendingFuelServiceDemands = new();
    private readonly List<PolicyState> _policies = new();
    private readonly HashSet<EntityId> _fuelDemandTargets = new();
    private ulong _nextRequestId;
    private long _completedRequestCount;
    private long _failedRequestCount;
    private ulong _totalDeliveryLatencyTicks;
    private ulong _maximumDeliveryLatencyTicks;
    private int _unservedDeficitCount;
    private int _committedDispatchCountThisTick;
    private double _committedDispatchQuantityThisTick;

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
        CaptureFuelDemand(context.Entities);
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

            // Pending work does not survive removal, disabling, or retargeting
            // of its authoritative stock policy. In-flight cargo is not canceled.
            if (request.State is LogisticsTransportRequestState.Pending or
                LogisticsTransportRequestState.RetryPending &&
                (!context.Entities.TryGetComponent(request.PolicyEntity, out LogisticsStockPolicy currentPolicy) ||
                 !currentPolicy.Enabled || currentPolicy.ResourceId != request.ResourceId ||
                 !_network.TryGetNodeForEntity(currentPolicy.TargetEntity, out LogisticsNodeId currentNode) ||
                 currentNode != request.Destination))
            {
                RetireUnneededRequest(request, context.Tick);
                continue;
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
                    request.AssignedTruck);
                if (hasReservation)
                {
                    context.Entities.RemoveComponent<CargoTransportReservation>(request.AssignedTruck);
                }

                if (cargoQuantity > QuantityEpsilon)
                {
                    MarkFailed(
                        request,
                        LogisticsTransportRequestFailureReason.TransportFailed,
                        context.Tick);
                }
                else
                {
                    // The failed order otherwise keeps an empty truck occupied
                    // forever, even though its delivery request is retried.
                    if (context.Entities.HasComponent<CargoTransportOrder>(request.AssignedTruck))
                    {
                        context.Entities.RemoveComponent<CargoTransportOrder>(request.AssignedTruck);
                    }

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
                if (hasReservation) context.Entities.RemoveComponent<CargoTransportReservation>(request.AssignedTruck);
                ReleaseCapacityReservationIfPresent(request);
                CompleteOrContinueRequest(
                    context,
                    request);
            }
        }
    }

    void ICargoTransportReservationObserver.OnCargoReservationConsumed(
        LogisticsTransportRequestId requestId, double quantity)
    {
        for (int index = 0; index < _requests.Count; index++)
        {
            RequestState request = _requests[index];
            if (request.Id != requestId)
            {
                continue;
            }

            request.ReservedQuantity = Math.Max(0, request.ReservedQuantity - quantity);
            if (request.ReservedQuantity <= QuantityEpsilon) request.ReservedSourceInventory = InventoryId.None;
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

            RequestState? existing = FindActiveRequest(policyState.PolicyEntity);
            // A new policy class changes only unassigned demand. A committed
            // truck and its physical cargo keep the priority accepted at dispatch.
            if (existing is not null &&
                existing.State is LogisticsTransportRequestState.Pending or
                    LogisticsTransportRequestState.RetryPending)
            {
                existing.Priority = policy.Priority;
            }
            double currentQuantity =
                _inventories.GetQuantity(destinationInventory, policy.ResourceId);

            if (existing is not null && existing.State is
                LogisticsTransportRequestState.Pending or LogisticsTransportRequestState.RetryPending)
            {
                // Pending/retrying quantity is a desired target, not committed
                // inbound stock. Reconcile it against the live policy without
                // resetting RequestId, CreatedAtTick, or WaitingSinceTick.
                double required = Math.Max(0.0, policy.DesiredTarget - currentQuantity);
                if (required <= QuantityEpsilon)
                    RetireUnneededRequest(existing, context.Tick);
                else
                {
                    existing.RequestedQuantity = required;
                    existing.Destination = destinationNodeId;
                    _unservedDeficitCount++;
                }
                continue;
            }

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
        _committedDispatchCountThisTick = 0;
        _committedDispatchQuantityThisTick = 0;

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
                    GetEffectivePriority(left, context.Tick, context.Entities).CompareTo(
                        GetEffectivePriority(right, context.Tick, context.Entities));

                if (priorityComparison != 0)
                {
                    return priorityComparison;
                }

                if (GetEffectivePriority(left, context.Tick, context.Entities) == (int)LogisticsStockPriority.Critical)
                {
                    int recovery = RecoveryDispatchRank(left, context.Entities).CompareTo(RecoveryDispatchRank(right, context.Entities));
                    if (recovery != 0) return recovery;
                }

                int ageComparison =
                    left.WaitingSinceTick.CompareTo(right.WaitingSinceTick);

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
        // One feasibility attempt per eligible pending request per tick.
        // RetryPending requests retain their place without reserving anything.
        request.LastEvaluatedAtTick = context.Tick;
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
        _committedDispatchCountThisTick++;
        _committedDispatchQuantityThisTick += selection.Quantity;
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
        bool hadUnderfueledTruck = false;
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
                    request.ResourceId,
                    request.Priority);
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
                    structuralRoute.Route,
                    request.ResourceId,
                    out EntityId truckEntity,
                    out CargoTransport truck,
                    out bool fuelBlocked))
            {
                hadUnderfueledTruck |= fuelBlocked;
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
            quantity = Math.Min(
                quantity,
                GetRouteWindowCapacity(
                    structuralRoute.Route));

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

            bool physicalService = entities.HasComponent<BuildingLoadingDock>(destination.Entity);
            bool comparableBatch = !physicalService || !best.Succeeded || quantity == best.Quantity;
            if (!best.Succeeded || physicalService && quantity > best.Quantity ||
                comparableBatch && capacityRoute.Route.TotalCost < best.Route!.TotalCost ||
                (comparableBatch && capacityRoute.Route.TotalCost == best.Route!.TotalCost &&
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
                        ? hadUnderfueledTruck
                            ? LogisticsTransportRequestFailureReason.NoDriveFuel
                            : LogisticsTransportRequestFailureReason.NoTruckAvailable
                        : hadCapacityBlockedRoute
                            ? LogisticsTransportRequestFailureReason.CapacitySaturated
                            : LogisticsTransportRequestFailureReason.AssignmentFailed;

        return DispatchSelection.Failed(failureReason);
    }

    private double GetRouteWindowCapacity(
        LogisticsRoute route)
    {
        double capacity =
            double.PositiveInfinity;

        if (!_network.TryGetNode(
                route.Source,
                out LogisticsNode source))
        {
            return 0.0;
        }

        capacity = Math.Min(
            capacity,
            source.ThroughputCapacityPerSecond *
            _capacityTracker.WindowSeconds);

        for (int index = 0;
             index < route.Segments.Count;
             index++)
        {
            LogisticsRouteSegment segment =
                route.Segments[index];

            capacity = Math.Min(
                capacity,
                segment.CapacityPerSecond *
                _capacityTracker.WindowSeconds);

            if (!_network.TryGetNode(
                    segment.To,
                    out LogisticsNode node))
            {
                return 0.0;
            }

            capacity = Math.Min(
                capacity,
                node.ThroughputCapacityPerSecond *
                _capacityTracker.WindowSeconds);
        }

        return double.IsFinite(capacity) &&
               capacity > QuantityEpsilon
            ? capacity
            : 0.0;
    }

    private bool TrySelectAvailableTruck(
        EntityRegistry entities,
        in LogisticsNode destination,
        in LogisticsNode source,
        LogisticsRoute route,
        ResourceId resource,
        out EntityId selectedEntity,
        out CargoTransport selectedTransport,
        out bool fuelBlocked)
    {
        selectedEntity = EntityId.Invalid;
        selectedTransport = default;
        fuelBlocked = false;
        _pendingFuelServiceDemands.Clear();

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
                entities.HasComponent<CargoFuelServiceState>(entity) ||
                entities.HasComponent<CargoTransportReservation>(entity) ||
                entities.HasComponent<ResupplyOrder>(entity) ||
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
            Vector3 truckPosition = source.WorldPosition;
            if (entities.TryGetComponent(
                    entity,
                    out WorldTransform transform))
            {
                truckPosition = transform.Position;
                distanceSquared =
                    Vector3.DistanceSquared(
                        truckPosition,
                        source.WorldPosition);
            }

            if (!HasSufficientFuelForTransport(
                    entities,
                    entity,
                    truckPosition,
                    source,
                    route,
                    resource == ResourceIds.Volatiles &&
                    entities.TryGetComponent(destination.Entity, out ProductionFacility facility) &&
                    facility.Supports(ProductionCapability.FuelProcessing)))
            {
                // Preserve the physical shipment; request real service instead of treating
                // an underfueled, otherwise free truck as permanently undispatchable.
                if (entities.HasComponent<UnitFuelState>(entity))
                {
                    fuelBlocked = true;
                    _pendingFuelServiceDemands.Add(entity);
                }
                continue;
            }

            if (!selectedEntity.IsValid ||
                distanceSquared < bestDistanceSquared)
            {
                selectedEntity = entity;
                selectedTransport = transport;
                bestDistanceSquared = distanceSquared;
            }
        }

        // Structural ECS writes must occur after the stable entity query ends.
        foreach (EntityId underfueled in _pendingFuelServiceDemands)
            if (!entities.HasComponent<CargoFuelServiceDemand>(underfueled))
                entities.AddComponent(underfueled, new CargoFuelServiceDemand(SimulationTick.Zero));
        return selectedEntity.IsValid;
    }

    private bool HasSufficientFuelForTransport(
        EntityRegistry entities,
        EntityId entity,
        Vector3 truckPosition,
        in LogisticsNode source,
        LogisticsRoute route,
        bool replenishesFuelProduction)
    {
        if (!entities.TryGetComponent(
                entity,
                out UnitFuelState fuel) ||
            fuel.ConsumptionPerMeter <= QuantityEpsilon)
        {
            return true;
        }

        if (!_inventories.Contains(fuel.InventoryId))
        {
            return false;
        }

        double deadheadDistance =
            Vector3.Distance(
                truckPosition,
                source.WorldPosition);
        double requiredFuel =
            checked(
                (deadheadDistance +
                 route.TotalDistanceMeters) *
                fuel.ConsumptionPerMeter);
        // Refineries refill operational tanks once their input arrives.
        // Keep a small margin, but do not strand the fuel supply chain
        // behind the normal reserve while every provider is running dry.
        double reserveFuel =
            CargoDeliveryFuelPolicy.ResolveReserveFuel(
                fuel,
                replenishesFuelProduction);
        double availableFuel =
            _inventories.GetQuantity(
                fuel.InventoryId,
                ResourceIds.Fuel);

        return availableFuel + QuantityEpsilon >=
            requiredFuel + reserveFuel;
    }

    private double GetRetainedSourceTarget(
        EntityId sourceEntity,
        ResourceId resourceId,
        LogisticsStockPriority requestPriority)
    {
        double retainedTarget = 0.0;

        for (int index = 0; index < _policies.Count; index++)
        {
            LogisticsStockPolicy policy = _policies[index].Policy;
            if (policy.Enabled &&
                policy.TargetEntity == sourceEntity &&
                policy.ResourceId == resourceId &&
                policy.Priority <= requestPriority)
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
        SimulationTick tick,
        EntityRegistry entities)
    {
        int basePriority = (int)request.Priority;
        if (request.Priority == LogisticsStockPriority.Critical &&
            entities.TryGetComponent(request.PolicyEntity, out LogisticsStockPolicy policy) &&
            _network.TryGetNode(request.Destination, out LogisticsNode destination) &&
            entities.HasComponent<BuildingLoadingDock>(destination.Entity) &&
            TryResolveDestinationInventory(entities, destination, out InventoryId inventory) &&
            _inventories.GetQuantity(inventory, policy.ResourceId) + QuantityEpsilon >= policy.DesiredMinimum)
            basePriority = (int)LogisticsStockPriority.High;
        ulong age = tick.Value >= request.WaitingSinceTick.Value
            ? tick.Value - request.WaitingSinceTick.Value
            : 0;
        ulong agingSteps = age / _fairnessAgingTicks;

        // Critical is reserved for recovery demand that must not be displaced
        // by ordinary backlog aging. Fairness can promote noncritical work as
        // far as High while preserving the explicit Critical recovery class.
        int priorityFloor = (int)LogisticsStockPriority.High;
        int maximumAging = Math.Max(
            0,
            basePriority - priorityFloor);
        int boundedAging = (int)Math.Min(
            (ulong)maximumAging,
            agingSteps);

        return basePriority - boundedAging;
    }

    private void CaptureFuelDemand(EntityRegistry entities)
    {
        _fuelDemandTargets.Clear();
        foreach (EntityId recipient in entities.Query<ResupplyOrder>())
        {
            var order = entities.GetComponent<ResupplyOrder>(recipient);
            if (order.RequestedResources.HasFlag(BattlefieldSupplyResource.Fuel)) _fuelDemandTargets.Add(order.Provider);
        }
        foreach (EntityId truck in entities.Query<SupplyLoadingOrder, SupplyTruck>())
        {
            var loading = entities.GetComponent<SupplyLoadingOrder>(truck);
            var supply = entities.GetComponent<SupplyTruck>(truck);
            if (_inventories.Contains(supply.InventoryId) &&
                _inventories.GetQuantity(supply.InventoryId, ResourceIds.Fuel) + QuantityEpsilon < supply.FuelTarget)
                _fuelDemandTargets.Add(loading.Source);
        }
    }
    private int RecoveryDispatchRank(RequestState request, EntityRegistry entities)
    {
        if (!_network.TryGetNode(request.Destination, out LogisticsNode node) ||
            !entities.HasComponent<BuildingLoadingDock>(node.Entity)) return 2;
        if (entities.HasComponent<CommandFacility>(node.Entity)) return 0;
        if (entities.HasComponent<SupplyDepot>(node.Entity) && request.ResourceId == ResourceIds.Fuel)
        {
            if (_fuelDemandTargets.Contains(node.Entity)) return 0;
            return 1;
        }
        if (entities.TryGetComponent(node.Entity, out UnitProductionFacility facility) &&
            facility.Status is UnitProductionStatus.NoInput or UnitProductionStatus.Running &&
            entities.TryGetComponent(node.Entity, out PowerConsumer power) && power.State == PowerOperationalState.Powered)
            return 0;
        return 2;
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

        // A truck became available. Reconsider waiting deficits before handing
        // it straight back to this bulk request between their retry intervals.
        foreach (RequestState waiting in _requests)
        {
            if (waiting.State == LogisticsTransportRequestState.RetryPending &&
                waiting.FailureReason == LogisticsTransportRequestFailureReason.NoTruckAvailable)
            {
                waiting.State = LogisticsTransportRequestState.Pending;
                waiting.FailureReason = LogisticsTransportRequestFailureReason.None;
                waiting.StateChangedAtTick = context.Tick;
                waiting.NextAttemptTick = context.Tick;
            }
        }

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
            _inventories.GetQuantity(destinationInventory, policy.ResourceId);
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
        // A delivered load has received its fair turn. Remaining bulk demand
        // must age again instead of monopolizing trucks ahead of new deficits.
        request.WaitingSinceTick = context.Tick;
        request.State = LogisticsTransportRequestState.Pending;
        request.FailureReason =
            LogisticsTransportRequestFailureReason.None;
        request.StateChangedAtTick = context.Tick;
        request.NextAttemptTick = context.Tick;
    }

    private void RetireUnneededRequest(RequestState request, SimulationTick tick)
    {
        ReleaseReservationIfPresent(request);
        ReleaseCapacityReservationIfPresent(request);
        request.State = LogisticsTransportRequestState.Completed;
        request.FailureReason = LogisticsTransportRequestFailureReason.None;
        request.AssignedTruck = EntityId.Invalid;
        request.Origin = LogisticsNodeId.None;
        request.StateChangedAtTick = tick;
        request.NextAttemptTick = tick;
        // Stock recovered without a shipment. Do not count a delivery or
        // consume a new truck/route/source reservation.
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
            LogisticsTransportRequestFailureReason.NoDriveFuel =>
                LogisticsBottleneckReason.DriveFuelUnavailable,
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
        EntityId truckEntity)
    {
        if (!entities.TryGetComponent(
                truckEntity,
                out CargoTransport transport) ||
            !_inventories.Contains(transport.CargoInventory))
        {
            return 0.0;
        }

        return _inventories.GetTotalQuantity(transport.CargoInventory);
    }

    private void UpdateMetricsAndDebugSnapshot(SimulationContext context)
    {
        int pending = 0;
        int assigned = 0;
        int inTransit = 0;
        int retryPending = 0;
        int backlogRequests = 0;
        int capacityBlockedRequests = 0;
        int sourceBlockedRequests = 0;
        int routeBlockedRequests = 0;
        int truckBlockedRequests = 0;
        int fuelBlockedRequests = 0;
        int dockBlockedTransports = 0;
        ulong oldestBacklogWait = 0;
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

            if (request.State is LogisticsTransportRequestState.Pending or
                LogisticsTransportRequestState.RetryPending)
            {
                ulong waitAge = context.Tick.Value >= request.WaitingSinceTick.Value
                    ? context.Tick.Value - request.WaitingSinceTick.Value : 0;
                oldestBacklogWait = Math.Max(oldestBacklogWait, waitAge);
                switch (request.FailureReason)
                {
                    case LogisticsTransportRequestFailureReason.NoSourceSurplus:
                        sourceBlockedRequests++;
                        break;
                    case LogisticsTransportRequestFailureReason.NoRoute:
                        routeBlockedRequests++;
                        break;
                    case LogisticsTransportRequestFailureReason.NoTruckAvailable:
                        truckBlockedRequests++;
                        break;
                    case LogisticsTransportRequestFailureReason.NoDriveFuel:
                        fuelBlockedRequests++;
                        break;
                }
            }

            VehicleDockBlockReason dockReason = VehicleDockBlockReason.None;
            if (request.State is LogisticsTransportRequestState.Assigned or LogisticsTransportRequestState.InTransit &&
                request.AssignedTruck.IsValid &&
                context.Entities.TryGetComponent(request.AssignedTruck, out VehicleDockVisit dockVisit) &&
                (dockVisit.Status is VehicleDockStatus.Busy or VehicleDockStatus.Blocked ||
                 dockVisit.Status == VehicleDockStatus.Exiting &&
                 dockVisit.Reason == VehicleDockBlockReason.ExitBlocked))
            {
                dockReason = dockVisit.Reason;
                dockBlockedTransports++;
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
                    request.CapacityReservationId,
                    request.WaitingSinceTick,
                    request.NextAttemptTick,
                    request.LastEvaluatedAtTick,
                    dockReason)
                {
                    EffectivePriority =
                        request.State is LogisticsTransportRequestState.Pending or
                            LogisticsTransportRequestState.RetryPending
                            ? (LogisticsStockPriority)GetEffectivePriority(
                                request, context.Tick, context.Entities)
                            : request.Priority
                };
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
            capacityBlockedRequests,
            oldestBacklogWait,
            sourceBlockedRequests,
            routeBlockedRequests,
            truckBlockedRequests,
            fuelBlockedRequests,
            dockBlockedTransports,
            _committedDispatchCountThisTick,
            _committedDispatchQuantityThisTick);

        LastCapacityDebugSnapshot =
            _capacityTracker.CaptureDebugSnapshot(
                _network,
                context.Tick,
                backlogRequests,
                backlogQuantity);

        LastDebugSnapshot =
            new AutomatedDistributionDebugSnapshot(
                Metrics,
                readModels) { Tick = context.Tick };
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
                out UnitProductionFacility unitProduction) &&
            _inventories.Contains(unitProduction.InputInventory))
        {
            inventoryId = unitProduction.InputInventory;
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
            WaitingSinceTick = createdAtTick;
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

        public SimulationTick WaitingSinceTick { get; set; }

        public SimulationTick StateChangedAtTick { get; set; }

        public SimulationTick NextAttemptTick { get; set; }

        public SimulationTick LastEvaluatedAtTick { get; set; }

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
