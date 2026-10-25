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
    private static (SimulationCoordinator, GroundMovementSystem, EntityId) Create()
    {
        var settings = new WorldGridSettings { ChunkSizeMeters = 64, HeightSamplesPerSide = 17 };
        var terrain = new TerrainWorld(settings, [new TerrainChunk(new(0, 0), new TerrainHeightfield(17, 64, new float[289]))]);
        var world = NavigationWorld.Build(terrain, gridSettings: new NavigationGridSettings { CellSizeMeters = 4 });
        var navigation = new HierarchicalNavigationSystem(new HierarchicalPathfinder(world));
        var movement = new GroundMovementSystem(terrain);
        movement.ConnectNavigation(navigation, terrain, []);
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
        entities.AddComponent(unit, new MovementOrder(new PlayerId(1), new(10, .5f, 40), SimulationTick.Zero, new(1)));
        return (simulation, movement, unit);
    }
}
