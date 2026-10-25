using System.Numerics;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Navigation.Tests;

public sealed class NavigationObstacleRasterizationTests
{
    [Theory]
    [InlineData(4f, 0f, 0)]
    [InlineData(4f, 0.5f, -2)]
    [InlineData(0.3f, 0.1f, -3)]
    [InlineData(0.3f, 0f, 1)]
    [InlineData(8f, 3.3f, 2)]
    public void RasterizationMatchesExhaustiveInclusiveOverlap(float cellSize, float clearance, int chunkOrigin)
    {
        float chunkSize = cellSize * 32;
        var settings = new WorldGridSettings { ChunkSizeMeters = chunkSize, HeightSamplesPerSide = 9 };
        var terrain = new TerrainWorld(settings,
            [new TerrainChunk(new ChunkCoordinate(chunkOrigin, chunkOrigin),
                new TerrainHeightfield(9, chunkSize, new float[81]))]);
        float origin = terrain.WorldBounds.Minimum.X;
        var random = new Random(20261024);
        var obstacles = new List<AxisAlignedBounds>();
        // Check individually as well as together: a dense union must not mask an omitted boundary cell.
        for (int index = 0; index < 80; index++)
        {
            float x = origin + (float)(random.NextDouble() * 1.5 - 0.25) * chunkSize;
            float z = origin + (float)(random.NextDouble() * 1.5 - 0.25) * chunkSize;
            float width = (float)random.NextDouble() * cellSize * 3;
            var obstacle = new AxisAlignedBounds(new Vector3(x, -1, z), new Vector3(x + width, 2, z + width));
            obstacles.Add(obstacle);
            Verify(terrain, [obstacle], cellSize, clearance);
        }
        float boundary = origin + 12 * cellSize;
        foreach (float edge in new[] { MathF.BitDecrement(boundary), boundary, MathF.BitIncrement(boundary) })
        {
            var touching = new AxisAlignedBounds(new Vector3(edge, -1, edge), new Vector3(edge, 2, edge));
            obstacles.Add(touching);
            Verify(terrain, [touching], cellSize, clearance);
        }
        Verify(terrain, obstacles, cellSize, clearance);
        Verify(terrain, [], cellSize, clearance);
        Verify(terrain, [new AxisAlignedBounds(new Vector3(-float.MaxValue, -1, -float.MaxValue),
            new Vector3(float.MaxValue, 2, float.MaxValue))], cellSize, clearance);
    }

    private static void Verify(TerrainWorld terrain, IReadOnlyList<AxisAlignedBounds> obstacles,
        float cellSize, float clearance)
    {
        NavigationGrid grid = NavigationGridBuilder.Build(terrain, obstacles, new NavigationGridSettings
        { CellSizeMeters = cellSize, StaticObstacleClearanceMeters = clearance });
        float half = cellSize * 0.5f;
        for (int z = 0; z < grid.Height; z++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                float centerX = grid.Origin.X + x * cellSize + half;
                float centerZ = grid.Origin.Y + z * cellSize + half;
                bool expected = false;
                foreach (AxisAlignedBounds obstacle in obstacles)
                    expected |= centerX - half <= obstacle.Maximum.X + clearance &&
                        centerX + half >= obstacle.Minimum.X - clearance &&
                        centerZ - half <= obstacle.Maximum.Z + clearance &&
                        centerZ + half >= obstacle.Minimum.Z - clearance;
                Assert.Equal(expected, grid.GetCell(new NavigationCellCoordinate(x, z)).StaticBlocked);
            }
        }
    }
}
