using ForgeLine.Core;

namespace ForgeLine.Logistics;

public sealed class LogisticsNetwork
{
    private readonly Dictionary<LogisticsNodeId, LogisticsNode> _nodes = new();
    private readonly Dictionary<EntityId, LogisticsNodeId> _nodesByEntity = new();
    private readonly Dictionary<LogisticsEdgeId, LogisticsEdge> _edges = new();
    private readonly Dictionary<LogisticsNodeId, List<LogisticsEdgeId>> _adjacency = new();
    private readonly Dictionary<RouteCacheKey, LogisticsRoute> _routeCache = new();
    private ulong _nextNodeId;
    private ulong _nextEdgeId;
    private ulong _version = LogisticsNetworkVersion.Initial.Value;
    private long _routeRequestCount;
    private long _failedRouteCount;
    private long _routeCacheHitCount;
    private double _lastRouteCost;
    private double _lastRouteLengthMeters;

    public LogisticsNetworkVersion Version => new(_version);

    public int NodeCount => _nodes.Count;

    public int EdgeCount => _edges.Count;

    public int RouteCacheEntryCount => _routeCache.Count;

    public LogisticsNetworkMetrics Metrics =>
        new(
            _nodes.Count,
            CountEnabledNodes(),
            _edges.Count,
            CountEnabledEdges(),
            CalculateConnectedComponentCount(),
            _routeRequestCount,
            _failedRouteCount,
            _routeCacheHitCount,
            _lastRouteCost,
            _lastRouteLengthMeters);

    public LogisticsNodeId AddNode(
        EntityId entity,
        System.Numerics.Vector3 worldPosition,
        LogisticsNodeKind kind,
        LogisticsNodeCapabilities capabilities,
        bool enabled = true)
    {
        if (_nodesByEntity.ContainsKey(entity))
        {
            throw new InvalidOperationException(
                $"Entity {entity} is already registered as a logistics node.");
        }

        LogisticsNodeId id = AllocateNodeId();
        var node = new LogisticsNode(
            id,
            entity,
            worldPosition,
            kind,
            capabilities,
            enabled);

        _nodes.Add(id, node);
        _nodesByEntity.Add(entity, id);
        _adjacency.Add(id, new List<LogisticsEdgeId>());
        Invalidate();
        return id;
    }

    public bool UpdateNode(
        LogisticsNodeId id,
        System.Numerics.Vector3 worldPosition,
        LogisticsNodeKind kind,
        LogisticsNodeCapabilities capabilities,
        bool enabled = true)
    {
        if (!_nodes.TryGetValue(id, out LogisticsNode existing))
        {
            return false;
        }

        var updated = new LogisticsNode(
            id,
            existing.Entity,
            worldPosition,
            kind,
            capabilities,
            enabled);

        if (updated == existing)
        {
            return false;
        }

        _nodes[id] = updated;
        Invalidate();
        return true;
    }

    public bool RemoveNode(LogisticsNodeId id)
    {
        if (!_nodes.TryGetValue(id, out LogisticsNode node))
        {
            return false;
        }

        LogisticsEdgeId[] incidentEdges =
            _adjacency[id].ToArray();

        for (int index = 0; index < incidentEdges.Length; index++)
        {
            RemoveEdgeInternal(incidentEdges[index]);
        }

        _adjacency.Remove(id);
        _nodes.Remove(id);
        _nodesByEntity.Remove(node.Entity);
        Invalidate();
        return true;
    }

    public bool TryGetNode(
        LogisticsNodeId id,
        out LogisticsNode node) =>
        _nodes.TryGetValue(id, out node);

    public bool TryGetNodeForEntity(
        EntityId entity,
        out LogisticsNodeId nodeId) =>
        _nodesByEntity.TryGetValue(entity, out nodeId);

    public IReadOnlyList<LogisticsNode> GetNodes()
    {
        LogisticsNode[] nodes = _nodes.Values.ToArray();
        Array.Sort(
            nodes,
            static (left, right) =>
                left.Id.CompareTo(right.Id));
        return nodes;
    }

    public LogisticsEdgeId AddEdge(
        LogisticsNodeId source,
        LogisticsNodeId destination,
        LogisticsTransportMode mode,
        double distanceMeters,
        double baseCost,
        double capacityPerSecond,
        bool bidirectional = true,
        bool enabled = true,
        double congestionPenalty = 0.0,
        double threatPenalty = 0.0)
    {
        RequireExistingNode(source, nameof(source));
        RequireExistingNode(destination, nameof(destination));

        LogisticsEdgeId id = AllocateEdgeId();
        var edge = new LogisticsEdge(
            id,
            source,
            destination,
            mode,
            distanceMeters,
            baseCost,
            capacityPerSecond,
            bidirectional,
            enabled,
            congestionPenalty,
            threatPenalty);

        _edges.Add(id, edge);
        AddAdjacency(source, id);
        AddAdjacency(destination, id);
        Invalidate();
        return id;
    }

