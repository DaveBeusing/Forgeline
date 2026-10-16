using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class GroundMovementContactTests
{
    [Theory]
    [InlineData(8.2f, false)]
    [InlineData(8.4f, true)]
    public void DiagonalCornerUsesCircularClearanceNotExpandedSquare(float extent, bool blocked)
    {
        var simulation = new SimulationCoordinator(ticksPerSecond: 20);
        var spatial = new SpatialGridIndex(new SpatialGridSettings
        {
            World = new WorldGridSettings { ChunkSizeMeters = 64, HeightSamplesPerSide = 3 },
            CellSizeMeters = 4
        });
        var synchronizer = new SpatialIndexSynchronizer(spatial);
        simulation.RegisterSystem(new GroundMovementSystem(spatialIndex: spatial,
            options: new GroundMovementSystemOptions
            {
                SeparationWeight = 0,
                ObstacleSteeringWeight = 0,
                ObstacleClearanceMeters = 0
            }));
        simulation.RegisterSystem(new SpatialIndexSystem(synchronizer));
        simulation.RegisterSystem(new SpatialIndexCleanupSystem(synchronizer));

        EntityId wall = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(wall,
            new WorldTransform(new Vector3(9, 0.5f, 9), Quaternion.Identity, new Vector3(2)));
        simulation.Entities.AddComponent(wall, new SpatialPresence(Vector3.One,
            new SpatialEntryMetadata(0, 1UL << 63, SpatialMobility.Static)));

        EntityId unit = simulation.Entities.CreateEntity();
        Vector3 start = new(7, 0.5f, extent);
        Vector3 target = new(extent, 0.5f, 7);
        simulation.Entities.AddComponent(unit, new WorldTransform(start, Quaternion.Identity, Vector3.One));
        simulation.Entities.AddComponent(unit, new GroundMovement(100, 100, 1, MathF.Tau,
            0.5f, 0.05f, 2, 0, 45, 0.5f));
        simulation.Entities.AddComponent(unit, GroundMovementState.Stationary());
        simulation.Entities.AddComponent(unit, new SpatialPresence(new Vector3(0.5f),
            new SpatialEntryMetadata(1, 1, SpatialMobility.Mobile)));
        simulation.AdvanceOneTick();

        simulation.Entities.SetComponent(unit, GroundMovementState.Stationary(MathF.PI * 0.75f) with
        {
            Velocity = Vector3.Normalize(target - start) * 40
        });
        simulation.Entities.AddComponent(unit, new MovementOrder(new PlayerId(1), target,
            simulation.CurrentTick, new SimulationTick(2)));
        simulation.AdvanceOneTick();

        Vector3 position = simulation.Entities.GetComponent<WorldTransform>(unit).Position;
        Assert.Equal(blocked, simulation.Entities.HasComponent<MovementOrder>(unit));
        if (blocked)
        {
            Assert.True(Vector2.Distance(new Vector2(position.X, position.Z), new Vector2(8, 8)) >= 0.4999f);
            Assert.True(Vector3.Distance(position, target) > 0.06f);
        }
        else
        {
            Assert.InRange(Vector3.Distance(position, target), 0, 0.06f);
        }
    }
}
