using ForgeLine.World;
using Xunit;

namespace ForgeLine.World.Tests;

public sealed class TerrainMeshTests
{
    [Fact]
    public void MeshGeneratorProducesExpectedTopology()
    {
        var settings = new WorldGridSettings
        {
            ChunkSizeMeters = 32.0f,
            HeightSamplesPerSide = 9
        };
        TerrainChunk chunk = DevelopmentTerrainFactory.CreateChunk(
            new ChunkCoordinate(2, -3),
            settings);

        TerrainMeshData mesh = TerrainMeshGenerator.Generate(
            chunk,
            new TerrainMeshSettings
            {
                VertexSamplesPerSide = 5
            });

        Assert.Equal(25, mesh.Vertices.Length);
        Assert.Equal(96, mesh.Indices.Length);
        Assert.Equal(32, mesh.TriangleCount);

        Assert.Equal(64.0f, mesh.Vertices[0].Position.X);
        Assert.Equal(-96.0f, mesh.Vertices[0].Position.Z);
        Assert.Equal(96.0f, mesh.Vertices[^1].Position.X);
        Assert.Equal(-64.0f, mesh.Vertices[^1].Position.Z);
    }

    [Fact]
    public void MeshSharedEdgesUseIdenticalPositionsAndHeights()
    {
        var settings = new WorldGridSettings
        {
            ChunkSizeMeters = 64.0f,
            HeightSamplesPerSide = 17
        };
        var meshSettings = new TerrainMeshSettings
        {
            VertexSamplesPerSide = 9
        };

        TerrainMeshData left = TerrainMeshGenerator.Generate(
            DevelopmentTerrainFactory.CreateChunk(
                new ChunkCoordinate(-1, 0),
                settings),
            meshSettings);
        TerrainMeshData right = TerrainMeshGenerator.Generate(
            DevelopmentTerrainFactory.CreateChunk(
                new ChunkCoordinate(0, 0),
                settings),
            meshSettings);

        int side = meshSettings.VertexSamplesPerSide;
        for (int z = 0; z < side; z++)
        {
            Assert.Equal(
                left.Vertices[z * side + side - 1].Position,
                right.Vertices[z * side].Position);
        }
    }
}
