using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;
namespace ForgeLine.Game.Tests;

public sealed class UnpredictedMotionVetoTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(7, true)]
    [InlineData(8, false)]
    [InlineData(9, false)]
    public void AStoppedVehicleCanUseASweptSlowClearanceAfterItsActualTurnIsVetoed(int control, bool moves)
    {
        using var scene = CentralDivideScenario.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 2026) with
        { Participants = CentralDivideScenario.CreateDefaultParticipants(false, false) }, TestContext.Current.CancellationToken);
        var capabilities = NavigationCapabilities.For(NavigationMovementClass.Wheeled);
        var world = scene.Services.Navigation.World;
        var snapshot = new NavigationWorldSnapshot(world, world.GetSectorGraph(capabilities), capabilities, scene.Terrain, 0, 1, NavigationModuleAvailability.NotConnected, [], []);
        var entity = new EntityId(5000, 1); var owner = new PlayerId(2);
        var pose = new NavigationPose(new(2621.2397f, 9.386013f, 1563.8428f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2));
        var profile = new NavigationVehicleProfile(new(1.6f, 1.2f, 3.2f), .25f, 8, 4, 4, 1);
        var root = new MovementOrder(owner, new(2546.3f, 10.014151f, 1580.5f), SimulationTick.Zero, new(1));
        var direct = root with { WorldTarget = new(2584, 8.584558f, 1560) };
        var vehicle = new NavigationVehicleObservation(entity, owner, pose, profile, GroundMovementState.Stationary(-MathF.PI / 2), direct, root, null, null, null, null);
        var binding = new NavigationOrderBinding(entity, root, 1, 1, 0, 0);
        var request = new NavigationSearchSpecification(1, pose, root.WorldTarget, NavigationTargetSemantics.Transit, new(0, .1f, null, 0), capabilities, profile, snapshot.Revision, snapshot.KnowledgeScope, 10000, 100000, 4);
        var route = new NavigationRouteProposal(request, root.WorldTarget, [direct.WorldTarget, new(2568, 8.769756f, 1576), root.WorldTarget], [], default, 128);
        var collision = new NavigationCollisionFrame(snapshot, 50, new AxisAlignedBounds(new(2500, -100, 1450), new(2700, 100, 1650)), true, [
         new(NavigationReadAdapter.GetObstacleIdentity(entity),NavigationObstacleKind.Vehicle,pose,profile.HalfExtents,Vector3.Zero),
   new(new(NavigationObstacleSource.Entity,5001,1),NavigationObstacleKind.Vehicle,new(new(2622.927f,9.34427f,1558.4082f),Quaternion.CreateFromAxisAngle(Vector3.UnitY,-.022758469f)),new(1.7f,1.25f,3.4f),Vector3.Zero)], 10, bodyPosesAreFixed: true);
        INavigationGeometryQuery geometry = control == 4 ? new VetoGeometry() : control == 5 ? new MissingGeometry() : new NavigationVehicleGeometry(snapshot, scene.Terrain, 128, .5f);
        var input = new NavigationMotionInput(binding, new(50), .05f, vehicle,
         new(NavigationModuleAvailability.Available, binding, new(50), NavigationTrafficAction.Proceed, 8, null, NavigationBlockReason.None), route, 8, true);
        if (control == 1) input = input with { Vehicle = vehicle with { Movement = vehicle.Movement with { Velocity = new(-2, 0, 0) } } };
        if (control == 7) input = input with { Vehicle = vehicle with { Movement = vehicle.Movement with { Velocity = new(-.5f, 0, 0) } } };
        if (control == 8) input = input with { Vehicle = vehicle with { Movement = vehicle.Movement with { Velocity = new(.5f, 0, 0) } } };
        if (control == 9) input = input with { Vehicle = vehicle with { Movement = vehicle.Movement with { Velocity = new(0, 0, .5f) } } };
        if (control == 2) input = input with { CanMove = false };
        if (control == 3) input = input with { Traffic = input.Traffic with { Action = NavigationTrafficAction.Hold } };
        if (control == 6) input = input with { Tick = new(49) };
        var planner = new ContinuousNavigationMotionPlanner(new(128, 8, 12, 2, 3));
        var proposal = planner.Propose(input, collision, geometry, 100000, out int work);
        Assert.InRange(work, 0, 100000);
        Assert.Equal(binding, proposal.Binding);
        Assert.True(moves == (proposal.End != proposal.Start), $"proposal={proposal}; work={work}");
        if (!moves) { Assert.Equal(0, proposal.DesiredSpeed); return; }
        Assert.InRange(proposal.DesiredSpeed, 0.0001f, 1);
        var checkedStep = geometry.Sweep(collision, new(NavigationReadAdapter.GetObstacleIdentity(entity), proposal.Start, proposal.End, profile, .05f), 10000);
        Assert.Equal(NavigationGeometryStatus.Clear, checkedStep.Status);
    }
    private sealed class VetoGeometry : INavigationGeometryQuery
    {
        public NavigationModuleAvailability Availability => NavigationModuleAvailability.Available;
        public NavigationSweepResult Sweep(NavigationCollisionFrame frame, in NavigationSweep movement, int workAllowance) => new(NavigationGeometryStatus.Blocked, frame.World.Revision, frame.Tick, 0, NavigationBlockReason.MovingVehicle, new(NavigationObstacleSource.Entity, 5001, 1), 1);
    }
    private sealed class MissingGeometry : INavigationGeometryQuery
    {
        public NavigationModuleAvailability Availability => NavigationModuleAvailability.Unavailable;
        public NavigationSweepResult Sweep(NavigationCollisionFrame frame, in NavigationSweep movement, int workAllowance) => throw new InvalidOperationException("Unavailable geometry cannot authorize clearance.");
    }
}
