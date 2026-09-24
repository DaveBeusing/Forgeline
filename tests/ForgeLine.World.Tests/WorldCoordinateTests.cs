using ForgeLine.World;
using Xunit;

namespace ForgeLine.World.Tests;

public sealed class WorldCoordinateTests
{
    private static readonly WorldGridSettings Settings = new()
    {
        ChunkSizeMeters = 256.0f,
        RegionSizeInChunks = 8,
        HeightSamplesPerSide = 65
    };

    [Theory]
    [InlineData(0.0f, 0, 0.0f)]
    [InlineData(255.999f, 0, 255.999f)]
    [InlineData(256.0f, 1, 0.0f)]
    [InlineData(-0.001f, -1, 255.999f)]
    [InlineData(-256.0f, -1, 0.0f)]
    [InlineData(-256.001f, -2, 255.999f)]
    public void WorldToChunkUsesFloorSemantics(
        float worldX,
        int expectedChunkX,
        float expectedLocalX)
    {
        ChunkLocation location = WorldCoordinateConverter.WorldToChunk(
            worldX,
            0.0f,
            Settings);

        Assert.Equal(expectedChunkX, location.Chunk.X);
        Assert.InRange(
            MathF.Abs(location.Local.X - expectedLocalX),
            0.0f,
            0.0011f);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(7, 0, 7)]
    [InlineData(8, 1, 0)]
    [InlineData(-1, -1, 7)]
    [InlineData(-8, -1, 0)]
    [InlineData(-9, -2, 7)]
    public void ChunkToRegionHandlesNegativeCoordinates(
        int chunkX,
        int expectedRegionX,
        int expectedLocalChunkX)
    {
        RegionChunkLocation location = WorldCoordinateConverter.ChunkToRegion(
            new ChunkCoordinate(chunkX, 0),
            Settings);

        Assert.Equal(expectedRegionX, location.Region.X);
        Assert.Equal(expectedLocalChunkX, location.LocalChunkX);
    }

    [Fact]
    public void ChunkOriginsUseCanonicalMeterUnits()
    {
        var chunk = new ChunkCoordinate(-2, 3);

        var origin = WorldCoordinateConverter.ChunkOrigin(chunk, Settings);

        Assert.Equal(-512.0f, origin.X);
        Assert.Equal(0.0f, origin.Y);
        Assert.Equal(768.0f, origin.Z);
    }
}
