using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class VehicleQueueApproachGateTests
{
    [Fact]
    public void LocalApproachHandoffRequiresTheCurrentOwnedSlotAndOriginalQueueTarget()
    {
        var entities = new EntityRegistry();
        var building = entities.CreateEntity();
        var truck = entities.CreateEntity();
        Vector3 target = new(2, .5f, 0);
        var root = new MovementOrder(new PlayerId(1), target, new(4), new(5));
        var pose = new VehicleDockPose(target + Vector3.UnitX * 5, target + Vector3.UnitX * 10,
            target + Vector3.UnitX * 15, MathF.PI / 2, .5f, .5f, target);
        entities.AddComponent(truck, new WorldTransform(new(0, .5f, 0), Quaternion.Identity, Vector3.One));
        entities.AddComponent(truck, new GroundMovement(6, 4, 4, 2, .5f, .1f, 1, 4, 45, .5f));
        entities.AddComponent(truck, GroundMovementState.Stationary());
        entities.AddComponent(truck, new ControllableEntity(new(1), ControllableEntityCategory.Logistics));
        entities.AddComponent(truck, root);
        var queue = new VehicleQueueEntry(building, new(1), 9, SimulationTick.Zero, VehicleQueueService.CargoPickup,
            Position: 1, WaitingCount: 1, Target: target, Heading: MathF.PI / 2);
        entities.AddComponent(truck, queue);
        var visit = new VehicleDockVisit(building, 12, VehicleDockStatus.Reserved, VehicleDockBlockReason.None, pose);
        var slot = new VehicleDockSlot(truck, 12, pose, new(1));
        entities.AddComponent(truck, visit);
        entities.AddComponent(building, slot);
        Assert.True(NavigationReadAdapter.TryCaptureVehicle(entities, truck, .25f, out var vehicle));
        Assert.True(NavigationPrecisionOwnership.TryBeginOwnedQueueApproach(vehicle, out var local));
        Assert.Equal(root.WorldTarget, local.WorldTarget);
        Assert.Equal(root.Issuer, local.Issuer);
        Assert.Equal(root.SubmittedAtTick, local.SubmittedAtTick);
        Assert.Equal(MovementOrderKind.LocalApproach, local.Kind);
        Assert.Equal(queue.Heading, local.TargetHeadingRadians);
        Assert.Equal(root, entities.GetComponent<MovementOrder>(truck));
        Assert.False(NavigationPrecisionOwnership.TryBeginOwnedQueueApproach(vehicle with { BuildingSlot = slot with { Generation = 11 } }, out _));
        Assert.False(NavigationPrecisionOwnership.TryBeginOwnedQueueApproach(vehicle with { BuildingSlot = slot with { Owner = new(2) } }, out _));
        Assert.False(NavigationPrecisionOwnership.TryBeginOwnedQueueApproach(vehicle with { QueueEntry = queue with { Owner = new(2) } }, out _));
        Assert.False(NavigationPrecisionOwnership.TryBeginOwnedQueueApproach(vehicle with { QueueEntry = queue with { Generation = 0 } }, out _));
        Assert.False(NavigationPrecisionOwnership.TryBeginOwnedQueueApproach(vehicle with { DirectOrder = root with { WorldTarget = new(20, .5f, 0) } }, out _));
        Assert.False(NavigationPrecisionOwnership.TryBeginOwnedQueueApproach(vehicle with { DockVisit = visit with { LaneEntered = true } }, out _));
    }

    [Fact]
    public void ArrivalAlreadyAheadMayAdvanceToItsOwnTailPoseWithoutTakingDockAdmission()
    {
        var entities = new EntityRegistry();
        var building = entities.CreateEntity();
        var predecessor = entities.CreateEntity();
        entities.AddComponent(predecessor, new WorldTransform(new(-10, 0, 0),
            Quaternion.Identity, new(3.2f, 2.2f, 6.4f)));
        var truck = entities.CreateEntity();
        var current = new WorldTransform(Vector3.Zero, Quaternion.Identity, new(3.2f, 2.2f, 6.4f));
        entities.AddComponent(truck, new VehicleQueueEntry(building, new PlayerId(1), 1,
            SimulationTick.Zero, VehicleQueueService.CargoDelivery, Position: 2, WaitingCount: 2,
            Target: new(20, 0, 0), Heading: MathF.PI / 2, Predecessor: predecessor));
        entities.AddComponent(truck, new VehicleQueueMotion(new(32, 0, 0), new(20, 0, 0),
            MathF.PI / 2, predecessor, 2, 1));
        Assert.False(VehicleDockingService.BlocksQueueStep(entities, truck, current,
            current with { Position = new(.1f, 0, 0) }));
        Assert.True(VehicleDockingService.BlocksQueueStep(entities, truck,
            current with { Position = new(20, 0, 0) }, current with { Position = new(20.2f, 0, 0) }));
        Assert.Equal(2, entities.GetComponent<VehicleQueueEntry>(truck).Position);
        Assert.False(entities.HasComponent<VehicleDockSlot>(building));
    }

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
