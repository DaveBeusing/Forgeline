using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Navigation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class NavigationReverseMotionTests
{
    [Fact]
    public void ForwardVelocityMustBrakeBeforeAnyReverseProposal()
    {
        var (world, vehicle) = Setup();
        vehicle = vehicle with { Movement = vehicle.Movement with { Velocity = Vector3.UnitZ } };
        Assert.False(NavigationReverseMotion.TryStep(vehicle, new(3000, 0.5f, 2990), 1, 0.05f, world, out var end, out _, out _));
        Assert.Equal(vehicle.Pose, end);
    }

    [Fact]
    public void QuantizedLargeWorldCoordinatesCannotExceedReverseSpeedCap()
    {
        var (world, vehicle) = Setup();
        vehicle = vehicle with { Movement = vehicle.Movement with { Velocity = -Vector3.UnitZ } };
        Assert.True(NavigationReverseMotion.TryStep(vehicle, new(3000, 0.5f, 2990), 1, 0.05f, world,
            out var end, out float speed, out _));
        double actual = Math.Abs((double)end.Position.Z - vehicle.Pose.Position.Z) / 0.05f;
        Assert.InRange(actual, 0.9, 1.00001);
        Assert.InRange(speed, 0.9f, 1.00001f);
    }

    [Theory]
    [InlineData(7, 4)]
    [InlineData(6, 1)]
    public void ReverseSteeringReachesASideEscapeWithinAccelerationAndYawBounds(float side, float rear)
    {
        var (world, vehicle) = Setup();
        Vector3 target = new(3000 - side, 0.5f, 3000 - rear);
        for (int tick = 0; tick < 300; tick++)
        {
            var start = vehicle;
            Assert.True(NavigationReverseMotion.TryStep(start, target, 1, 0.05f, world,
                out var end, out float speed, out float heading));
            Assert.InRange(speed, 0, 1.00001f);
            Assert.InRange(MathF.Abs(GroundMovementState.NormalizeAngle(heading - start.Movement.HeadingRadians)), 0,
                start.Profile.MaximumYawRate * 0.05f + 0.00001f);
            vehicle = start with { Pose = end, Movement = start.Movement with
                { Velocity = (end.Position - start.Pose.Position) / 0.05f, HeadingRadians = heading } };
            if (Vector3.Distance(vehicle.Pose.Position, target) <= 0.2f) break;
        }
        Assert.InRange(Vector3.Distance(vehicle.Pose.Position, target), 0, 0.2f);
    }

    private static (NavigationWorldSnapshot, NavigationVehicleObservation) Setup()
    {
        var settings = new WorldGridSettings { ChunkSizeMeters = 4096, HeightSamplesPerSide = 17 };
        var terrain = new TerrainWorld(settings,
            [new TerrainChunk(new ChunkCoordinate(0, 0), new TerrainHeightfield(17, 4096, new float[289]))]);
        var world = NavigationWorld.Build(terrain, gridSettings: new NavigationGridSettings { CellSizeMeters = 256 });
        var capabilities = NavigationCapabilities.For(NavigationMovementClass.Wheeled);
        var snapshot = new NavigationWorldSnapshot(world, world.GetSectorGraph(capabilities), capabilities, terrain,
            0, 1, NavigationModuleAvailability.NotConnected, [], []);
        var pose = new NavigationPose(new Vector3(3000, 0.5f, 3000), Quaternion.Identity);
        var vehicle = new NavigationVehicleObservation(new EntityId(1, 1), new PlayerId(1), pose,
            new(new Vector3(0.5f), 0.1f, 6, 4, 4, 2), GroundMovementState.Stationary(), null, null, null, null, null, null);
        return (snapshot, vehicle);
    }
}
