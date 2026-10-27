using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Logistics;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class CargoInboundCapacityTests
{
    [Fact]
    public void SharedHeadroomIncludesDifferentResourcesAndDestinationAliases()
    {
        var state = new Fixture();
        var alias = state.Destination(state.Inventory);
        state.Order(state.Node, ResourceIds.FerrousOre, 60);
        state.Order(alias, ResourceIds.Steel, 20);
        Assert.Equal(20, state.Capture().GetAddable(state.Stock, state.Inventory, ResourceIds.Silicates, 100));
    }

    [Fact]
    public void ResourceHeadroomRemainsBoundedWhenSharedCapacityIsAvailable()
    {
        var state = new Fixture(new Dictionary<ResourceId, double> { [ResourceIds.FerrousOre] = 50 });
        Assert.True(state.Stock.Add(state.Inventory, ResourceIds.FerrousOre, 10).Succeeded);
        state.Order(state.Node, ResourceIds.FerrousOre, 30);
        Assert.Equal(10, state.Capture().GetAddable(state.Stock, state.Inventory, ResourceIds.FerrousOre, 100));
        Assert.Equal(60, state.Capture().GetAddable(state.Stock, state.Inventory, ResourceIds.Steel, 100));
    }

    [Fact]
    public void PartialUnloadingCountsOnlyCargoStillOnTheTruck()
    {
        var state = new Fixture();
        var truck = state.Order(state.Node, ResourceIds.FerrousOre, 100);
        var cargo = state.Entities.GetComponent<CargoTransport>(truck).CargoInventory;
        Assert.True(state.Stock.Add(cargo, ResourceIds.FerrousOre, 60).Succeeded);
        Assert.True(state.Stock.Add(state.Inventory, ResourceIds.FerrousOre, 40).Succeeded);
        state.Entities.SetComponent(truck, CargoTransportRuntimeState.Idle with
        {
            Lifecycle = CargoTransportLifecycleState.Unloading, LoadedQuantity = 100, DeliveredQuantity = 40
        });
        Assert.Equal(0, state.Capture().GetAddable(state.Stock, state.Inventory, ResourceIds.Steel, 100));

        Assert.True(state.Stock.Transfer(cargo, state.Inventory, ResourceIds.FerrousOre, 60).Succeeded);
        Assert.True(state.Stock.Remove(state.Inventory, ResourceIds.FerrousOre, 60).Succeeded);
        state.Entities.SetComponent(truck, CargoTransportRuntimeState.Idle with
        {
            Lifecycle = CargoTransportLifecycleState.Unloading, LoadedQuantity = 100, DeliveredQuantity = 100
        });
        Assert.Equal(60, state.Capture().GetAddable(state.Stock, state.Inventory, ResourceIds.Steel, 100));
    }

    [Theory]
    [InlineData(CargoTransportLifecycleState.Failed)]
    [InlineData(CargoTransportLifecycleState.Idle)]
    public void TerminalOrdersDoNotHoldDestinationSpace(CargoTransportLifecycleState lifecycle)
    {
        var state = new Fixture();
        var truck = state.Order(state.Node, ResourceIds.FerrousOre, 100);
        state.Entities.SetComponent(truck, CargoTransportRuntimeState.Idle with { Lifecycle = lifecycle });
        Assert.Equal(100, state.Capture().GetAddable(state.Stock, state.Inventory, ResourceIds.Steel, 100));
    }

    [Fact]
    public void DestroyedAndReassignedTrucksReleaseTheOldDestination()
    {
        var state = new Fixture();
        var truck = state.Order(state.Node, ResourceIds.FerrousOre, 100);
        Assert.Equal(0, state.Capture().GetAddable(state.Stock, state.Inventory, ResourceIds.Steel, 100));
        Assert.True(state.Entities.DestroyEntity(truck));
        Assert.Equal(100, state.Capture().GetAddable(state.Stock, state.Inventory, ResourceIds.Steel, 100));
        var other = state.Destination(state.Stock.CreateInventory(new InventorySpecification(100)));
        var replacement = state.Order(other, ResourceIds.FerrousOre, 100);
        Assert.NotEqual(truck, replacement);
        Assert.Equal(100, state.Capture().GetAddable(state.Stock, state.Inventory, ResourceIds.Steel, 100));
    }

    [Fact]
    public void DesiredButUnassignedDemandDoesNotReserveStorage()
    {
        var state = new Fixture();
        var policy = state.Entities.CreateEntity();
        state.Entities.AddComponent(policy, new LogisticsStockPolicy(state.Target, ResourceIds.FerrousOre, 50, 100, 100));
        Assert.Equal(100, state.Capture().GetAddable(state.Stock, state.Inventory, ResourceIds.Steel, 100));
    }

    private sealed class Fixture
    {
        internal EntityRegistry Entities { get; } = new();
        internal InventoryStore Stock { get; } = new();
        internal LogisticsNetwork Network { get; } = new();
        internal InventoryId Inventory { get; }
        internal EntityId Target { get; private set; }
        internal LogisticsNodeId Node { get; }
        private readonly AutomatedDistributionSystem _distribution;
        private readonly LogisticsNodeId _origin;

        internal Fixture(IReadOnlyDictionary<ResourceId, double>? resourceCapacities = null)
        {
            Inventory = Stock.CreateInventory(new InventorySpecification(100, perResourceCapacities: resourceCapacities));
            Node = Destination(Inventory);
            var source = Entities.CreateEntity();
            Entities.AddComponent(source, new InventoryStorage(Stock.CreateInventory(new InventorySpecification(1000))));
            _origin = Network.AddNode(source, Vector3.Zero, LogisticsNodeKind.StorageDepot, LogisticsNodeCapabilities.CargoSource);
            _distribution = new(Network, Stock, new CargoTransportSystem(Network, Stock));
        }

        internal LogisticsNodeId Destination(InventoryId inventory)
        {
            var entity = Entities.CreateEntity();
            Entities.AddComponent(entity, new InventoryStorage(inventory));
            Target = entity;
            return Network.AddNode(entity, Vector3.One, LogisticsNodeKind.StorageDepot, LogisticsNodeCapabilities.CargoDestination);
        }

        internal EntityId Order(LogisticsNodeId destination, ResourceId resource, double quantity)
        {
            var entity = Entities.CreateEntity();
            Entities.AddComponent(entity, new CargoTransport(Stock.CreateInventory(new InventorySpecification(100)), 100, new PlayerId(1)));
            Entities.AddComponent(entity, new CargoTransportOrder(_origin, destination, resource, quantity, SimulationTick.Zero));
            Entities.AddComponent(entity, CargoTransportRuntimeState.Idle with { Lifecycle = CargoTransportLifecycleState.ToOrigin });
            return entity;
        }

        internal CargoInboundCapacity Capture() => _distribution.CaptureInboundCapacity(Entities);
    }
}
