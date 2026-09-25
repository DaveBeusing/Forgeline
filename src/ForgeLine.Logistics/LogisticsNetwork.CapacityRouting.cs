namespace ForgeLine.Logistics;

public sealed partial class LogisticsNetwork
{
    public bool SetNodeEnabled(
        LogisticsNodeId id,
        bool enabled)
    {
        if (!_nodes.TryGetValue(id, out LogisticsNode node))
        {
            return false;
        }

        return UpdateNode(
            id,
            node.WorldPosition,
            node.Kind,
            node.Capabilities,
            enabled);
    }

    public LogisticsRouteSearchResult FindCapacityAwareRoute(
        LogisticsNodeId source,
        LogisticsNodeId destination,
        LogisticsRouteCostPolicy policy,
        ILogisticsRouteCostAdjustment adjustment,
        double requestedQuantity)
    {
        ArgumentNullException.ThrowIfNull(adjustment);

        if (!double.IsFinite(requestedQuantity) ||
            requestedQuantity <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedQuantity));
        }

        _routeRequestCount++;

        if (!_nodes.TryGetValue(
                source,
                out LogisticsNode sourceNode))
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

        var frontier =
            new PriorityQueue<
                LogisticsNodeId,
                RoutePriority>();
        var bestCost =
            new Dictionary<
                LogisticsNodeId,
                double>
            {
                [source] = 0.0
            };
        var previous =
            new Dictionary<
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
                LogisticsRoute route =
                    ReconstructCapacityAwareRoute(
                        source,
                        destination,
                        policy,
                        adjustment,
                        requestedQuantity,
                        previous,
                        currentBest);

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

            for (int index = 0;
                 index < adjacency.Count;
                 index++)
            {
                LogisticsEdge edge =
                    _edges[adjacency[index]];
                consideredEdges++;

                if (!edge.CanTraverseFrom(current) ||
                    !policy.Allows(edge))
                {
                    continue;
                }

                LogisticsNodeId next =
                    edge.GetOther(current);
                LogisticsNode nextNode =
                    _nodes[next];

                if (!nextNode.Enabled)
                {
                    continue;
                }

                if (!adjustment.TryEvaluateTraversal(
                        edge,
                        _nodes[current],
                        nextNode,
                        requestedQuantity,
                        out double additionalCost))
                {
                    continue;
                }

                double edgeCost =
                    policy.Evaluate(edge) +
                    additionalCost;
                double candidate =
                    currentBest + edgeCost;

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
                    new RoutePriority(
                        candidate,
                        next.Value));
            }
        }

        return Failed(
            LogisticsRouteFailureReason.NoRoute,
            expandedNodes,
            consideredEdges);
    }

    private LogisticsRoute ReconstructCapacityAwareRoute(
        LogisticsNodeId source,
        LogisticsNodeId destination,
        LogisticsRouteCostPolicy policy,
        ILogisticsRouteCostAdjustment adjustment,
        double requestedQuantity,
        Dictionary<LogisticsNodeId, PreviousStep> previous,
        double totalCost)
    {
        var reversed =
            new List<LogisticsRouteSegment>();
        double totalDistance = 0.0;
        LogisticsNodeId current = destination;

        while (current != source)
        {
            PreviousStep step =
                previous[current];
            LogisticsEdge edge =
                _edges[step.EdgeId];
            LogisticsNode from =
                _nodes[step.PreviousNode];
            LogisticsNode to =
                _nodes[current];

            bool allowed =
                adjustment.TryEvaluateTraversal(
                    edge,
                    from,
                    to,
                    requestedQuantity,
                    out double additionalCost);

            if (!allowed)
            {
                throw new InvalidOperationException(
                    "Capacity-aware route changed while it was being reconstructed.");
            }

            double edgeCost =
                policy.Evaluate(edge) +
                additionalCost;

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
}
