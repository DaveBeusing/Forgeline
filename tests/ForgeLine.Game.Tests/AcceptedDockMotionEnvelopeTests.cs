using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Navigation;
using ForgeLine.World;
using Xunit;
namespace ForgeLine.Game.Tests;
public sealed class AcceptedDockMotionEnvelopeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcceptedOwnerSweepAllowsClearBrakingButRetainsPhysicalCollision(bool collision)
    {
        using var scene = CentralDivideScenario.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 2026) with
        { Participants = CentralDivideScenario.CreateDefaultParticipants(false, false) }, TestContext.Current.CancellationToken);
        var caps = NavigationCapabilities.For(NavigationMovementClass.Wheeled);
        var world = scene.Services.Navigation.World;
        var snapshot = new NavigationWorldSnapshot(world, world.GetSectorGraph(caps), caps, scene.Terrain, 0, 1, NavigationModuleAvailability.NotConnected, [], []);
        var profile = new NavigationVehicleProfile(new(1.6f, 1.2f, 3.2f), .25f, 14.5f, 7, 10, 2.5f);
        var ownerStart = new NavigationPose(new(2622.5999f, 9.324579f, 1553.7333f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, -.020107135f));
        var ownerEnd = new NavigationPose(new(2622.5996f, 9.324668f, 1553.7507f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, -.020104084f));
        var start = new NavigationPose(new(2626.405f, 9.312001f, 1558.7965f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, -1.5500659f));
        var end = start with { Position = new(2626.4297f, 9.311756f, 1558.7963f) };
        if (collision) { start = start with { Position = start.Position - Vector3.UnitX * .1f }; end = end with { Position = end.Position - Vector3.UnitX * .1f }; }
        Assert.True(NavigationMotionArbitrator.TryBuildSweptOccupancy(ownerStart, ownerEnd, profile, out var pose, out var half, includeNumericalPadding: false));
        var body = new NavigationCollisionBody(new(NavigationObstacleSource.TrafficReservation, 88, 1), NavigationObstacleKind.Reservation, pose, half, Vector3.Zero);
        var frame = new NavigationCollisionFrame(snapshot, 125, new AxisAlignedBounds(new(2500, -100, 1450), new(2750, 100, 1650)), true, [body], 4, bodyPosesAreFixed: true);
        var geometry = new NavigationVehicleGeometry(snapshot, scene.Terrain, 128, .5f);
        var result = geometry.Sweep(frame, new(new(NavigationObstacleSource.Entity, 90, 1), start, end, profile, .05f), 10000);
        Assert.Equal(collision ? NavigationGeometryStatus.Blocked : NavigationGeometryStatus.Clear, result.Status);
        if (collision) Assert.Equal(body.Identity, result.Blocker);
    }
}
