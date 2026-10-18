using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class GroundMovementPrecisionTests
{
    private static readonly PlayerId Player = new(1);

    [Theory]
    [InlineData(10, 0.0f)]
    [InlineData(20, 0.0f)]
    [InlineData(60, 0.0f)]
    [InlineData(10, 0.05f)]
    [InlineData(20, 0.05f)]
    [InlineData(60, 0.05f)]
    public void FastUnitCannotSkipSmallArrivalRegion(int ticksPerSecond, float stopRadius)
    {
        var simulation = new SimulationCoordinator(ticksPerSecond: ticksPerSecond);
        EntityId entity = AddUnit(simulation, new Vector3(8, 0.5f, 8), stopRadius);
        simulation.Entities.SetComponent(entity, GroundMovementState.Stationary() with
        {
            Velocity = new Vector3(0, 0, 40)
        });
        Vector3 target = new(8, 0.5f, 8.2f);
        Order(simulation, entity, target);
        simulation.RegisterSystem(new GroundMovementSystem());

        simulation.AdvanceOneTick();

        Vector3 position = simulation.Entities.GetComponent<WorldTransform>(entity).Position;
        GroundMovementState state = simulation.Entities.GetComponent<GroundMovementState>(entity);
        Assert.InRange(Vector3.Distance(position, target), 0, stopRadius + 0.01f);
        Assert.Equal(GroundMovementStatus.Arrived, state.Status);
        Assert.Equal(Vector3.Zero, state.Velocity);
        Assert.False(simulation.Entities.HasComponent<MovementOrder>(entity));
    }

    [Fact]
    public void PassingBesideTargetDoesNotSnapSidewaysOrCompleteOrder()
    {
        var simulation = new SimulationCoordinator(ticksPerSecond: 20);
        EntityId entity = AddUnit(simulation, new Vector3(8, 0.5f, 8));
        simulation.Entities.SetComponent(entity, Movement(stopRadius: 0.05f, turnRate: 0.0001f));
        simulation.Entities.SetComponent(entity, GroundMovementState.Stationary() with
        {
            Velocity = new Vector3(0, 0, 40)
        });
        Order(simulation, entity, new Vector3(8.5f, 0.5f, 8.2f));
        simulation.RegisterSystem(new GroundMovementSystem());

        simulation.AdvanceOneTick();

        Vector3 position = simulation.Entities.GetComponent<WorldTransform>(entity).Position;
        Assert.InRange(position.X, 8, 8.001f);
        Assert.True(position.Z > 9.9f);
        Assert.True(simulation.Entities.HasComponent<MovementOrder>(entity));
        Assert.NotEqual(GroundMovementStatus.Arrived,
            simulation.Entities.GetComponent<GroundMovementState>(entity).Status);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(60)]
    public void SweptClearanceStopsAtThinWallWithAvoidanceDisabled(int ticksPerSecond)
    {
        SimulationCoordinator simulation = CreateSpatialSimulation(ticksPerSecond);
        EntityId entity = AddUnit(simulation, new Vector3(8, 0.5f, 8));
        AddWall(simulation);
        simulation.AdvanceOneTick();
        simulation.Entities.SetComponent(entity, GroundMovementState.Stationary() with
        {
            Velocity = new Vector3(0, 0, 100)
        });
        Order(simulation, entity, new Vector3(8, 0.5f, 40));

        simulation.AdvanceOneTick();

        Vector3 position = simulation.Entities.GetComponent<WorldTransform>(entity).Position;
        // Wall front 8.975 m, unit radius 0.5 m and a 1 mm contact skin.
        Assert.InRange(position.Z, 8.47f, 8.475f);
        Assert.True(simulation.Entities.HasComponent<MovementOrder>(entity));
    }

    [Fact]
    public void TargetBehindWallIsNotAcceptedAsArrival()
    {
        SimulationCoordinator simulation = CreateSpatialSimulation(20);
        EntityId entity = AddUnit(simulation, new Vector3(8, 0.5f, 8));
        AddWall(simulation);
        simulation.AdvanceOneTick();
        simulation.Entities.SetComponent(entity, GroundMovementState.Stationary() with
        {
            Velocity = new Vector3(0, 0, 100)
        });
        Order(simulation, entity, new Vector3(8, 0.5f, 9.2f));

        simulation.AdvanceOneTick();

        Assert.InRange(simulation.Entities.GetComponent<WorldTransform>(entity).Position.Z, 8.47f, 8.475f);
        Assert.True(simulation.Entities.HasComponent<MovementOrder>(entity));
        Assert.NotEqual(GroundMovementStatus.Arrived,
            simulation.Entities.GetComponent<GroundMovementState>(entity).Status);
    }

    [Fact]
    public void MobilePenetrationStillResolvesWhenSeparationSteeringIsDisabled()
    {
        SimulationCoordinator simulation = CreateSpatialSimulation(20);
        EntityId moving = AddUnit(simulation, new Vector3(8, 0.5f, 8));
        EntityId stopped = AddUnit(simulation, new Vector3(8, 0.5f, 9.2f));
        simulation.AdvanceOneTick();
        simulation.Entities.SetComponent(moving, GroundMovementState.Stationary() with
        {
            Velocity = new Vector3(0, 0, 10)
        });
        Order(simulation, moving, new Vector3(8, 0.5f, 40));

        simulation.AdvanceOneTick();

        Vector3 movingPosition = simulation.Entities.GetComponent<WorldTransform>(moving).Position;
        Vector3 stoppedPosition = simulation.Entities.GetComponent<WorldTransform>(stopped).Position;
        Assert.True(Vector3.Distance(movingPosition, stoppedPosition) >= 0.9999f);
        Assert.True(movingPosition.Z <= 8.201f);
    }

    [Fact]
    public void ExistingPenetrationCanRecoverOutward()
    {
        SimulationCoordinator simulation = CreateSpatialSimulation(20);
        EntityId entity = AddUnit(simulation, new Vector3(8, 0.5f, 9.2f));
        AddWall(simulation);
        simulation.AdvanceOneTick();
        simulation.Entities.SetComponent(entity, GroundMovementState.Stationary() with
        {
            Velocity = Vector3.UnitZ
        });
        Order(simulation, entity, new Vector3(8, 0.5f, 40));

        simulation.AdvanceOneTick();

        Assert.True(simulation.Entities.GetComponent<WorldTransform>(entity).Position.Z >= 9.525f);
    }

    [Fact]
    public void TangentialTravelAlongWallRemainsPossible()
    {
        SimulationCoordinator simulation = CreateSpatialSimulation(20);
        EntityId entity = AddUnit(simulation, new Vector3(8, 0.5f, 8.475f));
        AddWall(simulation);
        simulation.AdvanceOneTick();
        simulation.Entities.SetComponent(entity, GroundMovementState.Stationary(MathF.PI * 0.5f) with
        {
            Velocity = new Vector3(10, 0, 0)
        });
        Order(simulation, entity, new Vector3(12, 0.5f, 8.475f));

        simulation.AdvanceOneTick();

        Vector3 position = simulation.Entities.GetComponent<WorldTransform>(entity).Position;
        Assert.True(position.X > 8.4f);
        Assert.InRange(position.Z, 8.474f, 8.476f);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(60)]
    public void PreciseTruckTurnConvergesFromObservedExitOrbit(int ticksPerSecond)
    {
        var simulation = new SimulationCoordinator(ticksPerSecond);
        Vector3 target = new(517.60004f, 5.925199f, 1550.5f);
        Vector3 initial = new(517.8571f, 5.9237876f, 1550.5386f);
        EntityId entity = AddUnit(simulation, initial);
        var movement = new GroundMovement(14.5f, 7, 10, 2.5f, 1.9f, 1, 5.5f, 8, 24, 1.1f);
        simulation.Entities.SetComponent(entity, movement);
        simulation.Entities.SetComponent(entity, GroundMovementState.Stationary(0.4568448f) with
        {
            Velocity = new Vector3(0.44067383f, 0.003709793f, 0.8984375f)
        });
        simulation.Entities.AddComponent(entity, new MovementOrder(Player, target, SimulationTick.Zero,
            new SimulationTick(1), MovementOrderKind.LocalApproach, MathF.PI / 2));
        simulation.RegisterSystem(new GroundMovementSystem());
        for (int tick = 0; tick < ticksPerSecond * 20 && simulation.Entities.HasComponent<MovementOrder>(entity); tick++)
        {
            float previousHeading = simulation.Entities.GetComponent<GroundMovementState>(entity).HeadingRadians;
            Vector3 previous = simulation.Entities.GetComponent<WorldTransform>(entity).Position;
            simulation.AdvanceOneTick();
            var current = simulation.Entities.GetComponent<GroundMovementState>(entity);
            Assert.InRange(MathF.Abs(GroundMovementState.NormalizeAngle(current.HeadingRadians - previousHeading)),
                0, movement.TurnRateRadiansPerSecond / ticksPerSecond + 0.0001f);
            Assert.InRange(Vector3.Distance(previous, simulation.Entities.GetComponent<WorldTransform>(entity).Position),
                0, movement.MaximumSpeed / ticksPerSecond + 0.0001f);
        }
        var final = simulation.Entities.GetComponent<GroundMovementState>(entity);
        Assert.False(simulation.Entities.HasComponent<MovementOrder>(entity),
            $"Truck kept orbiting: {simulation.Entities.GetComponent<WorldTransform>(entity).Position}; {final}.");
        Assert.InRange(Vector3.Distance(target, simulation.Entities.GetComponent<WorldTransform>(entity).Position), 0, 0.111f);
        Assert.InRange(MathF.Abs(GroundMovementState.NormalizeAngle(final.HeadingRadians - MathF.PI / 2)), 0, MathF.PI / 90);
        Assert.Equal(Vector3.Zero, final.Velocity);
    }

    private static SimulationCoordinator CreateSpatialSimulation(int ticksPerSecond)
    {
        var worldSettings = new WorldGridSettings
        {
            ChunkSizeMeters = 64,
            RegionSizeInChunks = 4,
            HeightSamplesPerSide = 3
        };
        var terrain = new TerrainWorld(worldSettings,
            [new TerrainChunk(new ChunkCoordinate(0, 0), new TerrainHeightfield(3, 64, new float[9]))]);
        var spatialIndex = new SpatialGridIndex(new SpatialGridSettings
        {
            World = worldSettings,
            CellSizeMeters = 4
        });
        var synchronizer = new SpatialIndexSynchronizer(spatialIndex);
        var simulation = new SimulationCoordinator(ticksPerSecond: ticksPerSecond);
        simulation.RegisterSystem(new GroundMovementSystem(terrain, spatialIndex,
            new GroundMovementSystemOptions
            {
                SeparationWeight = 0,
                ObstacleSteeringWeight = 0,
                ObstacleClearanceMeters = 0
            }));
        simulation.RegisterSystem(new SpatialIndexSystem(synchronizer));
        simulation.RegisterSystem(new SpatialIndexCleanupSystem(synchronizer));
        return simulation;
    }

    private static GroundMovement Movement(float stopRadius = 0.05f, float turnRate = MathF.Tau) =>
        new(maximumSpeed: 100, acceleration: 100, deceleration: 1,
            turnRateRadiansPerSecond: turnRate, radius: 0.5f, stopRadius: stopRadius,
            separationRadius: 2, obstacleLookAhead: 0, maximumSlopeDegrees: 45, heightOffset: 0.5f);

    private static EntityId AddUnit(SimulationCoordinator simulation, Vector3 position, float stopRadius = 0.05f)
    {
        EntityId entity = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(entity, new WorldTransform(position, Quaternion.Identity, Vector3.One));
        simulation.Entities.AddComponent(entity, Movement(stopRadius));
        simulation.Entities.AddComponent(entity, GroundMovementState.Stationary());
        simulation.Entities.AddComponent(entity, new SpatialPresence(new Vector3(0.5f),
            new SpatialEntryMetadata(Player.Value, 1, SpatialMobility.Mobile)));
        return entity;
    }

    private static void AddWall(SimulationCoordinator simulation)
    {
        EntityId wall = simulation.Entities.CreateEntity();
        Vector3 extents = new(2, 2, 0.025f);
        simulation.Entities.AddComponent(wall,
            new WorldTransform(new Vector3(8, 0.5f, 9), Quaternion.Identity, extents * 2));
        simulation.Entities.AddComponent(wall, new SpatialPresence(extents,
            new SpatialEntryMetadata(0, 1UL << 63, SpatialMobility.Static)));
    }

    private static void Order(SimulationCoordinator simulation, EntityId entity, Vector3 target)
    {
        simulation.Entities.AddComponent(entity,
            new MovementOrder(Player, target, simulation.CurrentTick, new SimulationTick(simulation.CurrentTick.Value + 1)));
    }
}
