using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Logistics;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class VehicleDockingIntegrationTests
{
    [Theory]
    [InlineData(10, false, 1)]
    [InlineData(20, false, 1)]
    [InlineData(10, true, 1)]
    [InlineData(20, true, 1)]
    [InlineData(20, false, 2)]
    public void CargoDeliveryIncludesStationaryServiceAndPhysicalExit(int ticksPerSecond, bool viaBuilding, int truckCount)
    {
        var simulation = new SimulationCoordinator(ticksPerSecond);
        var inventories = new InventoryStore();
        var network = new LogisticsNetwork();
        var cargo = new CargoTransportSystem(network, inventories);
        simulation.RegisterSystem(new GroundMovementSystem());
        simulation.RegisterSystem(cargo);
        var source = CreateBuilding(simulation, inventories, Vector3.Zero);
        var destination = CreateBuilding(simulation, inventories, new(120, 0, 0));
        Assert.True(inventories.Add(source.Inventory, ResourceIds.Steel, 100).Succeeded);
        var origin = network.AddNode(source.Entity, Vector3.Zero, LogisticsNodeKind.StorageDepot,
            LogisticsNodeCapabilities.CargoSource | LogisticsNodeCapabilities.CargoDestination);
        var target = network.AddNode(destination.Entity, new(120, 0, 0), LogisticsNodeKind.StorageDepot,
            LogisticsNodeCapabilities.CargoDestination);
        if (viaBuilding)
        {
            var middle = CreateBuilding(simulation, inventories, new(60, 0, 0));
            var waypoint = network.AddNode(middle.Entity, new(60, 0, 0), LogisticsNodeKind.StorageDepot,
                LogisticsNodeCapabilities.CargoDestination);
            network.AddEdge(origin, waypoint, LogisticsTransportMode.GroundRoad, 60, 60, 100);
            network.AddEdge(waypoint, target, LogisticsTransportMode.GroundRoad, 60, 60, 100);
        }
        else network.AddEdge(origin, target, LogisticsTransportMode.GroundRoad, 120, 120, 100);
        EntityId truck = CargoTruckFactory.Create(simulation.Entities, inventories, new(-50, 1, 14), new(1), cargo);
        simulation.Entities.SetComponent(truck, GroundMovementState.Stationary(MathF.PI / 2));
        Assert.True(cargo.TryAssignOrder(simulation.Entities, truck, new(origin, target, ResourceIds.Steel, 40, SimulationTick.Zero), SimulationTick.Zero));
        var trucks = new List<EntityId> { truck };
        if (truckCount == 2)
        {
            var second = CargoTruckFactory.Create(simulation.Entities, inventories, new(-60, 1, 30), new(1), cargo);
            Assert.True(cargo.TryAssignOrder(simulation.Entities, second, new(origin, target, ResourceIds.Steel, 40, SimulationTick.Zero), SimulationTick.Zero));
            trucks.Add(second);
        }
        int serviceTicks = 0;
        bool sawExit = false;
        for (int tick = 0; tick < ticksPerSecond * 180; tick++)
        {
            simulation.AdvanceOneTick();
            if (simulation.Entities.TryGetComponent(truck, out VehicleDockVisit visit))
            {
                if (visit.ServiceStartedTick > 0)
                {
                    serviceTicks++;
                    var motion = simulation.Entities.GetComponent<GroundMovementState>(truck);
                    Assert.True(motion.Velocity.Length() <= 0.05f);
                    Assert.True(visit.Pose.IsSettled(simulation.Entities.GetComponent<WorldTransform>(truck), motion));
                }
                sawExit |= visit.Status == VehicleDockStatus.Exiting;
            }
            if (trucks.All(entity => !simulation.Entities.HasComponent<CargoTransportOrder>(entity))) break;
        }
        var state = simulation.Entities.GetComponent<CargoTransportRuntimeState>(truck);
        Assert.True(state.Lifecycle == CargoTransportLifecycleState.Idle,
            $"Unfinished {state}; position={simulation.Entities.GetComponent<WorldTransform>(truck).Position}; visit={VisitDescription(simulation, truck)}");
        foreach (var entity in trucks)
            Assert.False(simulation.Entities.HasComponent<CargoTransportOrder>(entity), VisitDescription(simulation, entity));
        Assert.Equal(40 * truckCount, inventories.GetQuantity(destination.Inventory, ResourceIds.Steel));
        Assert.Equal(100 - 40 * truckCount, inventories.GetQuantity(source.Inventory, ResourceIds.Steel));
        Assert.True(serviceTicks >= ticksPerSecond);
        Assert.True(sawExit);
        Assert.False(simulation.Entities.HasComponent<VehicleDockSlot>(source.Entity));
        Assert.False(simulation.Entities.HasComponent<VehicleDockSlot>(destination.Entity));
    }

    [Fact]
    public void PassingSupplyTruckDoesNotLoadButExplicitResupplyLoadsAndExits()
    {
        var simulation = new SimulationCoordinator();
        var inventories = new InventoryStore();
        var cargo = new CargoTransportSystem(new LogisticsNetwork(), inventories);
        simulation.RegisterSystem(new GroundMovementSystem());
        simulation.RegisterSystem(new BattlefieldSupplySystem(inventories));
        var depot = CreateBuilding(simulation, inventories, Vector3.Zero);
        simulation.Entities.AddComponent(depot.Entity, new SupplyDepot(depot.Inventory, new(1)));
        simulation.Entities.AddComponent(depot.Entity, new SupplyProvider(depot.Inventory, new(1), 20));
        Assert.True(inventories.Add(depot.Inventory, ResourceIds.Fuel, 300).Succeeded);
        Assert.True(inventories.Add(depot.Inventory, ResourceIds.Ammunition, 300).Succeeded);
        EntityId truck = SupplyTruckFactory.Create(simulation.Entities, inventories, new(0, 1, 14), new(1), cargo);
        var supply = simulation.Entities.GetComponent<SupplyTruck>(truck);
        var driveFuel = simulation.Entities.GetComponent<UnitFuelState>(truck);
        simulation.Entities.SetComponent(truck, new UnitFuelState(driveFuel.InventoryId, driveFuel.Capacity, 0));
        simulation.RunTicks(5, TestContext.Current.CancellationToken);
        Assert.Equal(0, inventories.GetQuantity(supply.InventoryId, ResourceIds.Fuel));
        var command = new ResupplyCommand(new(1), [truck], simulation.CurrentTick);
        simulation.SubmitCommand(command, simulation.CurrentTick.Next());
        simulation.RunTicks(2000, TestContext.Current.CancellationToken);
        Assert.Equal(1, command.AcceptedTargetCount);
        Assert.Equal(supply.FuelTarget, inventories.GetQuantity(supply.InventoryId, ResourceIds.Fuel));
        Assert.Equal(supply.AmmunitionTarget, inventories.GetQuantity(supply.InventoryId, ResourceIds.Ammunition));
        Assert.False(simulation.Entities.HasComponent<SupplyLoadingOrder>(truck));
        Assert.False(simulation.Entities.HasComponent<VehicleDockSlot>(depot.Entity));
        Assert.Equal(300, inventories.GetQuantity(depot.Inventory, ResourceIds.Fuel) + inventories.GetQuantity(supply.InventoryId, ResourceIds.Fuel));
    }

    [Fact]
    public void EmptyDriveTankAllowsDockedSupplyServiceButKeepsExitOccupied()
    {
        var simulation = new SimulationCoordinator();
        var inventories = new InventoryStore();
        var cargo = new CargoTransportSystem(new LogisticsNetwork(), inventories);
        simulation.RegisterSystem(new GroundMovementSystem());
        simulation.RegisterSystem(new BattlefieldSupplySystem(inventories));
        var depot = CreateBuilding(simulation, inventories, Vector3.Zero);
        simulation.Entities.AddComponent(depot.Entity, new SupplyDepot(depot.Inventory, new(1)));
        Assert.True(inventories.Add(depot.Inventory, ResourceIds.Fuel, 300).Succeeded);
        Assert.True(inventories.Add(depot.Inventory, ResourceIds.Ammunition, 300).Succeeded);
        EntityId truck = SupplyTruckFactory.Create(simulation.Entities, inventories, new(0, 1, 14), new(1), cargo);
        var transform = simulation.Entities.GetComponent<WorldTransform>(truck);
        simulation.Entities.SetComponent(truck, transform with
        {
            Position = new(0, simulation.Entities.GetComponent<GroundMovement>(truck).HeightOffset, 14),
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2)
        });
        simulation.Entities.SetComponent(truck, GroundMovementState.Stationary(MathF.PI / 2));
        var fuel = simulation.Entities.GetComponent<UnitFuelState>(truck);
        inventories.Remove(fuel.InventoryId, ResourceIds.Fuel, fuel.Capacity);
        simulation.Entities.AddComponent(truck, new SupplyLoadingOrder(depot.Entity, simulation.CurrentTick));
        simulation.RunTicks(500, TestContext.Current.CancellationToken);
        var supply = simulation.Entities.GetComponent<SupplyTruck>(truck);
        Assert.Equal(supply.FuelTarget, inventories.GetQuantity(supply.InventoryId, ResourceIds.Fuel));
        Assert.Equal(0, inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel));
        Assert.True(simulation.Entities.HasComponent<VehicleDockSlot>(depot.Entity));
        Assert.Equal(VehicleDockStatus.Exiting, simulation.Entities.GetComponent<VehicleDockVisit>(truck).Status);
    }

    private static string VisitDescription(SimulationCoordinator simulation, EntityId truck) =>
        simulation.Entities.TryGetComponent(truck, out VehicleDockVisit visit) ? visit.ToString() : "none";

    private static (EntityId Entity, InventoryId Inventory) CreateBuilding(SimulationCoordinator simulation, InventoryStore inventories, Vector3 position)
    {
        EntityId entity = simulation.Entities.CreateEntity();
        InventoryId inventory = inventories.CreateInventory(new InventorySpecification(1000));
        simulation.Entities.AddComponent(entity, new WorldTransform(position, Quaternion.Identity, Vector3.One));
        simulation.Entities.AddComponent(entity, new CompletedBuilding(BuildingIds.StorageDepot, new(1), SimulationTick.Zero));
        simulation.Entities.AddComponent(entity, new BuildingLoadingDock(new(0, 0, 14), Vector3.UnitX, 18));
        simulation.Entities.AddComponent(entity, new InventoryStorage(inventory));
        return (entity, inventory);
    }
}
