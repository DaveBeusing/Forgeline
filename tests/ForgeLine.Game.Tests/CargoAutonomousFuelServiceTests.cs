using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Logistics;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class CargoAutonomousFuelServiceTests
{
    private static readonly PlayerId Owner = new(1);

    [Fact]
    public void EmptyTruckAtOwnedDepotPhysicallyRefuelsWithoutCreatingFuel()
    {
        var f = new Fixture(providerFuel: 300, initialFuel: 0);
        double before = f.Inventories.GetQuantity(f.ProviderInventory, ResourceIds.Fuel);
        bool sawService = false;
        bool sawExclusiveDock = false;

        for (int tick = 0; tick < 2000; tick++)
        {
            f.Simulation.AdvanceOneTick();
            if (!f.Simulation.Entities.TryGetComponent(f.Truck, out CargoFuelServiceState state))
                continue;
            sawService = true;
            if (f.Simulation.Entities.TryGetComponent(f.Provider, out VehicleDockSlot slot))
            {
                sawExclusiveDock = true;
                Assert.Equal(f.Truck, slot.Truck);
            }
            if (state.Phase == CargoFuelServicePhase.Exiting &&
                f.Inventories.GetQuantity(f.DriveInventory, ResourceIds.Fuel) > 0)
                break;
        }

        Assert.True(sawService);
        Assert.True(sawExclusiveDock);
        Assert.Equal(before - 120, f.Inventories.GetQuantity(f.ProviderInventory, ResourceIds.Fuel), 5);
        Assert.Equal(120, f.Inventories.GetQuantity(f.DriveInventory, ResourceIds.Fuel), 5);
        Assert.Equal(0, f.Inventories.GetQuantity(f.CargoInventory, ResourceIds.Fuel));
    }

    [Fact]
    public void RefuelingKeepsCargoOrderAndStockReservationUnchanged()
    {
        var f = new Fixture(providerFuel: 300, initialFuel: 0);
        EntityId destination = f.Simulation.Entities.CreateEntity();
        var destinationInventory = f.Inventories.CreateInventory(new InventorySpecification(500));
        f.Simulation.Entities.AddComponent(destination, new InventoryStorage(destinationInventory));
        f.Simulation.Entities.AddComponent(destination, new WorldTransform(new(120, 0, 0),
            Quaternion.Identity, Vector3.One));
        var origin = f.Network.AddNode(f.Provider, Vector3.Zero, LogisticsNodeKind.StorageDepot,
            LogisticsNodeCapabilities.CargoSource | LogisticsNodeCapabilities.CargoDestination);
        var target = f.Network.AddNode(destination, new(120, 0, 0), LogisticsNodeKind.StorageDepot,
            LogisticsNodeCapabilities.CargoDestination);
        f.Network.AddEdge(origin, target, LogisticsTransportMode.GroundRoad,
            distanceMeters: 120, baseCost: 120, capacityPerSecond: 100);
        Assert.True(f.Inventories.Add(f.ProviderInventory, ResourceIds.Steel, 40).Succeeded);
        Assert.True(f.Inventories.Reserve(f.ProviderInventory, ResourceIds.Steel, 40).Succeeded);
        var order = new CargoTransportOrder(origin, target, ResourceIds.Steel, 40, SimulationTick.Zero);
        Assert.True(f.Cargo.TryAssignOrder(f.Simulation.Entities, f.Truck, order, SimulationTick.Zero));
        var reservation = new CargoTransportReservation(new LogisticsTransportRequestId(76),
            f.ProviderInventory, ResourceIds.Steel, 40);
        f.Simulation.Entities.AddComponent(f.Truck, reservation);

        for (int tick = 0; tick < 500 && f.Inventories.GetQuantity(f.DriveInventory, ResourceIds.Fuel) < 120; tick++)
            f.Simulation.AdvanceOneTick();

        Assert.Equal(order, f.Simulation.Entities.GetComponent<CargoTransportOrder>(f.Truck));
        Assert.Equal(reservation, f.Simulation.Entities.GetComponent<CargoTransportReservation>(f.Truck));
        Assert.Equal(40, f.Inventories.GetReservedQuantity(f.ProviderInventory, ResourceIds.Steel));
        Assert.Equal(0, f.Inventories.GetQuantity(f.CargoInventory, ResourceIds.Steel));
        Assert.Equal(120, f.Inventories.GetQuantity(f.DriveInventory, ResourceIds.Fuel), 5);
    }

    [Fact]
    public void UnreachableOrEmptyProviderReportsRescueInsteadOfIdleOrFreeFuel()
    {
        var f = new Fixture(providerFuel: 0, initialFuel: 0);
        for (int tick = 0; tick < 100; tick++) f.Simulation.AdvanceOneTick();
        CargoFuelServiceState service = f.Simulation.Entities.GetComponent<CargoFuelServiceState>(f.Truck);
        Assert.Equal(CargoFuelServicePhase.AwaitingFuelRescue, service.Phase);
        Assert.Equal(CargoFuelServiceReason.NoEligibleProvider, service.Reason);
        Assert.Equal(0, f.Inventories.GetQuantity(f.DriveInventory, ResourceIds.Fuel));
        Assert.False(f.Simulation.Entities.HasComponent<VehicleDockSlot>(f.Provider));
    }

    [Fact]
    public void OtherPlayerFuelCannotRefillCargoTruck()
    {
        var f = new Fixture(providerFuel: 300, initialFuel: 0, providerOwner: new(2));
        for (int tick = 0; tick < 100; tick++) f.Simulation.AdvanceOneTick();
        var status = f.Simulation.Entities.GetComponent<CargoFuelServiceState>(f.Truck);
        Assert.Equal(CargoFuelServicePhase.AwaitingFuelRescue, status.Phase);
        Assert.Equal(0, f.Inventories.GetQuantity(f.DriveInventory, ResourceIds.Fuel));
        Assert.Equal(300, f.Inventories.GetQuantity(f.ProviderInventory, ResourceIds.Fuel));
    }

    private sealed class Fixture
    {
        public Fixture(double providerFuel, double initialFuel, PlayerId providerOwner = default)
        {
            if (!providerOwner.IsSpecified) providerOwner = Owner;
            Network = new LogisticsNetwork();
            Cargo = new CargoTransportSystem(Network, Inventories);
            Simulation.RegisterSystem(new GroundMovementSystem());
            Simulation.RegisterSystem(new BattlefieldSupplySystem(Inventories));
            Simulation.RegisterSystem(Cargo);

            ProviderInventory = Inventories.CreateInventory(new InventorySpecification(1000));
            Assert.True(Inventories.Add(ProviderInventory, ResourceIds.Fuel, providerFuel).Succeeded);
            Provider = Simulation.Entities.CreateEntity();
            Simulation.Entities.AddComponent(Provider, new WorldTransform(Vector3.Zero,
                Quaternion.Identity, Vector3.One));
            Simulation.Entities.AddComponent(Provider, new CompletedBuilding(
                BuildingIds.SupplyDepot, providerOwner, SimulationTick.Zero));
            Simulation.Entities.AddComponent(Provider, new InventoryStorage(ProviderInventory));
            Simulation.Entities.AddComponent(Provider, new BuildingLoadingDock(new(0, 0, 14),
                Vector3.UnitX, 18));
            Simulation.Entities.AddComponent(Provider, new SupplyDepot(ProviderInventory, providerOwner));
            Simulation.Entities.AddComponent(Provider, new SupplyProvider(ProviderInventory, providerOwner, 30));

            Truck = CargoTruckFactory.Create(Simulation.Entities, Inventories,
                new Vector3(0, 1, 14), Owner, Cargo);
            var position = Simulation.Entities.GetComponent<WorldTransform>(Truck);
            Simulation.Entities.SetComponent(Truck, position with
            {
                Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2)
            });
            Simulation.Entities.SetComponent(Truck, GroundMovementState.Stationary(MathF.PI / 2));
            DriveInventory = Inventories.CreateInventory(new InventorySpecification(120));
            Assert.True(Inventories.Add(DriveInventory, ResourceIds.Fuel, initialFuel).Succeeded);
            Simulation.Entities.AddComponent(Truck, new UnitFuelState(DriveInventory, 120, 0.01));
            CargoInventory = Simulation.Entities.GetComponent<CargoTransport>(Truck).CargoInventory;
        }

        public SimulationCoordinator Simulation { get; } = new();
        public InventoryStore Inventories { get; } = new();
        public LogisticsNetwork Network { get; }
        public CargoTransportSystem Cargo { get; }
        public EntityId Provider { get; }
        public EntityId Truck { get; }
        public InventoryId ProviderInventory { get; }
        public InventoryId DriveInventory { get; }
        public InventoryId CargoInventory { get; }
    }
}
