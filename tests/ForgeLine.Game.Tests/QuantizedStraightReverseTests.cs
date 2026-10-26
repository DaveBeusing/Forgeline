using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;
namespace ForgeLine.Game.Tests;

public sealed class QuantizedStraightReverseTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AQuantizedStraightTargetDoesNotAddRotationToAnOtherwiseClearReverse(bool lateralTarget)
    {
        using var scene = CentralDivideScenario.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 2026) with { Participants = CentralDivideScenario.CreateDefaultParticipants(false, false) }, TestContext.Current.CancellationToken);
        var caps = NavigationCapabilities.For(NavigationMovementClass.Wheeled); var world = scene.Services.Navigation.World;
        var snapshot = new NavigationWorldSnapshot(world, world.GetSectorGraph(caps), caps, scene.Terrain, 0, 1, NavigationModuleAvailability.NotConnected, [], []);
        var pose = new NavigationPose(new(713.8465f, 3.8246546f, 1561.5065f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 2.172616f));
        var profile = new NavigationVehicleProfile(new(1.6f, 1.2f, 3.2f), .25f, 14.5f, 7, 10, 2.5f);
        var vehicle = new NavigationVehicleObservation(new(5000, 1), new(1), pose, profile, GroundMovementState.Stationary(2.172616f), null, null, null, null, null, null);
        var target = pose.Position + Vector3.Transform(-Vector3.UnitZ, pose.Rotation) * 3;
        if (lateralTarget) target += Vector3.Transform(Vector3.UnitX, pose.Rotation) * .25f;
        Assert.True(NavigationReverseMotion.TryStep(vehicle, target, 1, .125f, snapshot, out var end, out var speed, out _));
        Assert.InRange(speed, 0, 1);
        if (lateralTarget) { Assert.NotEqual(pose.Rotation, end.Rotation); return; }
        Assert.Equal(pose.Rotation, end.Rotation);
        var collision = new NavigationCollisionFrame(snapshot, 0, new AxisAlignedBounds(new(600, -100, 1450), new(1100, 100, 1650)), true, [new(new(NavigationObstacleSource.Entity, 5001, 1), NavigationObstacleKind.Vehicle, new(new(719.8574f, 4.094695f, 1557.4961f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, -.8768718f)), new(2, 1.5f, 3.6f), Vector3.Zero)], 10, bodyPosesAreFixed: true);
        var geometry = new NavigationVehicleGeometry(snapshot, scene.Terrain, 128, .5f);
        Assert.Equal(NavigationGeometryStatus.Clear, geometry.Sweep(collision, new(NavigationReadAdapter.GetObstacleIdentity(vehicle.Entity), pose, pose, profile, .125f), 10000).Status);
        Assert.Equal(NavigationGeometryStatus.Clear, geometry.Sweep(collision, new(NavigationReadAdapter.GetObstacleIdentity(vehicle.Entity), pose, end, profile, .125f), 10000).Status);
    }
}
