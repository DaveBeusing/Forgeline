using System.Numerics;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Navigation.Tests;

public sealed class NavigationScaleTests
{
    [Fact]
    public void DistantRouteRefinementDoesNotExpandCompleteWorldGrid()
    {
        const int chunksPerSide = 8;
        const float chunkSize = 32.0f;

        var settings = new WorldGridSettings
        {
            ChunkSizeMeters = chunkSize,
            RegionSizeInChunks = 4,
            HeightSamplesPerSide = 3
        };

        var chunks =
            new List<TerrainChunk>(
                chunksPerSide * chunksPerSide);

        for (int z = 0; z < chunksPerSide; z++)
        {
            for (int x = 0; x < chunksPerSide; x++)
            {
                chunks.Add(
                    new TerrainChunk(
                        new ChunkCoordinate(x, z),
                        new TerrainHeightfield(
                            settings.HeightSamplesPerSide,
                            settings.ChunkSizeMeters,
                            new float[
                                settings.HeightSamplesPerSide *
                                settings.HeightSamplesPerSide])));
            }
        }

        var terrain = new TerrainWorld(settings, chunks);
        NavigationWorld world = NavigationWorld.Build(
            terrain,
            gridSettings: new NavigationGridSettings
            {
                CellSizeMeters = 4.0f
            },
            sectorSettings: new NavigationSectorSettings
            {
                SectorSizeCells = 4
            });

        NavigationSearchResult result =
            new HierarchicalPathfinder(world).FindPath(
                new Vector3(2.0f, 0.0f, 2.0f),
                new Vector3(
                    chunksPerSide * chunkSize - 2.0f,
                    0.0f,
                    chunksPerSide * chunkSize - 2.0f),
                NavigationCapabilities.For(
                    NavigationMovementClass.Tracked));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Path);
        Assert.True(
            result.Path.Diagnostics.ExpandedLocalNodes <
            world.Grid.CellCount);
        Assert.True(
            result.Path.Sectors.Count <
            world.Grid.CellCount);
    }
}
