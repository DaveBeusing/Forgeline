using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Logistics;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class CargoDockFailureRecoveryTests
{
    [Fact]
    public void FailedLoadingVisitPhysicallyClearsTheBayForItsQueuedSuccessor()
    {
        var simulation = new SimulationCoordinator(ticksPerSecond: 20);
        var inventories = new InventoryStore();
        var network = new LogisticsNetwork();
        var cargo = new CargoTransportSystem(network, inventories);
        simulation.RegisterSystem(new GroundMovementSystem());
        simulation.RegisterSystem(cargo);

        var source = CreateBuilding(simulation, inventories, Vector3.Zero);
        var destination = CreateBuilding(simulation, inventories, new Vector3(120, 0, 0));
        Assert.True(inventories.Add(source.Inventory, ResourceIds.Steel, 80).Succeeded);
        var origin = network.AddNode(source.Entity, Vector3.Zero, LogisticsNodeKind.StorageDepot,
            LogisticsNodeCapabilities.CargoSource | LogisticsNodeCapabilities.CargoDestination);
        var target = network.AddNode(destination.Entity, new Vector3(120, 0, 0), LogisticsNodeKind.StorageDepot,
            LogisticsNodeCapabilities.CargoDestination);
        network.AddEdge(origin, target, LogisticsTransportMode.GroundRoad, 120, 120, 100);

        EntityId interrupted = CargoTruckFactory.Create(simulation.Entities, inventories, new Vector3(0, 1, 14), new PlayerId(1), cargo);
        var transform = simulation.Entities.GetComponent<WorldTransform>(interrupted);
        var movement = simulation.Entities.GetComponent<GroundMovement>(interrupted);
        simulation.Entities.SetComponent(interrupted, transform with
        {
            Position = new Vector3(0, movement.HeightOffset, 14),
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2)
        });
        simulation.Entities.SetComponent(interrupted, GroundMovementState.Stationary(MathF.PI / 2));

        EntityId successor = CargoTruckFactory.Create(simulation.Entities, inventories, new Vector3(-50, 1, 14), new PlayerId(1), cargo);
        foreach (EntityId truck in new[] { interrupted, successor })
        {
            Assert.True(cargo.TryAssignOrder(simulation.Entities, truck,
                new CargoTransportOrder(origin, target, ResourceIds.Steel, 40, SimulationTick.Zero), SimulationTick.Zero));
        }

        bool enteredLoading = false;
        for (int tick = 0; tick < 600; tick++)
        {
            simulation.AdvanceOneTick();
            if (simulation.Entities.TryGetComponent(interrupted, out CargoTransportRuntimeState state) &&
                state.Lifecycle == CargoTransportLifecycleState.Loading &&
                simulation.Entities.TryGetComponent(interrupted, out VehicleDockVisit visit) &&
                visit.Generation > 0 && visit.Status is VehicleDockStatus.Docked or VehicleDockStatus.Servicing)
            {
                enteredLoading = true;
                break;
            }
        }
        Assert.True(enteredLoading, "The first truck never occupied the loading bay.");
        Assert.True(simulation.Entities.TryGetComponent(source.Entity, out VehicleDockSlot firstSlot));
        Assert.Equal(interrupted, firstSlot.Truck);
        Assert.Equal(0, inventories.GetQuantity(
            simulation.Entities.GetComponent<CargoTransport>(interrupted).CargoInventory, ResourceIds.Steel));

        inventories.DestroyInventory(source.Inventory);
        simulation.AdvanceOneTick();
        Assert.Equal(CargoTransportLifecycleState.Failed,
            simulation.Entities.GetComponent<CargoTransportRuntimeState>(interrupted).Lifecycle);

        // A failed order must not leave its physically parked vehicle blocking the next load.
        Assert.True(simulation.Entities.TryGetComponent(interrupted, out VehicleDockVisit departure));
        Assert.Equal(VehicleDockStatus.Exiting, departure.Status);

        InventoryId restored = inventories.CreateInventory(new InventorySpecification(1000));
        Assert.True(inventories.Add(restored, ResourceIds.Steel, 80).Succeeded);
        simulation.Entities.SetComponent(source.Entity, new InventoryStorage(restored));

        for (int tick = 0; tick < 5000 && simulation.Entities.HasComponent<CargoTransportOrder>(successor); tick++)
            simulation.AdvanceOneTick();

        Assert.False(simulation.Entities.HasComponent<CargoTransportOrder>(successor),
            simulation.Entities.GetComponent<CargoTransportRuntimeState>(successor).ToString());
        Assert.Equal(40, inventories.GetQuantity(destination.Inventory, ResourceIds.Steel));
        Assert.Equal(40, inventories.GetQuantity(restored, ResourceIds.Steel));
        Assert.False(simulation.Entities.HasComponent<VehicleDockSlot>(source.Entity));
        Assert.False(simulation.Entities.HasComponent<VehicleDockSlot>(destination.Entity));
        Assert.False(simulation.Entities.HasComponent<VehicleQueueEntry>(successor));
    }

    private static (EntityId Entity, InventoryId Inventory) CreateBuilding(
        SimulationCoordinator simulation, InventoryStore inventories, Vector3 position)
    {
        EntityId entity = simulation.Entities.CreateEntity();
        InventoryId inventory = inventories.CreateInventory(new InventorySpecification(1000));
        simulation.Entities.AddComponent(entity, new WorldTransform(position, Quaternion.Identity, Vector3.One));
        simulation.Entities.AddComponent(entity, new CompletedBuilding(BuildingIds.StorageDepot, new PlayerId(1), SimulationTick.Zero));
        simulation.Entities.AddComponent(entity, new BuildingLoadingDock(new Vector3(0, 0, 14), Vector3.UnitX, 18));
        simulation.Entities.AddComponent(entity, new InventoryStorage(inventory));
        return (entity, inventory);
    }
}
