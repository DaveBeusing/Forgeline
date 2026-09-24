using System.Collections.Concurrent;
using System.Numerics;

namespace ForgeLine.Navigation;

public sealed class HierarchicalPathfinder
{
    private readonly ConcurrentDictionary<
        HighLevelRouteCacheKey,
        Lazy<HighLevelRouteCacheEntry>> _highLevelCache = new();
    private NavigationWorld _world;

    public HierarchicalPathfinder(NavigationWorld world)
    {
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
        in NavigationCapabilities capabilities)
    {
        NavigationWorld world = World;
        NavigationGrid grid = world.Grid;

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
            return Failure(
                world.Version,
                NavigationFailureReason.StartBlocked);
        }

        if (!grid.IsTraversable(destinationCell, capabilities))
        {
            return Failure(
                world.Version,
                NavigationFailureReason.DestinationBlocked);
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

        var candidate =
            new Lazy<HighLevelRouteCacheEntry>(
                () => CreateHighLevelRouteEntry(
                    graph,
                    cacheKey),
                LazyThreadSafetyMode.ExecutionAndPublication);

        Lazy<HighLevelRouteCacheEntry> cached =
            _highLevelCache.GetOrAdd(
                cacheKey,
                candidate);
        bool cacheHit =
            !ReferenceEquals(candidate, cached);

        HighLevelRouteCacheEntry highLevel = cached.Value;

        if (!highLevel.Found ||
            highLevel.Route is null)
        {
            return Failure(
                world.Version,
                NavigationFailureReason.NoHighLevelRoute);
        }

        int localExpanded = 0;
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
        }

        if (!refined)
        {
            return Failure(
                world.Version,
                NavigationFailureReason.NoLocalRoute);
        }

        localExpanded += local.ExpandedNodeCount;

        Vector3[] waypoints =
            CreateWaypoints(
                grid,
                local.Cells,
                destination);
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
            destination,
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
