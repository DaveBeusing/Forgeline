namespace ForgeLine.Navigation;

public sealed class HighLevelRoute
{
    private readonly NavigationSectorCoordinate[] _sectors;
    private readonly NavigationPortal[] _portals;

    internal HighLevelRoute(
        NavigationSectorCoordinate[] sectors,
        NavigationPortal[] portals,
        int expandedNodeCount)
    {
        _sectors = sectors;
        _portals = portals;
        ExpandedNodeCount = expandedNodeCount;
    }

    public IReadOnlyList<NavigationSectorCoordinate> Sectors =>
        _sectors;

    public IReadOnlyList<NavigationPortal> Portals =>
        _portals;

    public int ExpandedNodeCount { get; }
}

public static class HighLevelRouteSearch
{
    public static bool TryFindRoute(
        NavigationSectorGraph graph,
        NavigationSectorCoordinate start,
        NavigationSectorCoordinate destination,
        out HighLevelRoute route)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (!graph.Contains(start) ||
            !graph.Contains(destination))
        {
            route = null!;
            return false;
        }

        if (start == destination)
        {
            route = new HighLevelRoute(
                [start],
                [],
                expandedNodeCount: 1);
            return true;
        }

        var frontier =
            new PriorityQueue<NavigationSectorCoordinate, float>();
        var bestCost = new Dictionary<
            NavigationSectorCoordinate,
            float>
        {
            [start] = 0.0f
        };
        var previous = new Dictionary<
            NavigationSectorCoordinate,
            PreviousStep>();

        frontier.Enqueue(start, Heuristic(start, destination));
        int expanded = 0;

        while (frontier.TryDequeue(
                   out NavigationSectorCoordinate current,
                   out _))
        {
            expanded++;

            if (current == destination)
            {
                route = Reconstruct(
                    start,
                    destination,
                    previous,
                    expanded);
                return true;
            }

            float currentCost = bestCost[current];
            ReadOnlySpan<NavigationSectorEdge> edges =
                graph.GetEdges(current);

            for (int index = 0; index < edges.Length; index++)
            {
                NavigationSectorEdge edge = edges[index];
                float candidate = currentCost + edge.Cost;

                if (bestCost.TryGetValue(
                        edge.To,
                        out float known) &&
                    candidate >= known)
                {
                    continue;
                }

                bestCost[edge.To] = candidate;
                previous[edge.To] =
                    new PreviousStep(current, edge.Portal);

                frontier.Enqueue(
                    edge.To,
                    candidate + Heuristic(edge.To, destination));
            }
        }

        route = null!;
        return false;
    }

    private static HighLevelRoute Reconstruct(
        NavigationSectorCoordinate start,
        NavigationSectorCoordinate destination,
        Dictionary<
            NavigationSectorCoordinate,
            PreviousStep> previous,
        int expanded)
    {
        var sectors = new List<NavigationSectorCoordinate>();
        var portals = new List<NavigationPortal>();

        NavigationSectorCoordinate current = destination;
        sectors.Add(current);

        while (current != start)
        {
            PreviousStep step = previous[current];
            portals.Add(step.Portal);
            current = step.Previous;
            sectors.Add(current);
        }

        sectors.Reverse();
        portals.Reverse();

        return new HighLevelRoute(
            sectors.ToArray(),
            portals.ToArray(),
            expanded);
    }

    private static float Heuristic(
        NavigationSectorCoordinate from,
        NavigationSectorCoordinate to)
    {
        int deltaX = Math.Abs(from.X - to.X);
        int deltaZ = Math.Abs(from.Z - to.Z);
        return deltaX + deltaZ;
    }

    private readonly record struct PreviousStep(
        NavigationSectorCoordinate Previous,
        NavigationPortal Portal);
}