    public bool UpdateEdge(
        LogisticsEdgeId id,
        LogisticsTransportMode mode,
        double distanceMeters,
        double baseCost,
        double capacityPerSecond,
        bool bidirectional,
        bool enabled,
        double congestionPenalty = 0.0,
        double threatPenalty = 0.0)
    {
        if (!_edges.TryGetValue(id, out LogisticsEdge existing))
        {
            return false;
        }

        var updated = new LogisticsEdge(
            id,
            existing.Source,
            existing.Destination,
            mode,
            distanceMeters,
            baseCost,
            capacityPerSecond,
            bidirectional,
            enabled,
            congestionPenalty,
            threatPenalty);

        if (updated == existing)
        {
            return false;
        }

        _edges[id] = updated;
        Invalidate();
        return true;
    }

    public bool SetEdgeEnabled(
        LogisticsEdgeId id,
        bool enabled)
    {
        if (!_edges.TryGetValue(id, out LogisticsEdge edge))
        {
            return false;
        }

        return UpdateEdge(
            id,
            edge.Mode,
            edge.DistanceMeters,
            edge.BaseCost,
            edge.CapacityPerSecond,
            edge.Bidirectional,
            enabled,
            edge.CongestionPenalty,
            edge.ThreatPenalty);
    }

    public bool RemoveEdge(LogisticsEdgeId id)
    {
        if (!RemoveEdgeInternal(id))
        {
            return false;
        }

        Invalidate();
        return true;
    }

    public bool TryGetEdge(
        LogisticsEdgeId id,
        out LogisticsEdge edge) =>
        _edges.TryGetValue(id, out edge);

    public IReadOnlyList<LogisticsEdge> GetEdges()
    {
        LogisticsEdge[] edges = _edges.Values.ToArray();
        Array.Sort(
            edges,
            static (left, right) =>
                left.Id.CompareTo(right.Id));
        return edges;
    }

    public LogisticsNetworkVersion InvalidateRoutes()
    {
        Invalidate();
        return Version;
    }

    public bool IsReachable(
        LogisticsNodeId source,
        LogisticsNodeId destination) =>
        IsReachable(
            source,
            destination,
            LogisticsRouteCostPolicy.Default);

    public bool IsReachable(
        LogisticsNodeId source,
        LogisticsNodeId destination,
        LogisticsRouteCostPolicy policy)
    {
        if (!_nodes.TryGetValue(source, out LogisticsNode sourceNode) ||
            !_nodes.TryGetValue(destination, out LogisticsNode destinationNode) ||
            !sourceNode.Enabled ||
            !destinationNode.Enabled)
        {
            return false;
        }

        if (source == destination)
        {
            return true;
        }

        var visited = new HashSet<LogisticsNodeId>
        {
            source
        };
        var pending = new Queue<LogisticsNodeId>();
        pending.Enqueue(source);

        while (pending.TryDequeue(out LogisticsNodeId current))
        {
            List<LogisticsEdgeId> adjacency = _adjacency[current];

            for (int index = 0; index < adjacency.Count; index++)
            {
                LogisticsEdge edge = _edges[adjacency[index]];
                if (!edge.CanTraverseFrom(current) ||
                    !policy.Allows(edge))
                {
                    continue;
                }

                LogisticsNodeId next = edge.GetOther(current);
                if (!_nodes[next].Enabled ||
                    !visited.Add(next))
                {
                    continue;
                }

                if (next == destination)
                {
                    return true;
                }

                pending.Enqueue(next);
            }
        }

        return false;
    }

    public LogisticsRouteSearchResult FindRoute(
        LogisticsNodeId source,
        LogisticsNodeId destination) =>
        FindRoute(
            source,
            destination,
            LogisticsRouteCostPolicy.Default);

