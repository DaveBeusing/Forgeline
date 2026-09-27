using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class CargoDeliveryRouteFuelTests
{
    private static readonly PlayerId Owner = new(1);
    private static readonly Vector3 Start = new(4.0f, 0.0f, 4.0f);
    private static readonly Vector3 Destination = new(60.0f, 0.0f, 4.0f);

    [Fact]
    public void PhysicalDetourBeyondMovementFuelIsDeferredBeforeTravel()
    {
        var settings = new WorldGridSettings
        {
            ChunkSizeMeters = 32.0f,
            HeightSamplesPerSide = 9
        };
        var chunks = new List<TerrainChunk>();
        for (int x = 0; x < 2; x++)
        {
            chunks.Add(
                new TerrainChunk(
                    new ChunkCoordinate(x, 0),
                    new TerrainHeightfield(
                        settings.HeightSamplesPerSide,
                        settings.ChunkSizeMeters,
                        new float[
                            settings.HeightSamplesPerSide *
                            settings.HeightSamplesPerSide])));
        }

        var terrain = new TerrainWorld(settings, chunks);
        var barrier = new AxisAlignedBounds(
            new Vector3(24.0f, -1.0f, 0.0f),
            new Vector3(32.0f, 4.0f, 24.0f));
        var navigation = new HierarchicalNavigationSystem(
            new HierarchicalPathfinder(
                NavigationWorld.Build(
                    terrain,
                    [barrier],
                    new NavigationGridSettings
                    {
                        CellSizeMeters = 4.0f
                    },
                    new NavigationSectorSettings
                    {
                        SectorSizeCells = 4
                    })));

        var inventories = new InventoryStore();
        var network = new LogisticsNetwork();
        var cargo = new CargoTransportSystem(
            network,
            inventories,
            navigation);
        var automatic =
            new AutomaticResupplyDecisionSystem(inventories);
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(automatic);
        simulation.RegisterSystem(navigation);
        simulation.RegisterSystem(new GroundMovementSystem());
        simulation.RegisterSystem(
            new BattlefieldSupplySystem(inventories));
        simulation.RegisterSystem(cargo);

        InventoryId sourceInventory =
            inventories.CreateInventory(
                new InventorySpecification(500.0));
        InventoryId destinationInventory =
            inventories.CreateInventory(
                new InventorySpecification(500.0));
        Assert.True(
            inventories.Add(
                sourceInventory,
                ResourceIds.Steel,
                100.0).Succeeded);

        EntityId source = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            source,
            new WorldTransform(
                Start,
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            source,
            new InventoryStorage(sourceInventory));

        EntityId destination =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            destination,
            new WorldTransform(
                Destination,
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            destination,
            new InventoryStorage(destinationInventory));

        LogisticsNodeId origin = network.AddNode(
            source,
            Start,
            LogisticsNodeKind.StorageDepot,
            LogisticsNodeCapabilities.CargoSource |
            LogisticsNodeCapabilities.CargoDestination);
        LogisticsNodeId target = network.AddNode(
            destination,
            Destination,
            LogisticsNodeKind.StorageDepot,
            LogisticsNodeCapabilities.CargoDestination);
        network.AddEdge(
            origin,
            target,
            LogisticsTransportMode.GroundRoad,
            distanceMeters: 56.0,
            baseCost: 56.0,
            capacityPerSecond: 100.0);

        EntityId truck = CargoTruckFactory.Create(
            simulation.Entities,
            inventories,
            Start,
            Owner,
            cargo);
        InventoryId fuelInventory =
            inventories.CreateInventory(
                new InventorySpecification(20.0));
        Assert.True(
            inventories.Add(
                fuelInventory,
                ResourceIds.Fuel,
                5.0).Succeeded);
        simulation.Entities.AddComponent(
            truck,
            new UnitFuelState(
                fuelInventory,
                capacity: 20.0,
                consumptionPerMeter: 0.08));
        simulation.Entities.AddComponent(
            truck,
            new AutomaticResupplyPolicy(
                ammunitionThreshold: 0.1,
                fuelThreshold: 0.1));

        CargoTransport transport =
            simulation.Entities.GetComponent<CargoTransport>(truck);
        Assert.True(
            cargo.TryAssignOrder(
                simulation.Entities,
                truck,
                new CargoTransportOrder(
                    origin,
                    target,
                    ResourceIds.Steel,
                    requestedQuantity: 40.0,
                    SimulationTick.Zero),
                SimulationTick.Zero));

        simulation.RunTicks(
            8,
            TestContext.Current.CancellationToken);

        CargoDeliveryFuelDeferral deferral =
            simulation.Entities.GetComponent<CargoDeliveryFuelDeferral>(truck);

        Assert.True(deferral.RequiredFuel > deferral.AvailableFuel);
        Assert.True(
            Vector3.Distance(Start, Destination) * 0.08 <
            deferral.AvailableFuel);
        Assert.Equal(
            Start,
            simulation.Entities.GetComponent<WorldTransform>(truck).Position);
        Assert.True(
            simulation.Entities.HasComponent<CargoTransportOrder>(truck));
        Assert.False(
            simulation.Entities.HasComponent<MovementOrder>(truck));
        Assert.False(
            simulation.Entities.HasComponent<NavigationRouteState>(truck));
        Assert.Equal(
            1UL,
            navigation.LastDiagnostics.QueuedPathCount);
        Assert.Equal(
            40.0,
            inventories.GetQuantity(
                transport.CargoInventory,
                ResourceIds.Steel),
            precision: 6);
        Assert.Equal(
            5.0,
            inventories.GetQuantity(
                fuelInventory,
                ResourceIds.Fuel),
            precision: 6);

        // The exact route deficit must request Fuel even though 25% tank
        // remains and the ordinary automatic threshold is only 10%.
        Assert.True(
            automatic.Metrics.ProviderUnavailableThisTick > 0);

        simulation.RunTicks(
            5,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            1UL,
            navigation.LastDiagnostics.QueuedPathCount);
    }
}
