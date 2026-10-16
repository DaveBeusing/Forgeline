using System.Numerics;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Navigation.Tests;

public sealed class LocalNavigationQualityTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void LocalRouteMatchesIndependentDijkstraCost(bool obstacles, bool slopes)
    {
        NavigationWorld world = CreateWorld(obstacles, slopes);
        NavigationCapabilities capabilities = NavigationCapabilities.For(NavigationMovementClass.Tracked);
        var start = new NavigationCellCoordinate(0, 0);
        var destination = new NavigationCellCoordinate(15, 15);
        var pathfinder = new HierarchicalPathfinder(world);

        NavigationSearchResult result = pathfinder.FindPath(
            world.Grid.GetCellCenter(start), world.Grid.GetCellCenter(destination), capabilities);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Path);
        float referenceCost = DijkstraCost(world.Grid, start, destination, capabilities);
        float actualCost = ValidateAndMeasureCells(world.Grid, result.Path.RefinedCells, capabilities);
        Assert.InRange(MathF.Abs(actualCost - referenceCost), 0.0f, 0.001f);
        Assert.Equal(start, result.Path.RefinedCells[0]);
        Assert.Equal(destination, result.Path.RefinedCells[^1]);
    }

    [Fact]
    public void EqualCostRoutesRemainIdenticalAcrossColdWarmAndConcurrentSearches()
    {
        NavigationWorld world = CreateWorld(obstacles: true, slopes: false);
        var pathfinder = new HierarchicalPathfinder(world);
        NavigationCapabilities capabilities = NavigationCapabilities.For(NavigationMovementClass.Infantry);
        Vector3 start = new(2, 0, 2);
        Vector3 destination = new(62, 0, 62);
        NavigationSearchResult expected = pathfinder.FindPath(start, destination, capabilities);
        Assert.NotNull(expected.Path);
        var results = new NavigationSearchResult[32];

        Parallel.For(0, results.Length, index =>
        {
            HierarchicalPathfinder selected = index % 2 == 0
                ? pathfinder
                : new HierarchicalPathfinder(world);
            results[index] = selected.FindPath(start, destination, capabilities);
        });

        foreach (NavigationSearchResult result in results)
        {
            Assert.True(result.Succeeded);
            Assert.NotNull(result.Path);
            Assert.Equal(expected.Path.RefinedCells.ToArray(), result.Path.RefinedCells.ToArray());
            Assert.Equal(expected.Path.Waypoints.ToArray(), result.Path.Waypoints.ToArray());
            Assert.Equal(expected.Path.Diagnostics.ExpandedLocalNodes, result.Path.Diagnostics.ExpandedLocalNodes);
        }
    }

    [Fact]
    public void DiagonalCornerCannotEscapeTwoBlockingNeighbors()
    {
        NavigationWorld basis = CreateWorld(obstacles: false, slopes: false);
        TerrainWorld terrain = CreateTerrain(slopes: false);
        NavigationWorld world = NavigationWorld.Build(terrain,
            [new AxisAlignedBounds(new Vector3(4.2f, -1, 0.2f), new Vector3(7.8f, 2, 3.8f)),
             new AxisAlignedBounds(new Vector3(0.2f, -1, 4.2f), new Vector3(3.8f, 2, 7.8f))],
            basis.Grid.Settings,
            new NavigationSectorSettings { SectorSizeCells = 16 });

        NavigationSearchResult result = new HierarchicalPathfinder(world).FindPath(
            new Vector3(2, 0, 2), new Vector3(6, 0, 6),
            NavigationCapabilities.For(NavigationMovementClass.Tracked));

        Assert.False(result.Succeeded);
        Assert.Equal(NavigationFailureReason.NoLocalRoute, result.FailureReason);
    }

    [Fact]
    public void OpenDiagonalDoesNotExpandAnAreaOfEquivalentCandidates()
    {
        NavigationWorld world = CreateWorld(obstacles: false, slopes: false);
        NavigationSearchResult result = new HierarchicalPathfinder(world).FindPath(
            new Vector3(2, 0, 2), new Vector3(62, 0, 62),
            NavigationCapabilities.For(NavigationMovementClass.Tracked));

        Assert.NotNull(result.Path);
        Assert.Equal(16, result.Path.Diagnostics.ExpandedLocalNodes);
    }

    private static NavigationWorld CreateWorld(bool obstacles, bool slopes)
    {
        AxisAlignedBounds[] bounds = obstacles
            ? [new AxisAlignedBounds(new Vector3(22.2f, -1, 2.2f), new Vector3(23.8f, 20, 38)),
               new AxisAlignedBounds(new Vector3(42.2f, -1, 26), new Vector3(43.8f, 20, 61.8f))]
            : [];
        return NavigationWorld.Build(CreateTerrain(slopes), bounds,
            new NavigationGridSettings { CellSizeMeters = 4, StaticObstacleClearanceMeters = 0 },
            new NavigationSectorSettings { SectorSizeCells = 16 });
    }

    private static TerrainWorld CreateTerrain(bool slopes)
    {
        var settings = new WorldGridSettings { ChunkSizeMeters = 64, HeightSamplesPerSide = 17 };
        var heights = new float[17 * 17];
        if (slopes)
        {
            for (int z = 0; z < 17; z++)
                for (int x = 0; x < 17; x++)
                    heights[z * 17 + x] = 0.5f * MathF.Sin(x * 0.5f) + 0.15f * x + 0.1f * z;
        }
        return new TerrainWorld(settings,
            [new TerrainChunk(new ChunkCoordinate(0, 0), new TerrainHeightfield(17, 64, heights))]);
    }

    private static float ValidateAndMeasureCells(NavigationGrid grid,
        IReadOnlyList<NavigationCellCoordinate> path, NavigationCapabilities capabilities)
    {
        float total = 0;
        for (int index = 1; index < path.Count; index++)
        {
            NavigationCellCoordinate current = path[index - 1];
            NavigationCellCoordinate end = path[index];
            int dx = Math.Sign(end.X - current.X);
            int dz = Math.Sign(end.Z - current.Z);
            int steps = Math.Max(Math.Abs(end.X - current.X), Math.Abs(end.Z - current.Z));
            for (int step = 0; step < steps; step++)
            {
                var next = new NavigationCellCoordinate(current.X + dx, current.Z + dz);
                Assert.True(grid.IsTraversable(next, capabilities));
                if (dx != 0 && dz != 0)
                {
                    Assert.True(grid.IsTraversable(new NavigationCellCoordinate(current.X + dx, current.Z), capabilities));
                    Assert.True(grid.IsTraversable(new NavigationCellCoordinate(current.X, current.Z + dz), capabilities));
                }
                total += (dx != 0 && dz != 0 ? 1.41421356f : 1.0f) *
                    0.5f * (grid.GetTraversalCost(current, capabilities) + grid.GetTraversalCost(next, capabilities));
                current = next;
            }
            Assert.Equal(end, current);
        }
        return total;
    }

    private static float DijkstraCost(NavigationGrid grid, NavigationCellCoordinate start,
        NavigationCellCoordinate destination, NavigationCapabilities capabilities)
    {
        var costs = new float[grid.CellCount];
        Array.Fill(costs, float.PositiveInfinity);
        costs[grid.GetIndex(start)] = 0;
        var queue = new PriorityQueue<NavigationCellCoordinate, float>();
        queue.Enqueue(start, 0);
        while (queue.TryDequeue(out NavigationCellCoordinate cell, out float cost))
        {
            if (cost != costs[grid.GetIndex(cell)]) continue;
            if (cell == destination) return cost;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    var next = new NavigationCellCoordinate(cell.X + dx, cell.Z + dz);
                    if (!grid.IsTraversable(next, capabilities)) continue;
                    bool diagonal = dx != 0 && dz != 0;
                    if (diagonal &&
                        (!grid.IsTraversable(new NavigationCellCoordinate(cell.X + dx, cell.Z), capabilities) ||
                         !grid.IsTraversable(new NavigationCellCoordinate(cell.X, cell.Z + dz), capabilities))) continue;
                    float candidate = cost + (diagonal ? 1.41421356f : 1.0f) * 0.5f *
                        (grid.GetTraversalCost(cell, capabilities) + grid.GetTraversalCost(next, capabilities));
                    int nextIndex = grid.GetIndex(next);
                    if (candidate >= costs[nextIndex]) continue;
                    costs[nextIndex] = candidate;
                    queue.Enqueue(next, candidate);
                }
            }
        }
        return float.PositiveInfinity;
    }
}