    public LogisticsRouteSearchResult FindRoute(
        LogisticsNodeId source,
        LogisticsNodeId destination,
        LogisticsRouteCostPolicy policy)
    {
        _routeRequestCount++;

        if (!_nodes.TryGetValue(source, out LogisticsNode sourceNode))
        {
            return Failed(
                LogisticsRouteFailureReason.SourceNodeNotFound);
        }

        if (!_nodes.TryGetValue(
                destination,
                out LogisticsNode destinationNode))
        {
            return Failed(
                LogisticsRouteFailureReason.DestinationNodeNotFound);
        }

        if (!sourceNode.Enabled)
        {
            return Failed(
                LogisticsRouteFailureReason.SourceNodeDisabled);
        }

        if (!destinationNode.Enabled)
        {
            return Failed(
                LogisticsRouteFailureReason.DestinationNodeDisabled);
        }

        var cacheKey = new RouteCacheKey(
            source,
            destination,
            policy);

        if (_routeCache.TryGetValue(
                cacheKey,
                out LogisticsRoute? cached))
        {
            _routeCacheHitCount++;
            _lastRouteCost = cached.TotalCost;
            _lastRouteLengthMeters =
                cached.TotalDistanceMeters;

            return new LogisticsRouteSearchResult(
                Version,
                LogisticsRouteFailureReason.None,
                cached,
                new LogisticsRouteDiagnostics(
                    0,
                    0,
                    CacheHit: true));
        }

        if (source == destination)
        {
            var route = new LogisticsRoute(
                Version,
                source,
                destination,
                policy,
                [],
                totalCost: 0.0,
                totalDistanceMeters: 0.0);

            _routeCache.Add(cacheKey, route);
            _lastRouteCost = 0.0;
            _lastRouteLengthMeters = 0.0;

            return new LogisticsRouteSearchResult(
                Version,
                LogisticsRouteFailureReason.None,
                route,
                new LogisticsRouteDiagnostics(
                    1,
                    0,
                    CacheHit: false));
        }

        var frontier = new PriorityQueue<
            LogisticsNodeId,
            RoutePriority>();
        var bestCost = new Dictionary<
            LogisticsNodeId,
            double>
        {
            [source] = 0.0
        };
        var previous = new Dictionary<
            LogisticsNodeId,
            PreviousStep>();

        frontier.Enqueue(
            source,
            new RoutePriority(0.0, source.Value));

        int expandedNodes = 0;
        int consideredEdges = 0;

        while (frontier.TryDequeue(
                   out LogisticsNodeId current,
                   out RoutePriority priority))
        {
            if (!bestCost.TryGetValue(
                    current,
                    out double currentBest) ||
                currentBest != priority.Cost)
            {
                continue;
            }

            expandedNodes++;

            if (current == destination)
            {
                LogisticsRoute route = ReconstructRoute(
                    source,
                    destination,
                    policy,
                    previous,
                    currentBest);

                _routeCache.Add(cacheKey, route);
                _lastRouteCost = route.TotalCost;
                _lastRouteLengthMeters =
                    route.TotalDistanceMeters;

                return new LogisticsRouteSearchResult(
                    Version,
                    LogisticsRouteFailureReason.None,
                    route,
                    new LogisticsRouteDiagnostics(
                        expandedNodes,
                        consideredEdges,
                        CacheHit: false));
            }

            List<LogisticsEdgeId> adjacency =
                _adjacency[current];

            for (int index = 0; index < adjacency.Count; index++)
            {
                LogisticsEdge edge =
                    _edges[adjacency[index]];
                consideredEdges++;

                if (!edge.CanTraverseFrom(current) ||
                    !policy.Allows(edge))
                {
                    continue;
                }

                LogisticsNodeId next = edge.GetOther(current);
                if (!_nodes[next].Enabled)
                {
                    continue;
                }

                double edgeCost = policy.Evaluate(edge);
                double candidate = currentBest + edgeCost;

                if (bestCost.TryGetValue(
                        next,
                        out double known) &&
                    candidate >= known)
                {
                    continue;
                }

                bestCost[next] = candidate;
                previous[next] =
                    new PreviousStep(current, edge.Id);

                frontier.Enqueue(
                    next,
                    new RoutePriority(candidate, next.Value));
            }
        }

        return Failed(
            LogisticsRouteFailureReason.NoRoute,
            expandedNodes,
            consideredEdges);
    }

    private LogisticsRoute ReconstructRoute(
        LogisticsNodeId source,
        LogisticsNodeId destination,
        LogisticsRouteCostPolicy policy,
        Dictionary<LogisticsNodeId, PreviousStep> previous,
        double totalCost)
    {
        var reversed = new List<LogisticsRouteSegment>();
        double totalDistance = 0.0;
        LogisticsNodeId current = destination;

        while (current != source)
        {
            PreviousStep step = previous[current];
            LogisticsEdge edge = _edges[step.EdgeId];
            double edgeCost = policy.Evaluate(edge);

            reversed.Add(
                new LogisticsRouteSegment(
                    edge.Id,
                    step.PreviousNode,
                    current,
                    edge.Mode,
                    edge.DistanceMeters,
                    edgeCost,
                    edge.CapacityPerSecond));

            totalDistance += edge.DistanceMeters;
            current = step.PreviousNode;
        }

        reversed.Reverse();

        return new LogisticsRoute(
            Version,
            source,
            destination,
            policy,
            reversed.ToArray(),
            totalCost,
            totalDistance);
    }

