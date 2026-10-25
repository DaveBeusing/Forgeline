using System.Numerics;
using ForgeLine.World;
using Xunit;
namespace ForgeLine.Navigation.Tests;
public sealed class FixedPoseOccupancyTests
{
    [Fact]
    public void FixedPoseKeepsExactVehicleFootprintWhileUnknownRotationStaysConservative()
    {
        var (world, geometry, body, profile, coverage) = Setup();
        var unknown = new NavigationCollisionFrame(world, 1, coverage, true, [body], 4);
        var fixedPose = new NavigationCollisionFrame(world, 1, coverage, true, [body], 4, bodyPosesAreFixed: true);
        var sweep = new NavigationSweep(new(NavigationObstacleSource.Entity, 1, 1), new(new(10, .5f, 10), Quaternion.Identity),
            new(new(10, .5f, 11), Quaternion.Identity), profile, .25f);
        Assert.Equal(NavigationGeometryStatus.Blocked, geometry.Sweep(unknown, sweep, 10000).Status);
        var clear = geometry.Sweep(fixedPose, sweep, 10000);
        Assert.Equal(NavigationGeometryStatus.Clear, clear.Status);
        Assert.Equal(1, clear.AllowedFraction);
        Assert.False(unknown.BodyPosesAreFixed);
        Assert.True(fixedPose.BodyPosesAreFixed);
    }
    [Fact]
    public void FixedPoseStillRejectsActualBodyContact()
    {
        var (world, geometry, body, profile, coverage) = Setup();
        var frame = new NavigationCollisionFrame(world, 1, coverage, true, [body], 4, bodyPosesAreFixed: true);
        var sweep = new NavigationSweep(new(NavigationObstacleSource.Entity, 1, 1), new(new(10, .5f, 10), Quaternion.Identity),
            new(new(13, .5f, 10), Quaternion.Identity), profile, 1);
        var blocked = geometry.Sweep(frame, sweep, 10000);
        Assert.NotEqual(NavigationGeometryStatus.Clear, blocked.Status);
        Assert.Equal(NavigationBlockReason.MovingVehicle, blocked.Reason);
        Assert.InRange(blocked.AllowedFraction, 0, .99999f);
    }
    [Fact]
    public void FixedPoseCannotCarryMovingBodyVelocity()
    {
        var (world, _, body, _, coverage) = Setup();
        Assert.Throws<ArgumentException>(() => new NavigationCollisionFrame(world, 1, coverage, true,
            [body with { Velocity = Vector3.UnitX }], 4, bodyPosesAreFixed: true));
    }
    [Theory]
    [InlineData(.0f, false)]
    [InlineData(.24f, false)]
    [InlineData(.26f, true)]
    [InlineData(.4f, true)]
    public void PhysicalClearanceIsSymmetricAcrossDifferentlyRotatedRequesters(float gap, bool clear)
    {
        var (world, geometry, _, _, coverage) = Setup();
        const float heading = -1.4734924f;
        var small = new NavigationPose(new(30, .9f, 30), Quaternion.CreateFromAxisAngle(Vector3.UnitY, heading));
        Vector3 smallHalf = new(.9f), largeHalf = new(1.7f, 1.25f, 3.4f);
        float projectedX = .9f * (MathF.Abs(MathF.Sin(heading)) + MathF.Abs(MathF.Cos(heading)));
        var large = new NavigationPose(new(30 - projectedX - 3.4f - gap, 1.1f, 27.6972f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2));
        foreach (bool reversed in new[] { false, true })
        {
            var moverPose = reversed ? large : small;
            var bodyPose = reversed ? small : large;
            var moverHalf = reversed ? largeHalf : smallHalf;
            var bodyHalf = reversed ? smallHalf : largeHalf;
            var body = new NavigationCollisionBody(new(NavigationObstacleSource.Entity, 2, 1), NavigationObstacleKind.Vehicle, bodyPose, bodyHalf, Vector3.Zero);
            var frame = new NavigationCollisionFrame(world, 1, coverage, true, [body], 4, bodyPosesAreFixed: true);
            var sweep = new NavigationSweep(new(NavigationObstacleSource.Entity, 1, 1), moverPose, moverPose, new(moverHalf, .25f, 4, 4, 4, 2), 0);
            var result = geometry.Sweep(frame, sweep, 10000);
            Assert.Equal(clear ? NavigationGeometryStatus.Clear : NavigationGeometryStatus.Blocked, result.Status);
            Assert.Equal(clear ? 1 : 0, result.AllowedFraction);
        }
    }

    private static (NavigationWorldSnapshot, NavigationVehicleGeometry, NavigationCollisionBody, NavigationVehicleProfile, AxisAlignedBounds) Setup()
    {
        var settings = new WorldGridSettings { ChunkSizeMeters = 64, HeightSamplesPerSide = 17 };
        var terrain = new TerrainWorld(settings, [new TerrainChunk(new(0, 0), new TerrainHeightfield(17, 64, new float[289]))]);
        var world = NavigationWorld.Build(terrain, gridSettings: new NavigationGridSettings { CellSizeMeters = 4 });
        var capabilities = NavigationCapabilities.For(NavigationMovementClass.Wheeled);
        var snapshot = new NavigationWorldSnapshot(world, world.GetSectorGraph(capabilities), capabilities, terrain, 0, 1,
            NavigationModuleAvailability.NotConnected, [], []);
        var body = new NavigationCollisionBody(new(NavigationObstacleSource.Entity, 2, 1), NavigationObstacleKind.Vehicle,
            new(new(12.5f, .5f, 10), Quaternion.Identity), new(.5f, .5f, 2), Vector3.Zero);
        return (snapshot, new(snapshot, terrain, 128, .25f), body, new(new(.5f, .5f, 2), .1f, 4, 4, 4, 2),
            new(new(0, -10, 0), new(64, 10, 64)));
    }
}
