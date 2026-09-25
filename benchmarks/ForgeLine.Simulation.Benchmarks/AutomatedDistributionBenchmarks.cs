using System.Numerics;
using BenchmarkDotNet.Attributes;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Logistics;
using ForgeLine.Simulation;

namespace ForgeLine.Simulation.Benchmarks;

[MemoryDiagnoser]
public sealed class AutomatedDistributionBenchmarks
{
    [Params(64, 256)]
    public int HubCount { get; set; }

    [Benchmark]
    public AutomatedDistributionMetrics ScheduleRegionalDistribution()
    {
        var simulation =
            new SimulationCoordinator(
                ticksPerSecond: 20,
                initialEntityCapacity:
                    Math.Max(512, HubCount * 3));
        var inventories =
            new InventoryStore();
        var network =
            new LogisticsNetwork();
        var cargo =
            new CargoTransportSystem(
                network,
                inventories);
        var distribution =
            new AutomatedDistributionSystem(
                network,
                inventories,
                cargo,
                retryDelayTicks: 5,
                fairnessAgingTicks: 20);

        simulation.RegisterSystem(distribution);

        InventoryId sourceInventory =
            inventories.CreateInventory(
                new InventorySpecification(
                    HubCount * 1_000.0));
        _ = inventories.Add(
            sourceInventory,
            ResourceIds.FerrousOre,
            HubCount * 500.0);

        EntityId sourceEntity =
            CreateInventoryEntity(
                simulation,
                sourceInventory,
                Vector3.Zero,
                isHub: false);
        LogisticsNodeId sourceNode =
            network.AddNode(
                sourceEntity,
                Vector3.Zero,
                LogisticsNodeKind.StorageDepot,
                LogisticsNodeCapabilities.CargoSource |
                LogisticsNodeCapabilities.Storage |
                LogisticsNodeCapabilities.Distribution);

        for (int index = 0; index < HubCount; index++)
        {
            Vector3 position =
                new(
                    20.0f + index * 2.0f,
                    0.0f,
                    20.0f + (index % 16) * 3.0f);
            InventoryId inventory =
                inventories.CreateInventory(
                    new InventorySpecification(500.0));
            EntityId hub =
                CreateInventoryEntity(
                    simulation,
                    inventory,
                    position,
                    isHub: true);
            LogisticsNodeId node =
                network.AddNode(
                    hub,
                    position,
                    LogisticsNodeKind.LogisticsHub,
                    LogisticsNodeCapabilities.CargoDestination |
                    LogisticsNodeCapabilities.Storage |
                    LogisticsNodeCapabilities.Distribution);

            network.AddEdge(
                sourceNode,
                node,
                LogisticsTransportMode.GroundRoad,
                distanceMeters:
                    Vector3.Distance(
                        Vector3.Zero,
                        position),
                baseCost: index + 1.0,
                capacityPerSecond: 500.0);

            EntityId policy =
                simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(
                policy,
                new LogisticsStockPolicy(
                    hub,
                    ResourceIds.FerrousOre,
                    desiredMinimum: 50.0,
                    desiredTarget: 100.0,
                    desiredMaximum: 200.0,
                    LogisticsStockPriority.Normal));
        }

        int truckCount =
            Math.Max(16, HubCount / 4);

        for (int index = 0; index < truckCount; index++)
        {
            _ = CargoTruckFactory.Create(
                simulation.Entities,
                inventories,
                Vector3.Zero,
                new PlayerId(1),
                cargo);
        }

        simulation.AdvanceOneTick();

        return distribution.Metrics;
    }

    private static EntityId CreateInventoryEntity(
        SimulationCoordinator simulation,
        InventoryId inventory,
        Vector3 position,
        bool isHub)
    {
        EntityId entity =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            entity,
            new WorldTransform(
                position,
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            entity,
            new InventoryStorage(inventory));

        if (isHub)
        {
            simulation.Entities.AddComponent(
                entity,
                new LogisticsHub(
                    inventory,
                    new FactionId(1)));
        }
        else
        {
            simulation.Entities.AddComponent(
                entity,
                new StorageDepot(
                    inventory,
                    new FactionId(1)));
        }

        return entity;
    }
}