    private LogisticsRouteSearchResult Failed(
        LogisticsRouteFailureReason reason,
        int expandedNodes = 0,
        int consideredEdges = 0)
    {
        _failedRouteCount++;

        return new LogisticsRouteSearchResult(
            Version,
            reason,
            null,
            new LogisticsRouteDiagnostics(
                expandedNodes,
                consideredEdges,
                CacheHit: false));
    }

    private LogisticsNodeId AllocateNodeId()
    {
        ulong value = checked(_nextNodeId + 1);
        if (value == 0)
        {
            throw new InvalidOperationException(
                "Logistics node identifier space is exhausted.");
        }

        _nextNodeId = value;
        return new LogisticsNodeId(value);
    }

    private LogisticsEdgeId AllocateEdgeId()
    {
        ulong value = checked(_nextEdgeId + 1);
        if (value == 0)
        {
            throw new InvalidOperationException(
                "Logistics edge identifier space is exhausted.");
        }

        _nextEdgeId = value;
        return new LogisticsEdgeId(value);
    }

    private void RequireExistingNode(
        LogisticsNodeId nodeId,
        string parameterName)
    {
        if (!nodeId.IsSpecified ||
            !_nodes.ContainsKey(nodeId))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Logistics node {nodeId} does not exist.");
        }
    }

    private void AddAdjacency(
        LogisticsNodeId node,
        LogisticsEdgeId edge)
    {
        List<LogisticsEdgeId> adjacency = _adjacency[node];
        adjacency.Add(edge);
        adjacency.Sort();
    }

    private bool RemoveEdgeInternal(LogisticsEdgeId id)
    {
        if (!_edges.Remove(id, out LogisticsEdge edge))
        {
            return false;
        }

        _adjacency[edge.Source].Remove(id);
        _adjacency[edge.Destination].Remove(id);
        return true;
    }

    private void Invalidate()
    {
        ulong next = checked(_version + 1);
        if (next == 0)
        {
            throw new InvalidOperationException(
                "Logistics network version space is exhausted.");
        }

        _version = next;
        _routeCache.Clear();
    }

    private int CountEnabledNodes()
    {
        int count = 0;
        foreach (LogisticsNode node in _nodes.Values)
        {
            if (node.Enabled)
            {
                count++;
            }
        }

        return count;
    }

    private int CountEnabledEdges()
    {
        int count = 0;
        foreach (LogisticsEdge edge in _edges.Values)
        {
            if (edge.Enabled &&
                _nodes[edge.Source].Enabled &&
                _nodes[edge.Destination].Enabled)
            {
                count++;
            }
        }

        return count;
    }

    private int CalculateConnectedComponentCount()
    {
        if (_nodes.Count == 0)
        {
            return 0;
        }

        var visited = new HashSet<LogisticsNodeId>();
        var pending = new Queue<LogisticsNodeId>();
        int componentCount = 0;

        LogisticsNodeId[] orderedNodes =
            _nodes.Keys.ToArray();
        Array.Sort(orderedNodes);

        for (int nodeIndex = 0;
             nodeIndex < orderedNodes.Length;
             nodeIndex++)
        {
            LogisticsNodeId start = orderedNodes[nodeIndex];
            if (!_nodes[start].Enabled ||
                !visited.Add(start))
            {
                continue;
            }

            componentCount++;
            pending.Enqueue(start);

            while (pending.TryDequeue(
                       out LogisticsNodeId current))
            {
                List<LogisticsEdgeId> adjacency =
                    _adjacency[current];

                for (int edgeIndex = 0;
                     edgeIndex < adjacency.Count;
                     edgeIndex++)
                {
                    LogisticsEdge edge =
                        _edges[adjacency[edgeIndex]];
                    if (!edge.Enabled)
                    {
                        continue;
                    }

                    LogisticsNodeId next =
                        edge.Source == current
                            ? edge.Destination
                            : edge.Source;

                    if (_nodes[next].Enabled &&
                        visited.Add(next))
                    {
                        pending.Enqueue(next);
                    }
                }
            }
        }

        return componentCount;
    }

    private readonly record struct RouteCacheKey(
        LogisticsNodeId Source,
        LogisticsNodeId Destination,
        LogisticsRouteCostPolicy Policy);

    private readonly record struct PreviousStep(
        LogisticsNodeId PreviousNode,
        LogisticsEdgeId EdgeId);

    private readonly record struct RoutePriority(
        double Cost,
        ulong NodeValue)
        : IComparable<RoutePriority>
    {
        public int CompareTo(RoutePriority other)
        {
            int costComparison = Cost.CompareTo(other.Cost);
            return costComparison != 0
                ? costComparison
                : NodeValue.CompareTo(other.NodeValue);
        }
    }
}
