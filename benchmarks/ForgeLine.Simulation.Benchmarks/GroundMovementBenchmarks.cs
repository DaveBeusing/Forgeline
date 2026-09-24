using System.Numerics;
using BenchmarkDotNet.Attributes;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Simulation.Benchmarks;

[MemoryDiagnoser]
public class GroundMovementBenchmarks
{
    private const int EntityCount = 1_000;

    private readonly EntityId[] _entities = new EntityId[EntityCount];
    private readonly Vector3[] _initialPositions = new Vector3[EntityCount];

    private SimulationCoordinator _simulation = null!;
    private SpatialIndexSynchronizer _synchronizer = null!;
    private GroundMovement _movement;

    [GlobalSetup]
    public void Setup()
    {
        var worldSettings = new WorldGridSettings
        {
            ChunkSizeMeters = 128.0f,
            RegionSizeInChunks = 4,
            HeightSamplesPerSide = 3
        };

        var terrain = new TerrainWorld(
            worldSettings,
            [
                new TerrainChunk(
                    new ChunkCoordinate(0, 0),
                    new TerrainHeightfield(
                        samplesPerSide: 3,
                        chunkSizeMeters: 128.0f,
                        new float[9]))
            ]);

        var spatialIndex = new SpatialGridIndex(
            new SpatialGridSettings
            {
                World = worldSettings,
                CellSizeMeters = 4.0f
            });

        _synchronizer = new SpatialIndexSynchronizer(spatialIndex);
        _simulation = new SimulationCoordinator(
            ticksPerSecond: 20,
            initialEntityCapacity: 1_024);
        _movement = new GroundMovement(
            maximumSpeed: 8.0f,
            acceleration: 20.0f,
            deceleration: 20.0f,
            turnRateRadiansPerSecond: MathF.PI,
            radius: 0.5f,
            stopRadius: 0.2f,
            separationRadius: 2.0f,
            obstacleLookAhead: 0.0f,
            maximumSlopeDegrees: 45.0f,
            heightOffset: 0.5f);

        var movementSystem = new GroundMovementSystem(
            terrain,
            spatialIndex);

        for (int index = 0; index < EntityCount; index++)
        {
            int xIndex = index % 32;
            int zIndex = index / 32;
            Vector3 position = new(
                4.0f + xIndex * 3.0f,
                0.5f,
                4.0f + zIndex * 3.0f);

            EntityId entity = _simulation.Entities.CreateEntity();
            _entities[index] = entity;
            _initialPositions[index] = position;

            _simulation.Entities.AddComponent(
                entity,
                new WorldTransform(
                    position,
                    Quaternion.Identity,
                    Vector3.One));
            _simulation.Entities.AddComponent(entity, _movement);
            _simulation.Entities.AddComponent(
                entity,
                GroundMovementState.Stationary());
            _simulation.Entities.AddComponent(
                entity,
                new SpatialPresence(
                    new Vector3(0.5f),
                    new SpatialEntryMetadata(
                        Faction: 1,
                        CategoryMask: 1,
                        SpatialMobility.Mobile)));
        }

        _simulation.RegisterSystem(movementSystem);
        _simulation.RegisterSystem(new SpatialIndexSystem(_synchronizer));
        _simulation.RegisterSystem(new SpatialIndexCleanupSystem(_synchronizer));
        _simulation.AdvanceOneTick();
    }

    [IterationSetup]
    public void ResetScenario()
    {
        var acceptedAtTick = new SimulationTick(
            checked(_simulation.CurrentTick.Value + 1));

        for (int index = 0; index < _entities.Length; index++)
        {
            EntityId entity = _entities[index];
            Vector3 position = _initialPositions[index];

            _simulation.Entities.SetComponent(
                entity,
                new WorldTransform(
                    position,
                    Quaternion.Identity,
                    Vector3.One));
            _simulation.Entities.SetComponent(
                entity,
                GroundMovementState.Stationary());

            var order = new MovementOrder(
                new PlayerId(1),
                position + new Vector3(20.0f, 0.0f, 0.0f),
                _simulation.CurrentTick,
                acceptedAtTick);

            if (_simulation.Entities.HasComponent<MovementOrder>(entity))
            {
                _simulation.Entities.SetComponent(entity, order);
            }
            else
            {
                _simulation.Entities.AddComponent(entity, order);
            }
        }

        _synchronizer.SynchronizeMovement(_simulation.Entities);
    }

    [Benchmark]
    public ulong MoveOneThousandEntitiesForTenTicks()
    {
        return _simulation.RunTicks(10);
    }
}
