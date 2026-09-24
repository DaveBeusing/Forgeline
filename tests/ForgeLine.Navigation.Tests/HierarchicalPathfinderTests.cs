using System.Numerics;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Navigation.Tests;

public sealed class HierarchicalPathfinderTests
{
    [Fact]
    public void RefinesMultiSectorRouteThroughChokePoint()
    {
        TerrainWorld terrain = CreateFlatWorld();
        AxisAlignedBounds[] obstacles =
        [
            new AxisAlignedBounds(
                new Vector3(30.0f, -1.0f, 0.0f),
                new Vector3(34.0f, 2.0f, 24.0f)),
            new AxisAlignedBounds(
                new Vector3(30.0f, -1.0f, 40.0f),
                new Vector3(34.0f, 2.0f, 64.0f))
        ];

        NavigationWorld world = NavigationWorld.Build(
            terrain,
            obstacles,
            new NavigationGridSettings
            {
                CellSizeMeters = 4.0f,
                StaticObstacleClearanceMeters = 0.0f
            },
            new NavigationSectorSettings
            {
                SectorSizeCells = 4
            });

        var pathfinder = new HierarchicalPathfinder(world);
        NavigationCapabilities capabilities =
            NavigationCapabilities.For(
                NavigationMovementClass.Tracked);

        NavigationSearchResult result =
            pathfinder.FindPath(
                new Vector3(4.0f, 0.0f, 4.0f),
                new Vector3(60.0f, 0.0f, 60.0f),
                capabilities);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Path);
        Assert.True(result.Path.Sectors.Count > 1);
        Assert.NotEmpty(result.Path.Portals);
        Assert.NotEmpty(result.Path.Waypoints);
        Assert.True(result.Path.Diagnostics.ExpandedHighLevelNodes > 0);
        Assert.True(result.Path.Diagnostics.ExpandedLocalNodes > 0);

        foreach (NavigationCellCoordinate cell in result.Path.RefinedCells)
        {
            Assert.True(
                world.Grid.IsTraversable(cell, capabilities));
        }
    }

    [Fact]
    public void ReusesVersionedHighLevelRoutes()
    {
        NavigationWorld world = NavigationWorld.Build(
            CreateFlatWorld(),
            gridSettings: new NavigationGridSettings
            {
                CellSizeMeters = 4.0f
            },
            sectorSettings: new NavigationSectorSettings
            {
                SectorSizeCells = 4
            });

        var pathfinder = new HierarchicalPathfinder(world);
        NavigationCapabilities capabilities =
            NavigationCapabilities.For(
                NavigationMovementClass.Infantry);

        NavigationSearchResult first =
            pathfinder.FindPath(
                new Vector3(4.0f, 0.0f, 4.0f),
                new Vector3(60.0f, 0.0f, 60.0f),
                capabilities);
        NavigationSearchResult second =
            pathfinder.FindPath(
                new Vector3(6.0f, 0.0f, 6.0f),
                new Vector3(58.0f, 0.0f, 58.0f),
                capabilities);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.False(first.Path!.Diagnostics.HighLevelCacheHit);
        Assert.True(second.Path!.Diagnostics.HighLevelCacheHit);
        Assert.Equal(1, pathfinder.HighLevelCacheEntryCount);
    }


    [Fact]
    public void ConcurrentRequestsShareOneHighLevelCacheEntry()
    {
        NavigationWorld world = NavigationWorld.Build(
            CreateFlatWorld(),
            gridSettings: new NavigationGridSettings
            {
                CellSizeMeters = 4.0f
            },
            sectorSettings: new NavigationSectorSettings
            {
                SectorSizeCells = 4
            });

        var pathfinder = new HierarchicalPathfinder(world);
        NavigationCapabilities capabilities =
            NavigationCapabilities.For(
                NavigationMovementClass.Tracked);
        var results = new NavigationSearchResult[16];

        Parallel.For(
            0,
            results.Length,
            index =>
            {
                results[index] = pathfinder.FindPath(
                    new Vector3(4.0f, 0.0f, 4.0f),
                    new Vector3(60.0f, 0.0f, 60.0f),
                    capabilities);
            });

        Assert.All(results, result => Assert.True(result.Succeeded));
        Assert.Equal(1, pathfinder.HighLevelCacheEntryCount);
    }

    [Fact]
    public void ReturnsExplicitFailureWhenDestinationIsSeparated()
    {
        TerrainWorld terrain = CreateFlatWorld();
        var wall = new AxisAlignedBounds(
            new Vector3(28.0f, -1.0f, 0.0f),
            new Vector3(36.0f, 2.0f, 64.0f));

        NavigationWorld world = NavigationWorld.Build(
            terrain,
            [wall],
            new NavigationGridSettings
            {
                CellSizeMeters = 4.0f,
                StaticObstacleClearanceMeters = 0.0f
            },
            new NavigationSectorSettings
            {
                SectorSizeCells = 4
            });

        var pathfinder = new HierarchicalPathfinder(world);

        NavigationSearchResult result =
            pathfinder.FindPath(
                new Vector3(4.0f, 0.0f, 32.0f),
                new Vector3(60.0f, 0.0f, 32.0f),
                NavigationCapabilities.For(
                    NavigationMovementClass.Tracked));

        Assert.False(result.Succeeded);
        Assert.Equal(
            NavigationFailureReason.NoHighLevelRoute,
            result.FailureReason);
    }

    private static TerrainWorld CreateFlatWorld()
    {
        var settings = new WorldGridSettings
        {
            ChunkSizeMeters = 32.0f,
            HeightSamplesPerSide = 9
        };

        var chunks = new List<TerrainChunk>();

        for (int z = 0; z < 2; z++)
        {
            for (int x = 0; x < 2; x++)
            {
                int count =
                    settings.HeightSamplesPerSide *
                    settings.HeightSamplesPerSide;

                chunks.Add(
                    new TerrainChunk(
                        new ChunkCoordinate(x, z),
                        new TerrainHeightfield(
                            settings.HeightSamplesPerSide,
                            settings.ChunkSizeMeters,
                            new float[count])));
            }
        }

        return new TerrainWorld(settings, chunks);
    }
}
