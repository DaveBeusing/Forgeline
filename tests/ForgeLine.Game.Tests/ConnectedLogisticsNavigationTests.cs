using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;
namespace ForgeLine.Game.Tests;
public sealed class ConnectedLogisticsNavigationTests
{
    [Fact]
    public void ConnectedTruckReachesItsOriginalGoal()
    {
        var (simulation, movement, unit) = Create();
        simulation.RunTicks(240, TestContext.Current.CancellationToken);
        var pose = simulation.Entities.GetComponent<WorldTransform>(unit);
        var state = simulation.Entities.GetComponent<GroundMovementState>(unit);
        var checkpoint = movement.CaptureNavigationState();
        Assert.True(Vector3.Distance(pose.Position, new(10, .5f, 40)) < .5f,
            $"Position={pose.Position}; state={state}; actors={string.Join(';', checkpoint.Actors)}");
        Assert.False(simulation.Entities.HasComponent<MovementOrder>(unit), $"Position={pose.Position}; state={state}; actors={string.Join(';', checkpoint.Actors)}");
    }
    [Fact]
    public void VerifiedTurningArcMayTemporarilyLoseProjectedProgressWithoutConsumingTheRoute()
    {
        var (simulation, movement, unit) = Create();
        simulation.RunTicks(30, TestContext.Current.CancellationToken);
        var route = simulation.Entities.GetComponent<NavigationRouteState>(unit);
        var pose = simulation.Entities.GetComponent<WorldTransform>(unit);
        simulation.Entities.SetComponent(unit, pose with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI) });
        simulation.Entities.SetComponent(unit, GroundMovementState.Stationary(MathF.PI));
        simulation.RunTicks(300, TestContext.Current.CancellationToken);
        Assert.InRange(Vector3.Distance(simulation.Entities.GetComponent<WorldTransform>(unit).Position, route.OriginalOrder.WorldTarget), 0, .5f);
        Assert.False(simulation.Entities.HasComponent<MovementOrder>(unit));
        Assert.Equal(route.OriginalOrder, Assert.Single(movement.CaptureNavigationState().Actors).OriginalOrder);
    }

    [Fact]
    public void RealCrossingTrucksReachBothGoalsWithoutOverlappingTheirOrientedFootprints()
    {
        var (simulation, _, first) = Create();
        var entities = simulation.Entities;
        var second = entities.CreateEntity();
        entities.AddComponent(second, new WorldTransform(new(4, .5f, 25), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2), Vector3.One));
        entities.AddComponent(second, entities.GetComponent<GroundMovement>(first));
        entities.AddComponent(second, GroundMovementState.Stationary(MathF.PI / 2));
        entities.AddComponent(second, new NavigationAgent(NavigationMovementClass.Wheeled));
        entities.AddComponent(second, entities.GetComponent<UnitIdentity>(first));
        entities.AddComponent(second, entities.GetComponent<ControllableEntity>(first));
        entities.AddComponent(second, entities.GetComponent<SpatialPresence>(first));
        entities.AddComponent(second, new MovementOrder(new PlayerId(1), new(40, .5f, 25), SimulationTick.Zero, new(1)));
        for (int tick = 0; tick < 600; tick++)
        {
            simulation.AdvanceOneTick();
            var a = entities.GetComponent<WorldTransform>(first);
            var b = entities.GetComponent<WorldTransform>(second);
            Vector3 ax = Vector3.Transform(Vector3.UnitX, a.Rotation), az = Vector3.Transform(Vector3.UnitZ, a.Rotation);
            Vector3 bx = Vector3.Transform(Vector3.UnitX, b.Rotation), bz = Vector3.Transform(Vector3.UnitZ, b.Rotation);
            Vector3 separation = a.Position - b.Position;
            bool separated = new[] { ax, az, bx, bz }.Any(axis => MathF.Abs(Vector3.Dot(separation, axis)) >
                .5f * (MathF.Abs(Vector3.Dot(ax, axis)) + MathF.Abs(Vector3.Dot(az, axis)) +
                MathF.Abs(Vector3.Dot(bx, axis)) + MathF.Abs(Vector3.Dot(bz, axis))) - .00001f);
            Assert.True(separated, $"tick={tick}; first={a}; second={b}");
        }
        Assert.InRange(Vector3.Distance(entities.GetComponent<WorldTransform>(first).Position, new(10, .5f, 40)), 0, .5f);
        Assert.InRange(Vector3.Distance(entities.GetComponent<WorldTransform>(second).Position, new(40, .5f, 25)), 0, .5f);
        Assert.False(entities.HasComponent<MovementOrder>(first));
        Assert.False(entities.HasComponent<MovementOrder>(second));
    }

    [Fact]
    public void ProjectedFinalWaypointHandsBackToTheOriginalLocalApproach()
    {
        Vector3 goal = new(20, .5f, 20);
        var (simulation, _, unit) = Create([new(new(23, -1, 23), new(23.5f, 2, 23.5f))], goal);
        bool projected = false, localApproach = false;
        for (int tick = 0; tick < 300; tick++)
        {
            simulation.AdvanceOneTick();
            if (simulation.Entities.TryGetComponent(unit, out NavigationRouteState route))
                projected |= route.Path.Waypoints[^1] != route.OriginalOrder.WorldTarget;
            if (simulation.Entities.TryGetComponent(unit, out MovementOrder order))
                localApproach |= order.Kind == MovementOrderKind.LocalApproach;
        }
        Assert.True(projected);
        Assert.True(localApproach);
        Assert.InRange(Vector3.Distance(simulation.Entities.GetComponent<WorldTransform>(unit).Position, goal), 0, .5f);
        Assert.False(simulation.Entities.HasComponent<MovementOrder>(unit));
        Assert.False(simulation.Entities.HasComponent<NavigationRouteState>(unit));
    }

    [Fact]
    public void IdleLongVehicleDoesNotBlockAPhysicallyClearParallelRoute()
    {
        var (simulation, _, first) = Create();
        var entities = simulation.Entities;
        var parked = entities.CreateEntity();
        var initial = new WorldTransform(new(13.2f, .5f, 25), Quaternion.Identity, new(3.4f, 2.5f, 6.8f));
        entities.AddComponent(parked, initial);
        entities.AddComponent(parked, entities.GetComponent<GroundMovement>(first));
        entities.AddComponent(parked, GroundMovementState.Stationary());
        entities.AddComponent(parked, new NavigationAgent(NavigationMovementClass.Wheeled));
        entities.AddComponent(parked, entities.GetComponent<UnitIdentity>(first));
        entities.AddComponent(parked, entities.GetComponent<ControllableEntity>(first));
        entities.AddComponent(parked, new SpatialPresence(initial.Scale * .5f, new SpatialEntryMetadata(1, 1, SpatialMobility.Mobile)));
        simulation.RunTicks(300, TestContext.Current.CancellationToken);
        Assert.Equal(initial, entities.GetComponent<WorldTransform>(parked));
        var position = entities.GetComponent<WorldTransform>(first).Position;
        Assert.True(Vector3.Distance(position, new(10, .5f, 40)) < .5f, $"Truck stopped at {position} despite a clear physical lane.");
        Assert.False(entities.HasComponent<MovementOrder>(first));
    }

    [Fact]
    public void RearNeighborCannotPreventASafeForwardClearanceBeforeTurning()
    {
        Vector3 goal = new(10, .5f, 30);
        var (simulation, movement, unit) = Create(goal: goal);
        var entities = simulation.Entities;
        var initial = new WorldTransform(new(30, .5f, 30), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2), new(3.2f, 2.2f, 6.4f));
        entities.SetComponent(unit, initial);
        entities.SetComponent(unit, GroundMovementState.Stationary(MathF.PI / 2));
        entities.SetComponent(unit, new SpatialPresence(initial.Scale * .5f, new SpatialEntryMetadata(1, 1, SpatialMobility.Mobile)));
        var parked = entities.CreateEntity();
        var parkedPose = initial with { Position = new(24.85f, .5f, 30), Rotation = Quaternion.Identity };
        entities.AddComponent(parked, parkedPose);
        entities.AddComponent(parked, entities.GetComponent<GroundMovement>(unit));
        entities.AddComponent(parked, GroundMovementState.Stationary());
        entities.AddComponent(parked, new NavigationAgent(NavigationMovementClass.Wheeled));
        entities.AddComponent(parked, entities.GetComponent<UnitIdentity>(unit));
        entities.AddComponent(parked, entities.GetComponent<ControllableEntity>(unit));
        entities.AddComponent(parked, new SpatialPresence(initial.Scale * .5f, new SpatialEntryMetadata(1, 1, SpatialMobility.Mobile)));
        bool forwardClearance = false;
        for (int tick = 0; tick < 600; tick++)
        {
            simulation.AdvanceOneTick();
            var pose = entities.GetComponent<WorldTransform>(unit);
            forwardClearance |= pose.Position.X > initial.Position.X + .05f;
        }
        Assert.True(forwardClearance, $"No initial straight clearance; final={entities.GetComponent<WorldTransform>(unit)}; movement={entities.GetComponent<GroundMovementState>(unit)}.");
        Assert.Equal(parkedPose, entities.GetComponent<WorldTransform>(parked));
        var final = entities.GetComponent<WorldTransform>(unit).Position;
        Assert.True(Vector3.Distance(final, goal) < .5f, $"Turning truck stopped at {final}; movement={entities.GetComponent<GroundMovementState>(unit)}; checkpoint={string.Join(';', movement.CaptureNavigationState().Actors)}; traffic={movement.CaptureNavigationState().Traffic}.");
        Assert.False(entities.HasComponent<MovementOrder>(unit));
    }

    [Fact]
    public void QueueHeadBesideAnArrivedFollowerCanClearBeforeTurningTowardItsOwnPose()
    {
        Vector3 goal = new(43.06922f, .5f, 24.7406f);
        var (simulation, movement, unit) = Create(goal: goal);
        var entities = simulation.Entities;
        var initial = new WorldTransform(new(30, .5f, 30), Quaternion.CreateFromAxisAngle(Vector3.UnitY, -2.218099f), new(3.2f, 2.2f, 6.4f));
        entities.SetComponent(unit, initial);
        entities.SetComponent(unit, GroundMovementState.Stationary(-2.218099f));
        entities.SetComponent(unit, new SpatialPresence(initial.Scale * .5f, new SpatialEntryMetadata(1, 1, SpatialMobility.Mobile)));
        entities.SetComponent(unit, new MovementOrder(new PlayerId(1), goal, SimulationTick.Zero, new(1),
            MovementOrderKind.Strategic));
        var building = entities.CreateEntity();
        var queue = new VehicleQueueEntry(building, new PlayerId(1), 1, SimulationTick.Zero,
            VehicleQueueService.CargoPickup, Position: 1, WaitingCount: 2, Target: goal, Heading: MathF.PI / 2);
        var dockPose = new VehicleDockPose(goal + new Vector3(5, 0, 0), goal + new Vector3(10, 0, 0),
            goal + new Vector3(15, 0, 0), MathF.PI / 2, 3.2f, 1.6f, goal);
        entities.AddComponent(building, new VehicleDockSlot(unit, 1, dockPose, new PlayerId(1)));
        entities.AddComponent(unit, new VehicleDockVisit(building, 1, VehicleDockStatus.Reserved, VehicleDockBlockReason.None, dockPose));
        entities.AddComponent(unit, queue);
        Assert.False(entities.HasComponent<VehicleQueueMotion>(unit));
        var parked = entities.CreateEntity();
        var parkedPose = initial with { Position = new(31.16922f, .5f, 24.7406f), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2) };
        entities.AddComponent(parked, parkedPose);
        entities.AddComponent(parked, entities.GetComponent<GroundMovement>(unit));
        entities.AddComponent(parked, GroundMovementState.Stationary(MathF.PI / 2));
        entities.AddComponent(parked, entities.GetComponent<UnitIdentity>(unit));
        entities.AddComponent(parked, entities.GetComponent<ControllableEntity>(unit));
        entities.AddComponent(parked, new SpatialPresence(initial.Scale * .5f, new SpatialEntryMetadata(1, 1, SpatialMobility.Mobile)));
        simulation.RunTicks(600, TestContext.Current.CancellationToken);
        Assert.Equal(parkedPose, entities.GetComponent<WorldTransform>(parked));
        Assert.Equal(queue, entities.GetComponent<VehicleQueueEntry>(unit));
        Assert.False(entities.HasComponent<MovementOrder>(unit), $"pose={entities.GetComponent<WorldTransform>(unit)}; motion={entities.GetComponent<GroundMovementState>(unit)}; veto={string.Join(';', movement.CaptureLegacyNavigationVetoes())}");
        Assert.InRange(Vector3.Distance(entities.GetComponent<WorldTransform>(unit).Position, goal), 0, .11f);
    }

    [Fact]
    public void ReservedHeadCanClearARearNeighborBeforeEnteringItsDockLane()
    {
        Vector3 goal = new(45.61064f, .5f, 29.082f);
        var (simulation, movement, unit) = Create(goal: goal);
        var entities = simulation.Entities;
        const float heading = 2.9160392f;
        var initial = new WorldTransform(new(30, .5f, 30), Quaternion.CreateFromAxisAngle(Vector3.UnitY, heading), new(3.2f, 2.4f, 6.4f));
        entities.SetComponent(unit, initial);
        entities.SetComponent(unit, GroundMovementState.Stationary(heading));
        entities.SetComponent(unit, new SpatialPresence(initial.Scale * .5f, new SpatialEntryMetadata(1, 1, SpatialMobility.Mobile)));
        entities.SetComponent(unit, new GroundMovement(14.5f, 7, 10, 2.5f, 1.9f, 1, 5.5f, 8, 24, .5f));
        entities.SetComponent(unit, new MovementOrder(new PlayerId(1), goal, SimulationTick.Zero, new(1), MovementOrderKind.LocalApproach, MathF.PI / 2));
        var building = entities.CreateEntity();
        Vector3 approach = new(30.21064f, .5f, 29.082f);
        var queue = new VehicleQueueEntry(building, new PlayerId(1), 15, SimulationTick.Zero,
            VehicleQueueService.CargoPickup, Position: 1, WaitingCount: 1, Target: approach, Heading: MathF.PI / 2);
        var dockPose = new VehicleDockPose(goal, goal + new Vector3(15.2f, 0, 0), goal + new Vector3(30.4f, 0, 0), MathF.PI / 2, 3.2f, 1.6f, approach);
        entities.AddComponent(building, new VehicleDockSlot(unit, 15, dockPose, new PlayerId(1)));
        entities.AddComponent(unit, new VehicleDockVisit(building, 15, VehicleDockStatus.Approaching, VehicleDockBlockReason.None, dockPose));
        entities.AddComponent(unit, queue);
        var parked = entities.CreateEntity();
        var parkedPose = initial with { Position = new(23.97074f, .5f, 30.915f), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1.9693964f) };
        entities.AddComponent(parked, parkedPose);
        entities.AddComponent(parked, entities.GetComponent<GroundMovement>(unit));
        entities.AddComponent(parked, GroundMovementState.Stationary(1.9693964f));
        entities.AddComponent(parked, entities.GetComponent<UnitIdentity>(unit));
        entities.AddComponent(parked, entities.GetComponent<ControllableEntity>(unit));
        entities.AddComponent(parked, new SpatialPresence(initial.Scale * .5f, new SpatialEntryMetadata(1, 1, SpatialMobility.Mobile)));
        simulation.RunTicks(600, TestContext.Current.CancellationToken);
        Assert.Equal(parkedPose, entities.GetComponent<WorldTransform>(parked));
        Assert.Equal(queue, entities.GetComponent<VehicleQueueEntry>(unit));
        Assert.False(entities.GetComponent<VehicleDockVisit>(unit).LaneEntered);
        Assert.False(entities.HasComponent<MovementOrder>(unit), $"pose={entities.GetComponent<WorldTransform>(unit)}; veto={string.Join(';', movement.CaptureLegacyNavigationVetoes())}");
        Assert.InRange(Vector3.Distance(entities.GetComponent<WorldTransform>(unit).Position, goal), 0, .11f);
    }

    [Fact]
    public void QueueOwnerCanBackIntoItsOwnWaitingPoseWithItsRequiredAlignment()
    {
        Vector3 goal = new(25, .5f, 30);
        var (simulation, _, unit) = Create(goal: goal);
        var entities = simulation.Entities;
        var initial = new WorldTransform(new(30, .5f, 30), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2), new(3.2f, 2.2f, 6.4f));
        entities.SetComponent(unit, initial);
        entities.SetComponent(unit, GroundMovementState.Stationary(MathF.PI / 2));
        entities.SetComponent(unit, new SpatialPresence(initial.Scale * .5f, new SpatialEntryMetadata(1, 1, SpatialMobility.Mobile)));
        entities.SetComponent(unit, new MovementOrder(new PlayerId(1), goal, SimulationTick.Zero, new(1),
            MovementOrderKind.LocalApproach, MathF.PI / 2));
        var building = entities.CreateEntity();
        var queue = new VehicleQueueEntry(building, new PlayerId(1), 1, SimulationTick.Zero,
            VehicleQueueService.CargoPickup, Position: 1, WaitingCount: 1, Target: goal, Heading: MathF.PI / 2);
        entities.AddComponent(unit, queue);
        entities.AddComponent(unit, new VehicleQueueMotion(goal, goal, MathF.PI / 2, default, 2, 1));
        var parked = entities.CreateEntity();
        var parkedPose = initial with { Position = new(30, .5f, 33.6f) };
        entities.AddComponent(parked, parkedPose);
        entities.AddComponent(parked, entities.GetComponent<GroundMovement>(unit));
        entities.AddComponent(parked, GroundMovementState.Stationary(MathF.PI / 2));
        entities.AddComponent(parked, entities.GetComponent<UnitIdentity>(unit));
        entities.AddComponent(parked, entities.GetComponent<ControllableEntity>(unit));
        entities.AddComponent(parked, new SpatialPresence(initial.Scale * .5f, new SpatialEntryMetadata(1, 1, SpatialMobility.Mobile)));
        simulation.RunTicks(200, TestContext.Current.CancellationToken);
        Assert.Equal(parkedPose, entities.GetComponent<WorldTransform>(parked));
        Assert.Equal(queue, entities.GetComponent<VehicleQueueEntry>(unit));
        Assert.False(entities.HasComponent<MovementOrder>(unit));
        Assert.InRange(Vector3.Distance(entities.GetComponent<WorldTransform>(unit).Position, goal), 0, .11f);
        Assert.InRange(MathF.Abs(GroundMovementState.NormalizeAngle(entities.GetComponent<GroundMovementState>(unit).HeadingRadians - MathF.PI / 2)), 0, MathF.PI / 90);
    }

    [Fact]
    public void ReplacementCommandGetsANewGenerationAndDestination()
    {
        var (simulation, movement, unit) = Create();
        simulation.RunTicks(20, TestContext.Current.CancellationToken);
        var before = Assert.Single(movement.CaptureNavigationState().Actors);
        simulation.Entities.SetComponent(unit, new MovementOrder(new PlayerId(1), new(40, .5f, 30), simulation.CurrentTick, simulation.CurrentTick));
        simulation.RunTicks(400, TestContext.Current.CancellationToken);
        var after = Assert.Single(movement.CaptureNavigationState().Actors);
        Assert.True(after.Generation > before.Generation);
        Assert.Equal(new Vector3(40, .5f, 30), after.OriginalOrder.WorldTarget);
        Assert.InRange(Vector3.Distance(simulation.Entities.GetComponent<WorldTransform>(unit).Position, new(40, .5f, 30)), 0, .5f);
    }
    [Fact]
    public void IncompletePhysicalNeighborhoodCannotFallBackToLegacyTravel()
    {
        var (simulation, movement, unit) = Create();
        for (int i = 0; i < 129; i++)
        {
            var body = simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(body, new WorldTransform(new(15 + i % 10, .5f, 10 + i / 10), Quaternion.Identity, Vector3.One));
            simulation.Entities.AddComponent(body, new SpatialPresence(new Vector3(.2f), new SpatialEntryMetadata(2, 1, SpatialMobility.Mobile)));
        }
        var initial = simulation.Entities.GetComponent<WorldTransform>(unit);
        simulation.RunTicks(100, TestContext.Current.CancellationToken);
        Assert.Equal(initial, simulation.Entities.GetComponent<WorldTransform>(unit));
        Assert.True(simulation.Entities.HasComponent<MovementOrder>(unit));
        var actor = Assert.Single(movement.CaptureNavigationState().Actors);
        Assert.NotEqual(NavigationBlockReason.None, actor.LastReason);
    }
    [Fact]
    public void FuelConstraintKeepsBothPositionAndOriginalOrder()
    {
        var (simulation, movement, unit) = Create();
        simulation.Entities.AddComponent(unit, new SupplyMovementConstraint(0, false));
        var initial = simulation.Entities.GetComponent<WorldTransform>(unit);
        simulation.RunTicks(100, TestContext.Current.CancellationToken);
        Assert.Equal(initial, simulation.Entities.GetComponent<WorldTransform>(unit));
        Assert.True(simulation.Entities.HasComponent<MovementOrder>(unit));
        Assert.Equal(GroundMovementStatus.OutOfFuel, simulation.Entities.GetComponent<GroundMovementState>(unit).Status);
    }
    [Fact]
    public void OfficialScenarioContinuesToDeliverCargoWithTrafficConnected()
    {
        using var runtime = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation, 2026), TestContext.Current.CancellationToken);
        runtime.Simulation.RunTicks(5000, TestContext.Current.CancellationToken);
        long earlierDeliveries = runtime.CargoTransport.Metrics.CompletedOrderCount;
        runtime.Simulation.RunTicks(5000, TestContext.Current.CancellationToken);
        var actors = runtime.Services.GroundMovement.CaptureNavigationState().Actors;
        var details = new List<string>();
        foreach (var entity in runtime.Simulation.Entities.Query<UnitIdentity>())
        {
            var es = runtime.Simulation.Entities;
            if (es.GetComponent<UnitIdentity>(entity).UnitId != UnitIds.CargoTruck) continue;
            es.TryGetComponent(entity, out GroundMovementState state);
            es.TryGetComponent(entity, out MovementOrder order);
            es.TryGetComponent(entity, out VehicleDockVisit dock);
            es.TryGetComponent(entity, out VehicleQueueEntry queue);
            var transform = es.GetComponent<WorldTransform>(entity);
            runtime.Terrain.TrySampleHeight(transform.Position.X, transform.Position.Z, out float ground);
            details.Add($"offset={transform.Position.Y - ground}; authored={es.GetComponent<GroundMovement>(entity).HeightOffset}; {entity} {es.GetComponent<WorldTransform>(entity).Position} {state} {order} {dock} {queue}");
        }
        foreach (var entity in runtime.Simulation.Entities.Query<WorldTransform, SpatialPresence>())
        {
            var es = runtime.Simulation.Entities;
            if (!NavigationMotionCapture.TryCaptureBody(es, es.GetComponent<SpatialPresence>(entity).CreateEntry(entity, es.GetComponent<WorldTransform>(entity)), out _))
                details.Add($"INVALID PHYSICAL BODY {entity} {es.GetComponent<SpatialPresence>(entity)}");
        }
        Assert.True(earlierDeliveries > 0 && runtime.CargoTransport.Metrics.CompletedOrderCount > earlierDeliveries,
            $"Deliveries={earlierDeliveries}->{runtime.CargoTransport.Metrics.CompletedOrderCount}; Actors: {string.Join(';', actors)}; Vehicles: {string.Join(';', details)}");
    }
    [Fact]
    public void ActiveTrafficSurvivesSerializedSaveReplayAndIndependentContinuation()
    {
        using var original = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation, 2026), TestContext.Current.CancellationToken);
        original.Simulation.RunTicks(950, TestContext.Current.CancellationToken);
        Assert.NotEmpty(original.Services.GroundMovement.CaptureNavigationState().Actors);
        var save = MatchPersistenceSerializer.DeserializeSave(MatchPersistenceSerializer.SerializeSave(MatchPersistenceService.CaptureSave(original)));
        var replay = MatchPersistenceSerializer.DeserializeReplay(MatchPersistenceSerializer.SerializeReplay(MatchPersistenceService.CaptureReplay(original)));
        using var restored = MatchPersistenceService.Restore(save);
        using var playback = MatchPersistenceService.PlayReplay(replay);
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
        foreach (ulong ticks in new ulong[] { 1, 30, 200 })
        {
            original.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
            restored.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
            playback.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
            string expected = MatchAuthoritativeSnapshot.Capture(original).ComputeSha256();
            Assert.Equal(expected, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
            Assert.Equal(expected, MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
        }
    }
    private static (SimulationCoordinator, GroundMovementSystem, EntityId) Create(IReadOnlyList<AxisAlignedBounds>? obstacles = null, Vector3? goal = null)
    {
        var settings = new WorldGridSettings { ChunkSizeMeters = 64, HeightSamplesPerSide = 17 };
        var terrain = new TerrainWorld(settings, [new TerrainChunk(new(0, 0), new TerrainHeightfield(17, 64, new float[289]))]);
        var world = NavigationWorld.Build(terrain, obstacles ?? [], gridSettings: new NavigationGridSettings { CellSizeMeters = 4 });
        var navigation = new HierarchicalNavigationSystem(new HierarchicalPathfinder(world));
        var movement = new GroundMovementSystem(terrain);
        movement.ConnectNavigation(navigation, terrain, obstacles ?? []);
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(navigation); simulation.RegisterSystem(movement);
        var entities = simulation.Entities; var unit = entities.CreateEntity();
        entities.AddComponent(unit, new WorldTransform(new(10, .5f, 10), Quaternion.Identity, Vector3.One));
        entities.AddComponent(unit, new GroundMovement(6, 4, 4, 2, .5f, .1f, 1, 4, 45, .5f));
        entities.AddComponent(unit, GroundMovementState.Stationary());
        entities.AddComponent(unit, new NavigationAgent(NavigationMovementClass.Wheeled));
        entities.AddComponent(unit, new UnitIdentity(UnitIds.CargoTruck, default));
        entities.AddComponent(unit, new ControllableEntity(new PlayerId(1), ControllableEntityCategory.Logistics));
        entities.AddComponent(unit, new SpatialPresence(new Vector3(.5f), new SpatialEntryMetadata(1, 1, SpatialMobility.Mobile)));
        entities.AddComponent(unit, new MovementOrder(new PlayerId(1), goal ?? new(10, .5f, 40), SimulationTick.Zero, new(1)));
        return (simulation, movement, unit);
    }
}
