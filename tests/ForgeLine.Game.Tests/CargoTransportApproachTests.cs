using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Logistics;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class CargoTransportApproachTests
{
    private static readonly PlayerId Owner = new(1);

    [Fact]
    public void ActiveLoadingApproachDoesNotFollowTheMovingTruck()
    {
        var fixture = new Fixture();
        fixture.Simulation.AdvanceOneTick();
        MovementOrder original = fixture.Simulation.Entities.GetComponent<MovementOrder>(fixture.Truck);
        CargoTransportMovementTarget target = fixture.Simulation.Entities.GetComponent<CargoTransportMovementTarget>(fixture.Truck);

        // A detour changes which edge point is closest, not the requested visit.
        fixture.Simulation.Entities.SetComponent(fixture.Truck,
            new WorldTransform(new Vector3(0.0f, 0.0f, 90.0f), Quaternion.Identity, Vector3.One));
        fixture.Simulation.AdvanceOneTick();

        Assert.Equal(original, fixture.Simulation.Entities.GetComponent<MovementOrder>(fixture.Truck));
        Assert.Equal(target, fixture.Simulation.Entities.GetComponent<CargoTransportMovementTarget>(fixture.Truck));
        Assert.Equal(CargoTransportLifecycleState.ToOrigin,
            fixture.Simulation.Entities.GetComponent<CargoTransportRuntimeState>(fixture.Truck).Lifecycle);
        Assert.Equal(100.0, fixture.Inventories.GetQuantity(fixture.SourceInventory, ResourceIds.Steel));
        Assert.Equal(0.0, fixture.Inventories.GetQuantity(fixture.CargoInventory, ResourceIds.Steel));
    }

    [Fact]
    public void InterruptedApproachCanBeIssuedAgainFromTheCurrentPosition()
    {
        var fixture = new Fixture();
        fixture.Simulation.AdvanceOneTick();
        MovementOrder original = fixture.Simulation.Entities.GetComponent<MovementOrder>(fixture.Truck);
        fixture.Simulation.Entities.RemoveComponent<MovementOrder>(fixture.Truck);
        fixture.Simulation.Entities.SetComponent(fixture.Truck,
            new WorldTransform(new Vector3(0.0f, 0.0f, 90.0f), Quaternion.Identity, Vector3.One));
        fixture.Simulation.AdvanceOneTick();

        MovementOrder replacement = fixture.Simulation.Entities.GetComponent<MovementOrder>(fixture.Truck);
        Assert.NotEqual(original.WorldTarget, replacement.WorldTarget);
        Assert.Equal(fixture.Simulation.CurrentTick, replacement.AcceptedAtTick);
        Assert.Equal(100.0, fixture.Inventories.GetQuantity(fixture.SourceInventory, ResourceIds.Steel));
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            var network = new LogisticsNetwork();
            var system = new CargoTransportSystem(network, Inventories);
            Simulation.RegisterSystem(system);
            SourceInventory = Inventories.CreateInventory(new InventorySpecification(1000.0));
            Assert.True(Inventories.Add(SourceInventory, ResourceIds.Steel, 100.0).Succeeded);
            EntityId source = Simulation.Entities.CreateEntity();
            Vector3 position = new(200.0f, 0.0f, 100.0f);
            Simulation.Entities.AddComponent(source, new WorldTransform(position, Quaternion.Identity, Vector3.One));
            Simulation.Entities.AddComponent(source, new InventoryStorage(SourceInventory));
            Simulation.Entities.AddComponent(source, new SpatialPresence(new Vector3(8.0f, 4.0f, 8.0f),
                new SpatialEntryMetadata(Owner.Value, 0, SpatialMobility.Static)));
            LogisticsNodeId origin = network.AddNode(source, position, LogisticsNodeKind.StorageDepot,
                LogisticsNodeCapabilities.CargoSource | LogisticsNodeCapabilities.CargoDestination);
            EntityId destination = Simulation.Entities.CreateEntity();
            InventoryId output = Inventories.CreateInventory(new InventorySpecification(1000.0));
            Simulation.Entities.AddComponent(destination, new InventoryStorage(output));
            LogisticsNodeId target = network.AddNode(destination, new Vector3(240.0f, 0.0f, 100.0f),
                LogisticsNodeKind.StorageDepot, LogisticsNodeCapabilities.CargoDestination);
            Truck = CargoTruckFactory.Create(Simulation.Entities, Inventories, Vector3.Zero, Owner, system);
            CargoInventory = Simulation.Entities.GetComponent<CargoTransport>(Truck).CargoInventory;
            Assert.True(system.TryAssignOrder(Simulation.Entities, Truck,
                new CargoTransportOrder(origin, target, ResourceIds.Steel, 40.0, SimulationTick.Zero), SimulationTick.Zero));
        }

        public SimulationCoordinator Simulation { get; } = new();
        public InventoryStore Inventories { get; } = new();
        public EntityId Truck { get; }
        public InventoryId SourceInventory { get; }
        public InventoryId CargoInventory { get; }
    }
}
