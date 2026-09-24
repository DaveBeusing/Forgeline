using System.Numerics;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Navigation.Tests;

public sealed class NavigationGridTests
{
    [Fact]
    public void GridUsesTerrainSlopeAndStaticObstacles()
    {
        TerrainWorld terrain = CreateRampTerrain(
            chunkSize: 32.0f,
            positiveXHeight: 16.0f);

        var obstacle = new AxisAlignedBounds(
            new Vector3(20.0f, -1.0f, 20.0f),
            new Vector3(24.0f, 4.0f, 24.0f));

        NavigationGrid grid = NavigationGridBuilder.Build(
            terrain,
            [obstacle],
            new NavigationGridSettings
            {
                CellSizeMeters = 4.0f,
                StaticObstacleClearanceMeters = 0.0f
            });

        Assert.Equal(8, grid.Width);
        Assert.Equal(8, grid.Height);

        Assert.True(
            grid.TryWorldToCell(
                new Vector3(22.0f, 0.0f, 22.0f),
                out NavigationCellCoordinate obstacleCell));
        Assert.True(grid.GetCell(obstacleCell).StaticBlocked);

        NavigationCellCoordinate slopeCell = new(1, 1);
        Assert.False(
            grid.IsTraversable(
                slopeCell,
                NavigationCapabilities.For(
                    NavigationMovementClass.Wheeled)));
        Assert.True(
            grid.IsTraversable(
                slopeCell,
                new NavigationCapabilities(
                    NavigationMovementClass.Infantry,
                    maximumSlopeDegrees: 60.0f)));
    }

    [Fact]
    public void NegativeWorldCoordinatesMapToStableGridCells()
    {
        var settings = new WorldGridSettings
        {
            ChunkSizeMeters = 32.0f,
            HeightSamplesPerSide = 9
        };

        TerrainWorld terrain = new(
            settings,
            [
                CreateFlatChunk(new ChunkCoordinate(-1, -1), settings),
                CreateFlatChunk(new ChunkCoordinate(0, -1), settings),
                CreateFlatChunk(new ChunkCoordinate(-1, 0), settings),
                CreateFlatChunk(new ChunkCoordinate(0, 0), settings)
            ]);

        NavigationGrid grid = NavigationGridBuilder.Build(
            terrain,
            settings: new NavigationGridSettings
            {
                CellSizeMeters = 8.0f
            });

        Assert.True(
            grid.TryWorldToCell(
                new Vector3(-31.0f, 0.0f, -31.0f),
                out NavigationCellCoordinate coordinate));
        Assert.Equal(new NavigationCellCoordinate(0, 0), coordinate);

        Vector3 center = grid.GetCellCenter(coordinate);
        Assert.Equal(-28.0f, center.X);
        Assert.Equal(-28.0f, center.Z);
    }

    private static TerrainWorld CreateRampTerrain(
        float chunkSize,
        float positiveXHeight)
    {
        var settings = new WorldGridSettings
        {
            ChunkSizeMeters = chunkSize,
            HeightSamplesPerSide = 3
        };

        float[] heights =
        [
            0.0f, positiveXHeight * 0.5f, positiveXHeight,
            0.0f, positiveXHeight * 0.5f, positiveXHeight,
            0.0f, positiveXHeight * 0.5f, positiveXHeight
        ];

        return new TerrainWorld(
            settings,
            [
                new TerrainChunk(
                    new ChunkCoordinate(0, 0),
                    new TerrainHeightfield(
                        3,
                        chunkSize,
                        heights))
            ]);
    }

    private static TerrainChunk CreateFlatChunk(
        ChunkCoordinate coordinate,
        WorldGridSettings settings)
    {
        int count =
            settings.HeightSamplesPerSide *
            settings.HeightSamplesPerSide;

        return new TerrainChunk(
            coordinate,
            new TerrainHeightfield(
                settings.HeightSamplesPerSide,
                settings.ChunkSizeMeters,
                new float[count]));
    }
}
