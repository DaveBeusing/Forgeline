using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class SpatialIndexIntegrationTests
{
    [Fact]
    public void MovementUpdatesSpatialOccupancyBeforeSensorPhase()
    {
        SpatialGridSettings settings = CreateSettings();
        var index = new SpatialGridIndex(settings);
        var synchronizer = new SpatialIndexSynchronizer(index);
        var simulation = new SimulationCoordinator(ticksPerSecond: 20);
        var entity = simulation.Entities.CreateEntity();

        simulation.Entities.AddComponent(
            entity,
            new WorldTransform(
                new Vector3(15.0f, 0.0f, 8.0f),
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            entity,
            new LinearVelocity(new Vector3(40.0f, 0.0f, 0.0f)));
        simulation.Entities.AddComponent(
            entity,
            new SpatialPresence(
                new Vector3(0.25f),
                new SpatialEntryMetadata(1, 1)));

        simulation.RegisterSystem(new LinearMotionSystem());
        simulation.RegisterSystem(new SpatialIndexSystem(synchronizer));
        simulation.RegisterSystem(new SpatialIndexCleanupSystem(synchronizer));

        simulation.AdvanceOneTick();

        var buffer = new SpatialQueryBuffer();

        Assert.Equal(
            0,
            index.QueryCell(
                SpatialAddressing.WorldToCell(15.0f, 8.0f, settings),
                buffer));
        Assert.Equal(
            1,
            index.QueryCell(
                SpatialAddressing.WorldToCell(17.0f, 8.0f, settings),
                buffer));
        Assert.Equal(entity, buffer.Results[0]);
    }

    [Fact]
    public void LifecycleCleanupRemovesEntitiesDestroyedLaterInTheTick()
    {
        SpatialGridSettings settings = CreateSettings();
        var index = new SpatialGridIndex(settings);
        var synchronizer = new SpatialIndexSynchronizer(index);
        var simulation = new SimulationCoordinator();
        var entity = simulation.Entities.CreateEntity();

        simulation.Entities.AddComponent(entity, WorldTransform.Identity);
        simulation.Entities.AddComponent(
            entity,
            new SpatialPresence(
                new Vector3(0.5f),
                new SpatialEntryMetadata(
                    1,
                    1,
                    SpatialMobility.Static)));

        simulation.RegisterSystem(new SpatialIndexSystem(synchronizer));
        simulation.RegisterSystem(new DestroyEntitySystem(entity));
        simulation.RegisterSystem(new SpatialIndexCleanupSystem(synchronizer));

        simulation.AdvanceOneTick();

        Assert.False(simulation.Entities.IsAlive(entity));
        Assert.False(index.Contains(entity));
        Assert.Equal(0, index.Count);
        Assert.Equal(0, synchronizer.TrackedEntityCount);
    }

    private static SpatialGridSettings CreateSettings()
    {
        return new SpatialGridSettings
        {
            World = new WorldGridSettings
            {
                ChunkSizeMeters = 256.0f,
                RegionSizeInChunks = 8,
                HeightSamplesPerSide = 65
            },
            CellSizeMeters = 16.0f
        };
    }

    private sealed class DestroyEntitySystem : ISimulationSystem
    {
        private readonly EntityId _entity;

        public DestroyEntitySystem(EntityId entity)
        {
            _entity = entity;
        }

        public SimulationPhase Phase => SimulationPhase.EntityLifecycle;

        public void Execute(SimulationContext context)
        {
            context.Entities.DestroyEntity(_entity);
        }
    }
}
