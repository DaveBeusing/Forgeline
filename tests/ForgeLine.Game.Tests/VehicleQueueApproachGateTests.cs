using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class VehicleQueueApproachGateTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(12, false)]
    public void OnlyAPredecessorInsideTheLaneOwnsItsNoPassingPlane(float predecessorLateral, bool blocked)
    {
        var entities = new EntityRegistry();
        var building = entities.CreateEntity();
        var predecessor = entities.CreateEntity();
        entities.AddComponent(predecessor, new WorldTransform(new(2, 0, predecessorLateral),
            Quaternion.Identity, new(3.2f, 2.2f, 6.4f)));
        var truck = entities.CreateEntity();
        var current = new WorldTransform(new(0, 0, 0), Quaternion.Identity, new(3.2f, 2.2f, 6.4f));
        entities.AddComponent(truck, new VehicleQueueEntry(building, new PlayerId(1), 1,
            SimulationTick.Zero, VehicleQueueService.CargoPickup, Position: 2, WaitingCount: 2,
            Target: new(1, 0, 0), Heading: MathF.PI / 2, Predecessor: predecessor));
        entities.AddComponent(truck, new VehicleQueueMotion(Vector3.Zero, new(1, 0, 0), MathF.PI / 2,
            predecessor, 2, 1));
        Assert.Equal(blocked, VehicleDockingService.BlocksQueueStep(entities, truck, current,
            current with { Position = new(.1f, 0, 0) }));
    }
}
