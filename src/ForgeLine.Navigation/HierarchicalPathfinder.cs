using System.Collections.Concurrent;
using System.Numerics;

namespace ForgeLine.Navigation;

public sealed class HierarchicalPathfinder
{
    private readonly ConcurrentDictionary<
        HighLevelRouteCacheKey,
        Lazy<HighLevelRouteCacheEntry>> _highLevelCache = new();
    private NavigationWorld _world;
    private readonly int _maximumFallbackExpandedNodes;

    public HierarchicalPathfinder(NavigationWorld world, int maximumFallbackExpandedNodes = 16_384)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFallbackExpandedNodes);
        _maximumFallbackExpandedNodes = maximumFallbackExpandedNodes;
        _world = world ??
            throw new ArgumentNullException(nameof(world));
    }

    public NavigationWorld World => Volatile.Read(ref _world);

    public int HighLevelCacheEntryCount => _highLevelCache.Count;

    public void UpdateWorld(NavigationWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        Volatile.Write(ref _world, world);
        _highLevelCache.Clear();
    }

    public NavigationSearchResult FindPath(
        Vector3 start,
        Vector3 destination,
        in NavigationCapabilities capabilities,
        bool projectBlockedEndpoints = false)
    {
        NavigationWorld world = World;
        NavigationGrid grid = world.Grid;
        Vector3 requestedDestination = destination;
        bool projectedStart = false;

        if (!grid.TryWorldToCell(
                start,
                out NavigationCellCoordinate startCell))
        {
            return Failure(
                world.Version,
                NavigationFailureReason.StartOutsideWorld);
        }

        if (!grid.TryWorldToCell(
                destination,
                out NavigationCellCoordinate destinationCell))
        {
            return Failure(
                world.Version,
                NavigationFailureReason.DestinationOutsideWorld);
        }

        if (!grid.IsTraversable(startCell, capabilities))
        {
            if (!projectBlockedEndpoints ||
                !TryProjectEndpoint(grid, start, capabilities, out startCell))
            {
                return Failure(world.Version, NavigationFailureReason.StartBlocked);
            }

            projectedStart = true;
        }

        if (!grid.IsTraversable(destinationCell, capabilities))
        {
            if (!projectBlockedEndpoints ||
                !TryProjectEndpoint(grid, destination, capabilities, out destinationCell))
            {
                return Failure(world.Version, NavigationFailureReason.DestinationBlocked);
            }

            destination = grid.GetCellCenter(destinationCell);
        }

        NavigationSectorGraph graph =
            world.GetSectorGraph(capabilities);
        NavigationSectorCoordinate startSector =
            graph.GetSector(startCell);
        NavigationSectorCoordinate destinationSector =
            graph.GetSector(destinationCell);

        var cacheKey = new HighLevelRouteCacheKey(
            world.Version,
            capabilities,
            startSector,
            destinationSector);

        bool cacheHit = true;
        if (!_highLevelCache.TryGetValue(cacheKey, out var cached))
        {
            cached = GetOrCreateHighLevelRoute(graph, cacheKey, out cacheHit);
        }

        HighLevelRouteCacheEntry highLevel = cached.Value;

        if (!highLevel.Found ||
            highLevel.Route is null)
        {
            return Failure(
                world.Version,
                NavigationFailureReason.NoHighLevelRoute);
        }

        LocalPathSearchResult local;

        bool refined = LocalPathSearch.TryFindPath(
            grid,
            graph,
            highLevel.Route,
            startCell,
            destinationCell,
            capabilities,
            corridorExpansion: 0,
            out local);
        int localExpanded = local.ExpandedNodeCount;

        if (!refined)
        {
            refined = LocalPathSearch.TryFindPath(
                grid,
                graph,
                highLevel.Route,
                startCell,
                destinationCell,
                capabilities,
                corridorExpansion: 1,
                out local);
            localExpanded += local.ExpandedNodeCount;
        }

        if (!refined)
        {
            // Only failed corridors enter the bounded general search. Use the
            // same topology, diagonal clearance and deterministic frontier.
            refined = LocalPathSearch.TryFindPath(
                grid, graph, highLevel.Route, startCell, destinationCell,
                capabilities, corridorExpansion: 0, out local,
                maximumExpandedNodes: _maximumFallbackExpandedNodes,
                restrictToCorridor: false);
            localExpanded += local.ExpandedNodeCount;
        }

        if (!refined)
        {
            return Failure(world.Version, local.BudgetExhausted
                ? NavigationFailureReason.SearchBudgetExhausted
                : NavigationFailureReason.NoLocalRoute);
        }

        Vector3[] waypoints =
            CreateWaypoints(
                grid,
                local.Cells,
                destination);
        if (projectedStart)
        {
            waypoints = [grid.GetCellCenter(startCell), .. waypoints];
        }
        float length =
            CalculateRouteLength(start, waypoints);

        var diagnostics = new NavigationPathDiagnostics(
            highLevel.Route.ExpandedNodeCount,
            localExpanded,
            local.Cells.Length,
            length,
            cacheHit);

        var path = new NavigationPath(
            world.Version,
            capabilities.MovementClass,
            requestedDestination,
            highLevel.Route.Sectors.ToArray(),
            highLevel.Route.Portals.ToArray(),
            local.Cells,
            waypoints,
            diagnostics);

        return new NavigationSearchResult(
            world.Version,
            NavigationFailureReason.None,
            path);
    }

    private Lazy<HighLevelRouteCacheEntry> GetOrCreateHighLevelRoute(
        NavigationSectorGraph graph,
        HighLevelRouteCacheKey key,
        out bool cacheHit)
    {
        // Keep closure creation on the miss path. Concurrent misses still
        // publish one lazy entry and evaluate its graph search only once.
        var candidate = new Lazy<HighLevelRouteCacheEntry>(
            () => CreateHighLevelRouteEntry(graph, key),
            LazyThreadSafetyMode.ExecutionAndPublication);
        Lazy<HighLevelRouteCacheEntry> cached =
            _highLevelCache.GetOrAdd(key, candidate);
        cacheHit = !ReferenceEquals(candidate, cached);
        return cached;
    }

    private static bool TryProjectEndpoint(
        NavigationGrid grid,
        Vector3 position,
        in NavigationCapabilities capabilities,
        out NavigationCellCoordinate projected)
    {
        projected = default;
        if (!grid.TryWorldToCell(position, out NavigationCellCoordinate origin))
        {
            return false;
        }

        float bestDistance = float.PositiveInfinity;
        for (int z = origin.Z - 2; z <= origin.Z + 2; z++)
        {
            for (int x = origin.X - 2; x <= origin.X + 2; x++)
            {
                var candidate = new NavigationCellCoordinate(x, z);
                if (!grid.IsTraversable(candidate, capabilities))
                {
                    continue;
                }

                Vector3 delta = grid.GetCellCenter(candidate) - position;
                float distance = delta.X * delta.X + delta.Z * delta.Z;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    projected = candidate;
                }
            }
        }

        return float.IsFinite(bestDistance);
    }

    private static Vector3[] CreateWaypoints(
        NavigationGrid grid,
        NavigationCellCoordinate[] cells,
        Vector3 destination)
    {
        if (cells.Length <= 1)
        {
            return [destination];
        }

        var waypoints = new Vector3[cells.Length - 1];

        for (int index = 1; index < cells.Length; index++)
        {
            waypoints[index - 1] =
                index == cells.Length - 1
                    ? destination
                    : grid.GetCellCenter(cells[index]);
        }

        return waypoints;
    }

    private static float CalculateRouteLength(
        Vector3 start,
        ReadOnlySpan<Vector3> waypoints)
    {
        Vector3 previous = start;
        float total = 0.0f;

        for (int index = 0; index < waypoints.Length; index++)
        {
            Vector3 current = waypoints[index];
            Vector2 delta = new(
                current.X - previous.X,
                current.Z - previous.Z);
            total += delta.Length();
            previous = current;
        }

        return total;
    }

    private static HighLevelRouteCacheEntry CreateHighLevelRouteEntry(
        NavigationSectorGraph graph,
        HighLevelRouteCacheKey key)
    {
        bool found =
            HighLevelRouteSearch.TryFindRoute(
                graph,
                key.Start,
                key.Destination,
                out HighLevelRoute route);

        return new HighLevelRouteCacheEntry(
            found,
            found ? route : null);
    }

    private static NavigationSearchResult Failure(
        NavigationVersion version,
        NavigationFailureReason reason)
    {
        return new NavigationSearchResult(
            version,
            reason,
            Path: null);
    }

    private readonly record struct HighLevelRouteCacheKey(
        NavigationVersion Version,
        NavigationCapabilities Capabilities,
        NavigationSectorCoordinate Start,
        NavigationSectorCoordinate Destination);

    private readonly record struct HighLevelRouteCacheEntry(
        bool Found,
        HighLevelRoute? Route);
}
