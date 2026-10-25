using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class PredictiveTrafficBatchTests
{
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

    private static (NavigationTrafficFrame, NavigationTrafficIntent[]) Crossing()
    {
        var settings = new WorldGridSettings { ChunkSizeMeters = 64, HeightSamplesPerSide = 17 };
        var terrain = new TerrainWorld(settings,
            [new TerrainChunk(new ChunkCoordinate(0, 0), new TerrainHeightfield(17, 64, new float[289]))]);
        var world = NavigationWorld.Build(terrain, gridSettings: new NavigationGridSettings { CellSizeMeters = 4 });
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
