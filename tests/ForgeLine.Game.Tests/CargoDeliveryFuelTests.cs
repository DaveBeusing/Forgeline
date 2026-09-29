using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Logistics;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class CargoDeliveryFuelTests
{
    private static readonly PlayerId Owner = new(1);

    [Fact]
    public void LoadedDeliveryCrossesEarlyRefuelThresholdWithoutReturningToOrigin()
    {
        var fixture = new Fixture();
        for (int tick = 0; tick < 6_000 && fixture.Cargo.Metrics.CompletedOrderCount == 0; tick++)
        {
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
            fixture.Simulation.AdvanceOneTick();
        }

        Assert.Equal(1L, fixture.Cargo.Metrics.CompletedOrderCount);
        Assert.Equal(0UL, fixture.Automatic.Metrics.TotalOrdersIssued);
        Assert.Equal(40.0, fixture.Inventories.GetQuantity(fixture.DestinationInventory, ResourceIds.Steel));
        Assert.Equal(60.0, fixture.Inventories.GetQuantity(fixture.SourceInventory, ResourceIds.Steel));
        Assert.Equal(0.0, fixture.Inventories.GetQuantity(fixture.CargoInventory, ResourceIds.Steel));
        double remainingFuel = fixture.Inventories.GetQuantity(fixture.FuelInventory, ResourceIds.Fuel);
        Assert.InRange(remainingFuel, 79.0, 82.0);
        double totalFuel = remainingFuel + fixture.Inventories.GetQuantity(fixture.SourceInventory, ResourceIds.Fuel);
        Assert.InRange(1120.0 - totalFuel, 39.0, 41.0);
        Assert.False(fixture.Simulation.Entities.HasComponent<CargoTransportOrder>(fixture.Truck));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnaffordableDeliveryDoesNotSuppressNormalResupply(bool reserveFuel)
    {
        var fixture = new Fixture();
        fixture.RunUntilLoaded();
        if (reserveFuel)
        {
            Assert.True(fixture.Inventories.Remove(fixture.FuelInventory, ResourceIds.Fuel, 30.0).Succeeded);
            Assert.True(fixture.Inventories.Reserve(fixture.FuelInventory, ResourceIds.Fuel, 60.0).Succeeded);
        }
        else
        {
            Assert.True(fixture.Inventories.Remove(fixture.FuelInventory, ResourceIds.Fuel, 90.0).Succeeded);
        }

        fixture.Simulation.AdvanceOneTick();

        Assert.Equal(1UL, fixture.Automatic.Metrics.TotalOrdersIssued);
        Assert.True(fixture.Simulation.Entities.HasComponent<CargoTransportOrder>(fixture.Truck));
        Assert.Equal(0.0, fixture.Inventories.GetQuantity(fixture.DestinationInventory, ResourceIds.Steel));
        if (reserveFuel)
        {
            Assert.Equal(60.0, fixture.Inventories.GetReservedQuantity(fixture.FuelInventory, ResourceIds.Fuel));
        }
    }

    [Fact]
    public void ExistingResupplyOrderIsNotCanceledByDeliveryPreference()
    {
        var fixture = new Fixture();
        fixture.RunUntilLoaded();
        Assert.True(fixture.Inventories.Remove(fixture.FuelInventory, ResourceIds.Fuel, 30.0).Succeeded);
        var order = new ResupplyOrder(fixture.Source, SimulationTick.Zero, fixture.Simulation.CurrentTick);
        fixture.Simulation.Entities.AddComponent(fixture.Truck, order);

        fixture.Simulation.AdvanceOneTick();

        Assert.Equal(1, fixture.Automatic.Metrics.ActiveResupplyOrders);
        Assert.Equal(0.0, fixture.Inventories.GetQuantity(fixture.DestinationInventory, ResourceIds.Steel));
        Assert.True(fixture.Simulation.Entities.HasComponent<CargoTransportOrder>(fixture.Truck));
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            var network = new LogisticsNetwork();
            Cargo = new CargoTransportSystem(network, Inventories);
            Automatic = new AutomaticResupplyDecisionSystem(Inventories);
            Simulation.RegisterSystem(Automatic);
            Simulation.RegisterSystem(new GroundMovementSystem());
            Simulation.RegisterSystem(new BattlefieldSupplySystem(Inventories));
            Simulation.RegisterSystem(Cargo);

            SourceInventory = Inventories.CreateInventory(new InventorySpecification(2_000.0));
            DestinationInventory = Inventories.CreateInventory(new InventorySpecification(2_000.0));
            FuelInventory = Inventories.CreateInventory(new InventorySpecification(120.0));
            Assert.True(Inventories.Add(SourceInventory, ResourceIds.Steel, 100.0).Succeeded);
            Assert.True(Inventories.Add(SourceInventory, ResourceIds.Fuel, 1_000.0).Succeeded);
            Assert.True(Inventories.Add(FuelInventory, ResourceIds.Fuel, 120.0).Succeeded);
            Source = Simulation.Entities.CreateEntity();
            Simulation.Entities.AddComponent(Source, new InventoryStorage(SourceInventory));
            Simulation.Entities.AddComponent(Source, new WorldTransform(Vector3.Zero, Quaternion.Identity, Vector3.One));
            Simulation.Entities.AddComponent(Source, new SupplyProvider(SourceInventory, Owner, 12.0f));
            EntityId destination = Simulation.Entities.CreateEntity();
            Simulation.Entities.AddComponent(destination, new InventoryStorage(DestinationInventory));
            Vector3 target = new(2_000.0f, 0.0f, 0.0f);
            Simulation.Entities.AddComponent(destination, new WorldTransform(target, Quaternion.Identity, Vector3.One));
            LogisticsNodeId origin = network.AddNode(Source, Vector3.Zero, LogisticsNodeKind.StorageDepot,
                LogisticsNodeCapabilities.CargoSource | LogisticsNodeCapabilities.CargoDestination);
            LogisticsNodeId end = network.AddNode(destination, target, LogisticsNodeKind.StorageDepot,
                LogisticsNodeCapabilities.CargoDestination);
            network.AddEdge(origin, end, LogisticsTransportMode.GroundRoad,
                distanceMeters: 2_000.0, baseCost: 2_000.0, capacityPerSecond: 100.0);
            Truck = CargoTruckFactory.Create(Simulation.Entities, Inventories, Vector3.Zero, Owner, Cargo);
            // This fixture deliberately uses the existing direct ground-movement
            // fallback; route-budget tests cover hierarchical detours separately.
            Simulation.Entities.RemoveComponent<NavigationAgent>(Truck);
            Simulation.Entities.AddComponent(Truck, new UnitFuelState(FuelInventory, 120.0, 0.02));
            Simulation.Entities.AddComponent(Truck, new AutomaticResupplyPolicy(0.8, 0.8));
            CargoInventory = Simulation.Entities.GetComponent<CargoTransport>(Truck).CargoInventory;
            Assert.True(Cargo.TryAssignOrder(Simulation.Entities, Truck,
                new CargoTransportOrder(origin, end, ResourceIds.Steel, 40.0, SimulationTick.Zero), SimulationTick.Zero));
        }

        public SimulationCoordinator Simulation { get; } = new();
        public InventoryStore Inventories { get; } = new();
        public CargoTransportSystem Cargo { get; }
        public AutomaticResupplyDecisionSystem Automatic { get; }
        public EntityId Source { get; }
        public EntityId Truck { get; }
        public InventoryId SourceInventory { get; }
        public InventoryId DestinationInventory { get; }
        public InventoryId FuelInventory { get; }
        public InventoryId CargoInventory { get; }

        public void RunUntilLoaded()
        {
            for (int tick = 0; tick < 20; tick++)
            {
                Simulation.AdvanceOneTick();
                if (Inventories.GetQuantity(CargoInventory, ResourceIds.Steel) > 0.0)
                {
                    return;
                }
            }

            Assert.Fail("The physical loading operation did not complete.");
        }
    }
}
