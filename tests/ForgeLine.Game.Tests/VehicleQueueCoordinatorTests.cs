using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class VehicleQueueCoordinatorTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(20)]
    public void EqualTickArrivalsUseEntityOrderRegardlessOfRegistrationCallOrder(int count)
    {
        var entities = new EntityRegistry();
        var queue = new VehicleQueueCoordinator();
        EntityId building = Building(entities);
        EntityId[] trucks = Enumerable.Range(0, count).Select(_ => Truck(entities)).ToArray();
        foreach (EntityId truck in trucks.Reverse())
            Assert.True(queue.TryRegister(entities, truck, building, new(1), new(10), VehicleQueueService.CargoPickup));
        Assert.Equal(trucks, queue.Collect(entities, building));
        Assert.False(queue.IsHead(entities, trucks[0], building, new(10)));
        Assert.True(queue.IsHead(entities, trucks[0], building, new(11)));
        foreach (EntityId truck in trucks.Skip(1)) Assert.False(queue.IsHead(entities, truck, building, new(11)));
        Assert.False(entities.HasComponent<VehicleDockSlot>(building));
    }

    [Fact]
    public void EarlierConfirmedArrivalPrecedesLowerIdentityAndOtherServiceTypes()
    {
        var entities = new EntityRegistry();
        var queue = new VehicleQueueCoordinator();
        EntityId building = Building(entities), first = Truck(entities), second = Truck(entities);
        Assert.True(queue.TryRegister(entities, second, building, new(1), new(9), VehicleQueueService.SupplyLoading));
        Assert.True(queue.TryRegister(entities, first, building, new(1), new(10), VehicleQueueService.CargoDelivery));
        Assert.Equal(new[] { second, first }, queue.Collect(entities, building));
        Assert.True(queue.IsHead(entities, second, building, new(11)));
    }

    [Fact]
    public void RepeatedObservationPreservesGenerationAndArrivalAndCannotClaimAnotherBuilding()
    {
        var entities = new EntityRegistry();
        var queue = new VehicleQueueCoordinator();
        EntityId building = Building(entities), other = Building(entities), truck = Truck(entities);
        Assert.True(queue.TryRegister(entities, truck, building, new(1), new(9), VehicleQueueService.CargoPickup));
        var original = entities.GetComponent<VehicleQueueEntry>(truck);
        Assert.True(queue.TryRegister(entities, truck, building, new(1), new(90), VehicleQueueService.CargoPickup));
        Assert.Equal(original, entities.GetComponent<VehicleQueueEntry>(truck));
        Assert.False(queue.TryRegister(entities, truck, other, new(1), new(90), VehicleQueueService.CargoDelivery));
        Assert.False(VehicleQueueCoordinator.Remove(entities, truck, building, original.Generation + 1));
        Assert.True(VehicleQueueCoordinator.Remove(entities, truck, building, original.Generation));
        Assert.True(queue.TryRegister(entities, truck, other, new(1), new(91), VehicleQueueService.CargoDelivery));
        Assert.True(entities.GetComponent<VehicleQueueEntry>(truck).Generation > original.Generation);
        Assert.False(VehicleQueueCoordinator.Remove(entities, truck, building, original.Generation));
    }

    [Fact]
    public void FullRosterDoesNotInventAnExtraPlaceAndInvalidOwnerCannotJoin()
    {
        var entities = new EntityRegistry();
        var queue = new VehicleQueueCoordinator();
        EntityId building = Building(entities), first = Truck(entities), second = Truck(entities);
        Assert.False(queue.TryRegister(entities, first, building, new(2), new(1), VehicleQueueService.CargoPickup));
        Assert.True(queue.TryRegister(entities, first, building, new(1), new(1), VehicleQueueService.CargoPickup, 1));
        Assert.False(queue.TryRegister(entities, second, building, new(1), new(1), VehicleQueueService.SupplyLoading, 1));
        Assert.False(entities.HasComponent<VehicleQueueEntry>(second));
    }

    [Fact]
    public void DestroyedHeadAndReusedEntityCannotInheritArrivalOrReleaseAnotherEntry()
    {
        var entities = new EntityRegistry();
        var queue = new VehicleQueueCoordinator();
        EntityId building = Building(entities), first = Truck(entities), second = Truck(entities);
        Assert.True(queue.TryRegister(entities, first, building, new(1), new(1), VehicleQueueService.CargoPickup));
        Assert.True(queue.TryRegister(entities, second, building, new(1), new(2), VehicleQueueService.SupplyLoading));
        var previous = entities.GetComponent<VehicleQueueEntry>(first);
        Assert.True(entities.DestroyEntity(first));
        EntityId replacement = Truck(entities);
        Assert.Equal(first.Index, replacement.Index);
        Assert.NotEqual(first.Generation, replacement.Generation);
        Assert.True(queue.IsHead(entities, second, building, new(3)));
        Assert.True(queue.TryRegister(entities, replacement, building, new(1), new(3), VehicleQueueService.CargoPickup));
        Assert.False(VehicleQueueCoordinator.Remove(entities, first, building, previous.Generation));
        Assert.True(queue.IsHead(entities, second, building, new(4)));
    }

    private static EntityId Building(EntityRegistry entities)
    {
        EntityId building = entities.CreateEntity();
        entities.AddComponent(building, new CompletedBuilding(BuildingIds.StorageDepot, new(1), SimulationTick.Zero));
        return building;
    }

    private static EntityId Truck(EntityRegistry entities)
    {
        EntityId truck = entities.CreateEntity();
        entities.AddComponent(truck, new ControllableEntity(new(1), ControllableEntityCategory.Unit));
        return truck;
    }
}
