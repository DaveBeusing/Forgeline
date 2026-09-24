using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Navigation;

public sealed record NavigationSectorSettings
{
    public int SectorSizeCells { get; init; } = 8;

    public void Validate(NavigationGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentOutOfRangeException.ThrowIfLessThan(SectorSizeCells, 1);

        if (grid.CellsPerChunk % SectorSizeCells != 0)
        {
            throw new InvalidOperationException(
                "Navigation sector size must divide the chunk-local navigation grid exactly.");
        }
    }
}

public readonly record struct NavigationSectorCoordinate(int X, int Z)
{
    public override string ToString() => $"({X},{Z})";
}

public readonly record struct NavigationPortal(
    NavigationSectorCoordinate From,
    NavigationSectorCoordinate To,
    NavigationCellCoordinate FromCell,
    NavigationCellCoordinate ToCell,
    Vector3 WorldPosition);

public readonly record struct NavigationSectorEdge(
    NavigationSectorCoordinate To,
    NavigationPortal Portal,
    float Cost);

public sealed class NavigationSectorGraph
{
    private readonly Dictionary<
        NavigationSectorCoordinate,
        NavigationSectorEdge[]> _edges;

    internal NavigationSectorGraph(
        NavigationGrid grid,
        NavigationSectorSettings settings,
        NavigationMovementClass movementClass,
        int width,
        int height,
        Dictionary<
            NavigationSectorCoordinate,
            NavigationSectorEdge[]> edges,
        NavigationPortal[] portals)
    {
        Grid = grid;
        Settings = settings;
        MovementClass = movementClass;
        Width = width;
        Height = height;
        _edges = edges;
        Portals = portals;
    }

    public NavigationGrid Grid { get; }

    public NavigationSectorSettings Settings { get; }

    public NavigationMovementClass MovementClass { get; }

    public int Width { get; }

    public int Height { get; }

    public int SectorCount => _edges.Count;

    public IReadOnlyList<NavigationPortal> Portals { get; }

    public bool Contains(NavigationSectorCoordinate sector) =>
        _edges.ContainsKey(sector);

    public ReadOnlySpan<NavigationSectorEdge> GetEdges(
        NavigationSectorCoordinate sector)
    {
        return _edges.TryGetValue(
            sector,
            out NavigationSectorEdge[]? edges)
                ? edges
                : ReadOnlySpan<NavigationSectorEdge>.Empty;
    }

    public NavigationSectorCoordinate GetSector(
        NavigationCellCoordinate cell)
    {
        if (!Grid.Contains(cell))
        {
            throw new ArgumentOutOfRangeException(nameof(cell));
        }

        return new NavigationSectorCoordinate(
            cell.X / Settings.SectorSizeCells,
            cell.Z / Settings.SectorSizeCells);
    }

    public void GetCellRange(
        NavigationSectorCoordinate sector,
        out int minimumX,
        out int minimumZ,
        out int maximumExclusiveX,
        out int maximumExclusiveZ)
    {
        if ((uint)sector.X >= (uint)Width ||
            (uint)sector.Z >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(sector));
        }

        minimumX = sector.X * Settings.SectorSizeCells;
        minimumZ = sector.Z * Settings.SectorSizeCells;
        maximumExclusiveX = Math.Min(
            minimumX + Settings.SectorSizeCells,
            Grid.Width);
        maximumExclusiveZ = Math.Min(
            minimumZ + Settings.SectorSizeCells,
            Grid.Height);
    }

    public AxisAlignedBounds GetWorldBounds(
        NavigationSectorCoordinate sector)
    {
        GetCellRange(
            sector,
            out int minimumX,
            out int minimumZ,
            out int maximumExclusiveX,
            out int maximumExclusiveZ);

        float cellSize = Grid.Settings.CellSizeMeters;
        float minimumWorldX =
            Grid.Origin.X + minimumX * cellSize;
        float minimumWorldZ =
            Grid.Origin.Y + minimumZ * cellSize;
        float maximumWorldX =
            Grid.Origin.X + maximumExclusiveX * cellSize;
        float maximumWorldZ =
            Grid.Origin.Y + maximumExclusiveZ * cellSize;

        return new AxisAlignedBounds(
            new Vector3(minimumWorldX, -0.05f, minimumWorldZ),
            new Vector3(maximumWorldX, 0.05f, maximumWorldZ));
    }
}

