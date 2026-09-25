using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class CargoTransportSystemTests
{
    private static readonly PlayerId LocalPlayer = new(1);

    [Fact]
    public void FullTransportCycleMovesCargoPhysicallyAndConservesResources()
    {
        TransportFixture fixture = CreateFixture(
            sourceQuantity: 80.0,
            sourcePosition: new Vector3(4.0f, 0.0f, 4.0f),
            destinationPosition: new Vector3(52.0f, 0.0f, 4.0f));

        fixture.Network.AddEdge(
            fixture.SourceNode,
            fixture.DestinationNode,
            LogisticsTransportMode.GroundRoad,
            distanceMeters: 48.0,
            baseCost: 48.0,
            capacityPerSecond: 100.0);

        EntityId truck = fixture.CreateTruckAtSource();
        double before = fixture.TotalConservedQuantity(truck);

        Assert.True(
            fixture.TransportSystem.TryAssignOrder(
                fixture.Simulation.Entities,
                truck,
                new CargoTransportOrder(
                    fixture.SourceNode,
                    fixture.DestinationNode,
                    ResourceIds.FerrousOre,
                    requestedQuantity: 50.0,
                    SimulationTick.Zero),
                SimulationTick.Zero));

        RunUntilOrderCompletes(fixture, truck);

        double after = fixture.TotalConservedQuantity(truck);

        Assert.Equal(before, after);
        Assert.Equal(
            30.0,
            fixture.Inventories.GetQuantity(
                fixture.SourceInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            50.0,
            fixture.Inventories.GetQuantity(
                fixture.DestinationInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            0.0,
            fixture.GetTruckCargo(truck));
        Assert.Equal(
            1,
            fixture.TransportSystem.Metrics.CompletedOrderCount);
        Assert.Equal(
            50.0,
            fixture.TransportSystem.Metrics.DeliveredQuantity);
    }

    [Fact]
    public void PartialLoadDeliversAvailableQuantityWithoutCreatingResources()
    {
        TransportFixture fixture = CreateFixture(
            sourceQuantity: 20.0);

        fixture.ConnectDirect();

        EntityId truck = fixture.CreateTruckAtSource();
        double before = fixture.TotalConservedQuantity(truck);

        Assert.True(
            fixture.TransportSystem.TryAssignOrder(
                fixture.Simulation.Entities,
                truck,
                new CargoTransportOrder(
                    fixture.SourceNode,
                    fixture.DestinationNode,
                    ResourceIds.FerrousOre,
                    requestedQuantity: 50.0,
                    SimulationTick.Zero,
                    CargoPartialLoadPolicy.AllowPartial),
                SimulationTick.Zero));

        RunUntilOrderCompletes(fixture, truck);

        Assert.Equal(
            before,
            fixture.TotalConservedQuantity(truck));
        Assert.Equal(
            0.0,
            fixture.Inventories.GetQuantity(
                fixture.SourceInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            20.0,
            fixture.Inventories.GetQuantity(
                fixture.DestinationInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            20.0,
            fixture.TransportSystem.Metrics.DeliveredQuantity);
    }

    [Fact]
    public void RequireRequestedQuantityWaitsForOriginInventoryToRecover()
    {
        TransportFixture fixture = CreateFixture(
            sourceQuantity: 20.0);

        fixture.ConnectDirect();

        EntityId truck = fixture.CreateTruckAtSource();

        Assert.True(
            fixture.TransportSystem.TryAssignOrder(
                fixture.Simulation.Entities,
                truck,
                new CargoTransportOrder(
                    fixture.SourceNode,
                    fixture.DestinationNode,
                    ResourceIds.FerrousOre,
                    requestedQuantity: 30.0,
                    SimulationTick.Zero,
                    CargoPartialLoadPolicy.RequireRequestedQuantity),
                SimulationTick.Zero));

        RunUntil(
            fixture,
            () =>
                fixture.Simulation.Entities.TryGetComponent(
                    truck,
                    out CargoTransportRuntimeState state) &&
                state.Lifecycle ==
                    CargoTransportLifecycleState.Waiting &&
                state.WaitReason ==
                    CargoTransportWaitReason.OriginResourceUnavailable);

        Assert.Equal(
            0.0,
            fixture.GetTruckCargo(truck));
        Assert.True(
            fixture.Inventories.Add(
                fixture.SourceInventory,
                ResourceIds.FerrousOre,
                10.0).Succeeded);

        RunUntilOrderCompletes(fixture, truck);

        Assert.Equal(
            30.0,
            fixture.Inventories.GetQuantity(
                fixture.DestinationInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            0.0,
            fixture.GetTruckCargo(truck));
    }

    [Fact]
    public void DestinationCapacityBlocksUnloadUntilSpaceBecomesAvailable()
    {
        TransportFixture fixture = CreateFixture(
            sourceQuantity: 50.0,
            destinationCapacity: 20.0);

        fixture.ConnectDirect();

        Assert.True(
            fixture.Inventories.Add(
                fixture.DestinationInventory,
                ResourceIds.FerrousOre,
                20.0).Succeeded);

        EntityId truck = fixture.CreateTruckAtSource();
        double before = fixture.TotalConservedQuantity(truck);

        Assert.True(
            fixture.TransportSystem.TryAssignOrder(
                fixture.Simulation.Entities,
                truck,
                new CargoTransportOrder(
                    fixture.SourceNode,
                    fixture.DestinationNode,
                    ResourceIds.FerrousOre,
                    requestedQuantity: 30.0,
                    SimulationTick.Zero),
                SimulationTick.Zero));

        RunUntil(
            fixture,
            () =>
                fixture.Simulation.Entities.TryGetComponent(
                    truck,
                    out CargoTransportRuntimeState state) &&
                state.Lifecycle ==
                    CargoTransportLifecycleState.Waiting &&
                state.WaitReason ==
                    CargoTransportWaitReason.DestinationCapacity);

        Assert.Equal(
            30.0,
            fixture.GetTruckCargo(truck));
        Assert.Equal(
            before,
            fixture.TotalConservedQuantity(truck));

        Assert.True(
            fixture.Inventories.Remove(
                fixture.DestinationInventory,
                ResourceIds.FerrousOre,
                20.0).Succeeded);

        before -= 20.0;

        RunUntil(
            fixture,
            () =>
                fixture.GetTruckCargo(truck) < 30.0);

        Assert.Equal(
            before,
            fixture.TotalConservedQuantity(truck));

        Assert.True(
            fixture.Inventories.Remove(
                fixture.DestinationInventory,
                ResourceIds.FerrousOre,
                20.0).Succeeded);

        before -= 20.0;

        RunUntilOrderCompletes(fixture, truck);

        Assert.Equal(
            before,
            fixture.TotalConservedQuantity(truck));
        Assert.Equal(
            10.0,
            fixture.Inventories.GetQuantity(
                fixture.DestinationInventory,
                ResourceIds.FerrousOre));
    }

    [Fact]
    public void InvalidatedRouteReturnsToLastAnchorAndUsesAlternateRoute()
    {
        TransportFixture fixture = CreateFixture(
            sourceQuantity: 40.0,
            sourcePosition: new Vector3(4.0f, 0.0f, 4.0f),
            destinationPosition: new Vector3(84.0f, 0.0f, 4.0f),
            worldChunkCountX: 3);

        LogisticsNodeId primary =
            fixture.AddIntermediateNode(
                new Vector3(36.0f, 0.0f, 4.0f));
        LogisticsNodeId alternate =
            fixture.AddIntermediateNode(
                new Vector3(36.0f, 0.0f, 20.0f));

        LogisticsEdgeId primaryEntry =
            fixture.Network.AddEdge(
                fixture.SourceNode,
                primary,
                LogisticsTransportMode.GroundRoad,
                distanceMeters: 32.0,
                baseCost: 10.0,
                capacityPerSecond: 100.0);
        fixture.Network.AddEdge(
            primary,
            fixture.DestinationNode,
            LogisticsTransportMode.GroundRoad,
            distanceMeters: 48.0,
            baseCost: 10.0,
            capacityPerSecond: 100.0);
        fixture.Network.AddEdge(
            fixture.SourceNode,
            alternate,
            LogisticsTransportMode.GroundRoad,
            distanceMeters: 36.0,
            baseCost: 30.0,
            capacityPerSecond: 100.0);
        fixture.Network.AddEdge(
            alternate,
            fixture.DestinationNode,
            LogisticsTransportMode.GroundRoad,
            distanceMeters: 52.0,
            baseCost: 30.0,
            capacityPerSecond: 100.0);

        EntityId truck = fixture.CreateTruckAtSource();

        Assert.True(
            fixture.TransportSystem.TryAssignOrder(
                fixture.Simulation.Entities,
                truck,
                new CargoTransportOrder(
                    fixture.SourceNode,
                    fixture.DestinationNode,
                    ResourceIds.FerrousOre,
                    requestedQuantity: 25.0,
                    SimulationTick.Zero),
                SimulationTick.Zero));

        RunUntil(
            fixture,
            () =>
                fixture.Simulation.Entities.TryGetComponent(
                    truck,
                    out CargoTransportRouteState routeState) &&
                routeState.Route is not null &&
                routeState.Route.Segments.Count == 2);

        CargoTransportRouteState initialRoute =
            fixture.Simulation.Entities.GetComponent<
                CargoTransportRouteState>(truck);
        Assert.Equal(
            primary,
            initialRoute.Route!.Segments[0].To);

        Assert.True(
            fixture.Network.SetEdgeEnabled(
                primaryEntry,
                enabled: false));

        RunUntil(
            fixture,
            () =>
                fixture.TransportSystem.Metrics.RerouteCount > 0);

        RunUntilOrderCompletes(
            fixture,
            truck,
            maximumTicks: 800);

        Assert.True(
            fixture.TransportSystem.Metrics.RerouteCount > 0);
        Assert.Equal(
            25.0,
            fixture.Inventories.GetQuantity(
                fixture.DestinationInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            0.0,
            fixture.GetTruckCargo(truck));
    }

    [Fact]
    public void DestroyedTransportDestroysCargoInventoryAndAccountsForLoss()
    {
        TransportFixture fixture = CreateFixture(
            sourceQuantity: 50.0);

        fixture.ConnectDirect();

        EntityId truck = fixture.CreateTruckAtSource();

        Assert.True(
            fixture.TransportSystem.TryAssignOrder(
                fixture.Simulation.Entities,
                truck,
                new CargoTransportOrder(
                    fixture.SourceNode,
                    fixture.DestinationNode,
                    ResourceIds.FerrousOre,
                    requestedQuantity: 30.0,
                    SimulationTick.Zero),
                SimulationTick.Zero));

        RunUntil(
            fixture,
            () =>
                fixture.GetTruckCargo(truck) >= 30.0);

        CargoTransport transport =
            fixture.Simulation.Entities.GetComponent<
                CargoTransport>(truck);
        InventoryId cargoInventory =
            transport.CargoInventory;

        Assert.True(
            fixture.Simulation.Entities.DestroyEntity(
                truck));

        fixture.Simulation.AdvanceOneTick();

        Assert.False(
            fixture.Inventories.Contains(
                cargoInventory));
        Assert.Equal(
            30.0,
            fixture.TransportSystem.Metrics.LostQuantity);
        Assert.Equal(
            20.0,
            fixture.Inventories.GetQuantity(
                fixture.SourceInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            0.0,
            fixture.Inventories.GetQuantity(
                fixture.DestinationInventory,
                ResourceIds.FerrousOre));
    }

    [Fact]
    public void HundredsOfTransportsCompleteWithoutResourceDrift()
    {
        const int transportCount = 200;
        const double quantityPerTransport = 10.0;

        TransportFixture fixture = CreateFixture(
            sourceQuantity:
                transportCount * quantityPerTransport,
            sourceCapacity:
                transportCount * quantityPerTransport + 100.0,
            destinationCapacity:
                transportCount * quantityPerTransport + 100.0,
            destinationPosition:
                new Vector3(28.0f, 0.0f, 4.0f));

        fixture.ConnectDirect();

        var trucks =
            new EntityId[transportCount];

        for (int index = 0;
             index < transportCount;
             index++)
        {
            EntityId truck =
                fixture.CreateTruckAtSource();
            trucks[index] = truck;

            Assert.True(
                fixture.TransportSystem.TryAssignOrder(
                    fixture.Simulation.Entities,
                    truck,
                    new CargoTransportOrder(
                        fixture.SourceNode,
                        fixture.DestinationNode,
                        ResourceIds.FerrousOre,
                        quantityPerTransport,
                        SimulationTick.Zero),
                    SimulationTick.Zero));
        }

        double before =
            fixture.TotalConservedQuantity(
                trucks);

        RunUntil(
            fixture,
            () =>
                fixture.TransportSystem.Metrics.CompletedOrderCount ==
                transportCount,
            maximumTicks: 600);

        double after =
            fixture.TotalConservedQuantity(
                trucks);

        Assert.Equal(before, after);
        Assert.Equal(
            transportCount * quantityPerTransport,
            fixture.Inventories.GetQuantity(
                fixture.DestinationInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            0,
            fixture.TransportSystem.Metrics.ActiveTransportCount);
        Assert.Equal(
            transportCount,
            fixture.TransportSystem.Metrics.CompletedOrderCount);
    }

    private static TransportFixture CreateFixture(
        double sourceQuantity,
        double sourceCapacity = 500.0,
        double destinationCapacity = 500.0,
        Vector3? sourcePosition = null,
        Vector3? destinationPosition = null,
        int worldChunkCountX = 2)
    {
        TerrainWorld terrain =
            CreateFlatWorld(
                worldChunkCountX);

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

        var simulation =
            new SimulationCoordinator(
                ticksPerSecond: 20);
        var inventories =
            new InventoryStore();
        var network =
            new LogisticsNetwork();
        var transportSystem =
            new CargoTransportSystem(
                network,
                inventories);

        simulation.RegisterSystem(
            new HierarchicalNavigationSystem(
                new HierarchicalPathfinder(
                    navigationWorld)));
        simulation.RegisterSystem(
            new GroundMovementSystem(
                terrain));
        simulation.RegisterSystem(
            transportSystem);

        Vector3 resolvedSourcePosition =
            sourcePosition ??
            new Vector3(
                4.0f,
                0.0f,
                4.0f);
        Vector3 resolvedDestinationPosition =
            destinationPosition ??
            new Vector3(
                52.0f,
                0.0f,
                4.0f);

        InventoryId sourceInventory =
            inventories.CreateInventory(
                new InventorySpecification(
                    sourceCapacity));
        InventoryId destinationInventory =
            inventories.CreateInventory(
                new InventorySpecification(
                    destinationCapacity));

        Assert.True(
            inventories.Add(
                sourceInventory,
                ResourceIds.FerrousOre,
                sourceQuantity).Succeeded);

        EntityId sourceEntity =
            CreateInventoryEntity(
                simulation,
                sourceInventory,
                resolvedSourcePosition);
        EntityId destinationEntity =
            CreateInventoryEntity(
                simulation,
                destinationInventory,
                resolvedDestinationPosition);

        LogisticsNodeId sourceNode =
            network.AddNode(
                sourceEntity,
                resolvedSourcePosition,
                LogisticsNodeKind.StorageDepot,
                LogisticsNodeCapabilities.CargoSource |
                LogisticsNodeCapabilities.CargoDestination |
                LogisticsNodeCapabilities.Storage |
                LogisticsNodeCapabilities.Distribution);
        LogisticsNodeId destinationNode =
            network.AddNode(
                destinationEntity,
                resolvedDestinationPosition,
                LogisticsNodeKind.StorageDepot,
                LogisticsNodeCapabilities.CargoSource |
                LogisticsNodeCapabilities.CargoDestination |
                LogisticsNodeCapabilities.Storage |
                LogisticsNodeCapabilities.Distribution);

        return new TransportFixture(
            terrain,
            simulation,
            inventories,
            network,
            transportSystem,
            sourceInventory,
            destinationInventory,
            sourceNode,
            destinationNode,
            resolvedSourcePosition);
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

    private static void RunUntilOrderCompletes(
        TransportFixture fixture,
        EntityId truck,
        int maximumTicks = 500)
    {
        RunUntil(
            fixture,
            () =>
                !fixture.Simulation.Entities.IsAlive(
                    truck) ||
                !fixture.Simulation.Entities.HasComponent<
                    CargoTransportOrder>(truck),
            maximumTicks);

        Assert.True(
            fixture.Simulation.Entities.IsAlive(
                truck));
        Assert.False(
            fixture.Simulation.Entities.HasComponent<
                CargoTransportOrder>(truck));

        CargoTransportRuntimeState state =
            fixture.Simulation.Entities.GetComponent<
                CargoTransportRuntimeState>(truck);
        Assert.Equal(
            CargoTransportLifecycleState.Idle,
            state.Lifecycle);
    }

    private static void RunUntil(
        TransportFixture fixture,
        Func<bool> condition,
        int maximumTicks = 500)
    {
        for (int tick = 0;
             tick < maximumTicks && !condition();
             tick++)
        {
            fixture.Simulation.AdvanceOneTick();
        }

        Assert.True(
            condition(),
            $"Condition was not reached within {maximumTicks} simulation ticks.");
    }

    private static TerrainWorld CreateFlatWorld(
        int chunkCountX)
    {
        var settings =
            new WorldGridSettings
            {
                ChunkSizeMeters = 32.0f,
                HeightSamplesPerSide = 9
            };

        var chunks =
            new List<TerrainChunk>(
                chunkCountX);

        for (int x = 0;
             x < chunkCountX;
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

    private sealed class TransportFixture
    {
        public TransportFixture(
            TerrainWorld terrain,
            SimulationCoordinator simulation,
            InventoryStore inventories,
            LogisticsNetwork network,
            CargoTransportSystem transportSystem,
            InventoryId sourceInventory,
            InventoryId destinationInventory,
            LogisticsNodeId sourceNode,
            LogisticsNodeId destinationNode,
            Vector3 sourcePosition)
        {
            Terrain = terrain;
            Simulation = simulation;
            Inventories = inventories;
            Network = network;
            TransportSystem = transportSystem;
            SourceInventory = sourceInventory;
            DestinationInventory = destinationInventory;
            SourceNode = sourceNode;
            DestinationNode = destinationNode;
            SourcePosition = sourcePosition;
        }

        public TerrainWorld Terrain { get; }

        public SimulationCoordinator Simulation { get; }

        public InventoryStore Inventories { get; }

        public LogisticsNetwork Network { get; }

        public CargoTransportSystem TransportSystem { get; }

        public InventoryId SourceInventory { get; }

        public InventoryId DestinationInventory { get; }

        public LogisticsNodeId SourceNode { get; }

        public LogisticsNodeId DestinationNode { get; }

        public Vector3 SourcePosition { get; }

        public void ConnectDirect()
        {
            LogisticsNode source =
                GetNode(
                    SourceNode);
            LogisticsNode destination =
                GetNode(
                    DestinationNode);

            double distance =
                Vector3.Distance(
                    source.WorldPosition,
                    destination.WorldPosition);

            Network.AddEdge(
                SourceNode,
                DestinationNode,
                LogisticsTransportMode.GroundRoad,
                distanceMeters: distance,
                baseCost: distance,
                capacityPerSecond: 100.0);
        }

        public LogisticsNodeId AddIntermediateNode(
            Vector3 position)
        {
            EntityId entity =
                Simulation.Entities.CreateEntity();
            Simulation.Entities.AddComponent(
                entity,
                new WorldTransform(
                    position,
                    Quaternion.Identity,
                    Vector3.One));

            return Network.AddNode(
                entity,
                position,
                LogisticsNodeKind.LogisticsHub,
                LogisticsNodeCapabilities.Distribution);
        }

        public EntityId CreateTruckAtSource()
        {
            return CargoTruckFactory.Create(
                Simulation.Entities,
                Inventories,
                SourcePosition,
                LocalPlayer,
                transportSystem:
                    TransportSystem);
        }

        public double GetTruckCargo(
            EntityId truck)
        {
            if (!Simulation.Entities.IsAlive(truck) ||
                !Simulation.Entities.TryGetComponent(
                    truck,
                    out CargoTransport transport) ||
                !Inventories.Contains(
                    transport.CargoInventory))
            {
                return 0.0;
            }

            return Inventories.GetTotalQuantity(
                transport.CargoInventory);
        }

        public double TotalConservedQuantity(
            params EntityId[] trucks)
        {
            double total =
                Inventories.GetTotalQuantity(
                    SourceInventory) +
                Inventories.GetTotalQuantity(
                    DestinationInventory);

            for (int index = 0;
                 index < trucks.Length;
                 index++)
            {
                total += GetTruckCargo(
                    trucks[index]);
            }

            return total;
        }

        private LogisticsNode GetNode(
            LogisticsNodeId id)
        {
            Assert.True(
                Network.TryGetNode(
                    id,
                    out LogisticsNode node));
            return node;
        }
    }
}
