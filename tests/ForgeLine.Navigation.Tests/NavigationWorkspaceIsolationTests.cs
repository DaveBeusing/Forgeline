using System.Numerics;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Navigation.Tests;

public sealed class NavigationWorkspaceIsolationTests
{
    [Fact]
    public void ReusedFrontiersRemainIsolatedAcrossWorldsSuccessFailureAndConcurrentSearches()
    {
        var settings = new WorldGridSettings { ChunkSizeMeters = 64, HeightSamplesPerSide = 17 };
        var terrain = new TerrainWorld(settings,
            [new TerrainChunk(new ChunkCoordinate(0, 0), new TerrainHeightfield(17, 64, new float[17 * 17]))]);
        var grid = new NavigationGridSettings { CellSizeMeters = 4, StaticObstacleClearanceMeters = 0 };
        var sectors = new NavigationSectorSettings { SectorSizeCells = 16 };
        var open = new HierarchicalPathfinder(NavigationWorld.Build(terrain, [], grid, sectors));
        var blocked = new HierarchicalPathfinder(NavigationWorld.Build(terrain,
            [new AxisAlignedBounds(new Vector3(4.2f, -1, 0.2f), new Vector3(7.8f, 2, 3.8f)),
             new AxisAlignedBounds(new Vector3(0.2f, -1, 4.2f), new Vector3(3.8f, 2, 7.8f))], grid, sectors));
        NavigationCapabilities capabilities = NavigationCapabilities.For(NavigationMovementClass.Tracked);

        Parallel.For(0, 128, iteration =>
        {
            NavigationSearchResult rejected = blocked.FindPath(new Vector3(2, 0, 2), new Vector3(6, 0, 6), capabilities);
            Assert.False(rejected.Succeeded);
            Assert.Equal(NavigationFailureReason.NoLocalRoute, rejected.FailureReason);
            Vector3 destination = iteration % 2 == 0 ? new Vector3(62, 0, 62) : new Vector3(2, 0, 62);
            NavigationSearchResult accepted = open.FindPath(new Vector3(2, 0, 2), destination, capabilities);
            Assert.True(accepted.Succeeded);
            Assert.NotNull(accepted.Path);
            Assert.Equal(destination, accepted.Path.Waypoints[^1]);
            Assert.Equal(16, accepted.Path.Diagnostics.ExpandedLocalNodes);
        });
    }
}
