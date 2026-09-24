using System.Numerics;
using ForgeLine.Navigation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class NavigationDebugVisualizationTests
{
    [Fact]
    public void NavigationWorldBuildsGridSectorAndRouteDebugGeometry()
    {
        TerrainWorld terrain = CreateFlatTerrain();
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

        NavigationCapabilities capabilities =
            NavigationCapabilities.For(
                NavigationMovementClass.Tracked);
        var pathfinder = new HierarchicalPathfinder(world);
        NavigationSearchResult search = pathfinder.FindPath(
            new Vector3(2.0f, 0.0f, 2.0f),
            new Vector3(30.0f, 0.0f, 30.0f),
            capabilities);

        Assert.True(search.Succeeded);

        var debugDraw = new DebugDraw
        {
            Enabled = true
        };

        NavigationDebugVisualization.Draw(
            debugDraw,
            world,
            capabilities,
            search.Path,
            new Vector3(16.0f, 0.0f, 16.0f),
            cellRadius: 3,
            sectorRadius: 1);

        Assert.True(debugDraw.Lines.Length > 0);
    }

    private static TerrainWorld CreateFlatTerrain()
    {
        var settings = new WorldGridSettings
        {
            ChunkSizeMeters = 32.0f,
            HeightSamplesPerSide = 9
        };

        return new TerrainWorld(
            settings,
            [
                new TerrainChunk(
                    new ChunkCoordinate(0, 0),
                    new TerrainHeightfield(
                        settings.HeightSamplesPerSide,
                        settings.ChunkSizeMeters,
                        new float[
                            settings.HeightSamplesPerSide *
                            settings.HeightSamplesPerSide]))
            ]);
    }
}
