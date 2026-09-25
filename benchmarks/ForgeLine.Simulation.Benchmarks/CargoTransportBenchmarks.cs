using System.Numerics;
using BenchmarkDotNet.Attributes;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.World;

namespace ForgeLine.Simulation.Benchmarks;

[MemoryDiagnoser]
public class CargoTransportBenchmarks
{
    private SimulationCoordinator _simulation = null!;
    private CargoTransportSystem _transportSystem = null!;

    [Params(100, 500)]
    public int TransportCount { get; set; }

    [IterationSetup]
    public void Setup()
    {
        TerrainWorld terrain = CreateFlatWorld();
        NavigationWorld navigationWorld =
            NavigationWorld.Build(
                terrain,
                gridSettings:
                    new NavigationGridSettings
                    {
                        CellSizeMeters = 4.0f
                    },
                sectorSettings:
                    new NavigationSectorSettings
                    {
                        SectorSizeCells = 4
                    });

        _simulation =
            new SimulationCoordinator(
                ticksPerSecond: 20,
                initialEntityCapacity:
                    TransportCount + 16);

        var inventories =
            new InventoryStore();
        var network =
            new LogisticsNetwork();
        _transportSystem =
            new CargoTransportSystem(
                network,
                inventories);

        _simulation.RegisterSystem(
            new HierarchicalNavigationSystem(
                new HierarchicalPathfinder(
                    navigationWorld)));
        _simulation.RegisterSystem(
            new GroundMovementSystem(
                terrain));
        _simulation.RegisterSystem(
            _transportSystem);

        Vector3 sourcePosition =
            new(4.0f, 0.0f, 4.0f);
        Vector3 destinationPosition =
            new(28.0f, 0.0f, 4.0f);

        InventoryId sourceInventory =
            inventories.CreateInventory(
                new InventorySpecification(
                    TransportCount * 10.0 + 100.0));
        InventoryId destinationInventory =
            inventories.CreateInventory(
                new InventorySpecification(
                    TransportCount * 10.0 + 100.0));

        InventoryOperationResult seed =
            inventories.Add(
                sourceInventory,
                ResourceIds.FerrousOre,
                TransportCount * 10.0);

        if (!seed.Succeeded)
        {
            throw new InvalidOperationException(
                "Unable to seed cargo transport benchmark inventory.");
        }

        EntityId sourceEntity =
            CreateInventoryEntity(
                _simulation,
                sourceInventory,
                sourcePosition);
        EntityId destinationEntity =
            CreateInventoryEntity(
                _simulation,
                destinationInventory,
                destinationPosition);

        LogisticsNodeId sourceNode =
            network.AddNode(
                sourceEntity,
                sourcePosition,
                LogisticsNodeKind.StorageDepot,
                LogisticsNodeCapabilities.CargoSource |
                LogisticsNodeCapabilities.CargoDestination |
                LogisticsNodeCapabilities.Storage |
                LogisticsNodeCapabilities.Distribution);
        LogisticsNodeId destinationNode =
            network.AddNode(
                destinationEntity,
                destinationPosition,
                LogisticsNodeKind.StorageDepot,
                LogisticsNodeCapabilities.CargoSource |
                LogisticsNodeCapabilities.CargoDestination |
                LogisticsNodeCapabilities.Storage |
                LogisticsNodeCapabilities.Distribution);

        network.AddEdge(
            sourceNode,
            destinationNode,
            LogisticsTransportMode.GroundRoad,
            distanceMeters: 24.0,
            baseCost: 24.0,
            capacityPerSecond:
                TransportCount * 10.0);

        var owner =
            new PlayerId(1);

        for (int index = 0;
             index < TransportCount;
             index++)
        {
            EntityId truck =
                CargoTruckFactory.Create(
                    _simulation.Entities,
                    inventories,
                    sourcePosition,
                    owner,
                    transportSystem:
                        _transportSystem);

            bool accepted =
                _transportSystem.TryAssignOrder(
                    _simulation.Entities,
                    truck,
                    new CargoTransportOrder(
                        sourceNode,
                        destinationNode,
                        ResourceIds.FerrousOre,
                        requestedQuantity: 10.0,
                        SimulationTick.Zero),
                    SimulationTick.Zero);

            if (!accepted)
            {
                throw new InvalidOperationException(
                    "Unable to assign cargo transport benchmark order.");
            }
        }
    }

    [Benchmark]
    public long RunCargoTransportBatch()
    {
        const int maximumTicks = 300;

        for (int tick = 0;
             tick < maximumTicks &&
             _transportSystem.Metrics.CompletedOrderCount <
                TransportCount;
             tick++)
        {
            _simulation.AdvanceOneTick();
        }

        if (_transportSystem.Metrics.CompletedOrderCount !=
            TransportCount)
        {
            throw new InvalidOperationException(
                "Cargo transport benchmark did not complete within the bounded tick budget.");
        }

        return _transportSystem.Metrics.CompletedOrderCount;
    }

    private static EntityId CreateInventoryEntity(
        SimulationCoordinator simulation,
        InventoryId inventoryId,
        Vector3 position)
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
            new InventoryStorage(
                inventoryId));
        simulation.Entities.AddComponent(
            entity,
            new StorageDepot(
                inventoryId,
                new FactionId(1)));

        return entity;
    }

    private static TerrainWorld CreateFlatWorld()
    {
        var settings =
            new WorldGridSettings
            {
                ChunkSizeMeters = 32.0f,
                HeightSamplesPerSide = 9
            };

        var chunks =
            new List<TerrainChunk>();

        for (int x = 0;
             x < 2;
             x++)
        {
            int sampleCount =
                settings.HeightSamplesPerSide *
                settings.HeightSamplesPerSide;

            chunks.Add(
                new TerrainChunk(
                    new ChunkCoordinate(
                        x,
                        0),
                    new TerrainHeightfield(
                        settings.HeightSamplesPerSide,
                        settings.ChunkSizeMeters,
                        new float[sampleCount])));
        }

        return new TerrainWorld(
            settings,
            chunks);
    }
}
