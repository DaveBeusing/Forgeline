using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class PredictiveTrafficBatchTests
{
    [Theory]
    [InlineData(21, 30, true)]
    [InlineData(14, 30, false)]
    [InlineData(21, 10, false)]
    public void ExistingForeignDockOccupantsMayLeaveOutwardWithoutTakingAdmission(float position, float goal, bool outward)
    {
        var (frame, intents) = Crossing();
        float heading = goal > position ? MathF.PI / 2 : -MathF.PI / 2;
        var root = intents[0].Binding.OriginalOrder with { WorldTarget = new(goal, .5f, 20) };
        var vehicle = frame.Vehicles[0] with { Pose = new(new(position, .5f, 20), Quaternion.CreateFromAxisAngle(Vector3.UnitY, heading)),
            DirectOrder = root, Movement = GroundMovementState.Stationary(heading) };
        var intent = intents[0] with { Binding = intents[0].Binding with { OriginalOrder = root }, SteeringTarget = root.WorldTarget };
        var passage = new NavigationTrafficPassage(1, new(new(20, .5f, 20), Quaternion.Identity), new(2, 1, 2));
        var reservation = new NavigationTrafficReservation(7, new(99, 1), 1, 0, NavigationReservationKind.Dock,
            passage.Pose, passage.HalfExtents, SimulationTick.Zero, SimulationTick.Zero, true);
        var captured = new NavigationTrafficFrame(frame.Tick, frame.Collision, true, [vehicle], [reservation], Limits(), [passage]);
        var planner = new PredictiveTrafficPlanner();
        var directive = Assert.Single(planner.PlanBatch(captured, [intent], new ClearGeometry(), 3, 10000, out _));
        Assert.Equal(outward ? NavigationTrafficAction.Proceed : NavigationTrafficAction.Hold, directive.Action);
        Assert.Equal(intent.Binding, directive.Binding);
        Assert.Empty(planner.CaptureState().Leases);
    }

    [Fact]
    public void OutwardDockEvacuationRejectsReentryRotationAndOtherReservationKinds()
    {
        var (frame, _) = Crossing();
        var vehicle = frame.Vehicles[0];
        var reservation = new NavigationTrafficReservation(7, new(99, 1), 1, 0, NavigationReservationKind.Dock,
            new(new(20, .5f, 20), Quaternion.Identity), new(2, 1, 2), SimulationTick.Zero, SimulationTick.Zero, true);
        var start = vehicle.Pose with { Position = new(21, .5f, 20) };
        var outside = start with { Position = new(30, .5f, 20) };
        Assert.False(PredictiveTrafficPlanner.BlocksServiceReservation(vehicle, reservation, [start, outside]));
        Assert.True(PredictiveTrafficPlanner.BlocksServiceReservation(vehicle, reservation, [start, outside, start]));
        Assert.True(PredictiveTrafficPlanner.BlocksServiceReservation(vehicle, reservation,
            [start, start with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, .3f) }]));
        Assert.True(PredictiveTrafficPlanner.BlocksServiceReservation(vehicle,
            reservation with { Kind = NavigationReservationKind.Queue }, [start, outside]));
        Assert.True(PredictiveTrafficPlanner.BlocksServiceReservation(vehicle, reservation, [start]));
    }

    [Fact]
    public void ForeignServiceAdmissionPreventsNewAndRetainedUnenteredPassageClaims()
    {
        var (frame, intents) = Crossing();
        var vehicle = frame.Vehicles[0];
        var passage = new NavigationTrafficPassage(1, new(new(20, .5f, 20), Quaternion.Identity), new(2, 1, 2));
        var reservation = new NavigationTrafficReservation(7, new(99, 1), 1, 0, NavigationReservationKind.Dock,
            passage.Pose, passage.HalfExtents, SimulationTick.Zero, SimulationTick.Zero, true);
        var planner = new PredictiveTrafficPlanner();
        NavigationTrafficDirective Observe(ulong tick, bool reserved, bool entered = false)
        {
            var collision = new NavigationCollisionFrame(frame.Collision.World, tick, frame.Collision.Coverage, true, [], 10);
            var observed = entered ? vehicle with { Pose = passage.Pose } : vehicle;
            var captured = new NavigationTrafficFrame(new(tick), collision, true, [observed],
                reserved ? [reservation] : [], Limits(), [passage]);
            return Assert.Single(planner.PlanBatch(captured, [intents[0]], new ClearGeometry(), 3, 10000, out _));
        }
        Assert.Equal(NavigationTrafficAction.Hold, Observe(1, true).Action);
        Assert.Empty(planner.CaptureState().Leases);
        Assert.Equal(NavigationTrafficAction.Proceed, Observe(2, false).Action);
        Assert.False(Assert.Single(planner.CaptureState().Leases).Entered);
        Assert.Equal(NavigationTrafficAction.Hold, Observe(3, true).Action);
        Assert.Empty(planner.CaptureState().Leases);
        planner.RestoreState(new(3, [], [new(passage, vehicle.Entity, 1, 1, 2, 3, true)]));
        Assert.Equal(NavigationTrafficAction.Hold, Observe(4, true, entered: true).Action);
        Assert.Equal(vehicle.Entity, Assert.Single(planner.CaptureState().Leases).Owner);
        Assert.True(Assert.Single(planner.CaptureState().Leases).Entered);
    }

    [Fact]
    public void FullyClearedVehicleCanContinueAwayWithoutBeingRecapturedByItsBoundingCircle()
    {
        var passage = new NavigationTrafficPassage(1, new(new(30, .5f, 30), Quaternion.Identity), new(4, 1, 4));
        var profile = new NavigationVehicleProfile(new(1.6f, 1.1f, 3.2f), .25f, 6, 4, 4, 2);
        var owner = new EntityId(1, 1);
        var other = new EntityId(2, 1);
        var planner = new PredictiveTrafficPlanner();
        planner.RestoreState(new(3, [], [new(passage, owner, 1, 1, 1, 1, true)]));
        var start = new NavigationPose(new(30, .5f, 37.55f), Quaternion.Identity);
        Assert.True(start.Position.Z - profile.HalfExtents.Z - profile.ClearanceMeters > passage.Pose.Position.Z + passage.HalfExtents.Z);
        var outward = start with { Position = start.Position + Vector3.UnitZ * .1f };
        Assert.True(planner.TryCheckPassage(other, start, outward, profile, 100, out _, out bool complete));
        Assert.True(complete);
        var inward = start with { Position = start.Position - Vector3.UnitZ * .3f };
        Assert.False(planner.TryCheckPassage(other, start, inward, profile, 100, out _, out _));
        Assert.Equal(owner, Assert.Single(planner.CaptureState().Leases).Owner);
        Assert.True(Assert.Single(planner.CaptureState().Leases).Entered);
    }

    [Fact]
    public void WaitingForAnExternalServiceReservationDoesNotInventAPhysicalReverseEscape()
    {
        var (frame, intents) = Crossing();
        var vehicle = frame.Vehicles[0] with { Movement = GroundMovementState.Stationary(MathF.PI / 2) };
        var reservation = new NavigationTrafficReservation(7, new(99, 1), 1, 0, NavigationReservationKind.Dock,
            new(new(20, .5f, 20), Quaternion.Identity), new(1, 1, 1), SimulationTick.Zero, SimulationTick.Zero, true);
        var planner = new PredictiveTrafficPlanner();
        NavigationTrafficDirective Observe(ulong tick)
        {
            var collision = new NavigationCollisionFrame(frame.Collision.World, tick, frame.Collision.Coverage, true, [], 10);
            var captured = new NavigationTrafficFrame(new(tick), collision, true, [vehicle], [reservation], Limits());
            return Assert.Single(planner.PlanBatch(captured, [intents[0]], new ClearGeometry(), 3, 10000, out _));
        }
        Assert.Equal(NavigationTrafficAction.Hold, Observe(1).Action);
        var waiting = Observe(50);
        Assert.Equal(NavigationBlockReason.ReservedCorridor, waiting.Reason);
        Assert.Equal(NavigationTrafficAction.Hold, waiting.Action);
        Assert.Null(Assert.Single(planner.CaptureState().Vehicles).RecoveryTarget);
    }

    [Fact]
    public void StaticFlankBuildingCannotOwnAdmissionToAnOtherwiseClearPassage()
    {
        var (frame, intents) = Crossing();
        var vehicle = frame.Vehicles[1];
        var intent = intents[1];
        var settings = new WorldGridSettings { ChunkSizeMeters = 64, HeightSamplesPerSide = 17 };
        var terrain = new TerrainWorld(settings,
            [new TerrainChunk(new ChunkCoordinate(0, 0), new TerrainHeightfield(17, 64, new float[289]))]);
        var body = new NavigationCollisionBody(new(NavigationObstacleSource.Entity, 99, 1),
            NavigationObstacleKind.Structure, new(new(24, .5f, 20), Quaternion.Identity), new(.5f), Vector3.Zero);
        var collision = new NavigationCollisionFrame(frame.Collision.World, frame.Tick.Value,
            new AxisAlignedBounds(new(0, -10, 0), new(64, 10, 64)), true, [body], 10);
        var passage = new NavigationTrafficPassage(1, new(new(20, .5f, 20), Quaternion.Identity), new(6, 1, 8));
        var geometry = new NavigationVehicleGeometry(collision.World, terrain, 128, .5f);
        var sweep = new NavigationSweep(NavigationReadAdapter.GetObstacleIdentity(vehicle.Entity), vehicle.Pose,
            vehicle.Pose with { Position = new(20, .5f, 19) }, vehicle.Profile, 1.5f);
        var trailingSweep = sweep with { Start = sweep.End, End = vehicle.Pose with { Position = new(20, .5f, 28) } };
        Assert.Equal(NavigationGeometryStatus.Clear, geometry.Sweep(collision, sweep, 10000).Status);
        Assert.Equal(NavigationGeometryStatus.Clear, geometry.Sweep(collision, trailingSweep, 10000).Status);
        var captured = new NavigationTrafficFrame(frame.Tick, collision, true, [vehicle], [], Limits(), [passage]);
        var planner = new PredictiveTrafficPlanner();
        var directive = Assert.Single(planner.PlanBatch(captured, [intent], geometry, 3, 10000, out _));
        Assert.Equal(NavigationTrafficAction.Proceed, directive.Action);
        Assert.Equal(vehicle.Entity, Assert.Single(planner.CaptureState().Leases).Owner);
        var blocked = new NavigationCollisionFrame(collision.World, collision.Tick, collision.Coverage, true,
            [body with { Pose = new(new(20, .5f, 20), Quaternion.Identity) }], 10);
        Assert.NotEqual(NavigationGeometryStatus.Clear, geometry.Sweep(blocked, trailingSweep, 10000).Status);
    }

    [Fact]
    public void DistantObstructionUsesItsVerifiedBrakingPrefixInsteadOfImmediateCrawl()
    {
        var (frame, intents) = Crossing();
        var stationary = frame.Vehicles[0] with { Movement = GroundMovementState.Stationary(MathF.PI / 2) };
        var single = new NavigationTrafficFrame(frame.Tick, frame.Collision, true, [stationary], [], Limits());
        var directive = Assert.Single(new PredictiveTrafficPlanner().PlanBatch(single, [intents[0]],
            new DistantGeometry(), 3, 10000, out _));
        Assert.Equal(NavigationTrafficAction.Proceed, directive.Action);
        Assert.Equal(NavigationBlockReason.MovingVehicle, directive.Reason);
        Assert.InRange(directive.MaximumSpeed, 1.01f, frame.Vehicles[0].Profile.MaximumSpeed);
        Assert.Equal(intents[0].Binding, directive.Binding);
    }

    private sealed class DistantGeometry : INavigationGeometryQuery
    {
        public NavigationModuleAvailability Availability => NavigationModuleAvailability.Available;
        public NavigationSweepResult Sweep(NavigationCollisionFrame frame, in NavigationSweep movement, int workAllowance) =>
            movement.End.Position.X >= 17
                ? new(NavigationGeometryStatus.Limited, frame.World.Revision, frame.Tick, .5f,
                    NavigationBlockReason.MovingVehicle, NavigationReadAdapter.GetObstacleIdentity(new EntityId(99, 1)), 1)
                : new(NavigationGeometryStatus.Clear, frame.World.Revision, frame.Tick, 1, NavigationBlockReason.None, null, 1);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(45, false)]
    [InlineData(0, true)]
    public void ClearParallelAndFollowingLanesDoNotCreatePhantomIntersectionLeases(float degrees, bool following)
    {
        var (frame, intents) = Crossing();
        float heading = degrees * MathF.PI / 180;
        Vector3 forward = new(MathF.Sin(heading), 0, MathF.Cos(heading));
        Vector3 side = new(MathF.Cos(heading), 0, -MathF.Sin(heading));
        Vector3 first = new(20, 1.1f, 20);
        Vector3 second = following ? first - forward * 7.2f : first + side * 5;
        var profile = new NavigationVehicleProfile(new(1.6f, 1.1f, 3.2f), .25f, 6, 4, 4, 2);
        var vehicles = frame.Vehicles.ToArray();
        for (int i = 0; i < 2; i++)
        {
            Vector3 start = i == 0 ? first : second;
            var order = intents[i].Binding.OriginalOrder with { WorldTarget = start + forward * 30 };
            vehicles[i] = vehicles[i] with { Pose = new(start, Quaternion.CreateFromAxisAngle(Vector3.UnitY, heading)),
                Profile = profile, Movement = GroundMovementState.Stationary(heading) with { Velocity = forward * 6 },
                DirectOrder = order };
            intents[i] = intents[i] with { Binding = intents[i].Binding with { OriginalOrder = order },
                SteeringTarget = order.WorldTarget };
        }
        frame = new(frame.Tick, frame.Collision, true, vehicles, [], Limits());
        var planner = new PredictiveTrafficPlanner();
        var directives = planner.PlanBatch(frame, intents, new ClearGeometry(), 3, 10000, out _);
        Assert.All(directives, directive => Assert.Equal(NavigationTrafficAction.Proceed, directive.Action));
        Assert.Empty(planner.CaptureState().Leases);
    }

    [Theory]
    [InlineData(1, false, true)]
    [InlineData(1, true, false)]
    [InlineData(3, false, false)]
    public void EarlierQueueMemberDoesNotWaitForAnUnoccupiedLaterWaitingPose(int position, bool occupied, bool proceed)
    {
        var (frame, intents) = Crossing();
        var vehicle = frame.Vehicles[0] with
        {
            QueueEntry = new VehicleQueueEntry(new(9, 1), new(1), 1, SimulationTick.Zero,
                VehicleQueueService.CargoPickup, Position: position, WaitingCount: 3, Target: new(30, .5f, 20)),
            PrecisionApproachRadius = 1
        };
        var intent = intents[0] with { Binding = intents[0].Binding with { QueueGeneration = 1 } };
        var reservation = new NavigationTrafficReservation(7, new(2, 1), 1, 0, NavigationReservationKind.Queue,
            new(new(20, .5f, 20), Quaternion.Identity), Vector3.One, SimulationTick.Zero, SimulationTick.Zero,
            occupied, QueueBuilding: new(9, 1), QueuePosition: 2);
        var observed = new NavigationTrafficFrame(frame.Tick, frame.Collision, true, [vehicle], [reservation], Limits());
        var directive = Assert.Single(new PredictiveTrafficPlanner().PlanBatch(observed, [intent], new ClearGeometry(), 3, 10000, out _));
        Assert.Equal(proceed ? NavigationTrafficAction.Proceed : NavigationTrafficAction.Hold, directive.Action);
        Assert.Equal(1UL, directive.Binding.QueueGeneration);
        Assert.Equal(NavigationTrafficAction.Hold,
            Assert.Single(new PredictiveTrafficPlanner().PlanBatch(observed, [intent], new BlockedGeometry(), 3, 10000, out _)).Action);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void FutureQueueTargetDoesNotReserveAnotherServiceApproach(bool occupied, bool proceed)
    {
        var (frame, intents) = Crossing();
        var reservation = new NavigationTrafficReservation(7, new(2, 1), 1, 0, NavigationReservationKind.Queue,
            new(new(20, .5f, 20), Quaternion.Identity), Vector3.One, SimulationTick.Zero, SimulationTick.Zero,
            occupied, QueueBuilding: new(9, 1), QueuePosition: 2);
        var observed = new NavigationTrafficFrame(frame.Tick, frame.Collision, true, [frame.Vehicles[0]], [reservation], Limits());
        var directive = Assert.Single(new PredictiveTrafficPlanner().PlanBatch(observed, [intents[0]], new ClearGeometry(), 3, 10000, out _));
        Assert.Equal(proceed ? NavigationTrafficAction.Proceed : NavigationTrafficAction.Hold, directive.Action);
        Assert.Equal(NavigationTrafficAction.Hold,
            Assert.Single(new PredictiveTrafficPlanner().PlanBatch(observed, [intents[0]], new BlockedGeometry(), 3, 10000, out _)).Action);
    }

    [Fact]
    public void CoarseWorldGridDoesNotTurnOneCrossingIntoAnEntireParkingArea()
    {
        var (frame, intents) = Crossing(16);
        var parked = new NavigationCollisionBody(new(NavigationObstacleSource.Entity, 99, 1), NavigationObstacleKind.Vehicle,
            new(new(36, .5f, 36), Quaternion.Identity), new(.5f), Vector3.Zero);
        var collision = new NavigationCollisionFrame(frame.Collision.World, frame.Tick.Value, frame.Collision.Coverage, true, [parked], 10);
        var observed = new NavigationTrafficFrame(frame.Tick, collision, true, frame.Vehicles.ToArray(), [], Limits());
        var planner = new PredictiveTrafficPlanner();
        var directives = planner.PlanBatch(observed, intents, new ClearGeometry(), 3, 10000, out _);
        Assert.Equal(NavigationTrafficAction.Proceed, directives[0].Action);
        Assert.Equal(NavigationTrafficAction.Hold, directives[1].Action);
        var lease = Assert.Single(planner.CaptureState().Leases);
        Assert.InRange(lease.Passage.HalfExtents.X, .5f, 8);
        Assert.InRange(lease.Passage.HalfExtents.Z, .5f, 8);
    }

    [Fact]
    public void ClearPredictionReportsNoHazardAndRetainsNoWaitingHistory()
    {
        var (frame, intents) = Crossing();
        var single = new NavigationTrafficFrame(frame.Tick, frame.Collision, true, [frame.Vehicles[0]], [], Limits());
        var planner = new PredictiveTrafficPlanner();
        var directive = Assert.Single(planner.PlanBatch(single, [intents[0]], new ClearGeometry(), 3, 10000, out _));
        Assert.Equal(NavigationBlockReason.None, directive.Reason);
        Assert.Empty(planner.CaptureState().Vehicles);
    }

    [Fact]
    public void VerifiedPartialHorizonAllowsBrakingApproachInsteadOfAnEarlyStop()
    {
        var (frame, intents) = Crossing();
        var single = new NavigationTrafficFrame(frame.Tick, frame.Collision, true, [frame.Vehicles[0]], [], Limits());
        var planner = new PredictiveTrafficPlanner();
        var directive = Assert.Single(planner.PlanBatch(single, [intents[0]], new LimitedGeometry(), 3, 10000, out _));
        Assert.Equal(NavigationTrafficAction.Proceed, directive.Action);
        Assert.Equal(NavigationBlockReason.MovingVehicle, directive.Reason);
        Assert.InRange(directive.MaximumSpeed, .01f, 5.99f);
        Assert.Equal(new EntityId(99, 1), Assert.Single(planner.CaptureState().Vehicles).WaitingFor);
    }

    private sealed class LimitedGeometry : INavigationGeometryQuery
    {
        public NavigationModuleAvailability Availability => NavigationModuleAvailability.Available;
        public NavigationSweepResult Sweep(NavigationCollisionFrame frame, in NavigationSweep movement, int workAllowance) =>
            new(NavigationGeometryStatus.Limited, frame.World.Revision, frame.Tick, .5f, NavigationBlockReason.MovingVehicle,
                NavigationReadAdapter.GetObstacleIdentity(new EntityId(99, 1)), 1);
    }

    [Fact]
    public void FarReservedDockKeepsItsExactSlotGenerationWithoutHoldingCoarseTravel()
    {
        var (frame, intent) = FarDock(VehicleDockStatus.Reserved, false);
        var result = new PredictiveTrafficPlanner().PlanBatch(frame, [intent], new ClearGeometry(), 3, 10000, out _);
        Assert.Equal(NavigationTrafficAction.Proceed, Assert.Single(result).Action);
        Assert.Equal(5UL, result[0].Binding.DockGeneration);
        Assert.Equal(VehicleDockStatus.Reserved, frame.Vehicles[0].DockVisit!.Value.Status);
    }

    [Fact]
    public void UnenteredBlockedDockDoesNotTakeOwnershipOfCoarseTravel()
    {
        var (frame, intent) = FarDock(VehicleDockStatus.Blocked, false);
        var vehicle = frame.Vehicles[0] with { DockVisit = frame.Vehicles[0].DockVisit!.Value with { Generation = 0 }, BuildingSlot = null };
        intent = intent with { Binding = intent.Binding with { DockGeneration = 0 } };
        var blocked = new NavigationTrafficFrame(frame.Tick, frame.Collision, true, [vehicle], [], Limits());
        Assert.Equal(NavigationTrafficAction.Proceed,
            Assert.Single(new PredictiveTrafficPlanner().PlanBatch(blocked, [intent], new ClearGeometry(), 3, 10000, out _)).Action);
    }

    [Theory]
    [InlineData(VehicleDockStatus.Reserved, true)]
    [InlineData(VehicleDockStatus.Approaching, false)]
    [InlineData(VehicleDockStatus.Aligning, false)]
    [InlineData(VehicleDockStatus.Docked, false)]
    [InlineData(VehicleDockStatus.Servicing, false)]
    [InlineData(VehicleDockStatus.Exiting, false)]
    public void PreciseDockOwnerRetainsMovement(VehicleDockStatus status, bool entered)
    {
        var (frame, intent) = FarDock(status, entered);
        var geometry = new TracingGeometry();
        var result = new PredictiveTrafficPlanner().PlanBatch(frame, [intent], geometry, 3, 10000, out _);
        Assert.Equal(NavigationTrafficAction.Hold, Assert.Single(result).Action);
        Assert.Equal(NavigationBlockReason.ReservedCorridor, result[0].Reason);
        Assert.Empty(geometry.Sweeps);
    }

    [Fact]
    public void FarQueueMemberTravelsWithItsGenerationAndHandsOffAtTheLocalQueue()
    {
        var (frame, intents) = Crossing();
        var vehicle = frame.Vehicles[0] with
        {
            QueueEntry = new VehicleQueueEntry(new EntityId(9, 1), new PlayerId(1), 3, new SimulationTick(1),
                VehicleQueueService.CargoPickup, Position: 2, WaitingCount: 2, Target: new Vector3(30, 0.5f, 20)),
            PrecisionApproachRadius = 10
        };
        var intent = intents[0] with { Binding = intents[0].Binding with { QueueGeneration = 3 } };
        var far = new NavigationTrafficFrame(frame.Tick, frame.Collision, true, [vehicle], [], Limits());
        Assert.Equal(NavigationTrafficAction.Proceed,
            Assert.Single(new PredictiveTrafficPlanner().PlanBatch(far, [intent], new ClearGeometry(), 3, 10000, out _)).Action);
        vehicle = vehicle with { Pose = vehicle.Pose with { Position = new Vector3(25, 0.5f, 20) } };
        var near = new NavigationTrafficFrame(frame.Tick, frame.Collision, true, [vehicle], [], Limits());
        Assert.Equal(NavigationTrafficAction.Hold,
            Assert.Single(new PredictiveTrafficPlanner().PlanBatch(near, [intent], new ClearGeometry(), 3, 10000, out _)).Action);
    }

    [Fact]
    public void CoarseTravelCannotBorrowAReassignedDockSlot()
    {
        var (frame, intent) = FarDock(VehicleDockStatus.Reserved, false);
        var vehicle = frame.Vehicles[0] with
            { BuildingSlot = frame.Vehicles[0].BuildingSlot!.Value with { Truck = new EntityId(2, 1) } };
        var stale = new NavigationTrafficFrame(frame.Tick, frame.Collision, true, [vehicle], [], Limits());
        Assert.Equal(NavigationModuleAvailability.Unavailable,
            Assert.Single(new PredictiveTrafficPlanner().PlanBatch(stale, [intent], new ClearGeometry(), 3, 10000, out _)).Availability);
    }

    private static (NavigationTrafficFrame, NavigationTrafficIntent) FarDock(VehicleDockStatus status, bool entered)
    {
        var (frame, intents) = Crossing();
        var vehicle = frame.Vehicles[0];
        var pose = new VehicleDockPose(new(40, 0.5f, 20), new(42, 0.5f, 20), new(44, 0.5f, 20), MathF.PI / 2,
            1, 0.5f, new Vector3(30, 0.5f, 20));
        vehicle = vehicle with
        {
            DockVisit = new VehicleDockVisit(new EntityId(8, 1), 5, status, VehicleDockBlockReason.None, pose, LaneEntered: entered),
            BuildingSlot = new VehicleDockSlot(vehicle.Entity, 5, pose, vehicle.Owner), PrecisionApproachRadius = 10
        };
        var intent = intents[0] with { Binding = intents[0].Binding with { DockGeneration = 5 } };
        return (new(frame.Tick, frame.Collision, true, [vehicle], [], Limits()), intent);
    }

    [Fact]
    public void PredictionContinuesAlongTheSelectedBentRoutePastTheFirstWaypoint()
    {
        var (frame, intents) = Crossing();
        var vehicle = frame.Vehicles[0];
        Vector3 final = new(20, 0.5f, 30);
        var root = intents[0].Binding.OriginalOrder with { WorldTarget = final };
        var waypoint = root with { WorldTarget = vehicle.Pose.Position };
        vehicle = vehicle with { RouteOrder = root, DirectOrder = waypoint };
        var request = new NavigationSearchSpecification(1, vehicle.Pose, final, NavigationTargetSemantics.PlayerGoal,
            new(0, 0.1f, null, 0), frame.Collision.World.Capabilities, vehicle.Profile, frame.Collision.World.Revision,
            frame.Collision.World.KnowledgeScope, 10000, 100000, 4);
        var route = new NavigationRouteProposal(request, final,
            [vehicle.Pose.Position, new Vector3(20, 0.5f, 20), final], [], default, 16);
        var intent = intents[0] with { Binding = intents[0].Binding with { OriginalOrder = root }, Route = route, CurrentWaypoint = 0 };
        var captured = new NavigationTrafficFrame(frame.Tick, frame.Collision, true, [vehicle], [], Limits());
        var geometry = new TracingGeometry();
        var result = new PredictiveTrafficPlanner().PlanBatch(captured, [intent], geometry, 3, 10000, out _);
        Assert.Equal(NavigationTrafficAction.Proceed, Assert.Single(result).Action);
        Assert.Contains(geometry.Sweeps, static sweep => sweep.End.Position.Z > 22);
        Assert.All(geometry.Sweeps, static sweep => Assert.Equal(0.5f, sweep.End.Position.Y));
    }

    private sealed class TracingGeometry : INavigationGeometryQuery
    {
        public List<NavigationSweep> Sweeps { get; } = [];
        public NavigationModuleAvailability Availability => NavigationModuleAvailability.Available;
        public NavigationSweepResult Sweep(NavigationCollisionFrame frame, in NavigationSweep movement, int workAllowance)
        {
            Sweeps.Add(movement);
            return new(NavigationGeometryStatus.Clear, frame.World.Revision, frame.Tick, 1, NavigationBlockReason.None, null, 1);
        }
    }

    [Fact]
    public void IncompleteCopiedNeighborhoodCannotBecomeCompleteThroughCallerMutation()
    {
        var (frame, intents) = Crossing();
        var incomplete = new NavigationCollisionFrame(frame.Collision.World, frame.Tick.Value,
            frame.Collision.Coverage, false, [], 10);
        var neighborhoods = new Dictionary<EntityId, NavigationCollisionFrame> { [intents[0].Binding.Requester] = incomplete };
        var captured = new NavigationTrafficFrame(frame.Tick, frame.Collision, true, frame.Vehicles.ToArray(), [], Limits(),
            vehicleCollisions: neighborhoods);
        neighborhoods[intents[0].Binding.Requester] = frame.Collision;
        var planner = new PredictiveTrafficPlanner();
        Assert.All(planner.PlanBatch(captured, intents, new ClearGeometry(), 3, 10000, out _),
            static d => Assert.Equal(NavigationModuleAvailability.Unavailable, d.Availability));
        Assert.Empty(planner.CaptureState().Leases);
    }

    [Fact]
    public void ZeroWorkClearChildCannotGrantUncheckedMovement()
    {
        var (frame, intents) = Crossing();
        var planner = new PredictiveTrafficPlanner();
        var result = planner.PlanBatch(frame, intents, new ZeroWorkGeometry(), 3, 10000, out int work);
        Assert.Equal(10000, work);
        Assert.All(result, static d => Assert.Equal(NavigationModuleAvailability.Unavailable, d.Availability));
        Assert.Empty(planner.CaptureState().Leases);
    }

    private sealed class ZeroWorkGeometry : INavigationGeometryQuery
    {
        public NavigationModuleAvailability Availability => NavigationModuleAvailability.Available;
        public NavigationSweepResult Sweep(NavigationCollisionFrame frame, in NavigationSweep movement, int workAllowance) =>
            new(NavigationGeometryStatus.Clear, frame.World.Revision, frame.Tick, 1, NavigationBlockReason.None, null, 0);
    }

    [Fact]
    public void RepeatedSingleCallsCannotSelectWinnersForAMultiVehicleFrame()
    {
        var (frame, intents) = Crossing();
        var planner = new PredictiveTrafficPlanner();
        foreach (var intent in intents)
        {
            var result = planner.Plan(frame, intent.Binding, new ClearGeometry(), 10000, out int work);
            Assert.Equal(NavigationModuleAvailability.Unavailable, result.Availability);
            Assert.Equal(NavigationTrafficAction.Hold, result.Action);
            Assert.Equal(0, work);
        }
        Assert.Empty(planner.CaptureState().Vehicles);
    }

    [Fact]
    public void CrossingOutsideFirstTickYieldsIndependentlyOfInputOrder()
    {
        var (frame, intents) = Crossing();
        var first = new PredictiveTrafficPlanner();
        var second = new PredictiveTrafficPlanner();
        var a = first.PlanBatch(frame, intents, new ClearGeometry(), 3, 10000, out int workA);
        Array.Reverse(intents);
        var b = second.PlanBatch(frame, intents, new ClearGeometry(), 3, 10000, out int workB);
        Assert.Equal(a.ToArray(), b.ToArray());
        Assert.Equal(workA, workB);
        Assert.Equal(NavigationTrafficAction.Proceed, a[0].Action);
        Assert.Equal(NavigationTrafficAction.Hold, a[1].Action);
        Assert.Equal(NavigationBlockReason.ReservedCorridor, a[1].Reason);
    }

    [Fact]
    public void ExhaustedBatchHoldsEveryoneAndDoesNotCommitWaitingState()
    {
        var (frame, intents) = Crossing();
        var planner = new PredictiveTrafficPlanner();
        var directives = planner.PlanBatch(frame, intents, new ClearGeometry(), 3, 2, out int work);
        Assert.Equal(2, work);
        Assert.All(directives, static d =>
        {
            Assert.Equal(NavigationModuleAvailability.Unavailable, d.Availability);
            Assert.Equal(NavigationTrafficAction.Hold, d.Action);
            Assert.Equal(NavigationBlockReason.SearchWork, d.Reason);
        });
        Assert.Empty(planner.CaptureState().Vehicles);
    }

    [Fact]
    public void StaleCommandAndReusedEntityCannotReceiveRightOfWay()
    {
        var (frame, intents) = Crossing();
        intents[0] = intents[0] with { Binding = intents[0].Binding with
        {
            OriginalOrder = intents[0].Binding.OriginalOrder with { SubmittedAtTick = new SimulationTick(3) }
        }};
        var planner = new PredictiveTrafficPlanner();
        Assert.All(planner.PlanBatch(frame, intents, new ClearGeometry(), 3, 10000, out _),
            static d => Assert.Equal(NavigationModuleAvailability.Unavailable, d.Availability));
        intents[0] = intents[0] with { Binding = intents[0].Binding with { Requester = new EntityId(1, 2) }};
        Assert.All(planner.PlanBatch(frame, intents, new ClearGeometry(), 3, 10000, out _),
            static d => Assert.Equal(NavigationBlockReason.StaleSnapshot, d.Reason));
    }

    [Fact]
    public void PhysicalGeometryVetoOverridesPredictedPriority()
    {
        var (frame, intents) = Crossing();
        var planner = new PredictiveTrafficPlanner();
        var result = planner.PlanBatch(frame, intents, new BlockedGeometry(), 3, 10000, out _);
        Assert.Equal(NavigationTrafficAction.Hold, result[0].Action);
        Assert.Equal(NavigationBlockReason.StaticObstacle, result[0].Reason);
    }

    [Fact]
    public void CheckpointPreservesWaitingPriorityAndReturnedArraysAreDetached()
    {
        var (frame, intents) = Crossing();
        var planner = new PredictiveTrafficPlanner();
        planner.PlanBatch(frame, intents, new ClearGeometry(), 3, 10000, out _);
        var checkpoint = planner.CaptureState();
        var restored = new PredictiveTrafficPlanner();
        restored.RestoreState(checkpoint);
        checkpoint.Vehicles[0] = default;
        var next = new NavigationTrafficFrame(new SimulationTick(2),
            new NavigationCollisionFrame(frame.Collision.World, 2, frame.Collision.Coverage, true, [], 10),
            true, frame.Vehicles.ToArray(), [], Limits());
        var expected = planner.PlanBatch(next, intents, new ClearGeometry(), 3, 10000, out _);
        var actual = restored.PlanBatch(next, intents, new ClearGeometry(), 3, 10000, out _);
        Assert.Equal(expected.ToArray(), actual.ToArray());
        Assert.Equal(NavigationTrafficAction.Proceed, actual[0].Action);
        Assert.Equal(NavigationTrafficAction.Hold, actual[1].Action);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void StoppedCycleGrantsAtMostOneCompletelySweptReverseEscape(bool blockRear, bool expectEscape)
    {
        var (frame, intents) = Crossing();
        var stopped = frame.Vehicles.Select(static v => v with
        {
            Movement = v.Movement with { Velocity = Vector3.Zero }
        }).ToArray();
        frame = new NavigationTrafficFrame(frame.Tick, frame.Collision, true, stopped, [], Limits());
        var planner = new PredictiveTrafficPlanner();
        var geometry = new CycleGeometry(blockRear);
        planner.PlanBatch(frame, intents, geometry, 3, 10000, out _);
        var restored = new PredictiveTrafficPlanner();
        restored.RestoreState(planner.CaptureState());
        var later = new NavigationTrafficFrame(new SimulationTick(50),
            new NavigationCollisionFrame(frame.Collision.World, 50, frame.Collision.Coverage, true, [], 10),
            true, stopped, [], Limits());
        var directives = planner.PlanBatch(later, intents, geometry, 3, 10000, out _);
        Assert.Equal(expectEscape ? 1 : 0, directives.Count(static d => d.Action == NavigationTrafficAction.Recovery));
        Assert.Equal(directives.ToArray(), restored.PlanBatch(later, intents, geometry, 3, 10000, out _).ToArray());
        foreach (var directive in directives.Where(static d => d.Action == NavigationTrafficAction.Recovery))
        {
            var vehicle = stopped.Single(v => v.Entity == directive.Binding.Requester);
            Assert.NotNull(directive.SuggestedTarget);
            Vector3 backwards = Vector3.Transform(-Vector3.UnitZ, vehicle.Pose.Rotation);
            Assert.True(Vector3.Dot(directive.SuggestedTarget.Value - vehicle.Pose.Position, backwards) > 0);
            Assert.InRange(directive.MaximumSpeed, 0.01f, 1);
        }
    }

    [Fact]
    public void ReverseSteeringEscapeRetainsItsGoalAcrossActualMotionAndCheckpoint()
    {
        var (frame, intents) = Crossing();
        var stopped = frame.Vehicles.Select(static v => v with { Movement = v.Movement with { Velocity = Vector3.Zero } }).ToArray();
        var planner = new PredictiveTrafficPlanner();
        var geometry = new CycleGeometry(false);
        frame = new(frame.Tick, frame.Collision, true, stopped, [], Limits());
        planner.PlanBatch(frame, intents, geometry, 3, 10000, out _);
        var later = new NavigationTrafficFrame(new(50), new(frame.Collision.World, 50, frame.Collision.Coverage, true, [], 10), true, stopped, [], Limits());
        var first = Assert.Single(planner.PlanBatch(later, intents, geometry, 3, 10000, out _), static d => d.Action == NavigationTrafficAction.Recovery);
        int index = Array.FindIndex(stopped, v => v.Entity == first.Binding.Requester);
        var vehicle = stopped[index];
        Vector3 side = Vector3.Transform(Vector3.UnitX, vehicle.Pose.Rotation);
        Assert.True(MathF.Abs(Vector3.Dot(first.SuggestedTarget!.Value - vehicle.Pose.Position, side)) >= 5.99f, $"Escape={first}; leases={string.Join(';', planner.CaptureState().Leases)}");
        Assert.True(NavigationReverseMotion.TryStep(vehicle, first.SuggestedTarget.Value, 1, .05f, frame.Collision.World, out var end, out _, out float heading));
        stopped[index] = vehicle with { Pose = end, Movement = vehicle.Movement with { Velocity = (end.Position - vehicle.Pose.Position) / .05f, HeadingRadians = heading } };
        var restored = new PredictiveTrafficPlanner(); restored.RestoreState(planner.CaptureState());
        var next = new NavigationTrafficFrame(new(51), new(frame.Collision.World, 51, frame.Collision.Coverage, true, [], 10), true, stopped, [], Limits());
        var actual = planner.PlanBatch(next, intents, geometry, 3, 10000, out _);
        Assert.Equal(actual.ToArray(), restored.PlanBatch(next, intents, geometry, 3, 10000, out _).ToArray());
        var continuing = Assert.Single(actual, d => d.Binding.Requester == first.Binding.Requester);
        Assert.Equal(NavigationTrafficAction.Recovery, continuing.Action);
        Assert.Equal(first.SuggestedTarget, continuing.SuggestedTarget);
    }

    private sealed class CycleGeometry(bool blockRear) : INavigationGeometryQuery
    {
        public NavigationModuleAvailability Availability => NavigationModuleAvailability.Available;
        public NavigationSweepResult Sweep(NavigationCollisionFrame frame, in NavigationSweep movement, int workAllowance)
        {
            Vector3 forward = Vector3.Transform(Vector3.UnitZ, movement.Start.Rotation);
            bool reverse = Vector3.Dot(movement.End.Position - movement.Start.Position, forward) < 0;
            if (reverse && !blockRear)
                return new(NavigationGeometryStatus.Clear, frame.World.Revision, frame.Tick, 1, NavigationBlockReason.None, null, 1);
            return new(NavigationGeometryStatus.Blocked, frame.World.Revision, frame.Tick, 0,
                NavigationBlockReason.MovingVehicle,
                new NavigationObstacleIdentity(NavigationObstacleSource.Entity, movement.Mover.Value == 1 ? 2UL : 1UL, 1), 1);
        }
    }

    [Fact]
    public void ForeignPassageAllowsOnlyOutwardDepartureFromAnAlreadyOccupiedRegion()
    {
        var (frame, _) = OpposingPassage(1, 23, 27);
        var passage = Assert.Single(frame.Passages);
        var owner = frame.Vehicles[0].Entity;
        var other = frame.Vehicles[1];
        var planner = new PredictiveTrafficPlanner();
        planner.RestoreState(new(3, [], [new(passage, owner, 1, 1, 1, 1, true)]));
        var start = other.Pose;
        var outward = start with { Position = start.Position + Vector3.UnitZ };
        var inward = start with { Position = start.Position - Vector3.UnitZ };
        Assert.True(planner.TryCheckPassage(other.Entity, start, outward, other.Profile, 100, out _, out bool complete));
        Assert.True(complete);
        Assert.False(planner.TryCheckPassage(other.Entity, start, inward, other.Profile, 100, out _, out _));
        var outside = start with { Position = start.Position + Vector3.UnitZ * 20 };
        Assert.False(planner.TryCheckPassage(other.Entity, outside, start, other.Profile, 100, out _, out _));
        Assert.Equal(owner, Assert.Single(planner.CaptureState().Leases).Owner);
        Assert.True(Assert.Single(planner.CaptureState().Leases).Entered);
    }

    [Fact]
    public void PassageLeaseSurvivesChangedCommandUntilWholeRearClears()
    {
        var (frame, intents) = OpposingPassage(1, 10, 40);
        var planner = new PredictiveTrafficPlanner();
        var first = planner.PlanBatch(frame, intents, new ClearGeometry(), 3, 10000, out _);
        Assert.Equal(NavigationTrafficAction.Proceed, first[0].Action);
        Assert.Equal(NavigationTrafficAction.Hold, first[1].Action);
        Assert.Equal(new EntityId(1, 1), Assert.Single(planner.CaptureState().Leases).Owner);
        (frame, intents) = OpposingPassage(2, 23, 40);
        planner.PlanBatch(frame, intents, new ClearGeometry(), 3, 10000, out _);
        Assert.True(Assert.Single(planner.CaptureState().Leases).Entered);
        (frame, intents) = OpposingPassage(3, 30.3f, 40);
        intents[0] = intents[0] with { Binding = intents[0].Binding with { CommandGeneration = 2 } };
        var changed = planner.PlanBatch(frame, intents, new ClearGeometry(), 3, 10000, out _);
        Assert.Equal(NavigationTrafficAction.Hold, changed[1].Action);
        Assert.Equal(new EntityId(1, 1), Assert.Single(planner.CaptureState().Leases).Owner);
        (frame, intents) = OpposingPassage(4, 31.4f, 40);
        var released = planner.PlanBatch(frame, intents, new ClearGeometry(), 3, 10000, out _);
        Assert.Equal(NavigationTrafficAction.Proceed, released[1].Action);
        Assert.Equal(new EntityId(2, 1), Assert.Single(planner.CaptureState().Leases).Owner);
    }

    [Fact]
    public void OccupiedSavedPassageDoesNotReleaseForDeadOwnerOrAReusedEntity()
    {
        var (frame, intents) = OpposingPassage(1, 23, 40);
        var planner = new PredictiveTrafficPlanner();
        planner.PlanBatch(frame, intents, new ClearGeometry(), 3, 10000, out _);
        var saved = planner.CaptureState();
        var restored = new PredictiveTrafficPlanner();
        restored.RestoreState(saved);
        var wreck = new NavigationCollisionBody(NavigationReadAdapter.GetObstacleIdentity(new EntityId(1, 1)),
            NavigationObstacleKind.Wreck, frame.Vehicles[0].Pose, frame.Vehicles[0].Profile.HalfExtents, Vector3.Zero);
        var collision = new NavigationCollisionFrame(frame.Collision.World, 2, frame.Collision.Coverage, true, [wreck], 10);
        var afterDeath = new NavigationTrafficFrame(new SimulationTick(2), collision, true, [frame.Vehicles[1]], [], Limits(), frame.Passages.ToArray());
        var held = restored.PlanBatch(afterDeath, [intents[1]], new ClearGeometry(), 3, 10000, out _);
        Assert.Equal(NavigationTrafficAction.Hold, Assert.Single(held).Action);
        Assert.Equal(new EntityId(1, 1), Assert.Single(restored.CaptureState().Leases).Owner);
        var reused = frame.Vehicles[0] with { Entity = new EntityId(1, 2),
            Pose = new NavigationPose(new Vector3(20, 0.5f, 5), Quaternion.Identity) };
        var reuseCollision = new NavigationCollisionFrame(frame.Collision.World, 3, frame.Collision.Coverage, true, [wreck], 10);
        var afterReuse = new NavigationTrafficFrame(new SimulationTick(3), reuseCollision, true,
            [frame.Vehicles[1], reused], [], Limits(), frame.Passages.ToArray());
        Assert.Equal(NavigationTrafficAction.Hold,
            Assert.Single(restored.PlanBatch(afterReuse, [intents[1]], new ClearGeometry(), 3, 10000, out _)).Action);
        Assert.Equal(new EntityId(1, 1), Assert.Single(restored.CaptureState().Leases).Owner);
        var clearedCollision = new NavigationCollisionFrame(frame.Collision.World, 4, frame.Collision.Coverage, true, [], 10);
        var afterRemoval = new NavigationTrafficFrame(new SimulationTick(4), clearedCollision, true, [frame.Vehicles[1]], [], Limits(), frame.Passages.ToArray());
        Assert.Equal(NavigationTrafficAction.Proceed,
            Assert.Single(restored.PlanBatch(afterRemoval, [intents[1]], new ClearGeometry(), 3, 10000, out _)).Action);
        saved.Leases[0] = default;
        Assert.Single(restored.CaptureState().Leases);
    }

    [Fact]
    public void VehiclesAlreadyOpposedInsidePassageFormAnEscapableWaitCycle()
    {
        var (frame, intents) = OpposingPassage(1, 23, 27);
        var stopped = frame.Vehicles.Select(static v => v with
            { Movement = v.Movement with { Velocity = Vector3.Zero } }).ToArray();
        frame = new(frame.Tick, frame.Collision, true, stopped, [], Limits(), frame.Passages.ToArray());
        var planner = new PredictiveTrafficPlanner();
        planner.PlanBatch(frame, intents, new ClearGeometry(), 3, 10000, out _);
        Assert.Empty(planner.CaptureState().Leases);
        var later = new NavigationTrafficFrame(new SimulationTick(50),
            new NavigationCollisionFrame(frame.Collision.World, 50, frame.Collision.Coverage, true, [], 10),
            true, stopped, [], Limits(), frame.Passages.ToArray());
        var result = planner.PlanBatch(later, intents, new ClearGeometry(), 3, 10000, out _);
        Assert.Single(result, static d => d.Action == NavigationTrafficAction.Recovery);
    }

    private static (NavigationTrafficFrame, NavigationTrafficIntent[]) OpposingPassage(ulong tick, float firstZ, float secondZ)
    {
        var (frame, intents) = Crossing();
        var vehicles = frame.Vehicles.ToArray();
        for (int i = 0; i < 2; i++)
        {
            float heading = i == 0 ? 0 : MathF.PI;
            Vector3 start = new(20, 0.5f, i == 0 ? firstZ : secondZ);
            Vector3 target = new(20, 0.5f, i == 0 ? 50 : 0);
            var order = vehicles[i].DirectOrder!.Value with { WorldTarget = target };
            vehicles[i] = vehicles[i] with
            {
                Pose = new(start, Quaternion.CreateFromAxisAngle(Vector3.UnitY, heading)),
                Movement = vehicles[i].Movement with { HeadingRadians = heading, Velocity = new Vector3(0, 0, i == 0 ? 6 : -6) },
                DirectOrder = order
            };
            intents[i] = intents[i] with { Binding = intents[i].Binding with { OriginalOrder = order }, SteeringTarget = target };
        }
        var collision = new NavigationCollisionFrame(frame.Collision.World, tick, frame.Collision.Coverage, true, [], 10);
        NavigationTrafficPassage passage = new(1, new(new Vector3(20, 0.5f, 25), Quaternion.Identity), new(1, 1, 5));
        return (new(new SimulationTick(tick), collision, true, vehicles, [], Limits(), [passage]), intents);
    }

    private static (NavigationTrafficFrame, NavigationTrafficIntent[]) Crossing(float cellSizeMeters = 4)
    {
        var settings = new WorldGridSettings { ChunkSizeMeters = 64, HeightSamplesPerSide = 17 };
        var terrain = new TerrainWorld(settings,
            [new TerrainChunk(new ChunkCoordinate(0, 0), new TerrainHeightfield(17, 64, new float[289]))]);
        var world = NavigationWorld.Build(terrain, gridSettings: new NavigationGridSettings { CellSizeMeters = cellSizeMeters },
            sectorSettings: new NavigationSectorSettings { SectorSizeCells = Math.Min(8, (int)(64 / cellSizeMeters)) });
        var capabilities = NavigationCapabilities.For(NavigationMovementClass.Wheeled);
        var snapshot = new NavigationWorldSnapshot(world, world.GetSectorGraph(capabilities), capabilities,
            terrain, 0, 1, NavigationModuleAvailability.NotConnected, [], []);
        var collision = new NavigationCollisionFrame(snapshot, 1, terrain.WorldBounds, true, [], 10);
        var vehicles = new NavigationVehicleObservation[2];
        var intents = new NavigationTrafficIntent[2];
        for (int i = 0; i < 2; i++)
        {
            EntityId entity = new((uint)(i + 1), 1);
            Vector3 start = i == 0 ? new(10, 0.5f, 20) : new(20, 0.5f, 10);
            Vector3 target = i == 0 ? new(30, 0.5f, 20) : new(20, 0.5f, 30);
            float heading = i == 0 ? MathF.PI / 2 : 0;
            var order = new MovementOrder(new PlayerId(1), target, SimulationTick.Zero, new SimulationTick(1));
            var profile = new NavigationVehicleProfile(new Vector3(0.5f), 0.1f, 6, 4, 4, 2);
            var movement = GroundMovementState.Stationary(heading) with
            {
                Velocity = i == 0 ? new Vector3(6, 0, 0) : new Vector3(0, 0, 6)
            };
            vehicles[i] = new(entity, order.Issuer, new(start, Quaternion.CreateFromAxisAngle(Vector3.UnitY, heading)),
                profile, movement, order, null, null, null, null, null);
            intents[i] = new(new(entity, order, 1, 1, 0, 0), target, 6, true);
        }
        return (new(new SimulationTick(1), collision, true, vehicles, [], Limits()), intents);
    }

    private static NavigationIntegrationLimits Limits() => new(10000, 10000, 16, 100000, 100000, 4, 20, 3, 64, 64, 64);

    private sealed class ClearGeometry : INavigationGeometryQuery
    {
        public NavigationModuleAvailability Availability => NavigationModuleAvailability.Available;
        public NavigationSweepResult Sweep(NavigationCollisionFrame frame, in NavigationSweep movement, int workAllowance) =>
            new(NavigationGeometryStatus.Clear, frame.World.Revision, frame.Tick, 1, NavigationBlockReason.None, null, 1);
    }

    private sealed class BlockedGeometry : INavigationGeometryQuery
    {
        public NavigationModuleAvailability Availability => NavigationModuleAvailability.Available;
        public NavigationSweepResult Sweep(NavigationCollisionFrame frame, in NavigationSweep movement, int workAllowance) =>
            new(NavigationGeometryStatus.Blocked, frame.World.Revision, frame.Tick, 0, NavigationBlockReason.StaticObstacle, null, 1);
    }
}
