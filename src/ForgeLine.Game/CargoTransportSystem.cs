using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Logistics;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public sealed class CargoTransportSystem : ISimulationSystem
{
    private const double QuantityEpsilon = 0.000000001;

    private readonly LogisticsNetwork _network;
    private readonly InventoryStore _inventories;
    private readonly List<EntityId> _transportEntities = new();
    private readonly Dictionary<EntityId, InventoryId> _trackedInventories = new();
    private readonly List<EntityId> _staleTrackedEntities = new();
    private long _completedOrderCount;
    private long _rerouteCount;
    private long _routeFailureCount;
    private double _deliveredQuantity;
    private double _lostQuantity;

    public CargoTransportSystem(
        LogisticsNetwork network,
        InventoryStore inventories)
    {
        _network = network ??
            throw new ArgumentNullException(nameof(network));
        _inventories = inventories ??
            throw new ArgumentNullException(nameof(inventories));
    }

    public SimulationPhase Phase =>
        SimulationPhase.Logistics;

    public CargoTransportMetrics Metrics { get; private set; }

    public CargoTransportDebugSnapshot LastDebugSnapshot
    {
        get;
        private set;
    } = CargoTransportDebugSnapshot.Empty;

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
                order.RequestedQuantity);

        if (order.PartialLoadPolicy ==
            CargoPartialLoadPolicy.RequireRequestedQuantity)
        {
            if (available + QuantityEpsilon <
                order.RequestedQuantity)
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
                order.RequestedQuantity)
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
            order.RequestedQuantity,
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
            context.Entities.RemoveComponent<CargoTransportReservation>(
                entity);
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

        if (!IsSettledAtNode(
                context.Entities,
                entity,
                targetNode))
        {
            EnsureMovementToNode(
                context,
                entity,
                transport,
                targetNode);
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

        InventoryOperationResult transfer =
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
            Wait(
                context.Entities,
                entity,
                unloadedState,
                CargoTransportWaitReason.DestinationCapacity,
                context.Tick);
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

        if (!IsSettledAtNode(
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

    private static void EnsureMovementToNode(
        SimulationContext context,
        EntityId entity,
        in CargoTransport transport,
        in LogisticsNode node)
    {
        if (context.Entities.TryGetComponent(
                entity,
                out CargoTransportMovementTarget target) &&
            target.NodeId == node.Id)
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
            node.WorldPosition,
            context.Tick,
            context.Tick);

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
                node.WorldPosition,
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

    private static bool IsSettledAtNode(
        EntityRegistry entities,
        EntityId entity,
        in LogisticsNode node)
    {
        if (!entities.TryGetComponent(
                entity,
                out WorldTransform transform) ||
            !entities.TryGetComponent(
                entity,
                out GroundMovement movement))
        {
            return false;
        }

        if (entities.HasComponent<MovementOrder>(entity) ||
            entities.HasComponent<NavigationPendingPath>(entity) ||
            entities.HasComponent<NavigationRouteState>(entity))
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
        SetState(
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
