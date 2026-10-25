using System.Numerics;
using ForgeLine.Navigation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed partial class PredictiveTrafficBatchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HorizonObservationIsAnExistingGeometryQueryAndDoesNotChangeMotionWorkOrQueries(bool localDetour)
    {
        var (frame, intents) = Crossing();
        var intent = intents[0];
        var vehicle = frame.Vehicles[0] with { RouteOrder = intent.Binding.OriginalOrder };
        if (localDetour) vehicle = vehicle with { Movement = GroundMovementState.Stationary(vehicle.Movement.HeadingRadians) };
        var request = new NavigationSearchSpecification(intent.Binding.RequestId, vehicle.Pose,
            intent.Binding.OriginalOrder.WorldTarget, NavigationTargetSemantics.Transit, new(0, .1f, null, 0),
            frame.Collision.World.Capabilities, vehicle.Profile, frame.Collision.World.Revision,
            frame.Collision.World.KnowledgeScope, 10000, 100000, 4);
        var route = new NavigationRouteProposal(request, request.OriginalDestination, [request.OriginalDestination], [], default, 128);
        var input = new NavigationMotionInput(intent.Binding, frame.Tick, .05f, vehicle,
            new(NavigationModuleAvailability.Available, intent.Binding, frame.Tick, NavigationTrafficAction.Proceed,
                localDetour ? 1 : vehicle.Profile.MaximumSpeed, localDetour ? new Vector3(14, .5f, 24) : null,
                localDetour ? NavigationBlockReason.MovingVehicle : NavigationBlockReason.None), route, vehicle.Profile.MaximumSpeed, true);
        var planner = new ContinuousNavigationMotionPlanner(new(128, 8, 12, 2, 3));
        var plainGeometry = new HorizonGeometry();
        var tracedGeometry = new HorizonGeometry();
        var plain = planner.ProposeCore(input, frame.Collision, plainGeometry, 10000,
            out int plainWork, out _, out _, out _, out var disabled);
        var traced = planner.ProposeCore(input, frame.Collision, tracedGeometry, 10000,
            out int tracedWork, out _, out _, out int usedWaypoint, out var captured, captureDiagnostics: true);
        Assert.Null(disabled);
        if (localDetour) { Assert.Equal(-1, usedWaypoint); Assert.NotEqual(traced.Start, traced.End); }
        Assert.Equal(plain, traced);
        Assert.Equal(plainWork, tracedWork);
        Assert.Equal(plainGeometry.Sweeps.ToArray(), tracedGeometry.Sweeps.ToArray());
        var horizon = Assert.IsType<NavigationCheckedHorizon>(captured);
        Assert.Equal(input.Binding, horizon.Binding);
        Assert.Equal(frame.Tick, horizon.Source.SourceTick);
        Assert.Equal(frame.Collision.World.Revision, horizon.Source.Revision);
        Assert.Equal(1, horizon.AllowedFraction);
        Assert.Contains(tracedGeometry.Sweeps, query => query.Start == horizon.Start && query.End == horizon.CheckedEnd &&
            query.Vehicle == horizon.Profile && query.DurationSeconds == horizon.DurationSeconds);
    }

    private sealed class HorizonGeometry : INavigationGeometryQuery
    {
        public NavigationModuleAvailability Availability => NavigationModuleAvailability.Available;
        public List<NavigationSweep> Sweeps { get; } = new();
        public NavigationSweepResult Sweep(NavigationCollisionFrame frame, in NavigationSweep movement, int workAllowance)
        {
            Sweeps.Add(movement);
            return new(NavigationGeometryStatus.Clear, frame.World.Revision, frame.Tick, 1, NavigationBlockReason.None, null, 1);
        }
    }
}
