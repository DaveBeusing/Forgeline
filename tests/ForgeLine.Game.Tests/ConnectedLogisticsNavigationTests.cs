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
