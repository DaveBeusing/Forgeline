using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class GroundMovementSystemTests
{
    private static readonly PlayerId LocalPlayer = new(1);

    [Fact]
    public void ConstantVelocityCoversSameDistanceAcrossTickRates()
    {
        float atTenHertz = RunConstantVelocityForOneSecond(10);
        float atTwentyHertz = RunConstantVelocityForOneSecond(20);

        Assert.InRange(atTenHertz, 9.999f, 10.001f);
        Assert.InRange(atTwentyHertz, 9.999f, 10.001f);
        Assert.InRange(
            MathF.Abs(atTenHertz - atTwentyHertz),
            0.0f,
            0.0001f);
    }

    [Fact]
    public void AccelerationAndTurnRateAreBoundedPerFixedTick()
    {
        TerrainWorld terrain = CreateFlatTerrain(64.0f);
        var simulation = new SimulationCoordinator(ticksPerSecond: 20);
        var movementSystem = new GroundMovementSystem(terrain);

        GroundMovement movement = CreateMovement(
            maximumSpeed: 20.0f,
            acceleration: 4.0f,
            deceleration: 8.0f,
            turnRateRadiansPerSecond: MathF.PI * 0.5f);

        EntityId entity = AddGroundUnit(
            simulation,
            new Vector3(8.0f, 0.5f, 8.0f),
            movement);

        AddOrder(
            simulation,
            entity,
            new Vector3(50.0f, 0.0f, 8.0f),
            acceptedAtTick: new SimulationTick(1));

        simulation.RegisterSystem(movementSystem);
        simulation.AdvanceOneTick();

        GroundMovementState state =
            simulation.Entities.GetComponent<GroundMovementState>(entity);

        Assert.InRange(
            Horizontal(state.Velocity).Length(),
            0.199f,
            0.201f);
        Assert.InRange(
            state.HeadingRadians,
            MathF.PI / 40.0f - 0.0001f,
            MathF.PI / 40.0f + 0.0001f);
        Assert.Equal(GroundMovementStatus.Moving, state.Status);
    }

    [Fact]
    public void ArrivalStopsUnitAndConsumesMovementOrder()
    {
        TerrainWorld terrain = CreateFlatTerrain(64.0f);
        var simulation = new SimulationCoordinator(ticksPerSecond: 20);
        var movementSystem = new GroundMovementSystem(terrain);

        EntityId entity = AddGroundUnit(
            simulation,
            new Vector3(8.0f, 0.5f, 8.0f),
            CreateMovement(
                maximumSpeed: 10.0f,
                acceleration: 100.0f,
                deceleration: 100.0f,
                turnRateRadiansPerSecond: MathF.Tau,
                stopRadius: 0.25f));

        AddOrder(
            simulation,
            entity,
            new Vector3(8.0f, 0.0f, 12.0f),
            acceptedAtTick: new SimulationTick(1));

        simulation.RegisterSystem(movementSystem);

        int executedTicks = 0;
        while (simulation.Entities.HasComponent<MovementOrder>(entity) &&
               executedTicks < 100)
        {
            simulation.AdvanceOneTick();
            executedTicks++;
        }

        Assert.True(executedTicks < 100);
        Assert.False(
            simulation.Entities.HasComponent<MovementOrder>(entity));

        GroundMovementState state =
            simulation.Entities.GetComponent<GroundMovementState>(entity);

        Assert.Equal(GroundMovementStatus.Arrived, state.Status);
        Assert.Equal(Vector3.Zero, state.Velocity);
        Assert.Equal(1, movementSystem.LastDiagnostics.ArrivedUnitCount);
    }

    [Fact]
    public void TerrainFollowingUsesAuthoritativeHeightfield()
    {
        TerrainWorld terrain = CreateRampTerrain(
            chunkSize: 10.0f,
            positiveXHeight: 2.0f);
        var simulation = new SimulationCoordinator(ticksPerSecond: 20);
        var movementSystem = new GroundMovementSystem(terrain);

        GroundMovement movement = CreateMovement(
            maximumSpeed: 6.0f,
            acceleration: 100.0f,
            deceleration: 100.0f,
            turnRateRadiansPerSecond: MathF.Tau,
            maximumSlopeDegrees: 30.0f,
            heightOffset: 0.75f);

        EntityId entity = AddGroundUnit(
            simulation,
            new Vector3(1.0f, 0.95f, 5.0f),
            movement);

        AddOrder(
            simulation,
            entity,
            new Vector3(9.0f, 0.0f, 5.0f),
            acceptedAtTick: new SimulationTick(1));

        simulation.RegisterSystem(movementSystem);
        simulation.AdvanceOneTick();

        WorldTransform transform =
            simulation.Entities.GetComponent<WorldTransform>(entity);

        Assert.True(
            terrain.TrySampleHeight(
                transform.Position.X,
                transform.Position.Z,
                out float expectedHeight));
        Assert.InRange(
            transform.Position.Y,
            expectedHeight + movement.HeightOffset - 0.0001f,
            expectedHeight + movement.HeightOffset + 0.0001f);
        Assert.True(transform.Position.X > 1.0f);
    }

    [Fact]
    public void SlopeConstraintBlocksMovementAndReportsStuckUnit()
    {
        TerrainWorld terrain = CreateRampTerrain(
            chunkSize: 10.0f,
            positiveXHeight: 20.0f);
        var simulation = new SimulationCoordinator(ticksPerSecond: 20);
        var movementSystem = new GroundMovementSystem(
            terrain,
            options: new GroundMovementSystemOptions
            {
                StuckTickThreshold = 3,
                ProgressEpsilonMeters = 0.01f
            });

        GroundMovement movement = CreateMovement(
            maximumSpeed: 8.0f,
            acceleration: 100.0f,
            deceleration: 100.0f,
            turnRateRadiansPerSecond: MathF.Tau,
            maximumSlopeDegrees: 10.0f);

        Vector3 start = new(1.0f, 2.5f, 5.0f);
        EntityId entity = AddGroundUnit(
            simulation,
            start,
            movement);

        AddOrder(
            simulation,
            entity,
            new Vector3(9.0f, 0.0f, 5.0f),
            acceptedAtTick: new SimulationTick(1));

        simulation.RegisterSystem(movementSystem);

        simulation.RunTicks(3);

        WorldTransform transform =
            simulation.Entities.GetComponent<WorldTransform>(entity);
        GroundMovementState state =
            simulation.Entities.GetComponent<GroundMovementState>(entity);

        Assert.InRange(
            Vector3.Distance(transform.Position, start),
            0.0f,
            0.0001f);
        Assert.Equal(GroundMovementStatus.Stuck, state.Status);
        Assert.Equal(1, movementSystem.LastDiagnostics.StuckUnitCount);
        Assert.Equal(
            1,
            movementSystem.LastDiagnostics.TerrainBlockedUnitCount);
    }

    [Fact]
    public void NewMovementOrderResetsStuckProgressTracking()
    {
        TerrainWorld terrain = CreateRampTerrain(
            chunkSize: 10.0f,
            positiveXHeight: 20.0f);
        var simulation = new SimulationCoordinator(ticksPerSecond: 20);
        var movementSystem = new GroundMovementSystem(
            terrain,
            options: new GroundMovementSystemOptions
            {
                StuckTickThreshold = 3,
                ProgressEpsilonMeters = 0.01f
            });

        EntityId entity = AddGroundUnit(
            simulation,
            new Vector3(1.0f, 2.5f, 5.0f),
            CreateMovement(
                maximumSpeed: 8.0f,
                acceleration: 100.0f,
                deceleration: 100.0f,
                turnRateRadiansPerSecond: MathF.Tau,
                maximumSlopeDegrees: 10.0f));

        AddOrder(
            simulation,
            entity,
            new Vector3(9.0f, 0.0f, 5.0f),
            acceptedAtTick: new SimulationTick(1));
        simulation.RegisterSystem(movementSystem);
        simulation.RunTicks(3);

        Assert.Equal(
            GroundMovementStatus.Stuck,
            simulation.Entities
                .GetComponent<GroundMovementState>(entity)
                .Status);

        AddOrder(
            simulation,
            entity,
            new Vector3(8.0f, 0.0f, 5.0f),
            acceptedAtTick: new SimulationTick(4));
        simulation.AdvanceOneTick();

        GroundMovementState resetState =
            simulation.Entities.GetComponent<GroundMovementState>(entity);

        Assert.Equal(GroundMovementStatus.Moving, resetState.Status);
        Assert.Equal(1, resetState.StalledTicks);
    }

    [Fact]
    public void SpatialOccupancyTracksMovementAcrossChunkBoundary()
    {
        TerrainWorld terrain = CreateFlatTerrain(
            chunkSize: 10.0f,
            minimumChunkX: 0,
            maximumChunkX: 1);
        SpatialGridSettings settings = CreateSpatialSettings(
            terrain.Settings,
            cellSizeMeters: 5.0f);
        var spatialIndex = new SpatialGridIndex(settings);
        var synchronizer = new SpatialIndexSynchronizer(spatialIndex);
        var simulation = new SimulationCoordinator(ticksPerSecond: 20);
        var movementSystem = new GroundMovementSystem(
            terrain,
            spatialIndex);

        EntityId entity = AddGroundUnit(
            simulation,
            new Vector3(9.0f, 0.5f, 5.0f),
            CreateMovement(
                maximumSpeed: 12.0f,
                acceleration: 100.0f,
                deceleration: 100.0f,
                turnRateRadiansPerSecond: MathF.Tau));

        simulation.RegisterSystem(movementSystem);
        simulation.RegisterSystem(new SpatialIndexSystem(synchronizer));
        simulation.RegisterSystem(new SpatialIndexCleanupSystem(synchronizer));

        simulation.AdvanceOneTick();

        AddOrder(
            simulation,
            entity,
            new Vector3(16.0f, 0.0f, 5.0f),
            acceptedAtTick: new SimulationTick(2));

        for (int tick = 0; tick < 20; tick++)
        {
            simulation.AdvanceOneTick();

            WorldTransform transform =
                simulation.Entities.GetComponent<WorldTransform>(entity);
            if (transform.Position.X > 10.25f)
            {
                break;
            }
        }

        Assert.True(
            spatialIndex.TryGetEntry(
                entity,
                out SpatialEntry entry));

        ChunkLocation location = WorldCoordinateConverter.WorldToChunk(
            entry.Position.X,
            entry.Position.Z,
            terrain.Settings);

        Assert.Equal(new ChunkCoordinate(1, 0), location.Chunk);
    }

    [Fact]
    public void LocalSeparationPreventsUnitsFromSharingSameGroundSpace()
    {
        TerrainWorld terrain = CreateFlatTerrain(64.0f);
        SpatialGridSettings settings = CreateSpatialSettings(
            terrain.Settings,
            cellSizeMeters: 4.0f);
        var spatialIndex = new SpatialGridIndex(settings);
        var synchronizer = new SpatialIndexSynchronizer(spatialIndex);
        var simulation = new SimulationCoordinator(ticksPerSecond: 20);
        var movementSystem = new GroundMovementSystem(
            terrain,
            spatialIndex);

        GroundMovement movement = CreateMovement(
            maximumSpeed: 8.0f,
            acceleration: 20.0f,
            deceleration: 20.0f,
            turnRateRadiansPerSecond: MathF.Tau,
            radius: 1.0f,
            stopRadius: 0.25f,
            separationRadius: 5.0f);

        EntityId left = AddGroundUnit(
            simulation,
            new Vector3(28.0f, 0.5f, 12.0f),
            movement);
        EntityId right = AddGroundUnit(
            simulation,
            new Vector3(36.0f, 0.5f, 12.0f),
            movement);

        simulation.RegisterSystem(movementSystem);
        simulation.RegisterSystem(new SpatialIndexSystem(synchronizer));
        simulation.RegisterSystem(new SpatialIndexCleanupSystem(synchronizer));
        simulation.AdvanceOneTick();

        Vector3 sharedTarget = new(32.0f, 0.0f, 48.0f);
        AddOrder(
            simulation,
            left,
            sharedTarget,
            acceptedAtTick: new SimulationTick(2));
        AddOrder(
            simulation,
            right,
            sharedTarget,
            acceptedAtTick: new SimulationTick(2));

        bool sawNeighborAdjustment = false;

        for (int tick = 0; tick < 100; tick++)
        {
            simulation.AdvanceOneTick();
            sawNeighborAdjustment |=
                movementSystem.LastDiagnostics.NeighborAdjustmentCount > 0;
        }

        Vector3 leftPosition =
            simulation.Entities.GetComponent<WorldTransform>(left).Position;
        Vector3 rightPosition =
            simulation.Entities.GetComponent<WorldTransform>(right).Position;

        float separation =
            Horizontal(leftPosition - rightPosition).Length();

        Assert.True(
            separation >= movement.Radius * 2.0f - 0.05f,
            $"Expected at least {movement.Radius * 2.0f:F2} m separation, got {separation:F3} m.");
        Assert.True(sawNeighborAdjustment);
    }

    [Fact]
    public void StaticObstacleSteeringProducesShortRangeDetour()
    {
        TerrainWorld terrain = CreateFlatTerrain(64.0f);
        SpatialGridSettings settings = CreateSpatialSettings(
            terrain.Settings,
            cellSizeMeters: 4.0f);
        var spatialIndex = new SpatialGridIndex(settings);
        var synchronizer = new SpatialIndexSynchronizer(spatialIndex);
        var simulation = new SimulationCoordinator(ticksPerSecond: 20);
        var movementSystem = new GroundMovementSystem(
            terrain,
            spatialIndex);

        GroundMovement movement = CreateMovement(
            maximumSpeed: 9.0f,
            acceleration: 20.0f,
            deceleration: 20.0f,
            turnRateRadiansPerSecond: MathF.PI,
            radius: 0.75f,
            stopRadius: 0.3f,
            separationRadius: 3.0f,
            obstacleLookAhead: 7.0f);

        EntityId unit = AddGroundUnit(
            simulation,
            new Vector3(8.0f, 0.5f, 32.0f),
            movement);
        _ = AddStaticObstacle(
            simulation,
            new Vector3(32.0f, 1.0f, 32.0f),
            new Vector3(2.5f, 1.0f, 2.5f));

        simulation.RegisterSystem(movementSystem);
        simulation.RegisterSystem(new SpatialIndexSystem(synchronizer));
        simulation.RegisterSystem(new SpatialIndexCleanupSystem(synchronizer));
        simulation.AdvanceOneTick();

        AddOrder(
            simulation,
            unit,
            new Vector3(56.0f, 0.0f, 32.0f),
            acceptedAtTick: new SimulationTick(2));

        float maximumLateralDeviation = 0.0f;
        bool sawObstacleAdjustment = false;

        for (int tick = 0; tick < 160; tick++)
        {
            simulation.AdvanceOneTick();
            Vector3 position =
                simulation.Entities.GetComponent<WorldTransform>(unit).Position;
            maximumLateralDeviation = MathF.Max(
                maximumLateralDeviation,
                MathF.Abs(position.Z - 32.0f));
            sawObstacleAdjustment |=
                movementSystem.LastDiagnostics.ObstacleAdjustmentCount > 0;

            if (!simulation.Entities.HasComponent<MovementOrder>(unit))
            {
                break;
            }
        }

        WorldTransform finalTransform =
            simulation.Entities.GetComponent<WorldTransform>(unit);

        Assert.True(maximumLateralDeviation > 1.0f);
        Assert.True(finalTransform.Position.X > 34.0f);
        Assert.True(sawObstacleAdjustment);
    }

    [Fact]
    public void OneThousandGroundUnitsRemainFiniteUnderFixedTickLoad()
    {
        TerrainWorld terrain = CreateFlatTerrain(128.0f);
        SpatialGridSettings settings = CreateSpatialSettings(
            terrain.Settings,
            cellSizeMeters: 4.0f);
        var spatialIndex = new SpatialGridIndex(settings);
        var synchronizer = new SpatialIndexSynchronizer(spatialIndex);
        var simulation = new SimulationCoordinator(
            ticksPerSecond: 20,
            initialEntityCapacity: 1_024);
        var movementSystem = new GroundMovementSystem(
            terrain,
            spatialIndex);

        GroundMovement movement = CreateMovement(
            maximumSpeed: 8.0f,
            acceleration: 20.0f,
            deceleration: 20.0f,
            turnRateRadiansPerSecond: MathF.PI,
            radius: 0.5f,
            stopRadius: 0.2f,
            separationRadius: 2.0f,
            obstacleLookAhead: 0.0f);

        var entities = new EntityId[1_000];

        for (int index = 0; index < entities.Length; index++)
        {
            int xIndex = index % 32;
            int zIndex = index / 32;
            Vector3 position = new(
                4.0f + xIndex * 3.0f,
                0.5f,
                4.0f + zIndex * 3.0f);

            entities[index] = AddGroundUnit(
                simulation,
                position,
                movement);
        }

        simulation.RegisterSystem(movementSystem);
        simulation.RegisterSystem(new SpatialIndexSystem(synchronizer));
        simulation.RegisterSystem(new SpatialIndexCleanupSystem(synchronizer));
        simulation.AdvanceOneTick();

        for (int index = 0; index < entities.Length; index++)
        {
            WorldTransform transform =
                simulation.Entities.GetComponent<WorldTransform>(
                    entities[index]);
            AddOrder(
                simulation,
                entities[index],
                transform.Position + new Vector3(12.0f, 0.0f, 0.0f),
                acceptedAtTick: new SimulationTick(2));
        }

        simulation.RunTicks(10);

        Assert.Equal(
            1_000,
            movementSystem.LastDiagnostics.GroundUnitCount);
        Assert.Equal(
            1_000,
            movementSystem.LastDiagnostics.OrderedUnitCount);
        Assert.Equal(
            0,
            movementSystem.LastDiagnostics.StuckUnitCount);

        for (int index = 0; index < entities.Length; index++)
        {
            WorldTransform transform =
                simulation.Entities.GetComponent<WorldTransform>(
                    entities[index]);

            Assert.True(float.IsFinite(transform.Position.X));
            Assert.True(float.IsFinite(transform.Position.Y));
            Assert.True(float.IsFinite(transform.Position.Z));
        }
    }

    private static float RunConstantVelocityForOneSecond(
        int ticksPerSecond)
    {
        var simulation = new SimulationCoordinator(ticksPerSecond);
        var movementSystem = new GroundMovementSystem();

        GroundMovement movement = CreateMovement(
            maximumSpeed: 10.0f,
            acceleration: 100.0f,
            deceleration: 100.0f,
            turnRateRadiansPerSecond: MathF.Tau,
            obstacleLookAhead: 0.0f);

        EntityId entity = AddGroundUnit(
            simulation,
            Vector3.Zero,
            movement,
            new GroundMovementState(
                new Vector3(0.0f, 0.0f, 10.0f),
                0.0f,
                GroundMovementStatus.Moving,
                SimulationTick.Zero,
                float.PositiveInfinity,
                0),
            addSpatialPresence: false);

        AddOrder(
            simulation,
            entity,
            new Vector3(0.0f, 0.0f, 1_000.0f),
            acceptedAtTick: new SimulationTick(1));

        simulation.RegisterSystem(movementSystem);
        simulation.RunTicks((ulong)ticksPerSecond);

        return simulation.Entities
            .GetComponent<WorldTransform>(entity)
            .Position
            .Z;
    }

    private static EntityId AddGroundUnit(
        SimulationCoordinator simulation,
        Vector3 position,
        in GroundMovement movement,
        GroundMovementState? state = null,
        bool addSpatialPresence = true)
    {
        EntityId entity = simulation.Entities.CreateEntity();

        simulation.Entities.AddComponent(
            entity,
            new WorldTransform(
                position,
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(entity, movement);
        simulation.Entities.AddComponent(
            entity,
            state ?? GroundMovementState.Stationary());

        if (addSpatialPresence)
        {
            simulation.Entities.AddComponent(
                entity,
                new SpatialPresence(
                    new Vector3(
                        movement.Radius,
                        0.5f,
                        movement.Radius),
                    new SpatialEntryMetadata(
                        LocalPlayer.Value,
                        1,
                        SpatialMobility.Mobile)));
        }

        return entity;
    }

    private static EntityId AddStaticObstacle(
        SimulationCoordinator simulation,
        Vector3 position,
        Vector3 halfExtents)
    {
        EntityId entity = simulation.Entities.CreateEntity();

        simulation.Entities.AddComponent(
            entity,
            new WorldTransform(
                position,
                Quaternion.Identity,
                halfExtents * 2.0f));
        simulation.Entities.AddComponent(
            entity,
            new SpatialPresence(
                halfExtents,
                new SpatialEntryMetadata(
                    0,
                    1UL << 63,
                    SpatialMobility.Static)));

        return entity;
    }

    private static void AddOrder(
        SimulationCoordinator simulation,
        EntityId entity,
        Vector3 target,
        SimulationTick acceptedAtTick)
    {
        var order = new MovementOrder(
            LocalPlayer,
            target,
            simulation.CurrentTick,
            acceptedAtTick);

        if (simulation.Entities.HasComponent<MovementOrder>(entity))
        {
            simulation.Entities.SetComponent(entity, order);
        }
        else
        {
            simulation.Entities.AddComponent(entity, order);
        }
    }

    private static GroundMovement CreateMovement(
        float maximumSpeed = 10.0f,
        float acceleration = 20.0f,
        float deceleration = 20.0f,
        float turnRateRadiansPerSecond = MathF.PI,
        float radius = 0.5f,
        float stopRadius = 0.2f,
        float separationRadius = 2.0f,
        float obstacleLookAhead = 4.0f,
        float maximumSlopeDegrees = 45.0f,
        float heightOffset = 0.5f)
    {
        return new GroundMovement(
            maximumSpeed,
            acceleration,
            deceleration,
            turnRateRadiansPerSecond,
            radius,
            stopRadius,
            separationRadius,
            obstacleLookAhead,
            maximumSlopeDegrees,
            heightOffset);
    }

    private static TerrainWorld CreateFlatTerrain(
        float chunkSize,
        int minimumChunkX = 0,
        int maximumChunkX = 0)
    {
        var settings = new WorldGridSettings
        {
            ChunkSizeMeters = chunkSize,
            RegionSizeInChunks = 4,
            HeightSamplesPerSide = 3
        };

        var chunks = new List<TerrainChunk>();

        for (int chunkX = minimumChunkX;
             chunkX <= maximumChunkX;
             chunkX++)
        {
            chunks.Add(
                new TerrainChunk(
                    new ChunkCoordinate(chunkX, 0),
                    new TerrainHeightfield(
                        samplesPerSide: 3,
                        chunkSizeMeters: chunkSize,
                        new float[9])));
        }

        return new TerrainWorld(settings, chunks);
    }

    private static TerrainWorld CreateRampTerrain(
        float chunkSize,
        float positiveXHeight)
    {
        var settings = new WorldGridSettings
        {
            ChunkSizeMeters = chunkSize,
            RegionSizeInChunks = 4,
            HeightSamplesPerSide = 2
        };

        float[] heights =
        [
            0.0f,
            positiveXHeight,
            0.0f,
            positiveXHeight
        ];

        return new TerrainWorld(
            settings,
            [
                new TerrainChunk(
                    new ChunkCoordinate(0, 0),
                    new TerrainHeightfield(
                        samplesPerSide: 2,
                        chunkSizeMeters: chunkSize,
                        heights))
            ]);
    }

    private static SpatialGridSettings CreateSpatialSettings(
        WorldGridSettings world,
        float cellSizeMeters)
    {
        return new SpatialGridSettings
        {
            World = world,
            CellSizeMeters = cellSizeMeters
        };
    }

    private static Vector3 Horizontal(Vector3 value)
    {
        return new Vector3(value.X, 0.0f, value.Z);
    }
}
