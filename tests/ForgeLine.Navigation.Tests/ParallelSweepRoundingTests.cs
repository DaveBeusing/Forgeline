using System.Numerics;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Navigation.Tests;

public sealed class ParallelSweepRoundingTests
{
    [Theory]
    [InlineData(0f, 0f, true)]
    [InlineData(-.02f, 0f, false)]
    [InlineData(0f, .03f, false)]
    [InlineData(.02f, 0f, true)]
    public void RoundedMidpointCannotBlockAConservativelySeparatedParallelSweep(float extraGap, float turn, bool clear)
    {
        var settings = new WorldGridSettings { ChunkSizeMeters = 64, HeightSamplesPerSide = 17 };
        var terrain = new TerrainWorld(settings,
            [new TerrainChunk(new(41, 24), new TerrainHeightfield(17, 64, new float[289]))]);
        var world = NavigationWorld.Build(terrain, gridSettings: new NavigationGridSettings { CellSizeMeters = 4 });
        var caps = NavigationCapabilities.For(NavigationMovementClass.Wheeled);
        var snapshot = new NavigationWorldSnapshot(world, world.GetSectorGraph(caps), caps, terrain, 0, 1,
            NavigationModuleAvailability.NotConnected, [], []);
        var geometry = new NavigationVehicleGeometry(snapshot, terrain, 128, .25f);
        var obstaclePose = new NavigationPose(new(2634.2917f, 1.2f, 1550.5f), new(0, .70710677f, 0, .70710677f));
        var start = new NavigationPose(new(2638.5103f, 1.2f, 1553.8772f), new(0, -.30569097f, 0, .9521308f));
        var profile = new NavigationVehicleProfile(new(1.6f, 1.2f, 3.2f), .25f, 13, 6.5f, 10, 2.5f);
        var frame = new NavigationCollisionFrame(snapshot, 1, new(new(2624, -10, 1536), new(2688, 10, 1600)), true,
            [new(new(NavigationObstacleSource.Entity, 1, 1), NavigationObstacleKind.Vehicle, obstaclePose,
                profile.HalfExtents, Vector3.Zero)], 4, bodyPosesAreFixed: true);
        start = start with { Position = start.Position + Vector3.Transform(Vector3.UnitX, start.Rotation) * extraGap };
        float yaw = -.6213281f + turn;
        var end = new NavigationPose(start.Position + new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw)) * .25f,
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw));
        var result = geometry.Sweep(frame, new(new(NavigationObstacleSource.Entity, 2, 1), start, end, profile, .25f), 10000);
        Assert.Equal(clear ? NavigationGeometryStatus.Clear : NavigationGeometryStatus.Blocked, result.Status);
        Assert.Equal(clear ? 1 : 0, result.AllowedFraction);
        Assert.InRange(result.WorkConsumed, 1, 10000);
    }
}
