using ForgeLine.World;
using Xunit;

namespace ForgeLine.Navigation.Tests;

public sealed class SectorGraphTests
{
    [Fact]
    public void SectorGraphFindsPortalsAndRoutesAcrossChunks()
    {
        var worldSettings = new WorldGridSettings
        {
            ChunkSizeMeters = 32.0f,
            HeightSamplesPerSide = 9
        };

        TerrainWorld terrain = CreateFlatWorld(
            worldSettings,
            chunksX: 2,
            chunksZ: 1);

        NavigationGrid grid = NavigationGridBuilder.Build(
            terrain,
            settings: new NavigationGridSettings
            {
                CellSizeMeters = 4.0f
            });

        NavigationCapabilities capabilities =
            NavigationCapabilities.For(
                NavigationMovementClass.Tracked);

        NavigationSectorGraph graph =
            NavigationSectorGraphBuilder.Build(
                grid,
                capabilities,
                new NavigationSectorSettings
                {
                    SectorSizeCells = 4
                });

        NavigationSectorCoordinate start = new(0, 0);
        NavigationSectorCoordinate destination = new(3, 1);

        Assert.True(graph.Contains(start));
        Assert.True(graph.Contains(destination));
        Assert.NotEmpty(graph.Portals);

        Assert.True(
            HighLevelRouteSearch.TryFindRoute(
                graph,
                start,
                destination,
                out HighLevelRoute route));

        Assert.Equal(start, route.Sectors[0]);
        Assert.Equal(
            destination,
            route.Sectors[^1]);
        Assert.Equal(
            route.Sectors.Count - 1,
            route.Portals.Count);
        Assert.True(route.ExpandedNodeCount > 0);
    }

    [Fact]
    public void BlockedBoundaryRemovesSectorConnectivity()
    {
        var worldSettings = new WorldGridSettings
        {
            ChunkSizeMeters = 32.0f,
            HeightSamplesPerSide = 9
        };

        TerrainWorld terrain = CreateFlatWorld(
            worldSettings,
            chunksX: 1,
            chunksZ: 1);

        var wall = new AxisAlignedBounds(
            new System.Numerics.Vector3(15.0f, -1.0f, 0.0f),
            new System.Numerics.Vector3(17.0f, 2.0f, 32.0f));

        NavigationGrid grid = NavigationGridBuilder.Build(
            terrain,
            [wall],
            new NavigationGridSettings
            {
                CellSizeMeters = 4.0f,
                StaticObstacleClearanceMeters = 0.0f
            });

        NavigationCapabilities capabilities =
            NavigationCapabilities.For(
                NavigationMovementClass.Tracked);
        NavigationSectorGraph graph =
            NavigationSectorGraphBuilder.Build(
                grid,
                capabilities,
                new NavigationSectorSettings
                {
                    SectorSizeCells = 4
                });

        Assert.False(
            HighLevelRouteSearch.TryFindRoute(
                graph,
                new NavigationSectorCoordinate(0, 0),
                new NavigationSectorCoordinate(1, 0),
                out _));
    }

    private static TerrainWorld CreateFlatWorld(
        WorldGridSettings settings,
        int chunksX,
        int chunksZ)
    {
        var chunks = new List<TerrainChunk>();

        for (int z = 0; z < chunksZ; z++)
        {
            for (int x = 0; x < chunksX; x++)
            {
                int sampleCount =
                    settings.HeightSamplesPerSide *
                    settings.HeightSamplesPerSide;

                chunks.Add(
                    new TerrainChunk(
                        new ChunkCoordinate(x, z),
                        new TerrainHeightfield(
                            settings.HeightSamplesPerSide,
                            settings.ChunkSizeMeters,
                            new float[sampleCount])));
            }
        }

        return new TerrainWorld(settings, chunks);
    }
}
