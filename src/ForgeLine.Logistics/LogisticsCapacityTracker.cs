using ForgeLine.Simulation;

namespace ForgeLine.Logistics;

public sealed class LogisticsCapacityTracker :
    ILogisticsRouteCostAdjustment
{
    private const double QuantityEpsilon = 0.000000001;

    private readonly int _ticksPerSecond;
    private readonly ulong _windowTicks;
    private readonly double _windowSeconds;
    private readonly double _busyThreshold;
    private readonly double _saturatedThreshold;
    private readonly double _congestionCostScale;
    private readonly Dictionary<LogisticsEdgeId, double> _edgeLoad = new();
    private readonly Dictionary<LogisticsNodeId, double> _nodeLoad = new();
    private readonly Dictionary<
        LogisticsThroughputReservationId,
        ReservationState> _reservations = new();
    private readonly List<LogisticsThroughputReservationId> _staleReservations = new();
    private ulong _nextReservationId;
    private ulong _revision = 1;
    private long _deniedReservationCount;
    private LogisticsNetworkVersion _observedNetworkVersion;
    private SimulationTick _currentTick;

    public LogisticsCapacityTracker(
        int ticksPerSecond = 20,
        ulong windowTicks = 20,
        double busyThreshold = 0.70,
        double saturatedThreshold = 0.90,
        double congestionCostScale = 8.0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerSecond);
        ArgumentOutOfRangeException.ThrowIfZero(windowTicks);

        if (!double.IsFinite(busyThreshold) ||
            busyThreshold <= 0.0 ||
            busyThreshold >= 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(busyThreshold));
        }

        if (!double.IsFinite(saturatedThreshold) ||
            saturatedThreshold <= busyThreshold ||
            saturatedThreshold > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(saturatedThreshold));
        }

        if (!double.IsFinite(congestionCostScale) ||
            congestionCostScale < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(congestionCostScale));
        }

        _ticksPerSecond = ticksPerSecond;
        _windowTicks = windowTicks;
        _windowSeconds =
            (double)windowTicks / ticksPerSecond;
        _busyThreshold = busyThreshold;
        _saturatedThreshold = saturatedThreshold;
        _congestionCostScale = congestionCostScale;
    }

    public ulong Revision => _revision;

    public int ActiveReservationCount => _reservations.Count;

    public long DeniedReservationCount => _deniedReservationCount;

    public ulong WindowTicks => _windowTicks;

    public double WindowSeconds => _windowSeconds;

    public SimulationTick CurrentTick => _currentTick;

    public void Advance(
        LogisticsNetwork network,
        SimulationTick tick)
    {
        ArgumentNullException.ThrowIfNull(network);

        _currentTick = tick;
        _staleReservations.Clear();

        foreach ((
                     LogisticsThroughputReservationId id,
                     ReservationState reservation)
                 in _reservations)
        {
            if (tick >= reservation.ExpiresAtTick ||
                !ReservationTopologyRemainsValid(
                    network,
                    reservation))
            {
                _staleReservations.Add(id);
            }
        }

        for (int index = 0;
             index < _staleReservations.Count;
             index++)
        {
            RemoveReservation(_staleReservations[index]);
        }

        _observedNetworkVersion = network.Version;
    }

    public bool TryReserveRoute(
        LogisticsNetwork network,
        LogisticsRoute route,
        double quantity,
        SimulationTick tick,
        out LogisticsThroughputReservationId reservationId,
        out LogisticsCapacityBottleneck bottleneck)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(route);

        if (!double.IsFinite(quantity) || quantity <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        Advance(network, tick);

        if (route.Version != network.Version)
        {
            reservationId = LogisticsThroughputReservationId.None;
            bottleneck = LogisticsCapacityBottleneck.None;
            _deniedReservationCount++;
            return false;
        }

        if (!TryValidateRouteCapacity(
                network,
                route,
                quantity,
                out bottleneck))
        {
            reservationId = LogisticsThroughputReservationId.None;
            _deniedReservationCount++;
            return false;
        }

        LogisticsEdgeId[] edgeIds =
            new LogisticsEdgeId[route.Segments.Count];
        var nodeIds =
            new LogisticsNodeId[route.Segments.Count + 1];
        nodeIds[0] = route.Source;

        for (int index = 0;
             index < route.Segments.Count;
             index++)
        {
            LogisticsRouteSegment segment = route.Segments[index];
            edgeIds[index] = segment.EdgeId;
            nodeIds[index + 1] = segment.To;
            AddLoad(_edgeLoad, segment.EdgeId, quantity);
        }

        for (int index = 0; index < nodeIds.Length; index++)
        {
            AddLoad(_nodeLoad, nodeIds[index], quantity);
        }

        reservationId =
            new LogisticsThroughputReservationId(
                checked(++_nextReservationId));

        var reservation =
            new ReservationState(
                reservationId,
                edgeIds,
                nodeIds,
                quantity,
                tick,
                new SimulationTick(
                    checked(tick.Value + _windowTicks)));

        _reservations.Add(reservationId, reservation);
        IncrementRevision();
        bottleneck = LogisticsCapacityBottleneck.None;
        return true;
    }

    public bool Release(
        LogisticsThroughputReservationId reservationId)
    {
        if (!reservationId.IsSpecified)
        {
            return false;
        }

        return RemoveReservation(reservationId);
    }

    public bool Contains(
        LogisticsThroughputReservationId reservationId) =>
        reservationId.IsSpecified &&
        _reservations.ContainsKey(reservationId);

    public bool TryEvaluateTraversal(
        in LogisticsEdge edge,
        in LogisticsNode from,
        in LogisticsNode to,
        double requestedQuantity,
        out double additionalCost)
    {
        if (!double.IsFinite(requestedQuantity) ||
            requestedQuantity <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedQuantity));
        }

        if (!edge.Enabled || !from.Enabled || !to.Enabled)
        {
            additionalCost = double.PositiveInfinity;
            return false;
        }

        double edgeCapacity =
            GetWindowCapacity(edge.CapacityPerSecond);
        double fromCapacity =
            GetWindowCapacity(
                from.ThroughputCapacityPerSecond);
        double toCapacity =
            GetWindowCapacity(
                to.ThroughputCapacityPerSecond);

        double edgeLoad =
            GetLoad(_edgeLoad, edge.Id);
        double fromLoad =
            GetLoad(_nodeLoad, from.Id);
        double toLoad =
            GetLoad(_nodeLoad, to.Id);

        if (edgeLoad + requestedQuantity >
                edgeCapacity + QuantityEpsilon ||
            fromLoad + requestedQuantity >
                fromCapacity + QuantityEpsilon ||
            toLoad + requestedQuantity >
                toCapacity + QuantityEpsilon)
        {
            additionalCost = double.PositiveInfinity;
            return false;
        }

        double edgeUtilization =
            ClampUtilization(
                edgeLoad + requestedQuantity,
                edgeCapacity);
        double nodeUtilization =
            Math.Max(
                ClampUtilization(
                    fromLoad + requestedQuantity,
                    fromCapacity),
                ClampUtilization(
                    toLoad + requestedQuantity,
                    toCapacity));

        additionalCost =
            _congestionCostScale *
            (edgeUtilization * edgeUtilization +
             nodeUtilization * nodeUtilization);

        return true;
    }

    public LogisticsCapacityDebugSnapshot CaptureDebugSnapshot(
        LogisticsNetwork network,
        SimulationTick tick,
        int backlogRequestCount = 0,
        double backlogQuantity = 0.0,
        IReadOnlySet<LogisticsNodeId>? nodeScope = null)
    {
        ArgumentNullException.ThrowIfNull(network);

        if (backlogRequestCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(backlogRequestCount));
        }

        if (!double.IsFinite(backlogQuantity) ||
            backlogQuantity < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(backlogQuantity));
        }

        Advance(network, tick);

        IReadOnlyList<LogisticsNode> nodes = network.GetNodes();
        IReadOnlyList<LogisticsEdge> edges = network.GetEdges();

        var nodeModels =
            new List<LogisticsNodeCapacityReadModel>(nodes.Count);
        var includedNodes = new HashSet<LogisticsNodeId>();

        int healthyNodes = 0;
        int busyNodes = 0;
        int saturatedNodes = 0;
        int blockedNodes = 0;

        for (int index = 0; index < nodes.Count; index++)
        {
            LogisticsNode node = nodes[index];

            if (nodeScope is not null &&
                !nodeScope.Contains(node.Id))
            {
                continue;
            }

            includedNodes.Add(node.Id);

            double capacityPerSecond =
                node.ThroughputCapacityPerSecond;
            double windowCapacity =
                GetWindowCapacity(capacityPerSecond);
            double load = GetLoad(_nodeLoad, node.Id);
            double utilization =
                ClampUtilization(load, windowCapacity);
            LogisticsLoadState state =
                ResolveState(node.Enabled, utilization);

            CountState(
                state,
                ref healthyNodes,
                ref busyNodes,
                ref saturatedNodes,
                ref blockedNodes);

            nodeModels.Add(
                new LogisticsNodeCapacityReadModel(
                    node.Id,
                    node.Kind,
                    node.WorldPosition,
                    node.Enabled,
                    capacityPerSecond,
                    windowCapacity,
                    load,
                    utilization,
                    state));
        }

        var edgeModels =
            new List<LogisticsEdgeCapacityReadModel>(edges.Count);

        int healthyEdges = 0;
        int busyEdges = 0;
        int saturatedEdges = 0;
        int blockedEdges = 0;

        for (int index = 0; index < edges.Count; index++)
        {
            LogisticsEdge edge = edges[index];

            if (nodeScope is not null &&
                (!includedNodes.Contains(edge.Source) ||
                 !includedNodes.Contains(edge.Destination)))
            {
                continue;
            }

            bool enabled =
                edge.Enabled &&
                network.TryGetNode(
                    edge.Source,
                    out LogisticsNode source) &&
                source.Enabled &&
                network.TryGetNode(
                    edge.Destination,
                    out LogisticsNode destination) &&
                destination.Enabled;

            double windowCapacity =
                GetWindowCapacity(edge.CapacityPerSecond);
            double load = GetLoad(_edgeLoad, edge.Id);
            double utilization =
                ClampUtilization(load, windowCapacity);
            LogisticsLoadState state =
                ResolveState(enabled, utilization);

            CountState(
                state,
                ref healthyEdges,
                ref busyEdges,
                ref saturatedEdges,
                ref blockedEdges);

            edgeModels.Add(
                new LogisticsEdgeCapacityReadModel(
                    edge.Id,
                    edge.Source,
                    edge.Destination,
                    network.TryGetNode(
                        edge.Source,
                        out LogisticsNode sourceNode)
                        ? sourceNode.WorldPosition
                        : default,
                    network.TryGetNode(
                        edge.Destination,
                        out LogisticsNode destinationNode)
                        ? destinationNode.WorldPosition
                        : default,
                    enabled,
                    edge.CapacityPerSecond,
                    windowCapacity,
                    load,
                    utilization,
                    state));
        }

        var health =
            new LogisticsHealthSummary(
                nodeModels.Count,
                edgeModels.Count,
                healthyNodes,
                busyNodes,
                saturatedNodes,
                blockedNodes,
                healthyEdges,
                busyEdges,
                saturatedEdges,
                blockedEdges,
                backlogRequestCount,
                backlogQuantity,
                _deniedReservationCount);

        return new LogisticsCapacityDebugSnapshot(
            tick,
            health,
            nodeModels.ToArray(),
            edgeModels.ToArray());
    }

    private bool TryValidateRouteCapacity(
        LogisticsNetwork network,
        LogisticsRoute route,
        double quantity,
        out LogisticsCapacityBottleneck bottleneck)
    {
        if (!network.TryGetNode(
                route.Source,
                out LogisticsNode source))
        {
            bottleneck = LogisticsCapacityBottleneck.None;
            return false;
        }

        LogisticsNode current = source;

        for (int index = 0;
             index < route.Segments.Count;
             index++)
        {
            LogisticsRouteSegment segment = route.Segments[index];

            if (!network.TryGetEdge(
                    segment.EdgeId,
                    out LogisticsEdge edge) ||
                !network.TryGetNode(
                    segment.To,
                    out LogisticsNode next))
            {
                bottleneck = LogisticsCapacityBottleneck.None;
                return false;
            }

            if (!TryValidateTraversalCapacity(
                    edge,
                    current,
                    next,
                    quantity,
                    out bottleneck))
            {
                return false;
            }

            current = next;
        }

        bottleneck = LogisticsCapacityBottleneck.None;
        return true;
    }

    private bool TryValidateTraversalCapacity(
        in LogisticsEdge edge,
        in LogisticsNode from,
        in LogisticsNode to,
        double quantity,
        out LogisticsCapacityBottleneck bottleneck)
    {
        double edgeCapacity =
            GetWindowCapacity(edge.CapacityPerSecond);
        double edgeLoad =
            GetLoad(_edgeLoad, edge.Id);

        if (!edge.Enabled ||
            edgeLoad + quantity >
                edgeCapacity + QuantityEpsilon)
        {
            bottleneck =
                new LogisticsCapacityBottleneck(
                    LogisticsCapacityBottleneckKind.Edge,
                    edge.Id,
                    LogisticsNodeId.None,
                    edgeLoad,
                    quantity,
                    edgeCapacity,
                    ClampUtilization(
                        edgeLoad,
                        edgeCapacity));
            return false;
        }

        if (!TryValidateNodeCapacity(
                from,
                quantity,
                out bottleneck))
        {
            return false;
        }

        if (!TryValidateNodeCapacity(
                to,
                quantity,
                out bottleneck))
        {
            return false;
        }

        bottleneck = LogisticsCapacityBottleneck.None;
        return true;
    }

    private bool TryValidateNodeCapacity(
        in LogisticsNode node,
        double quantity,
        out LogisticsCapacityBottleneck bottleneck)
    {
        double capacity =
            GetWindowCapacity(
                node.ThroughputCapacityPerSecond);
        double load =
            GetLoad(_nodeLoad, node.Id);

        if (!node.Enabled ||
            load + quantity >
                capacity + QuantityEpsilon)
        {
            bottleneck =
                new LogisticsCapacityBottleneck(
                    LogisticsCapacityBottleneckKind.Node,
                    LogisticsEdgeId.None,
                    node.Id,
                    load,
                    quantity,
                    capacity,
                    ClampUtilization(load, capacity));
            return false;
        }

        bottleneck = LogisticsCapacityBottleneck.None;
        return true;
    }

    private bool ReservationTopologyRemainsValid(
        LogisticsNetwork network,
        ReservationState reservation)
    {
        for (int index = 0;
             index < reservation.EdgeIds.Length;
             index++)
        {
            if (!network.TryGetEdge(
                    reservation.EdgeIds[index],
                    out LogisticsEdge edge) ||
                !edge.Enabled)
            {
                return false;
            }
        }

        for (int index = 0;
             index < reservation.NodeIds.Length;
             index++)
        {
            if (!network.TryGetNode(
                    reservation.NodeIds[index],
                    out LogisticsNode node) ||
                !node.Enabled)
            {
                return false;
            }
        }

        return true;
    }

    private bool RemoveReservation(
        LogisticsThroughputReservationId reservationId)
    {
        if (!_reservations.Remove(
                reservationId,
                out ReservationState reservation))
        {
            return false;
        }

        for (int index = 0;
             index < reservation.EdgeIds.Length;
             index++)
        {
            RemoveLoad(
                _edgeLoad,
                reservation.EdgeIds[index],
                reservation.Quantity);
        }

        for (int index = 0;
             index < reservation.NodeIds.Length;
             index++)
        {
            RemoveLoad(
                _nodeLoad,
                reservation.NodeIds[index],
                reservation.Quantity);
        }

        IncrementRevision();
        return true;
    }

    private double GetWindowCapacity(double capacityPerSecond) =>
        capacityPerSecond * _windowSeconds;

    private LogisticsLoadState ResolveState(
        bool enabled,
        double utilization)
    {
        if (!enabled)
        {
            return LogisticsLoadState.Blocked;
        }

        if (utilization >= _saturatedThreshold)
        {
            return LogisticsLoadState.Saturated;
        }

        if (utilization >= _busyThreshold)
        {
            return LogisticsLoadState.Busy;
        }

        return LogisticsLoadState.Healthy;
    }

    private static double ClampUtilization(
        double load,
        double capacity)
    {
        if (capacity <= QuantityEpsilon)
        {
            return load <= QuantityEpsilon
                ? 0.0
                : 1.0;
        }

        return Math.Clamp(load / capacity, 0.0, 1.0);
    }

    private static double GetLoad<TKey>(
        Dictionary<TKey, double> loads,
        TKey key)
        where TKey : notnull =>
        loads.TryGetValue(key, out double load)
            ? load
            : 0.0;

    private static void AddLoad<TKey>(
        Dictionary<TKey, double> loads,
        TKey key,
        double quantity)
        where TKey : notnull
    {
        loads[key] =
            GetLoad(loads, key) + quantity;
    }

    private static void RemoveLoad<TKey>(
        Dictionary<TKey, double> loads,
        TKey key,
        double quantity)
        where TKey : notnull
    {
        if (!loads.TryGetValue(key, out double existing))
        {
            return;
        }

        double remaining = existing - quantity;

        if (remaining <= QuantityEpsilon)
        {
            loads.Remove(key);
        }
        else
        {
            loads[key] = remaining;
        }
    }

    private static void CountState(
        LogisticsLoadState state,
        ref int healthy,
        ref int busy,
        ref int saturated,
        ref int blocked)
    {
        switch (state)
        {
            case LogisticsLoadState.Healthy:
                healthy++;
                break;
            case LogisticsLoadState.Busy:
                busy++;
                break;
            case LogisticsLoadState.Saturated:
                saturated++;
                break;
            case LogisticsLoadState.Blocked:
                blocked++;
                break;
        }
    }

    private void IncrementRevision()
    {
        _revision = checked(_revision + 1);
        if (_revision == 0)
        {
            throw new InvalidOperationException(
                "Logistics capacity revision space is exhausted.");
        }
    }

    private sealed class ReservationState
    {
        public ReservationState(
            LogisticsThroughputReservationId id,
            LogisticsEdgeId[] edgeIds,
            LogisticsNodeId[] nodeIds,
            double quantity,
            SimulationTick createdAtTick,
            SimulationTick expiresAtTick)
        {
            Id = id;
            EdgeIds = edgeIds;
            NodeIds = nodeIds;
            Quantity = quantity;
            CreatedAtTick = createdAtTick;
            ExpiresAtTick = expiresAtTick;
        }

        public LogisticsThroughputReservationId Id { get; }

        public LogisticsEdgeId[] EdgeIds { get; }

        public LogisticsNodeId[] NodeIds { get; }

        public double Quantity { get; }

        public SimulationTick CreatedAtTick { get; }

        public SimulationTick ExpiresAtTick { get; }
    }
}