public static class NavigationSectorGraphBuilder
{
    public static NavigationSectorGraph Build(
        NavigationGrid grid,
        in NavigationCapabilities capabilities,
        NavigationSectorSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(grid);

        NavigationSectorSettings resolved =
            settings ?? new NavigationSectorSettings();
        resolved.Validate(grid);

        int width =
            (grid.Width + resolved.SectorSizeCells - 1) /
            resolved.SectorSizeCells;
        int height =
            (grid.Height + resolved.SectorSizeCells - 1) /
            resolved.SectorSizeCells;

        var mutableEdges = new Dictionary<
            NavigationSectorCoordinate,
            List<NavigationSectorEdge>>();
        var portals = new List<NavigationPortal>();

        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                NavigationSectorCoordinate sector = new(x, z);
                if (SectorContainsTraversableCell(
                        grid,
                        sector,
                        resolved.SectorSizeCells,
                        capabilities))
                {
                    mutableEdges.Add(
                        sector,
                        new List<NavigationSectorEdge>());
                }
            }
        }

        foreach (NavigationSectorCoordinate sector in mutableEdges.Keys)
        {
            NavigationSectorCoordinate right =
                new(sector.X + 1, sector.Z);
            if (mutableEdges.ContainsKey(right))
            {
                ConnectVerticalBoundary(
                    grid,
                    sector,
                    right,
                    resolved.SectorSizeCells,
                    capabilities,
                    mutableEdges,
                    portals);
            }

            NavigationSectorCoordinate down =
                new(sector.X, sector.Z + 1);
            if (mutableEdges.ContainsKey(down))
            {
                ConnectHorizontalBoundary(
                    grid,
                    sector,
                    down,
                    resolved.SectorSizeCells,
                    capabilities,
                    mutableEdges,
                    portals);
            }
        }

        var frozenEdges = new Dictionary<
            NavigationSectorCoordinate,
            NavigationSectorEdge[]>(mutableEdges.Count);

        foreach (var pair in mutableEdges)
        {
            pair.Value.Sort(
                static (left, right) =>
                {
                    int z = left.To.Z.CompareTo(right.To.Z);
                    return z != 0
                        ? z
                        : left.To.X.CompareTo(right.To.X);
                });
            frozenEdges.Add(pair.Key, pair.Value.ToArray());
        }

        return new NavigationSectorGraph(
            grid,
            resolved,
            capabilities.MovementClass,
            width,
            height,
            frozenEdges,
            portals.ToArray());
    }

    private static bool SectorContainsTraversableCell(
        NavigationGrid grid,
        NavigationSectorCoordinate sector,
        int sectorSize,
        in NavigationCapabilities capabilities)
    {
        int minimumX = sector.X * sectorSize;
        int minimumZ = sector.Z * sectorSize;
        int maximumX = Math.Min(minimumX + sectorSize, grid.Width);
        int maximumZ = Math.Min(minimumZ + sectorSize, grid.Height);

        for (int z = minimumZ; z < maximumZ; z++)
        {
            for (int x = minimumX; x < maximumX; x++)
            {
                if (grid.IsTraversable(
                        new NavigationCellCoordinate(x, z),
                        capabilities))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void ConnectVerticalBoundary(
        NavigationGrid grid,
        NavigationSectorCoordinate left,
        NavigationSectorCoordinate right,
        int sectorSize,
        in NavigationCapabilities capabilities,
        Dictionary<
            NavigationSectorCoordinate,
            List<NavigationSectorEdge>> edges,
        List<NavigationPortal> portals)
    {
        int leftX =
            Math.Min((left.X + 1) * sectorSize, grid.Width) - 1;
        int rightX = leftX + 1;
        int minimumZ = left.Z * sectorSize;
        int maximumZ = Math.Min(minimumZ + sectorSize, grid.Height);

        int runStart = -1;

        for (int z = minimumZ; z <= maximumZ; z++)
        {
            bool open =
                z < maximumZ &&
                grid.IsTraversable(
                    new NavigationCellCoordinate(leftX, z),
                    capabilities) &&
                grid.IsTraversable(
                    new NavigationCellCoordinate(rightX, z),
                    capabilities);

            if (open && runStart < 0)
            {
                runStart = z;
            }

            if ((!open || z == maximumZ) && runStart >= 0)
            {
                int runEnd = z - 1;
                int midpoint = runStart + ((runEnd - runStart) / 2);
                AddBidirectionalPortal(
                    grid,
                    left,
                    right,
                    new NavigationCellCoordinate(leftX, midpoint),
                    new NavigationCellCoordinate(rightX, midpoint),
                    edges,
                    portals);
                runStart = -1;
            }
        }
    }

    private static void ConnectHorizontalBoundary(
        NavigationGrid grid,
        NavigationSectorCoordinate top,
        NavigationSectorCoordinate bottom,
        int sectorSize,
        in NavigationCapabilities capabilities,
        Dictionary<
            NavigationSectorCoordinate,
            List<NavigationSectorEdge>> edges,
        List<NavigationPortal> portals)
    {
        int topZ =
            Math.Min((top.Z + 1) * sectorSize, grid.Height) - 1;
        int bottomZ = topZ + 1;
        int minimumX = top.X * sectorSize;
        int maximumX = Math.Min(minimumX + sectorSize, grid.Width);

        int runStart = -1;

        for (int x = minimumX; x <= maximumX; x++)
        {
            bool open =
                x < maximumX &&
                grid.IsTraversable(
                    new NavigationCellCoordinate(x, topZ),
                    capabilities) &&
                grid.IsTraversable(
                    new NavigationCellCoordinate(x, bottomZ),
                    capabilities);

            if (open && runStart < 0)
            {
                runStart = x;
            }

            if ((!open || x == maximumX) && runStart >= 0)
            {
                int runEnd = x - 1;
                int midpoint = runStart + ((runEnd - runStart) / 2);
                AddBidirectionalPortal(
                    grid,
                    top,
                    bottom,
                    new NavigationCellCoordinate(midpoint, topZ),
                    new NavigationCellCoordinate(midpoint, bottomZ),
                    edges,
                    portals);
                runStart = -1;
            }
        }
    }

    private static void AddBidirectionalPortal(
        NavigationGrid grid,
        NavigationSectorCoordinate first,
        NavigationSectorCoordinate second,
        NavigationCellCoordinate firstCell,
        NavigationCellCoordinate secondCell,
        Dictionary<
            NavigationSectorCoordinate,
            List<NavigationSectorEdge>> edges,
        List<NavigationPortal> portals)
    {
        Vector3 firstWorld = grid.GetCellCenter(firstCell);
        Vector3 secondWorld = grid.GetCellCenter(secondCell);
        Vector3 worldPosition = (firstWorld + secondWorld) * 0.5f;
        float cost = Vector2.Distance(
            new Vector2(first.X, first.Z),
            new Vector2(second.X, second.Z));

        var forward = new NavigationPortal(
            first,
            second,
            firstCell,
            secondCell,
            worldPosition);
        var reverse = new NavigationPortal(
            second,
            first,
            secondCell,
            firstCell,
            worldPosition);

        edges[first].Add(
            new NavigationSectorEdge(second, forward, cost));
        edges[second].Add(
            new NavigationSectorEdge(first, reverse, cost));

        portals.Add(forward);
    }
}
