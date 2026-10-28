using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class NavigationAcceptedMotionProgressTests
{
    [Fact]
    public void HeightOnlyFinalSegmentRetainsWaypointWithoutVetoingAcceptedMotion()
    {
        var tick = new SimulationTick(10);
        var order = new MovementOrder(new(1), new(4, 1, 4), SimulationTick.Zero, new(1));
        var binding = new NavigationOrderBinding(new EntityId(1, 1), order, 1, 1, 0, 0);
        var revision = new NavigationSnapshotRevision(new NavigationVersion(1), 0);
        var profile = new NavigationVehicleProfile(new(1, 1, 2), .25f, 10, 5, 5, 2);
        var specification = new NavigationSearchSpecification(1, new(new(4, 0, 4), Quaternion.Identity),
            order.WorldTarget, NavigationTargetSemantics.Transit, new(0, 1, null, 0),
            NavigationCapabilities.For(NavigationMovementClass.Wheeled), profile, revision, 1, 1000, 1024 * 1024, 16);
        var route = new NavigationRouteProposal(specification, order.WorldTarget, [order.WorldTarget], [], default, 16);
        var start = new NavigationPose(new(7, 0, 4), Quaternion.Identity);
        var vehicle = default(NavigationVehicleObservation) with { Pose = start };
        var input = new NavigationMotionInput(binding, tick, .05f, vehicle, default, route, 1, true);
        var accepted = new NavigationMotionProposal(NavigationModuleAvailability.Available, binding, tick, revision,
            start, start with { Position = new(6.95f, 0, 4) }, 1, 0, 0, 3, NavigationBlockReason.None);
        Assert.True(NavigationAcceptedMotionProgress.TryGetNextWaypoint(input, accepted, 0, 0, 17, out _, out int next));
        Assert.Equal(0, next);
    }
}
