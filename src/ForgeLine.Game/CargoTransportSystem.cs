using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public sealed class CargoTransportSystem : ISimulationSystem
{
    private const double QuantityEpsilon = 0.000000001;
    private const float LogicalTransitNodeApproachRadiusMeters = 72.0f;

    private readonly VehicleDockingService _docks;
    private readonly LogisticsNetwork _network;
    private readonly InventoryStore _inventories;
    private readonly HierarchicalNavigationSystem? _navigation;
    private readonly List<EntityId> _transportEntities = new();
    private readonly Dictionary<EntityId, InventoryId> _trackedInventories = new();
    private readonly List<EntityId> _staleTrackedEntities = new();
    private ICargoTransportReservationObserver? _reservationObserver;
    private ulong? _executedTick;
    private long _completedOrderCount;
    private long _rerouteCount;
    private long _routeFailureCount;
    private double _deliveredQuantity;
    private double _lostQuantity;

    public CargoTransportSystem(
        LogisticsNetwork network,
        InventoryStore inventories,
        HierarchicalNavigationSystem? navigation = null, VehicleDockingService? docking = null)
    {
        _network = network ??
            throw new ArgumentNullException(nameof(network));
        _inventories = inventories ??
            throw new ArgumentNullException(nameof(inventories));
        _navigation = navigation;
        _docks = docking ?? new VehicleDockingService();
    }

    public SimulationPhase Phase =>
        SimulationPhase.Logistics;

    public CargoTransportMetrics Metrics { get; private set; }

    public CargoTransportDebugSnapshot LastDebugSnapshot
    {
        get;
        private set;
    } = CargoTransportDebugSnapshot.Empty;

    internal void SetReservationObserver(
        ICargoTransportReservationObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);

        if (_reservationObserver is not null &&
            !ReferenceEquals(_reservationObserver, observer))
        {
            throw new InvalidOperationException(
                "Cargo transport reservation observer is already configured.");
        }

        _reservationObserver = observer;
    }

    internal void TrackTransport(
        EntityId entity,
        InventoryId inventoryId)
    {
        if (!entity.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(entity));
        }

        if (!inventoryId.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(inventoryId));
        }

        _trackedInventories[entity] = inventoryId;
    }

    public bool TryAssignOrder(
        EntityRegistry entities,
        EntityId transportEntity,
        in CargoTransportOrder order,
        SimulationTick acceptedAtTick)
    {
        ArgumentNullException.ThrowIfNull(entities);

        if (!entities.IsAlive(transportEntity) ||
            !entities.TryGetComponent(
                transportEntity,
                out CargoTransport transport) ||
            !_inventories.Contains(transport.CargoInventory) ||
            entities.HasComponent<SupplyLoadingOrder>(transportEntity) ||
            entities.HasComponent<ResupplyOrder>(transportEntity) ||
            entities.HasComponent<SupplyRescueAssignment>(transportEntity) ||
            entities.HasComponent<VehicleDockVisit>(transportEntity) ||
            entities.HasComponent<CargoTransportOrder>(
                transportEntity) ||
            _inventories.GetTotalQuantity(
                transport.CargoInventory) > QuantityEpsilon)
        {
            return false;
        }

        ClearNavigation(entities, transportEntity);
        RemoveRouteState(entities, transportEntity);

        entities.AddComponent(
            transportEntity,
            order);

        var state = new CargoTransportRuntimeState(
            CargoTransportLifecycleState.ToOrigin,
            CargoTransportWaitReason.None,
            CargoTransportFailureReason.None,
            LogisticsNodeId.None,
            _network.Version,
            LoadedQuantity: 0.0,
            DeliveredQuantity: 0.0,
            StateChangedAtTick: acceptedAtTick);

        if (entities.HasComponent<
                CargoTransportRuntimeState>(transportEntity))
        {
            entities.SetComponent(
                transportEntity,
                state);
        }
        else
        {
            entities.AddComponent(
                transportEntity,
                state);
        }

        TrackTransport(
            transportEntity,
            transport.CargoInventory);
        return true;
    }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_executedTick == context.Tick.Value) return;
        _executedTick = context.Tick.Value;
        _docks.Cleanup(context.Entities);
        CleanupDestroyedTransports(context.Entities);

        _transportEntities.Clear();
        foreach (EntityId entity in
                 context.Entities.Query<CargoTransport>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            _transportEntities.Add(entity);
        }

        for (int index = 0;
             index < _transportEntities.Count;
             index++)
        {
            EntityId entity = _transportEntities[index];

            if (!context.Entities.TryGetComponent(
                    entity,
                    out CargoTransport transport))
            {
                continue;
            }

            TrackTransport(
                entity,
                transport.CargoInventory);

            ProcessTransport(
                context,
                entity,
                transport);
        }

        UpdateMetricsAndDebugSnapshot(context.Entities);
    }

    private void ProcessTransport(
        SimulationContext context,
        EntityId entity,
        in CargoTransport transport)
    {
        if (!_inventories.Contains(
                transport.CargoInventory))
        {
            Fail(
                context.Entities,
                entity,
                CargoTransportRuntimeState.Idle,
                CargoTransportFailureReason.CargoInventoryUnavailable,
                context.Tick);
            return;
        }

        if (context.Entities.HasComponent<SupplyLoadingOrder>(entity)) return;

        CargoTransportRuntimeState state =
            context.Entities.TryGetComponent(
                entity,
                out CargoTransportRuntimeState existingState)
                ? existingState
                : CargoTransportRuntimeState.Idle;

        if (!context.Entities.TryGetComponent(
                entity,
                out CargoTransportOrder order))
        {
            if (state.Lifecycle !=
                CargoTransportLifecycleState.Idle)
            {
                ClearNavigation(
                    context.Entities,
                    entity);
                RemoveRouteState(
                    context.Entities,
                    entity);

                state = CargoTransportRuntimeState.Idle with
                {
                    ObservedNetworkVersion = _network.Version,
                    StateChangedAtTick = context.Tick
                };
                SetState(
                    context.Entities,
                    entity,
                    state);
            }

            return;
        }

        // Battlefield resupply temporarily owns locomotion. Keep the cargo
        // order and runtime state intact so the same delivery resumes once
        // the resupply order is completed.
        if (context.Entities.HasComponent<ResupplyOrder>(entity) || context.Entities.HasComponent<SupplyRescueAssignment>(entity))
        {
            return;
        }
        if (context.Entities.TryGetComponent(entity, out VehicleDockVisit dockExit) &&
            (dockExit.Status == VehicleDockStatus.Exiting || state.Lifecycle == CargoTransportLifecycleState.Waiting &&
             state.WaitReason is CargoTransportWaitReason.OriginResourceUnavailable or CargoTransportWaitReason.DestinationCapacity))
        {
            if (!_docks.Exit(context, entity)) { PrepareDockFuelBudget(context, entity); return; }
        }

        switch (state.Lifecycle)
        {
            case CargoTransportLifecycleState.Idle:
                SetState(
                    context.Entities,
                    entity,
                    state with
                    {
                        Lifecycle =
                            CargoTransportLifecycleState.ToOrigin,
                        WaitReason =
                            CargoTransportWaitReason.None,
                        FailureReason =
                            CargoTransportFailureReason.None,
                        AnchorNode =
                            LogisticsNodeId.None,
                        ObservedNetworkVersion =
                            _network.Version,
                        LoadedQuantity = 0.0,
                        DeliveredQuantity = 0.0,
                        StateChangedAtTick =
                            context.Tick
                    });
                break;

            case CargoTransportLifecycleState.ToOrigin:
                ProcessToOrigin(
                    context,
                    entity,
                    transport,
                    order,
                    state);
                break;

            case CargoTransportLifecycleState.Loading:
                ProcessLoading(
                    context,
                    entity,
                    transport,
                    order,
                    state);
                break;

            case CargoTransportLifecycleState.ToDestination:
                ProcessToDestination(
                    context,
                    entity,
                    transport,
                    order,
                    state);
                break;

            case CargoTransportLifecycleState.Unloading:
                ProcessUnloading(
                    context,
                    entity,
                    transport,
                    order,
                    state);
                break;

            case CargoTransportLifecycleState.Waiting:
                ProcessWaiting(
                    context,
                    entity,
                    transport,
                    order,
                    state);
                break;

            case CargoTransportLifecycleState.Failed:
                break;

            default:
                Fail(
                    context.Entities,
                    entity,
                    state,
                    CargoTransportFailureReason.InvalidTransport,
                    context.Tick);
                break;
        }
    }

    private void ProcessToOrigin(
        SimulationContext context,
        EntityId entity,
        in CargoTransport transport,
        in CargoTransportOrder order,
        in CargoTransportRuntimeState state)
    {
        if (!_network.TryGetNode(
                order.Origin,
                out LogisticsNode origin))
        {
            Fail(
                context.Entities,
                entity,
                state,
                CargoTransportFailureReason.InvalidOrigin,
                context.Tick);
            return;
        }

        if (!origin.Capabilities.HasFlag(
                LogisticsNodeCapabilities.CargoSource))
        {
            Fail(
                context.Entities,
                entity,
                state,
                CargoTransportFailureReason.InvalidOrigin,
                context.Tick);
            return;
        }

        if (!origin.Enabled)
        {
            Wait(
                context.Entities,
                entity,
                state,
                CargoTransportWaitReason.OriginUnavailable,
                context.Tick);
            return;
        }

        if (HasNavigationFailure(
                context.Entities,
                entity))
        {
            Fail(
                context.Entities,
                entity,
                state,
                CargoTransportFailureReason.NavigationFailed,
                context.Tick);
            return;
        }

        if (IsSettledAtNode(
                context.Entities,
                entity,
                origin))
        {
            ClearNavigation(
                context.Entities,
                entity);

            SetState(
                context.Entities,
                entity,
                state with
                {
                    Lifecycle =
                        CargoTransportLifecycleState.Loading,
                    WaitReason =
                        CargoTransportWaitReason.None,
                    FailureReason =
                        CargoTransportFailureReason.None,
                    AnchorNode =
                        order.Origin,
                    ObservedNetworkVersion =
                        _network.Version,
                    StateChangedAtTick =
                        context.Tick
                });
            return;
        }

        EnsureMovementToNode(
            context,
            entity,
            transport,
            origin);
    }

    private void ProcessLoading(
        SimulationContext context,
        EntityId entity,
        in CargoTransport transport,
        in CargoTransportOrder order,
        in CargoTransportRuntimeState state)
    {
        if (!_network.TryGetNode(
                order.Origin,
                out LogisticsNode origin))
        {
            Fail(
                context.Entities,
                entity,
                state,
                CargoTransportFailureReason.InvalidOrigin,
                context.Tick);
            return;
        }

        if (!origin.Enabled)
        {
            Wait(
                context.Entities,
                entity,
                state,
                CargoTransportWaitReason.OriginUnavailable,
                context.Tick);
            return;
        }

        if (!TryResolveSourceInventory(
                context.Entities,
                origin,
                out InventoryId sourceInventory))
        {
            Fail(
                context.Entities,
                entity,
                state,
                CargoTransportFailureReason.SourceInventoryUnavailable,
                context.Tick);
            return;
        }

        if (order.PartialLoadPolicy ==
                CargoPartialLoadPolicy.RequireRequestedQuantity &&
            order.RequestedQuantity >
                transport.Capacity + QuantityEpsilon)
        {
            Fail(
                context.Entities,
                entity,
                state,
                CargoTransportFailureReason.CargoCapacityInsufficient,
                context.Tick);
            return;
        }

        double remainingRequested = Math.Max(0, order.RequestedQuantity - state.LoadedQuantity);
        bool hasAutomatedReservation =
            context.Entities.TryGetComponent(
                entity,
                out CargoTransportReservation automatedReservation);

        if (hasAutomatedReservation &&
            (automatedReservation.SourceInventory != sourceInventory ||
             automatedReservation.ResourceId != order.ResourceId))
        {
            Fail(
                context.Entities,
                entity,
                state,
                CargoTransportFailureReason.TransferFailed,
                context.Tick);
            return;
        }

        double available =
            hasAutomatedReservation
                ? automatedReservation.Quantity
                : _inventories.GetAvailableQuantity(
                    sourceInventory,
                    order.ResourceId);
        double addable =
            _inventories.GetAddableQuantity(
                transport.CargoInventory,
                order.ResourceId,
                remainingRequested);

        if (order.PartialLoadPolicy ==
            CargoPartialLoadPolicy.RequireRequestedQuantity)
        {
            if (available + QuantityEpsilon <
                remainingRequested)
            {
                Wait(
                    context.Entities,
                    entity,
                    state,
                    CargoTransportWaitReason
                        .OriginResourceUnavailable,
                    context.Tick);
                return;
            }

            if (addable + QuantityEpsilon <
                remainingRequested)
            {
                Fail(
                    context.Entities,
                    entity,
                    state,
                    CargoTransportFailureReason
                        .CargoCapacityInsufficient,
                    context.Tick);
                return;
            }
        }

        double loadQuantity = Math.Min(
            remainingRequested,
            Math.Min(
                available,
                addable));

        if (loadQuantity <= QuantityEpsilon)
        {
            if (addable <= QuantityEpsilon &&
                _inventories.GetTotalQuantity(
                    transport.CargoInventory) >
                    QuantityEpsilon)
            {
                Fail(
                    context.Entities,
                    entity,
                    state,
                    CargoTransportFailureReason
                        .CargoCapacityInsufficient,
                    context.Tick);
            }
            else
            {
                Wait(
                    context.Entities,
                    entity,
                    state,
                    CargoTransportWaitReason
                        .OriginResourceUnavailable,
                    context.Tick);
            }

            return;
        }

        bool dockRequired = context.Entities.HasComponent<BuildingLoadingDock>(origin.Entity);
        if (dockRequired)
        {
            loadQuantity = Math.Min(20, loadQuantity);
            if (!_docks.Approach(context, entity, origin.Entity, transport.Owner) ||
                !context.Entities.TryGetComponent(entity, out VehicleDockVisit loadingVisit) ||
                !_docks.CanTransfer(context, entity, loadingVisit.Generation, loadQuantity)) return;
        }
        if (hasAutomatedReservation)
        {
            InventoryOperationResult release =
                _inventories.ReleaseReservation(
                    sourceInventory,
                    order.ResourceId,
                    loadQuantity);

            if (!release.Succeeded)
            {
                Fail(
                    context.Entities,
                    entity,
                    state,
                    CargoTransportFailureReason.TransferFailed,
                    context.Tick);
                return;
            }
        }

        InventoryOperationResult transfer =
            _inventories.Transfer(
                sourceInventory,
                transport.CargoInventory,
                order.ResourceId,
                loadQuantity);

        if (!transfer.Succeeded)
        {
            if (hasAutomatedReservation)
            {
                InventoryOperationResult restore =
                    _inventories.Reserve(
                        sourceInventory,
                        order.ResourceId,
                        loadQuantity);

                EngineInvariant.Require(
                    restore.Succeeded,
                    DiagnosticCategory.Simulation,
                    "CARGO_RESERVATION_RESTORE_FAILED",
                    $"Unable to restore cargo reservation for source inventory {sourceInventory}.");
            }

            Fail(
                context.Entities,
                entity,
                state,
                CargoTransportFailureReason.TransferFailed,
                context.Tick);
            return;
        }

        if (hasAutomatedReservation)
        {
            _reservationObserver?.OnCargoReservationConsumed(
                automatedReservation.RequestId, loadQuantity);
            double reservedRest = automatedReservation.Quantity - loadQuantity;
            if (reservedRest > QuantityEpsilon)
                context.Entities.SetComponent(entity, new CargoTransportReservation(automatedReservation.RequestId,
                    automatedReservation.SourceInventory, automatedReservation.ResourceId, reservedRest));
            else context.Entities.RemoveComponent<CargoTransportReservation>(entity);
        }

        CargoTransportRuntimeState loadedState =
            state with
            {
                AnchorNode = order.Origin,
                LoadedQuantity =
                    state.LoadedQuantity + loadQuantity,
                ObservedNetworkVersion =
                    _network.Version,
                StateChangedAtTick =
                    context.Tick
            };

        if (context.Entities.TryGetComponent(entity, out VehicleDockVisit loadedVisit))
            VehicleDockingService.RecordTransfer(context, entity, loadedVisit.Generation);
        if (dockRequired && loadedState.LoadedQuantity + QuantityEpsilon < order.RequestedQuantity &&
            _inventories.GetAddableQuantity(transport.CargoInventory, order.ResourceId, 1) > QuantityEpsilon &&
            (hasAutomatedReservation ? automatedReservation.Quantity - loadQuantity : _inventories.GetAvailableQuantity(sourceInventory, order.ResourceId)) > QuantityEpsilon)
        {
            SetState(context.Entities, entity, loadedState);
            return;
        }

        if (!TryPlanRoute(
                context.Entities,
                entity,
                order,
                loadedState,
                order.Origin,
                context.Tick))
        {
            Wait(
                context.Entities,
                entity,
                loadedState,
                CargoTransportWaitReason.RouteUnavailable,
                context.Tick);
        }
        if (context.Entities.HasComponent<VehicleDockVisit>(entity)) { _docks.Exit(context, entity); PrepareDockFuelBudget(context, entity); }
    }

    private void ProcessToDestination(
        SimulationContext context,
        EntityId entity,
        in CargoTransport transport,
        in CargoTransportOrder order,
        in CargoTransportRuntimeState state)
    {
        if (!context.Entities.TryGetComponent(
                entity,
                out CargoTransportRouteState routeState) ||
            routeState.Route is null)
        {
            if (!TryPlanRoute(
                    context.Entities,
                    entity,
                    order,
                    state,
                    state.AnchorNode,
                    context.Tick))
            {
                Wait(
                    context.Entities,
                    entity,
                    state,
                    CargoTransportWaitReason.RouteUnavailable,
                    context.Tick);
            }

            return;
        }

        LogisticsRoute route = routeState.Route;

        if (route.Version != _network.Version)
        {
            BeginReroute(
                context.Entities,
                entity,
                state,
                context.Tick);
            return;
        }

        if (routeState.NextSegmentIndex >=
            route.Segments.Count)
        {
            SetState(
                context.Entities,
                entity,
                state with
                {
                    Lifecycle =
                        CargoTransportLifecycleState.Unloading,
                    WaitReason =
                        CargoTransportWaitReason.None,
                    StateChangedAtTick =
                        context.Tick
                });
            return;
        }

        LogisticsRouteSegment segment =
            route.Segments[
                routeState.NextSegmentIndex];

        if (segment.From != state.AnchorNode)
        {
            Fail(
                context.Entities,
                entity,
                state,
                CargoTransportFailureReason.InvalidRouteAnchor,
                context.Tick);
            return;
        }

        if (!_network.TryGetNode(
                segment.To,
                out LogisticsNode targetNode))
        {
            BeginReroute(
                context.Entities,
                entity,
                state,
                context.Tick);
            return;
        }

        if (HasNavigationFailure(
                context.Entities,
                entity))
        {
            Fail(
                context.Entities,
                entity,
                state,
                CargoTransportFailureReason.NavigationFailed,
                context.Tick);
            return;
        }

        bool nextSegmentIsDestination = segment.To == order.Destination;
        if (!IsSettledAtNode(
                context.Entities,
                entity,
                targetNode, requireDock: nextSegmentIsDestination))
        {
            EnsureMovementToNode(
                context,
                entity,
                transport,
                targetNode, requireDock: nextSegmentIsDestination);
            return;
        }

        ClearNavigation(
            context.Entities,
            entity);

        int nextIndex =
            checked(routeState.NextSegmentIndex + 1);
        context.Entities.SetComponent(
            entity,
            routeState with
            {
                NextSegmentIndex = nextIndex
            });

        CargoTransportRuntimeState advancedState =
            state with
            {
                AnchorNode = segment.To,
                ObservedNetworkVersion =
                    route.Version,
                StateChangedAtTick =
                    context.Tick
            };

        if (nextIndex >= route.Segments.Count)
        {
            SetState(
                context.Entities,
                entity,
                advancedState with
                {
                    Lifecycle =
                        CargoTransportLifecycleState.Unloading,
                    WaitReason =
                        CargoTransportWaitReason.None
                });
        }
        else
        {
            SetState(
                context.Entities,
                entity,
                advancedState);
            if (context.Entities.HasComponent<VehicleDockVisit>(entity))
            {
                _docks.Exit(context, entity);
                PrepareDockFuelBudget(context, entity);
            }
        }
    }

    private void ProcessUnloading(
        SimulationContext context,
        EntityId entity,
        in CargoTransport transport,
        in CargoTransportOrder order,
        in CargoTransportRuntimeState state)
    {
        if (!_network.TryGetNode(
                order.Destination,
                out LogisticsNode destination))
        {
            Fail(
                context.Entities,
                entity,
                state,
                CargoTransportFailureReason.InvalidDestination,
                context.Tick);
            return;
        }

        if (!destination.Capabilities.HasFlag(
                LogisticsNodeCapabilities.CargoDestination))
        {
            Fail(
                context.Entities,
                entity,
                state,
                CargoTransportFailureReason.InvalidDestination,
                context.Tick);
            return;
        }

        if (!destination.Enabled)
        {
            Wait(
                context.Entities,
                entity,
                state,
                CargoTransportWaitReason.DestinationUnavailable,
                context.Tick);
            return;
        }

        if (!TryResolveDestinationInventory(
                context.Entities,
                destination,
                out InventoryId destinationInventory))
        {
            Fail(
                context.Entities,
                entity,
                state,
                CargoTransportFailureReason
                    .DestinationInventoryUnavailable,
                context.Tick);
            return;
        }

        double cargoQuantity =
            _inventories.GetAvailableQuantity(
                transport.CargoInventory,
                order.ResourceId);

        if (cargoQuantity <= QuantityEpsilon)
        {
            CompleteOrder(
                context.Entities,
                entity,
                state,
                context.Tick);
            return;
        }

        double unloadQuantity =
            _inventories.GetAddableQuantity(
                destinationInventory,
                order.ResourceId,
                cargoQuantity);

        if (unloadQuantity <= QuantityEpsilon)
        {
            Wait(
                context.Entities,
                entity,
                state,
                CargoTransportWaitReason.DestinationCapacity,
                context.Tick);
            return;
        }

        if (context.Entities.HasComponent<BuildingLoadingDock>(destination.Entity))
        {
            unloadQuantity = Math.Min(20, unloadQuantity);
            if (!_docks.Approach(context, entity, destination.Entity, transport.Owner) ||
                !context.Entities.TryGetComponent(entity, out VehicleDockVisit unloadingVisit) ||
                !_docks.CanTransfer(context, entity, unloadingVisit.Generation, unloadQuantity)) return;
        }        InventoryOperationResult transfer =
            _inventories.Transfer(
                transport.CargoInventory,
                destinationInventory,
                order.ResourceId,
                unloadQuantity);

        if (!transfer.Succeeded)
        {
            Fail(
                context.Entities,
                entity,
                state,
                CargoTransportFailureReason.TransferFailed,
                context.Tick);
            return;
        }

        if (context.Entities.TryGetComponent(entity, out VehicleDockVisit unloadedVisit))
            VehicleDockingService.RecordTransfer(context, entity, unloadedVisit.Generation);
        _deliveredQuantity += unloadQuantity;

        CargoTransportRuntimeState unloadedState =
            state with
            {
                DeliveredQuantity =
                    state.DeliveredQuantity +
                    unloadQuantity,
                StateChangedAtTick =
                    context.Tick
            };

        double remaining =
            _inventories.GetAvailableQuantity(
                transport.CargoInventory,
                order.ResourceId);

        if (remaining > QuantityEpsilon)
        {
            if (_inventories.GetAddableQuantity(destinationInventory, order.ResourceId, 1) <= QuantityEpsilon)
                Wait(context.Entities, entity, unloadedState, CargoTransportWaitReason.DestinationCapacity, context.Tick);
            else SetState(context.Entities, entity, unloadedState);
            return;
        }
        if (context.Entities.HasComponent<VehicleDockVisit>(entity) && !_docks.Exit(context, entity))
        {
            PrepareDockFuelBudget(context, entity);
            SetState(context.Entities, entity, unloadedState);
            return;
        }
        CompleteOrder(
            context.Entities,
            entity,
            unloadedState,
            context.Tick);
    }

    private void ProcessWaiting(
        SimulationContext context,
        EntityId entity,
        in CargoTransport transport,
        in CargoTransportOrder order,
        in CargoTransportRuntimeState state)
    {
        switch (state.WaitReason)
        {
            case CargoTransportWaitReason.OriginUnavailable:
                if (_network.TryGetNode(
                        order.Origin,
                        out LogisticsNode origin) &&
                    origin.Enabled &&
                    origin.Capabilities.HasFlag(
                        LogisticsNodeCapabilities.CargoSource))
                {
                    SetState(
                        context.Entities,
                        entity,
                        state with
                        {
                            Lifecycle =
                                CargoTransportLifecycleState.ToOrigin,
                            WaitReason =
                                CargoTransportWaitReason.None,
                            ObservedNetworkVersion =
                                _network.Version,
                            StateChangedAtTick =
                                context.Tick
                        });
                }
                break;

            case CargoTransportWaitReason
                    .OriginResourceUnavailable:
                if (_network.TryGetNode(
                        order.Origin,
                        out LogisticsNode sourceNode) &&
                    TryResolveSourceInventory(
                        context.Entities,
                        sourceNode,
                        out InventoryId sourceInventory))
                {
                    double required =
                        order.PartialLoadPolicy ==
                        CargoPartialLoadPolicy
                            .RequireRequestedQuantity
                            ? order.RequestedQuantity
                            : QuantityEpsilon;
                    double available =
                        context.Entities.TryGetComponent(
                            entity,
                            out CargoTransportReservation reservation) &&
                        reservation.SourceInventory == sourceInventory &&
                        reservation.ResourceId == order.ResourceId
                            ? reservation.Quantity
                            : _inventories.GetAvailableQuantity(
                                sourceInventory,
                                order.ResourceId);

                    if (available + QuantityEpsilon >=
                        required)
                    {
                        SetState(
                            context.Entities,
                            entity,
                            state with
                            {
                                Lifecycle =
                                    CargoTransportLifecycleState.Loading,
                                WaitReason =
                                    CargoTransportWaitReason.None,
                                StateChangedAtTick =
                                    context.Tick
                            });
                    }
                }
                break;

            case CargoTransportWaitReason.RouteUnavailable:
                if (_network.Version !=
                    state.ObservedNetworkVersion)
                {
                    if (!TryPlanRoute(
                            context.Entities,
                            entity,
                            order,
                            state,
                            state.AnchorNode,
                            context.Tick))
                    {
                        Wait(
                            context.Entities,
                            entity,
                            state,
                            CargoTransportWaitReason.RouteUnavailable,
                            context.Tick);
                    }
                }
                break;

            case CargoTransportWaitReason.RouteInvalidated:
                ProcessRouteInvalidationRecovery(
                    context,
                    entity,
                    transport,
                    order,
                    state);
                break;

            case CargoTransportWaitReason.DestinationUnavailable:
                if (_network.TryGetNode(
                        order.Destination,
                        out LogisticsNode destination) &&
                    destination.Enabled &&
                    destination.Capabilities.HasFlag(
                        LogisticsNodeCapabilities.CargoDestination))
                {
                    SetState(
                        context.Entities,
                        entity,
                        state with
                        {
                            Lifecycle =
                                CargoTransportLifecycleState.Unloading,
                            WaitReason =
                                CargoTransportWaitReason.None,
                            ObservedNetworkVersion =
                                _network.Version,
                            StateChangedAtTick =
                                context.Tick
                        });
                }
                break;

            case CargoTransportWaitReason.DestinationCapacity:
                if (_network.TryGetNode(
                        order.Destination,
                        out LogisticsNode destinationNode) &&
                    TryResolveDestinationInventory(
                        context.Entities,
                        destinationNode,
                        out InventoryId destinationInventory))
                {
                    double cargoQuantity =
                        _inventories.GetAvailableQuantity(
                            transport.CargoInventory,
                            order.ResourceId);
                    double addable =
                        _inventories.GetAddableQuantity(
                            destinationInventory,
                            order.ResourceId,
                            cargoQuantity);

                    if (cargoQuantity <= QuantityEpsilon ||
                        addable > QuantityEpsilon)
                    {
                        SetState(
                            context.Entities,
                            entity,
                            state with
                            {
                                Lifecycle =
                                    CargoTransportLifecycleState.Unloading,
                                WaitReason =
                                    CargoTransportWaitReason.None,
                                StateChangedAtTick =
                                    context.Tick
                            });
                    }
                }
                break;

            default:
                Fail(
                    context.Entities,
                    entity,
                    state,
                    CargoTransportFailureReason.InvalidTransport,
                    context.Tick);
                break;
        }
    }

    private void ProcessRouteInvalidationRecovery(
        SimulationContext context,
        EntityId entity,
        in CargoTransport transport,
        in CargoTransportOrder order,
        in CargoTransportRuntimeState state)
    {
        if (!_network.TryGetNode(
                state.AnchorNode,
                out LogisticsNode anchor))
        {
            Fail(
                context.Entities,
                entity,
                state,
                CargoTransportFailureReason.InvalidRouteAnchor,
                context.Tick);
            return;
        }

        if (HasNavigationFailure(
                context.Entities,
                entity))
        {
            Fail(
                context.Entities,
                entity,
                state,
                CargoTransportFailureReason.NavigationFailed,
                context.Tick);
            return;
        }

        bool loadedBuildingAnchor = context.Entities.HasComponent<CompletedBuilding>(anchor.Entity) &&
            state.LoadedQuantity > QuantityEpsilon && !context.Entities.HasComponent<VehicleDockVisit>(entity);
        if (!loadedBuildingAnchor && !IsSettledAtNode(
                context.Entities,
                entity,
                anchor))
        {
            EnsureMovementToNode(
                context,
                entity,
                transport,
                anchor);
            return;
        }

        ClearNavigation(
            context.Entities,
            entity);

        if (!TryPlanRoute(
                context.Entities,
                entity,
                order,
                state,
                state.AnchorNode,
                context.Tick))
        {
            Wait(
                context.Entities,
                entity,
                state,
                CargoTransportWaitReason.RouteUnavailable,
                context.Tick);
        }
        if (context.Entities.HasComponent<VehicleDockVisit>(entity))
        {
            _docks.Exit(context, entity);
            PrepareDockFuelBudget(context, entity);
        }
    }

    private bool TryPlanRoute(
        EntityRegistry entities,
        EntityId entity,
        in CargoTransportOrder order,
        in CargoTransportRuntimeState state,
        LogisticsNodeId source,
        SimulationTick tick)
    {
        if (!source.IsSpecified ||
            !_network.TryGetNode(
                source,
                out LogisticsNode sourceNode) ||
            !sourceNode.Enabled ||
            !_network.TryGetNode(
                order.Destination,
                out LogisticsNode destinationNode) ||
            !destinationNode.Enabled)
        {
            _routeFailureCount++;
            return false;
        }

        LogisticsRouteSearchResult search =
            _network.FindRoute(
                source,
                order.Destination,
                LogisticsRouteCostPolicy.Default);

        if (!search.Succeeded ||
            search.Route is null)
        {
            _routeFailureCount++;
            return false;
        }

        var routeState =
            new CargoTransportRouteState(
                search.Route,
                NextSegmentIndex: 0);

        if (entities.HasComponent<
                CargoTransportRouteState>(entity))
        {
            entities.SetComponent(
                entity,
                routeState);
        }
        else
        {
            entities.AddComponent(
                entity,
                routeState);
        }

        SetState(
            entities,
            entity,
            state with
            {
                Lifecycle =
                    search.Route.Segments.Count == 0
                        ? CargoTransportLifecycleState.Unloading
                        : CargoTransportLifecycleState.ToDestination,
                WaitReason =
                    CargoTransportWaitReason.None,
                FailureReason =
                    CargoTransportFailureReason.None,
                AnchorNode =
                    source,
                ObservedNetworkVersion =
                    search.Route.Version,
                StateChangedAtTick =
                    tick
            });

        return true;
    }

    private void BeginReroute(
        EntityRegistry entities,
        EntityId entity,
        in CargoTransportRuntimeState state,
        SimulationTick tick)
    {
        _rerouteCount++;
        ClearNavigation(
            entities,
            entity);
        RemoveRouteState(
            entities,
            entity);

        if (entities.TryGetComponent(entity, out VehicleDockVisit dock))
            VehicleDockingService.Put(entities, entity, dock with { Status = VehicleDockStatus.Exiting,
                ServiceStartedTick = 0, ServiceProgress = 0 });

        Wait(
            entities,
            entity,
            state with
            {
                ObservedNetworkVersion =
                    _network.Version
            },
            CargoTransportWaitReason.RouteInvalidated,
            tick);
    }

    private void EnsureMovementToNode(
        SimulationContext context,
        EntityId entity,
        in CargoTransport transport,
        in LogisticsNode node, bool requireDock = true)
    {
        if (!context.Entities.TryGetComponent(
                entity,
                out WorldTransform transportTransform) ||
            !context.Entities.TryGetComponent(
                entity,
                out GroundMovement groundMovement))
        {
            return;
        }

        if (requireDock && (context.Entities.HasComponent<CompletedBuilding>(node.Entity) || TryGetStaticNodeBounds(context.Entities, node, out _)))
        {
            _docks.Approach(context, entity, node.Entity, transport.Owner);
            PrepareDockFuelBudget(context, entity);
            if (context.Entities.TryGetComponent(entity, out MovementOrder dockMovement) &&
                (!context.Entities.TryGetComponent(entity, out CargoTransportMovementTarget currentDockTarget) ||
                 currentDockTarget.WorldPosition != dockMovement.WorldTarget))
                VehicleDockingService.Put(context.Entities, entity, new CargoTransportMovementTarget(node.Id, dockMovement.WorldTarget, context.Tick));
            return;
        }
        // An approach is a fixed destination for this node visit, not a point
        // that follows the truck as it steers. Recomputing it during an active
        // route repeatedly replaces navigation with a path from the new start.
        if (context.Entities.TryGetComponent(entity, out CargoTransportMovementTarget activeTarget) &&
            activeTarget.NodeId == node.Id &&
            TacticalCommandUtilities.TryGetMovementIntent(context, entity, out MovementOrder activeOrder) &&
            Vector3.DistanceSquared(activeOrder.WorldTarget, activeTarget.WorldPosition) <= 1.0f)
        {
            return;
        }

        Vector3 movementPosition = requireDock ? node.WorldPosition : ResolveTransitApproachPosition(context.Entities, node, transportTransform.Position, groundMovement);

        if (_navigation is not null &&
            context.Entities.TryGetComponent(entity, out NavigationAgent agent))
        {
            movementPosition = ResolveNavigableApproach(movementPosition, agent.Capabilities);
        }

        if (context.Entities.TryGetComponent(
                entity,
                out CargoTransportMovementTarget target) &&
            target.NodeId == node.Id &&
            Vector3.DistanceSquared(
                target.WorldPosition,
                movementPosition) <= 1.0f)
        {
            bool navigationActive =
                context.Entities.HasComponent<
                    MovementOrder>(entity) ||
                context.Entities.HasComponent<
                    NavigationPendingPath>(entity) ||
                context.Entities.HasComponent<
                    NavigationRouteState>(entity);

            if (navigationActive)
            {
                return;
            }
        }

        var movementOrder = new MovementOrder(
            transport.Owner,
            movementPosition,
            context.Tick,
            context.Tick);

        if (_navigation is not null &&
            context.Entities.HasComponent<NavigationAgent>(entity))
        {
            if (CargoDeliveryFuelPolicy.ShouldDeferMovement(
                    context.Entities,
                    _inventories,
                    entity,
                    movementOrder,
                    _navigation.World.Version))
            {
                return;
            }

            CargoDeliveryFuelPolicy.PrepareMovementBudget(
                context.Entities,
                _inventories,
                entity,
                movementOrder,
                IsFuelProductionRecoveryOrder(
                    context.Entities,
                    entity));
        }

        if (context.Entities.HasComponent<
                MovementOrder>(entity))
        {
            context.Entities.SetComponent(
                entity,
                movementOrder);
        }
        else
        {
            context.Entities.AddComponent(
                entity,
                movementOrder);
        }

        var movementTarget =
            new CargoTransportMovementTarget(
                node.Id,
                movementPosition,
                context.Tick);

        if (context.Entities.HasComponent<
                CargoTransportMovementTarget>(entity))
        {
            context.Entities.SetComponent(
                entity,
                movementTarget);
        }
        else
        {
            context.Entities.AddComponent(
                entity,
                movementTarget);
        }
    }

    private void PrepareDockFuelBudget(SimulationContext context, EntityId entity)
    {
        CargoDeliveryFuelPolicy.PrepareDockMovement(context, _inventories, entity, _navigation?.World.Version ?? default,
            IsFuelProductionRecoveryOrder(context.Entities, entity));
    }
    private bool IsFuelProductionRecoveryOrder(
        EntityRegistry entities,
        EntityId entity)
    {
        if (!entities.TryGetComponent(
                entity,
                out CargoTransportOrder order) ||
            order.ResourceId != ResourceIds.Volatiles ||
            !_network.TryGetNode(
                order.Destination,
                out LogisticsNode destination) ||
            !entities.TryGetComponent(
                destination.Entity,
                out ProductionFacility production))
        {
            return false;
        }

        return production.Supports(
            ProductionCapability.FuelProcessing);
    }

    private Vector3 ResolveNavigableApproach(
        Vector3 requested,
        in NavigationCapabilities capabilities)
    {
        NavigationGrid grid = _navigation!.World.Grid;
        if (!grid.TryWorldToCell(requested, out NavigationCellCoordinate cell) ||
            grid.IsTraversable(cell, capabilities))
        {
            return requested;
        }

        Vector3 nearest = requested;
        float bestDistance = float.PositiveInfinity;
        for (int z = cell.Z - 2; z <= cell.Z + 2; z++)
        {
            for (int x = cell.X - 2; x <= cell.X + 2; x++)
            {
                var candidate = new NavigationCellCoordinate(x, z);
                if (!grid.IsTraversable(candidate, capabilities))
                {
                    continue;
                }

                Vector3 position = grid.GetCellCenter(candidate);
                Vector3 delta = position - requested;
                float distance = delta.X * delta.X + delta.Z * delta.Z;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    nearest = position;
                }
            }
        }

        return nearest;
    }

    private static Vector3 ResolveTransitApproachPosition(
        EntityRegistry entities,
        in LogisticsNode node,
        Vector3 origin,
        in GroundMovement movement)
    {
        if (!TryGetStaticNodeBounds(
                entities,
                node,
                out ForgeLine.World.AxisAlignedBounds bounds))
        {
            return node.WorldPosition;
        }

        const float NavigationCellClearanceMeters = 32.0f;

        float clearance =
            movement.ObstacleLookAhead +
            movement.Radius +
            NavigationCellClearanceMeters;
        float minimumX =
            bounds.Minimum.X -
            clearance;
        float maximumX =
            bounds.Maximum.X +
            clearance;
        float minimumZ =
            bounds.Minimum.Z -
            clearance;
        float maximumZ =
            bounds.Maximum.Z +
            clearance;
        Vector3 center =
            (bounds.Minimum +
             bounds.Maximum) *
            0.5f;
        float deltaX =
            origin.X -
            center.X;
        float deltaZ =
            origin.Z -
            center.Z;

        if (MathF.Abs(deltaX) >=
            MathF.Abs(deltaZ))
        {
            return new Vector3(
                deltaX >= 0.0f
                    ? maximumX
                    : minimumX,
                node.WorldPosition.Y,
                Math.Clamp(
                    origin.Z,
                    minimumZ,
                    maximumZ));
        }

        return new Vector3(
            Math.Clamp(
                origin.X,
                minimumX,
                maximumX),
            node.WorldPosition.Y,
            deltaZ >= 0.0f
                ? maximumZ
                : minimumZ);
    }

    private static bool TryGetStaticNodeBounds(
        EntityRegistry entities,
        in LogisticsNode node,
        out ForgeLine.World.AxisAlignedBounds bounds)
    {
        if (!entities.IsAlive(
                node.Entity))
        {
            bounds = default;
            return false;
        }

        if (entities.TryGetComponent(
                node.Entity,
                out ResourceExtractor extractor) &&
            entities.IsAlive(
                extractor.Deposit) &&
            entities.TryGetComponent(
                extractor.Deposit,
                out ResourceDeposit deposit))
        {
            bounds =
                deposit.Bounds;
            return true;
        }

        if (!entities.TryGetComponent(
                node.Entity,
                out WorldTransform transform) ||
            !entities.TryGetComponent(
                node.Entity,
                out SpatialPresence presence) ||
            presence.Metadata.Mobility !=
                ForgeLine.World.SpatialMobility.Static)
        {
            bounds = default;
            return false;
        }

        bounds =
            presence.CreateEntry(
                node.Entity,
                transform).Bounds;
        return true;
    }

    private static bool IsSettledAtNode(
        EntityRegistry entities,
        EntityId entity,
        in LogisticsNode node, bool requireDock = true)
    {
        if (requireDock && (entities.HasComponent<CompletedBuilding>(node.Entity) || TryGetStaticNodeBounds(entities, node, out _)))
        {
            return entities.TryGetComponent(entity, out VehicleDockVisit visit) && visit.Building == node.Entity &&
                visit.Status is VehicleDockStatus.Docked or VehicleDockStatus.Servicing &&
                entities.TryGetComponent(entity, out WorldTransform truckPose) &&
                entities.TryGetComponent(entity, out GroundMovementState motion) && visit.Pose.IsSettled(truckPose, motion);
        }        if (!entities.TryGetComponent(
                entity,
                out WorldTransform transform) ||
            !entities.TryGetComponent(
                entity,
                out GroundMovement movement))
        {
            return false;
        }

        if (IsWithinLogicalTransitNodeApproachRange(
                entities,
                node,
                transform.Position))
        {
            return true;
        }

        bool navigationActive =
            entities.HasComponent<MovementOrder>(entity) ||
            entities.HasComponent<NavigationPendingPath>(entity) ||
            entities.HasComponent<NavigationRouteState>(entity);

        if (entities.TryGetComponent(
                entity,
                out CargoTransportMovementTarget approachTarget) &&
            approachTarget.NodeId == node.Id)
        {
            Vector3 approachDelta =
                approachTarget.WorldPosition -
                transform.Position;
            approachDelta.Y = 0.0f;

            float approachTolerance =
                movement.StopRadius +
                1.5f;



            if (approachDelta.LengthSquared() <=
                approachTolerance *
                approachTolerance)
            {
                return true;
            }
        }

        if (navigationActive)
        {
            return false;
        }

        Vector3 delta =
            node.WorldPosition - transform.Position;
        delta.Y = 0.0f;

        float tolerance =
            movement.StopRadius + 0.25f;
        return delta.LengthSquared() <=
            tolerance * tolerance;
    }

    private static bool IsWithinLogicalTransitNodeApproachRange(
        EntityRegistry entities,
        in LogisticsNode node,
        Vector3 position)
    {
        if (!entities.IsAlive(node.Entity) ||
            entities.HasComponent<CompletedBuilding>(node.Entity) ||
            entities.HasComponent<ResourceExtractor>(node.Entity) ||
            entities.HasComponent<InventoryStorage>(node.Entity) ||
            entities.HasComponent<ProductionFacility>(node.Entity) ||
            entities.HasComponent<UnitProductionFacility>(node.Entity) ||
            entities.HasComponent<LogisticsHub>(node.Entity) ||
            entities.HasComponent<SupplyDepot>(node.Entity))
        {
            return false;
        }

        Vector3 delta =
            node.WorldPosition -
            position;
        delta.Y = 0.0f;

        return delta.LengthSquared() <=
            LogicalTransitNodeApproachRadiusMeters *
            LogicalTransitNodeApproachRadiusMeters;
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
            _inventories.Contains(
                production.OutputInventory))
        {
            inventoryId =
                production.OutputInventory;
            return true;
        }

        if (entities.TryGetComponent(
                node.Entity,
                out InventoryStorage storage) &&
            _inventories.Contains(
                storage.InventoryId))
        {
            inventoryId = storage.InventoryId;
            return true;
        }

        if (entities.TryGetComponent(
                node.Entity,
                out ResourceExtractor extractor) &&
            extractor.OutputInventory.IsValid &&
            entities.TryGetComponent(
                extractor.OutputInventory,
                out InventoryStorage outputStorage) &&
            _inventories.Contains(
                outputStorage.InventoryId))
        {
            inventoryId =
                outputStorage.InventoryId;
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
            _inventories.Contains(
                production.InputInventory))
        {
            inventoryId =
                production.InputInventory;
            return true;
        }

        if (entities.TryGetComponent(
                node.Entity,
                out InventoryStorage storage) &&
            _inventories.Contains(
                storage.InventoryId))
        {
            inventoryId = storage.InventoryId;
            return true;
        }

        inventoryId = InventoryId.None;
        return false;
    }

    private void CompleteOrder(
        EntityRegistry entities,
        EntityId entity,
        in CargoTransportRuntimeState state,
        SimulationTick tick)
    {
        ClearNavigation(
            entities,
            entity);
        RemoveRouteState(
            entities,
            entity);

        if (entities.HasComponent<
                CargoTransportOrder>(entity))
        {
            entities.RemoveComponent<
                CargoTransportOrder>(entity);
        }

        SetState(
            entities,
            entity,
            state with
            {
                Lifecycle =
                    CargoTransportLifecycleState.Idle,
                WaitReason =
                    CargoTransportWaitReason.None,
                FailureReason =
                    CargoTransportFailureReason.None,
                AnchorNode =
                    LogisticsNodeId.None,
                ObservedNetworkVersion =
                    _network.Version,
                LoadedQuantity = 0.0,
                StateChangedAtTick =
                    tick
            });

        _completedOrderCount++;
    }

    private static void Wait(
        EntityRegistry entities,
        EntityId entity,
        in CargoTransportRuntimeState state,
        CargoTransportWaitReason reason,
        SimulationTick tick)
    {
        if (entities.TryGetComponent(entity, out VehicleDockVisit dock) &&
            reason is CargoTransportWaitReason.OriginResourceUnavailable or CargoTransportWaitReason.DestinationCapacity or
                CargoTransportWaitReason.OriginUnavailable or CargoTransportWaitReason.DestinationUnavailable)
            VehicleDockingService.Put(entities, entity, dock with { ServiceStartedTick = 0, ServiceProgress = 0,
                Reason = reason == CargoTransportWaitReason.DestinationCapacity ? VehicleDockBlockReason.TargetFull : VehicleDockBlockReason.SourceEmpty });        SetState(
            entities,
            entity,
            state with
            {
                Lifecycle =
                    CargoTransportLifecycleState.Waiting,
                WaitReason = reason,
                FailureReason =
                    CargoTransportFailureReason.None,
                StateChangedAtTick =
                    tick
            });
    }

    private static void Fail(
        EntityRegistry entities,
        EntityId entity,
        in CargoTransportRuntimeState state,
        CargoTransportFailureReason reason,
        SimulationTick tick)
    {
        if (!entities.IsAlive(entity))
        {
            return;
        }

        VehicleDockingService.Cancel(entities, entity);
        ClearNavigation(
            entities,
            entity);
        RemoveRouteState(
            entities,
            entity);

        SetState(
            entities,
            entity,
            state with
            {
                Lifecycle =
                    CargoTransportLifecycleState.Failed,
                WaitReason =
                    CargoTransportWaitReason.None,
                FailureReason = reason,
                StateChangedAtTick =
                    tick
            });
    }

    private static void SetState(
        EntityRegistry entities,
        EntityId entity,
        in CargoTransportRuntimeState state)
    {
        if (entities.HasComponent<
                CargoTransportRuntimeState>(entity))
        {
            entities.SetComponent(
                entity,
                state);
        }
        else
        {
            entities.AddComponent(
                entity,
                state);
        }
    }

    private static bool HasNavigationFailure(
        EntityRegistry entities,
        EntityId entity) =>
        entities.HasComponent<
            NavigationFailureState>(entity);

    private static void ClearNavigation(
        EntityRegistry entities,
        EntityId entity)
    {
        if (!entities.IsAlive(entity))
        {
            return;
        }

        if (entities.HasComponent<
                CargoTransportMovementTarget>(entity))
        {
            entities.RemoveComponent<
                CargoTransportMovementTarget>(entity);
        }

        if (entities.HasComponent<
                MovementOrder>(entity))
        {
            entities.RemoveComponent<
                MovementOrder>(entity);
        }

        if (entities.HasComponent<
                NavigationPendingPath>(entity))
        {
            entities.RemoveComponent<
                NavigationPendingPath>(entity);
        }

        if (entities.HasComponent<
                NavigationRouteState>(entity))
        {
            entities.RemoveComponent<
                NavigationRouteState>(entity);
        }

        if (entities.HasComponent<
                NavigationFailureState>(entity))
        {
            entities.RemoveComponent<
                NavigationFailureState>(entity);
        }

        CargoDeliveryFuelPolicy.ClearTravelState(
            entities,
            entity);
    }

    private static void RemoveRouteState(
        EntityRegistry entities,
        EntityId entity)
    {
        if (entities.IsAlive(entity) &&
            entities.HasComponent<
                CargoTransportRouteState>(entity))
        {
            entities.RemoveComponent<
                CargoTransportRouteState>(entity);
        }
    }

    private void CleanupDestroyedTransports(
        EntityRegistry entities)
    {
        _staleTrackedEntities.Clear();

        foreach ((EntityId entity, InventoryId inventory)
                 in _trackedInventories)
        {
            if (entities.IsAlive(entity))
            {
                continue;
            }

            if (_inventories.Contains(inventory))
            {
                _lostQuantity +=
                    _inventories.GetTotalQuantity(
                        inventory);
                _inventories.DestroyInventory(
                    inventory);
            }

            _staleTrackedEntities.Add(entity);
        }

        for (int index = 0;
             index < _staleTrackedEntities.Count;
             index++)
        {
            _trackedInventories.Remove(
                _staleTrackedEntities[index]);
        }
    }

    private void UpdateMetricsAndDebugSnapshot(
        EntityRegistry entities)
    {
        int transportCount = 0;
        int activeCount = 0;
        int waitingCount = 0;
        int failedCount = 0;
        double cargoInTransit = 0.0;
        var readModels =
            new List<CargoTransportReadModel>(
                _transportEntities.Count);

        for (int index = 0;
             index < _transportEntities.Count;
             index++)
        {
            EntityId entity =
                _transportEntities[index];

            if (!entities.TryGetComponent(
                    entity,
                    out CargoTransport transport))
            {
                continue;
            }

            transportCount++;

            double cargoQuantity =
                _inventories.Contains(
                    transport.CargoInventory)
                    ? _inventories.GetTotalQuantity(
                        transport.CargoInventory)
                    : 0.0;
            cargoInTransit += cargoQuantity;

            bool hasOrder =
                entities.TryGetComponent(
                    entity,
                    out CargoTransportOrder order);
            CargoTransportRuntimeState state =
                entities.TryGetComponent(
                    entity,
                    out CargoTransportRuntimeState runtime)
                    ? runtime
                    : CargoTransportRuntimeState.Idle;

            if (hasOrder)
            {
                activeCount++;
            }

            if (state.Lifecycle ==
                CargoTransportLifecycleState.Waiting)
            {
                waitingCount++;
            }
            else if (state.Lifecycle ==
                     CargoTransportLifecycleState.Failed)
            {
                failedCount++;
            }

            if (!entities.TryGetComponent(
                    entity,
                    out WorldTransform transform))
            {
                continue;
            }

            bool hasMovementTarget =
                entities.TryGetComponent(
                    entity,
                    out CargoTransportMovementTarget movementTarget);
            LogisticsNetworkVersion routeVersion =
                entities.TryGetComponent(
                    entity,
                    out CargoTransportRouteState routeState) &&
                routeState.Route is not null
                    ? routeState.Route.Version
                    : default;

            readModels.Add(
                new CargoTransportReadModel(
                    entity,
                    state.Lifecycle,
                    state.WaitReason,
                    state.FailureReason,
                    hasOrder
                        ? order.ResourceId
                        : ResourceId.None,
                    hasOrder
                        ? order.RequestedQuantity
                        : 0.0,
                    cargoQuantity,
                    state.DeliveredQuantity,
                    hasOrder
                        ? order.Origin
                        : LogisticsNodeId.None,
                    hasOrder
                        ? order.Destination
                        : LogisticsNodeId.None,
                    state.AnchorNode,
                    routeVersion,
                    transform.Position,
                    hasMovementTarget,
                    hasMovementTarget
                        ? movementTarget.WorldPosition
                        : default));
        }

        Metrics = new CargoTransportMetrics(
            transportCount,
            activeCount,
            waitingCount,
            failedCount,
            cargoInTransit,
            _deliveredQuantity,
            _lostQuantity,
            _completedOrderCount,
            _rerouteCount,
            _routeFailureCount);

        LastDebugSnapshot =
            new CargoTransportDebugSnapshot(
                Metrics,
                readModels.ToArray());
    }
}
