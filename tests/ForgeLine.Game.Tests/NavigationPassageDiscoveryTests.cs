using System.Numerics;
using ForgeLine.Navigation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class NavigationPassageDiscoveryTests
{
    [Fact]
    public void OneCoarseCellWideDoesNotMeanOnePhysicalVehicleLane()
    {
        var settings = new WorldGridSettings { ChunkSizeMeters = 128, HeightSamplesPerSide = 17 };
        var terrain = new TerrainWorld(settings,
            [new TerrainChunk(new ChunkCoordinate(0, 0), new TerrainHeightfield(17, 128, new float[289]))]);
        AxisAlignedBounds[] walls = [new(new(0, -1, 8), new(31, 4, 120)),
            new(new(65, -1, 8), new(128, 4, 120))];
        var world = NavigationWorld.Build(terrain, walls, new NavigationGridSettings { CellSizeMeters = 32 },
            new NavigationSectorSettings { SectorSizeCells = 4 });
        var capabilities = NavigationCapabilities.For(NavigationMovementClass.Wheeled);
        var snapshot = new NavigationWorldSnapshot(world, world.GetSectorGraph(capabilities), capabilities, terrain,
            0, 1, NavigationModuleAvailability.NotConnected, [], []);
        Assert.True(snapshot.Grid.IsTraversable(new(1, 1), capabilities));
        Assert.False(snapshot.Grid.IsTraversable(new(0, 1), capabilities));
        Assert.False(snapshot.Grid.IsTraversable(new(2, 1), capabilities));
        var result = NavigationPassageDiscovery.Capture(snapshot,
            [new(new(48, .5f, 16), new(48, .5f, 112), 64, 3.7f)], 10000);
        Assert.True(result.Complete);
        Assert.Empty(result.Passages);
    }

    [Fact]
    public void OppositeApproachesCaptureTheSameWholeDirectedLane()
    {
        var snapshot = Corridor();
        NavigationPassageProbe[] probes =
            [new(new(18, 0.5f, 2), new(18, 0.5f, 62), 36, 3.7f), new(new(18, 0.5f, 62), new(18, 0.5f, 2), 36, 3.7f)];
        var first = NavigationPassageDiscovery.Capture(snapshot, probes, 10000);
        Array.Reverse(probes);
        var second = NavigationPassageDiscovery.Capture(snapshot, probes, 10000);
        Assert.True(first.Complete);
        Assert.True(second.Complete);
        Assert.Equal(first.Passages.ToArray(), second.Passages.ToArray());
        var passage = Assert.Single(first.Passages);
        Assert.Equal(Quaternion.Identity, passage.Pose.Rotation);
        Assert.Equal(18, passage.Pose.Position.X);
        Assert.True(passage.HalfExtents.Z > 20);
        Assert.Equal(2, passage.HalfExtents.X);
        Assert.InRange(first.WorkConsumed, 1, 10000);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ExhaustedCaptureCannotCertifyPartialLane(int allowance)
    {
        var result = NavigationPassageDiscovery.Capture(Corridor(),
            [new(new(18, 0.5f, 2), new(18, 0.5f, 62), 36, 3.7f)], allowance);
        Assert.False(result.Complete);
        Assert.InRange(result.WorkConsumed, 0, allowance);
    }

    [Fact]
    public void OversizedProbeIsRejectedBeforeItsSampleCountCanOverflow()
    {
        var result = NavigationPassageDiscovery.Capture(Corridor(),
            [new(new(18, 0.5f, 2), new(18, 0.5f, 1e20f), 1e20f, 3.7f)], 10000);
        Assert.False(result.Complete);
        Assert.Equal(0, result.WorkConsumed);
        Assert.Empty(result.Passages);
    }

    private static NavigationWorldSnapshot Corridor()
    {
        var settings = new WorldGridSettings { ChunkSizeMeters = 64, HeightSamplesPerSide = 17 };
        var terrain = new TerrainWorld(settings,
            [new TerrainChunk(new ChunkCoordinate(0, 0), new TerrainHeightfield(17, 64, new float[289]))]);
        AxisAlignedBounds[] walls = [new(new Vector3(0, -1, 8), new Vector3(15.4f, 4, 56)),
            new(new Vector3(20.6f, -1, 8), new Vector3(64, 4, 56))];
        var world = NavigationWorld.Build(terrain, walls, new NavigationGridSettings { CellSizeMeters = 4 });
        var capabilities = NavigationCapabilities.For(NavigationMovementClass.Wheeled);
        return new(world, world.GetSectorGraph(capabilities), capabilities, terrain,
            0, 1, NavigationModuleAvailability.NotConnected, [], []);
    }
}
