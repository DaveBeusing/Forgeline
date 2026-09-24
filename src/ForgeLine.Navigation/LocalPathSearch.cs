using System.Numerics;

namespace ForgeLine.Navigation;

internal readonly record struct LocalPathSearchResult(
    NavigationCellCoordinate[] Cells,
    int ExpandedNodeCount);

internal static class LocalPathSearch
{
    private static readonly (int X, int Z, float Cost)[] s_neighbors =
    [
        (0, -1, 1.0f),
        (1, 0, 1.0f),
        (0, 1, 1.0f),
        (-1, 0, 1.0f),
        (1, -1, 1.41421356f),
        (1, 1, 1.41421356f),
        (-1, 1, 1.41421356f),
        (-1, -1, 1.41421356f)
    ];

    public static bool TryFindPath(
        NavigationGrid grid,
        NavigationSectorGraph graph,
        HighLevelRoute highLevelRoute,
        NavigationCellCoordinate start,
        NavigationCellCoordinate destination,
        in NavigationCapabilities capabilities,
        int corridorExpansion,
        out LocalPathSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(highLevelRoute);
        ArgumentOutOfRangeException.ThrowIfNegative(corridorExpansion);

        if (start == destination)
        {
            result = new LocalPathSearchResult(
                [start],
                ExpandedNodeCount: 1);
            return true;
        }

        HashSet<NavigationSectorCoordinate> corridor =
            BuildCorridor(
                graph,
                highLevelRoute,
                corridorExpansion);

        int startIndex = grid.GetIndex(start);
        int destinationIndex = grid.GetIndex(destination);

        var frontier = new PriorityQueue<int, float>();
        var bestCost = new Dictionary<int, float>
        {
            [startIndex] = 0.0f
        };
        var previous = new Dictionary<int, int>();

        frontier.Enqueue(
            startIndex,
            Heuristic(start, destination));

        int expanded = 0;

        while (frontier.TryDequeue(out int currentIndex, out _))
        {
            NavigationCellCoordinate current =
                grid.GetCoordinate(currentIndex);
            expanded++;

            if (currentIndex == destinationIndex)
            {
                result = new LocalPathSearchResult(
                    Reconstruct(
                        grid,
                        startIndex,
                        destinationIndex,
                        previous),
                    expanded);
                return true;
            }

            float currentCost = bestCost[currentIndex];

            for (int neighborIndex = 0;
                 neighborIndex < s_neighbors.Length;
                 neighborIndex++)
            {
                (int offsetX, int offsetZ, float moveCost) =
                    s_neighbors[neighborIndex];

                NavigationCellCoordinate next = new(
                    current.X + offsetX,
                    current.Z + offsetZ);

                if (!grid.Contains(next) ||
                    !grid.IsTraversable(next, capabilities) ||
                    !corridor.Contains(graph.GetSector(next)))
                {
                    continue;
                }

                bool diagonal =
                    offsetX != 0 && offsetZ != 0;
                if (diagonal &&
                    !CanTraverseDiagonal(
                        grid,
                        current,
                        offsetX,
                        offsetZ,
                        capabilities))
                {
                    continue;
                }

                int nextIndex = grid.GetIndex(next);
                float traversal =
                    0.5f *
                    (grid.GetTraversalCost(current, capabilities) +
                     grid.GetTraversalCost(next, capabilities));
                float candidate =
                    currentCost + moveCost * traversal;

                if (bestCost.TryGetValue(
                        nextIndex,
                        out float known) &&
                    candidate >= known)
                {
                    continue;
                }

                bestCost[nextIndex] = candidate;
                previous[nextIndex] = currentIndex;

                frontier.Enqueue(
                    nextIndex,
                    candidate + Heuristic(next, destination));
            }
        }

        result = default;
        return false;
    }

    private static HashSet<NavigationSectorCoordinate> BuildCorridor(
        NavigationSectorGraph graph,
        HighLevelRoute route,
        int expansion)
    {
        var corridor =
            new HashSet<NavigationSectorCoordinate>();

        foreach (NavigationSectorCoordinate sector in route.Sectors)
        {
            corridor.Add(sector);

            for (int offsetZ = -expansion;
                 offsetZ <= expansion;
                 offsetZ++)
            {
                for (int offsetX = -expansion;
                     offsetX <= expansion;
                     offsetX++)
                {
                    NavigationSectorCoordinate candidate = new(
                        sector.X + offsetX,
                        sector.Z + offsetZ);

                    if (graph.Contains(candidate))
                    {
                        corridor.Add(candidate);
                    }
                }
            }
        }

        return corridor;
    }

    private static bool CanTraverseDiagonal(
        NavigationGrid grid,
        NavigationCellCoordinate current,
        int offsetX,
        int offsetZ,
        in NavigationCapabilities capabilities)
    {
        NavigationCellCoordinate horizontal =
            new(current.X + offsetX, current.Z);
        NavigationCellCoordinate vertical =
            new(current.X, current.Z + offsetZ);

        return grid.IsTraversable(horizontal, capabilities) &&
               grid.IsTraversable(vertical, capabilities);
    }

    private static NavigationCellCoordinate[] Reconstruct(
        NavigationGrid grid,
        int startIndex,
        int destinationIndex,
        Dictionary<int, int> previous)
    {
        var reversed = new List<NavigationCellCoordinate>();
        int current = destinationIndex;
        reversed.Add(grid.GetCoordinate(current));

        while (current != startIndex)
        {
            current = previous[current];
            reversed.Add(grid.GetCoordinate(current));
        }

        reversed.Reverse();
        return Simplify(reversed);
    }

    private static NavigationCellCoordinate[] Simplify(
        List<NavigationCellCoordinate> path)
    {
        if (path.Count <= 2)
        {
            return path.ToArray();
        }

        var simplified =
            new List<NavigationCellCoordinate>(path.Count);
        simplified.Add(path[0]);

        int previousX = Math.Sign(path[1].X - path[0].X);
        int previousZ = Math.Sign(path[1].Z - path[0].Z);

        for (int index = 1; index < path.Count - 1; index++)
        {
            int nextX =
                Math.Sign(path[index + 1].X - path[index].X);
            int nextZ =
                Math.Sign(path[index + 1].Z - path[index].Z);

            if (nextX != previousX || nextZ != previousZ)
            {
                simplified.Add(path[index]);
                previousX = nextX;
                previousZ = nextZ;
            }
        }

        simplified.Add(path[^1]);
        return simplified.ToArray();
    }

    private static float Heuristic(
        NavigationCellCoordinate from,
        NavigationCellCoordinate to)
    {
        int deltaX = Math.Abs(from.X - to.X);
        int deltaZ = Math.Abs(from.Z - to.Z);
        int diagonal = Math.Min(deltaX, deltaZ);
        int straight = Math.Max(deltaX, deltaZ) - diagonal;

        return diagonal * 1.41421356f + straight;
    }
}
