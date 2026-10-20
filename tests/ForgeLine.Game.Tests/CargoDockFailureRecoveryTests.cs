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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultipleDockedSourcesAndDestinationsRecoverRepeatedPhysicalDeficits(bool reverseLayout)
    {
        var simulation = new SimulationCoordinator(ticksPerSecond: 20);
        var inventories = new InventoryStore();
        var network = new LogisticsNetwork();
        var cargo = new CargoTransportSystem(network, inventories);
        var distribution = new AutomatedDistributionSystem(network, inventories, cargo, retryDelayTicks: 5);
        simulation.RegisterSystem(new GroundMovementSystem());
        simulation.RegisterSystem(distribution);
        simulation.RegisterSystem(cargo);

        float Z(float value) => reverseLayout ? 120 - value : value;
        var firstMine = CreateBuilding(simulation, inventories, new Vector3(0, 0, Z(0)));
        var secondMine = CreateBuilding(simulation, inventories, new Vector3(0, 0, Z(60)));
        var thirdMine = CreateBuilding(simulation, inventories, new Vector3(0, 0, Z(120)));
        var firstFactory = CreateBuilding(simulation, inventories, new Vector3(120, 0, Z(0)));
        var secondFactory = CreateBuilding(simulation, inventories, new Vector3(120, 0, Z(120)));
        var depot = CreateBuilding(simulation, inventories, new Vector3(60, 0, Z(60)));
        foreach (EntityId mine in new[] { firstMine.Entity, secondMine.Entity, thirdMine.Entity })
            simulation.Entities.SetComponent(mine,
                new CompletedBuilding(BuildingIds.Extractor, new PlayerId(1), SimulationTick.Zero));
        simulation.Entities.SetComponent(firstFactory.Entity,
            new CompletedBuilding(BuildingIds.VehicleFactory, new PlayerId(1), SimulationTick.Zero));
        simulation.Entities.SetComponent(secondFactory.Entity,
            new CompletedBuilding(BuildingIds.ElectronicsPlant, new PlayerId(1), SimulationTick.Zero));
        simulation.Entities.SetComponent(depot.Entity,
            new CompletedBuilding(BuildingIds.StorageDepot, new PlayerId(1), SimulationTick.Zero));

        Assert.True(inventories.Add(firstMine.Inventory, ResourceIds.Steel, 180).Succeeded);
        Assert.True(inventories.Add(secondMine.Inventory, ResourceIds.Steel, 180).Succeeded);
        Assert.True(inventories.Add(secondMine.Inventory, ResourceIds.Silicates, 180).Succeeded);
        Assert.True(inventories.Add(thirdMine.Inventory, ResourceIds.Silicates, 180).Succeeded);

        var sources = new[] { firstMine, secondMine, thirdMine };
        var destinations = new[] { firstFactory, secondFactory };
        var sourceNodes = new LogisticsNodeId[sources.Length];
        for (int i = 0; i < sources.Length; i++)
            sourceNodes[i] = network.AddNode(sources[i].Entity,
                simulation.Entities.GetComponent<WorldTransform>(sources[i].Entity).Position,
                LogisticsNodeKind.StorageDepot, LogisticsNodeCapabilities.CargoSource);
        var depotNode = network.AddNode(depot.Entity, simulation.Entities.GetComponent<WorldTransform>(depot.Entity).Position,
            LogisticsNodeKind.LogisticsHub, LogisticsNodeCapabilities.Storage);
        for (int i = 0; i < sources.Length; i++)
        {
            var p = simulation.Entities.GetComponent<WorldTransform>(sources[i].Entity).Position;
            var d = Vector3.Distance(p, simulation.Entities.GetComponent<WorldTransform>(depot.Entity).Position);
            network.AddEdge(sourceNodes[i], depotNode, LogisticsTransportMode.GroundRoad, d, d, 200);
        }
        foreach (var factory in destinations)
        {
            var p = simulation.Entities.GetComponent<WorldTransform>(factory.Entity).Position;
            var d = Vector3.Distance(p, simulation.Entities.GetComponent<WorldTransform>(depot.Entity).Position);
            var id = network.AddNode(factory.Entity, p, LogisticsNodeKind.StorageDepot,
                LogisticsNodeCapabilities.CargoDestination);
            network.AddEdge(depotNode, id, LogisticsTransportMode.GroundRoad, d, d, 200);
        }

        EntityId steelPolicy = simulation.Entities.CreateEntity();
        EntityId silicatePolicy = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(steelPolicy, new LogisticsStockPolicy(firstFactory.Entity, ResourceIds.Steel,
            40, 80, 160, LogisticsStockPriority.Normal));
        simulation.Entities.AddComponent(silicatePolicy, new LogisticsStockPolicy(secondFactory.Entity, ResourceIds.Silicates,
            40, 80, 160, LogisticsStockPriority.Normal));
        var trucks = new List<EntityId>();
        for (int i = 0; i < 4; i++)
        {
            float z = Z(i % 2 == 0 ? 0 : 120);
            trucks.Add(CargoTruckFactory.Create(simulation.Entities, inventories,
                new Vector3(-45 - (i / 2) * 20, 1, z + 14), new PlayerId(1), cargo));
        }

        double Total(ResourceId resource)
        {
            double amount = 0;
            foreach (var building in sources.Concat(destinations))
                amount += inventories.GetQuantity(building.Inventory, resource);
            foreach (EntityId truck in trucks)
                amount += inventories.GetQuantity(simulation.Entities.GetComponent<CargoTransport>(truck).CargoInventory, resource);
            return amount;
        }

        double initialSteel = Total(ResourceIds.Steel);
        double initialSilicates = Total(ResourceIds.Silicates);
        for (int stage = 0; stage < 2; stage++)
        {
            double expected = stage == 0 ? 80 : 120;
            bool fulfilled = false;
            for (int tick = 0; tick < 8000; tick++)
            {
                simulation.AdvanceOneTick();
                if (tick % 50 == 0)
                {
                    Assert.Equal(initialSteel, Total(ResourceIds.Steel), 6);
                    Assert.Equal(initialSilicates, Total(ResourceIds.Silicates), 6);
                }
                if (inventories.GetQuantity(firstFactory.Inventory, ResourceIds.Steel) >= expected &&
                    inventories.GetQuantity(secondFactory.Inventory, ResourceIds.Silicates) >= expected)
                {
                    fulfilled = true;
                    break;
                }
            }
            Assert.True(fulfilled, $"Physical supply did not reach stage {stage}; " +
                string.Join("; ", distribution.LastDebugSnapshot.Requests.Select(request => request.ToString())));
            if (stage == 0)
            {
                simulation.Entities.SetComponent(steelPolicy, new LogisticsStockPolicy(firstFactory.Entity, ResourceIds.Steel,
                    100, 120, 160, LogisticsStockPriority.Normal));
                simulation.Entities.SetComponent(silicatePolicy, new LogisticsStockPolicy(secondFactory.Entity, ResourceIds.Silicates,
                    100, 120, 160, LogisticsStockPriority.Normal));
            }
        }

        Assert.Equal(initialSteel, Total(ResourceIds.Steel), 6);
        Assert.Equal(initialSilicates, Total(ResourceIds.Silicates), 6);
        foreach (var source in sources)
        {
            Assert.Equal(0, inventories.GetReservedQuantity(source.Inventory, ResourceIds.Steel));
            Assert.Equal(0, inventories.GetReservedQuantity(source.Inventory, ResourceIds.Silicates));
        }
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
