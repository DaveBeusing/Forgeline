using System.Numerics;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.World.Tests;

public sealed class TerrainHeightfieldTests
{
    [Fact]
    public void BilinearHeightSamplingInterpolatesGrid()
    {
        var heightfield = new TerrainHeightfield(
            2,
            10.0f,
            [
                0.0f, 10.0f,
                20.0f, 30.0f
            ]);

        Assert.Equal(15.0f, heightfield.SampleHeight(5.0f, 5.0f));
    }

    [Fact]
    public void FlatHeightfieldReturnsWorldUpNormal()
    {
        var heightfield = new TerrainHeightfield(
            3,
            10.0f,
            [
                4.0f, 4.0f, 4.0f,
                4.0f, 4.0f, 4.0f,
                4.0f, 4.0f, 4.0f
            ]);

        Vector3 normal = heightfield.SampleNormal(5.0f, 5.0f);

        Assert.InRange(Vector3.Distance(normal, Vector3.UnitY), 0.0f, 0.0001f);
    }

    [Fact]
    public void AdjacentGeneratedChunksShareIdenticalEdgeHeights()
    {
        var settings = new WorldGridSettings
        {
            ChunkSizeMeters = 256.0f,
            HeightSamplesPerSide = 65
        };

        TerrainChunk left = DevelopmentTerrainFactory.CreateChunk(
            new ChunkCoordinate(-1, 2),
            settings);
        TerrainChunk right = DevelopmentTerrainFactory.CreateChunk(
            new ChunkCoordinate(0, 2),
            settings);

        for (int sampleZ = 0; sampleZ < settings.HeightSamplesPerSide; sampleZ++)
        {
            Assert.Equal(
                left.Heightfield.GetHeight(
                    settings.HeightSamplesPerSide - 1,
                    sampleZ),
                right.Heightfield.GetHeight(0, sampleZ));
        }
    }

    [Fact]
    public void TerrainQueriesRemainIndependentFromGraphics()
    {
        var settings = new WorldGridSettings
        {
            ChunkSizeMeters = 64.0f,
            HeightSamplesPerSide = 17
        };
        TerrainWorld world = DevelopmentTerrainFactory.CreateRepresentativeWorld(
            settings,
            chunkRadius: 1);

        Assert.True(world.TrySampleHeight(-0.5f, 0.5f, out float height));
        Assert.True(float.IsFinite(height));
        Assert.True(world.TrySampleNormal(-0.5f, 0.5f, out Vector3 normal));
        Assert.InRange(normal.Length(), 0.999f, 1.001f);

        Assert.False(world.TrySampleHeight(10_000.0f, 10_000.0f, out _));
    }
}
