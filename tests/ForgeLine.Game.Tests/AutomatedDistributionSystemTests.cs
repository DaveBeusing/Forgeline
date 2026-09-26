using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class AutomatedDistributionSystemTests
{
    private static readonly PlayerId LocalPlayer = new(1);
    private static readonly FactionId LocalFaction = new(1);

    [Fact]
    public void ReplenishesDestinationToConfiguredTargetThroughPhysicalTruck()
    {
        DistributionFixture fixture =
            CreateFixture(sourceQuantity: 100.0);

        fixture.Connect(
            fixture.SourceNode,
            fixture.DestinationNode);
        fixture.AddPolicy(
            fixture.DestinationEntity,
            minimum: 20.0,
            target: 60.0,
            maximum: 100.0,
            LogisticsStockPriority.Normal);
        EntityId truck =
            fixture.CreateTruck(fixture.SourcePosition);

        double conservedBefore =
            fixture.TotalConservedQuantity(truck);

        RunUntil(
            fixture,
            () =>
                fixture.Inventories.GetQuantity(
                    fixture.DestinationInventory,
                    ResourceIds.FerrousOre) >= 60.0 &&
                fixture.Distribution.Metrics.CompletedRequestCount >= 1,
            maximumTicks: 600);

        double conservedAfter =
            fixture.TotalConservedQuantity(truck);

        Assert.Equal(conservedBefore, conservedAfter);
        Assert.Equal(
            40.0,
            fixture.Inventories.GetQuantity(
                fixture.SourceInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            60.0,
            fixture.Inventories.GetQuantity(
                fixture.DestinationInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            0.0,
            fixture.Inventories.GetReservedQuantity(
                fixture.SourceInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            1L,
            fixture.Distribution.Metrics.CompletedRequestCount);
    }

    [Fact]
    public void ShipmentQuantityRespectsRouteThroughputWindow()
    {
        DistributionFixture fixture =
            CreateFixture(sourceQuantity: 100.0);

        fixture.Connect(
            fixture.SourceNode,
            fixture.DestinationNode,
            capacityPerSecond: 40.0);
        fixture.AddPolicy(
            fixture.DestinationEntity,
            minimum: 20.0,
            target: 80.0,
            maximum: 100.0,
            LogisticsStockPriority.Normal);
        _ = fixture.CreateTruck(
            fixture.SourcePosition);

        RunUntil(
            fixture,
            () =>
                fixture.Inventories.GetQuantity(
                    fixture.DestinationInventory,
                    ResourceIds.FerrousOre) >= 80.0 &&
                fixture.Distribution.Metrics.CompletedRequestCount >= 1,
            maximumTicks: 1_200);

        Assert.Equal(
            80.0,
            fixture.Inventories.GetQuantity(
                fixture.DestinationInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            20.0,
            fixture.Inventories.GetQuantity(
                fixture.SourceInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            1L,
            fixture.Distribution.Metrics.CompletedRequestCount);
    }

    [Fact]
    public void ResupplyingTruckIsNotDispatchedForCargo()
    {
        DistributionFixture fixture =
            CreateFixture(sourceQuantity: 100.0);

        fixture.Connect(
            fixture.SourceNode,
            fixture.DestinationNode);
        fixture.AddPolicy(
            fixture.DestinationEntity,
            minimum: 20.0,
            target: 60.0,
            maximum: 100.0,
            LogisticsStockPriority.Normal);
        EntityId truck =
            fixture.CreateTruck(fixture.SourcePosition);

        fixture.Simulation.Entities.AddComponent(
            truck,
            new ResupplyOrder(
                fixture.DestinationEntity,
                SimulationTick.Zero,
                SimulationTick.Zero));

        fixture.Simulation.AdvanceOneTick();

        LogisticsTransportRequestReadModel request =
            Assert.Single(
                fixture.Distribution.LastDebugSnapshot.Requests);

        Assert.Equal(
            LogisticsTransportRequestState.RetryPending,
            request.State);
        Assert.Equal(
            LogisticsTransportRequestFailureReason.NoTruckAvailable,
            request.FailureReason);
        Assert.False(
            fixture.Simulation.Entities.HasComponent<CargoTransportOrder>(
                truck));
    }

    [Fact]
    public void DoesNotSourceCargoFromAnotherFaction()
    {
        DistributionFixture fixture =
            CreateFixture(sourceQuantity: 0.0);

        EntityId foreignSource =
            fixture.CreateStorageNode(
                new Vector3(28.0f, 0.0f, 4.0f),
                new FactionId(2),
                out InventoryId foreignInventory,
                out LogisticsNodeId foreignNode);
        _ = foreignSource;

        Assert.True(
            fixture.Inventories.Add(
                foreignInventory,
                ResourceIds.FerrousOre,
                100.0).Succeeded);

        fixture.Connect(
            foreignNode,
            fixture.DestinationNode);
        fixture.AddPolicy(
            fixture.DestinationEntity,
            minimum: 20.0,
            target: 60.0,
            maximum: 100.0,
            LogisticsStockPriority.Normal);
        _ = fixture.CreateTruck(
            fixture.SourcePosition);

        fixture.Simulation.RunTicks(
            200,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            0.0,
            fixture.Inventories.GetQuantity(
                fixture.DestinationInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            100.0,
            fixture.Inventories.GetQuantity(
                foreignInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            0L,
            fixture.Distribution.Metrics.CompletedRequestCount);
    }

    [Fact]
    public void TargetLargerThanTruckCapacityUsesMultiplePhysicalShipments()
    {
        DistributionFixture fixture =
            CreateFixture(sourceQuantity: 300.0);

        fixture.Connect(
            fixture.SourceNode,
            fixture.DestinationNode);
        fixture.AddPolicy(
            fixture.DestinationEntity,
            minimum: 20.0,
            target: 220.0,
            maximum: 300.0,
            LogisticsStockPriority.Normal);
        EntityId truck =
            fixture.CreateTruck(fixture.SourcePosition);

        double conservedBefore =
            fixture.TotalConservedQuantity(truck);

        RunUntil(
            fixture,
            () =>
                fixture.Inventories.GetQuantity(
                    fixture.DestinationInventory,
                    ResourceIds.FerrousOre) >= 220.0 &&
                fixture.Distribution.Metrics.CompletedRequestCount >= 1,
            maximumTicks: 1_800);

        Assert.Equal(
            conservedBefore,
            fixture.TotalConservedQuantity(truck));
        Assert.Equal(
            220.0,
            fixture.Inventories.GetQuantity(
                fixture.DestinationInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            80.0,
            fixture.Inventories.GetQuantity(
                fixture.SourceInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            1L,
            fixture.Distribution.Metrics.CompletedRequestCount);
    }

    [Fact]
    public void RepeatedDeficitDetectionCoalescesWithoutDuplicateRequests()
    {
        DistributionFixture fixture =
            CreateFixture(sourceQuantity: 100.0);

        fixture.Connect(
            fixture.SourceNode,
            fixture.DestinationNode);
        fixture.AddPolicy(
            fixture.DestinationEntity,
            minimum: 20.0,
            target: 60.0,
            maximum: 100.0,
            LogisticsStockPriority.Normal);

        fixture.Simulation.RunTicks(
            100,
            TestContext.Current.CancellationToken);

        Assert.Single(
            fixture.Distribution.LastDebugSnapshot.Requests);
        Assert.Equal(
            LogisticsTransportRequestState.RetryPending,
            fixture.Distribution.LastDebugSnapshot.Requests[0].State);
        Assert.Equal(
            LogisticsTransportRequestFailureReason.NoTruckAvailable,
            fixture.Distribution.LastDebugSnapshot.Requests[0].FailureReason);
        Assert.Equal(
            0.0,
            fixture.Inventories.GetReservedQuantity(
                fixture.SourceInventory,
                ResourceIds.FerrousOre));
    }

    [Fact]
    public void DestroyedAssignedTruckReleasesSourceReservation()
    {
        DistributionFixture fixture =
            CreateFixture(
                sourceQuantity: 100.0,
                destinationPosition:
                    new Vector3(52.0f, 0.0f, 4.0f));

        fixture.Connect(
            fixture.SourceNode,
            fixture.DestinationNode);
        fixture.AddPolicy(
            fixture.DestinationEntity,
            minimum: 20.0,
            target: 60.0,
            maximum: 100.0,
            LogisticsStockPriority.Normal);

        EntityId truck =
            fixture.CreateTruck(
                new Vector3(52.0f, 0.0f, 24.0f));

        fixture.Simulation.AdvanceOneTick();

        Assert.Equal(
            60.0,
            fixture.Inventories.GetReservedQuantity(
                fixture.SourceInventory,
                ResourceIds.FerrousOre));
        Assert.True(
            fixture.Simulation.Entities.DestroyEntity(truck));

        fixture.Simulation.AdvanceOneTick();

        Assert.Equal(
            0.0,
            fixture.Inventories.GetReservedQuantity(
                fixture.SourceInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            LogisticsTransportRequestState.RetryPending,
            fixture.Distribution.LastDebugSnapshot.Requests[0].State);
    }

    [Fact]
    public void HigherPriorityDeficitReceivesSingleAvailableTruckFirst()
    {
        DistributionFixture fixture =
            CreateFixture(sourceQuantity: 200.0);

        EntityId lowDestination =
            fixture.CreateStorageNode(
                new Vector3(52.0f, 0.0f, 20.0f),
                out InventoryId lowInventory,
                out LogisticsNodeId lowNode);

        fixture.Connect(
            fixture.SourceNode,
            fixture.DestinationNode);
        fixture.Connect(
            fixture.SourceNode,
            lowNode);

        fixture.AddPolicy(
            fixture.DestinationEntity,
            minimum: 10.0,
            target: 40.0,
            maximum: 80.0,
            LogisticsStockPriority.Critical);
        fixture.AddPolicy(
            lowDestination,
            minimum: 10.0,
            target: 40.0,
            maximum: 80.0,
            LogisticsStockPriority.Low);

        _ = lowInventory;
        fixture.CreateTruck(fixture.SourcePosition);

        fixture.Simulation.AdvanceOneTick();

        LogisticsTransportRequestReadModel critical =
            fixture.Distribution.LastDebugSnapshot.Requests
                .Single(
                    request =>
                        request.Destination ==
                        fixture.DestinationNode);
        LogisticsTransportRequestReadModel low =
            fixture.Distribution.LastDebugSnapshot.Requests
                .Single(
                    request =>
                        request.Destination ==
                        lowNode);

        Assert.Equal(
            LogisticsTransportRequestState.Assigned,
            critical.State);
        Assert.Equal(
            LogisticsTransportRequestState.RetryPending,
            low.State);
        Assert.Equal(
            LogisticsTransportRequestFailureReason.NoTruckAvailable,
            low.FailureReason);
    }

    [Fact]
    public void AgingAllowsOlderLowPriorityDeficitToCompeteWithNewCriticalDemand()
    {
        DistributionFixture fixture =
            CreateFixture(sourceQuantity: 200.0);

        EntityId lowDestination =
            fixture.CreateStorageNode(
                new Vector3(52.0f, 0.0f, 20.0f),
                out _,
                out LogisticsNodeId lowNode);
        fixture.Connect(
            fixture.SourceNode,
            lowNode);
        fixture.AddPolicy(
            lowDestination,
            minimum: 10.0,
            target: 40.0,
            maximum: 80.0,
            LogisticsStockPriority.Low);

        fixture.Simulation.RunTicks(
            60,
            TestContext.Current.CancellationToken);

        fixture.Connect(
            fixture.SourceNode,
            fixture.DestinationNode);
        fixture.AddPolicy(
            fixture.DestinationEntity,
            minimum: 10.0,
            target: 40.0,
            maximum: 80.0,
            LogisticsStockPriority.Critical);
        fixture.CreateTruck(fixture.SourcePosition);

        fixture.Simulation.AdvanceOneTick();

        LogisticsTransportRequestReadModel low =
            fixture.Distribution.LastDebugSnapshot.Requests
                .Single(
                    request =>
                        request.Destination == lowNode);
        LogisticsTransportRequestReadModel critical =
            fixture.Distribution.LastDebugSnapshot.Requests
                .Single(
                    request =>
                        request.Destination ==
                        fixture.DestinationNode);

        Assert.Equal(
            LogisticsTransportRequestState.Assigned,
            low.State);
        Assert.Equal(
            LogisticsTransportRequestState.RetryPending,
            critical.State);
        Assert.Equal(
            LogisticsTransportRequestFailureReason.NoTruckAvailable,
            critical.FailureReason);
    }

    [Fact]
    public void MissingRouteRetriesWithoutReservingStock()
    {
        DistributionFixture fixture =
            CreateFixture(sourceQuantity: 100.0);

        fixture.AddPolicy(
            fixture.DestinationEntity,
            minimum: 20.0,
            target: 60.0,
            maximum: 100.0,
            LogisticsStockPriority.Normal);
        fixture.CreateTruck(fixture.SourcePosition);

        fixture.Simulation.AdvanceOneTick();

        LogisticsTransportRequestReadModel request =
            Assert.Single(
                fixture.Distribution.LastDebugSnapshot.Requests);

        Assert.Equal(
            LogisticsTransportRequestState.RetryPending,
            request.State);
        Assert.Equal(
            LogisticsTransportRequestFailureReason.NoRoute,
            request.FailureReason);
        Assert.Equal(
            0.0,
            fixture.Inventories.GetReservedQuantity(
                fixture.SourceInventory,
                ResourceIds.FerrousOre));
    }

    [Fact]
    public void SaturatedRouteCreatesBacklogAndRecoversAfterCapacityWindow()
    {
        DistributionFixture fixture =
            CreateFixture(sourceQuantity: 200.0);

        fixture.Connect(
            fixture.SourceNode,
            fixture.DestinationNode);
        fixture.AddPolicy(
            fixture.DestinationEntity,
            minimum: 20.0,
            target: 60.0,
            maximum: 100.0,
            LogisticsStockPriority.Normal);
        fixture.CreateTruck(fixture.SourcePosition);

        LogisticsRoute route =
            fixture.Network.FindRoute(
                fixture.SourceNode,
                fixture.DestinationNode).Route!;

        Assert.True(
            fixture.Distribution.CapacityTracker.TryReserveRoute(
                fixture.Network,
                route,
                quantity: 80.0,
                SimulationTick.Zero,
                out _,
                out _));

        fixture.Simulation.AdvanceOneTick();

        LogisticsTransportRequestReadModel blocked =
            Assert.Single(
                fixture.Distribution.LastDebugSnapshot.Requests);

        Assert.Equal(
            LogisticsTransportRequestState.RetryPending,
            blocked.State);
        Assert.Equal(
            LogisticsTransportRequestFailureReason.CapacitySaturated,
            blocked.FailureReason);
        Assert.Equal(
            LogisticsBottleneckReason.SaturatedLinkOrHub,
            blocked.BottleneckReason);
        Assert.Equal(
            1,
            fixture.Distribution.Metrics.BacklogRequestCount);
        Assert.Equal(
            60.0,
            fixture.Distribution.Metrics.BacklogQuantity);
        Assert.Equal(
            1,
            fixture.Distribution.Metrics.CapacityBlockedRequestCount);

        RunUntil(
            fixture,
            () =>
                fixture.Inventories.GetQuantity(
                    fixture.DestinationInventory,
                    ResourceIds.FerrousOre) >= 60.0 &&
                fixture.Distribution.Metrics.CompletedRequestCount >= 1,
            maximumTicks: 700);

        Assert.Equal(
            60.0,
            fixture.Inventories.GetQuantity(
                fixture.DestinationInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            0,
            fixture.Distribution.Metrics.BacklogRequestCount);
    }

    [Fact]
    public void DisabledCriticalLinkBlocksDeliveryUntilRestored()
    {
        DistributionFixture fixture =
            CreateFixture(sourceQuantity: 100.0);

        fixture.Connect(
            fixture.SourceNode,
            fixture.DestinationNode);
        LogisticsEdgeId criticalEdge =
            Assert.Single(fixture.Network.GetEdges()).Id;

        Assert.True(
            fixture.Network.SetEdgeEnabled(
                criticalEdge,
                enabled: false));

        fixture.AddPolicy(
            fixture.DestinationEntity,
            minimum: 20.0,
            target: 60.0,
            maximum: 100.0,
            LogisticsStockPriority.High);
        fixture.CreateTruck(fixture.SourcePosition);

        fixture.Simulation.AdvanceOneTick();

        LogisticsTransportRequestReadModel disconnected =
            Assert.Single(
                fixture.Distribution.LastDebugSnapshot.Requests);

        Assert.Equal(
            LogisticsTransportRequestState.RetryPending,
            disconnected.State);
        Assert.Equal(
            LogisticsTransportRequestFailureReason.NoRoute,
            disconnected.FailureReason);
        Assert.Equal(
            LogisticsBottleneckReason.DisconnectedRoute,
            disconnected.BottleneckReason);
        Assert.Equal(
            0.0,
            fixture.Inventories.GetQuantity(
                fixture.DestinationInventory,
                ResourceIds.FerrousOre));

        Assert.True(
            fixture.Network.SetEdgeEnabled(
                criticalEdge,
                enabled: true));

        RunUntil(
            fixture,
            () =>
                fixture.Inventories.GetQuantity(
                    fixture.DestinationInventory,
                    ResourceIds.FerrousOre) >= 60.0 &&
                fixture.Distribution.Metrics.CompletedRequestCount >= 1,
            maximumTicks: 700);

        Assert.Equal(
            60.0,
            fixture.Inventories.GetQuantity(
                fixture.DestinationInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(1, fixture.Network.EdgeCount);
        Assert.Equal(
            0.0,
            fixture.Inventories.GetReservedQuantity(
                fixture.SourceInventory,
                ResourceIds.FerrousOre));
    }

    [Fact]
    public void SourceSelectionUsesAvailableCapacityAcrossRoutableSources()
    {
        DistributionFixture fixture =
            CreateFixture(sourceQuantity: 100.0);

        fixture.Connect(
            fixture.SourceNode,
            fixture.DestinationNode);

        _ = fixture.CreateStorageNode(
            new Vector3(4.0f, 0.0f, 28.0f),
            out InventoryId alternateInventory,
            out LogisticsNodeId alternateSource);

        Assert.True(
            fixture.Inventories.Add(
                alternateInventory,
                ResourceIds.FerrousOre,
                100.0).Succeeded);

        fixture.Connect(
            alternateSource,
            fixture.DestinationNode);

        LogisticsRoute primaryRoute =
            fixture.Network.FindRoute(
                fixture.SourceNode,
                fixture.DestinationNode).Route!;

        Assert.True(
            fixture.Distribution.CapacityTracker.TryReserveRoute(
                fixture.Network,
                primaryRoute,
                quantity: 80.0,
                SimulationTick.Zero,
                out _,
                out _));

        fixture.AddPolicy(
            fixture.DestinationEntity,
            minimum: 20.0,
            target: 60.0,
            maximum: 100.0,
            LogisticsStockPriority.High);
        fixture.CreateTruck(
            new Vector3(4.0f, 0.0f, 28.0f));

        fixture.Simulation.AdvanceOneTick();

        LogisticsTransportRequestReadModel request =
            Assert.Single(
                fixture.Distribution.LastDebugSnapshot.Requests);

        Assert.Equal(
            LogisticsTransportRequestState.Assigned,
            request.State);
        Assert.Equal(
            LogisticsTransportRequestFailureReason.None,
            request.FailureReason);
        Assert.Equal(
            LogisticsBottleneckReason.None,
            request.BottleneckReason);
        Assert.Equal(
            alternateSource,
            request.Origin);
        Assert.True(
            request.CapacityReservationId.IsSpecified);
    }

    private static DistributionFixture CreateFixture(
        double sourceQuantity,
        Vector3? sourcePosition = null,
        Vector3? destinationPosition = null)
    {
        TerrainWorld terrain =
            CreateFlatWorld();

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

        simulation.RegisterSystem(
            new HierarchicalNavigationSystem(
                new HierarchicalPathfinder(
                    navigationWorld)));
        simulation.RegisterSystem(
            new GroundMovementSystem(
                terrain));
        simulation.RegisterSystem(distribution);
        simulation.RegisterSystem(cargo);

        Vector3 source =
            sourcePosition ??
            new Vector3(4.0f, 0.0f, 4.0f);
        Vector3 destination =
            destinationPosition ??
            new Vector3(52.0f, 0.0f, 4.0f);

        EntityId sourceEntity =
            CreateStorageEntity(
                simulation,
                inventories,
                source,
                capacity: 1_000.0,
                out InventoryId sourceInventory);
        EntityId destinationEntity =
            CreateStorageEntity(
                simulation,
                inventories,
                destination,
                capacity: 1_000.0,
                out InventoryId destinationInventory);

        Assert.True(
            inventories.Add(
                sourceInventory,
                ResourceIds.FerrousOre,
                sourceQuantity).Succeeded);

        LogisticsNodeId sourceNode =
            network.AddNode(
                sourceEntity,
                source,
                LogisticsNodeKind.StorageDepot,
                LogisticsNodeCapabilities.CargoSource |
                LogisticsNodeCapabilities.CargoDestination |
                LogisticsNodeCapabilities.Storage |
                LogisticsNodeCapabilities.Distribution);
        LogisticsNodeId destinationNode =
            network.AddNode(
                destinationEntity,
                destination,
                LogisticsNodeKind.StorageDepot,
                LogisticsNodeCapabilities.CargoSource |
                LogisticsNodeCapabilities.CargoDestination |
                LogisticsNodeCapabilities.Storage |
                LogisticsNodeCapabilities.Distribution);

        return new DistributionFixture(
            simulation,
            inventories,
            network,
            cargo,
            distribution,
            sourceEntity,
            destinationEntity,
            sourceInventory,
            destinationInventory,
            sourceNode,
            destinationNode,
            source);
    }

    private static EntityId CreateStorageEntity(
        SimulationCoordinator simulation,
        InventoryStore inventories,
        Vector3 position,
        double capacity,
        out InventoryId inventoryId,
        FactionId owner = default)
    {
        inventoryId =
            inventories.CreateInventory(
                new InventorySpecification(capacity));

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
            new InventoryStorage(inventoryId));
        simulation.Entities.AddComponent(
            entity,
            new StorageDepot(
                inventoryId,
                owner.IsSpecified
                    ? owner
                    : LocalFaction));
        return entity;
    }

    private static void RunUntil(
        DistributionFixture fixture,
        Func<bool> condition,
        int maximumTicks)
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

        for (int x = 0; x < 3; x++)
        {
            int sampleCount =
                settings.HeightSamplesPerSide *
                settings.HeightSamplesPerSide;

            chunks.Add(
                new TerrainChunk(
                    new ChunkCoordinate(x, 0),
                    new TerrainHeightfield(
                        settings.HeightSamplesPerSide,
                        settings.ChunkSizeMeters,
                        new float[sampleCount])));
        }

        return new TerrainWorld(
            settings,
            chunks);
    }

    private sealed class DistributionFixture
    {
        public DistributionFixture(
            SimulationCoordinator simulation,
            InventoryStore inventories,
            LogisticsNetwork network,
            CargoTransportSystem cargo,
            AutomatedDistributionSystem distribution,
            EntityId sourceEntity,
            EntityId destinationEntity,
            InventoryId sourceInventory,
            InventoryId destinationInventory,
            LogisticsNodeId sourceNode,
            LogisticsNodeId destinationNode,
            Vector3 sourcePosition)
        {
            Simulation = simulation;
            Inventories = inventories;
            Network = network;
            Cargo = cargo;
            Distribution = distribution;
            SourceEntity = sourceEntity;
            DestinationEntity = destinationEntity;
            SourceInventory = sourceInventory;
            DestinationInventory = destinationInventory;
            SourceNode = sourceNode;
            DestinationNode = destinationNode;
            SourcePosition = sourcePosition;
        }

        public SimulationCoordinator Simulation { get; }

        public InventoryStore Inventories { get; }

        public LogisticsNetwork Network { get; }

        public CargoTransportSystem Cargo { get; }

        public AutomatedDistributionSystem Distribution { get; }

        public EntityId SourceEntity { get; }

        public EntityId DestinationEntity { get; }

        public InventoryId SourceInventory { get; }

        public InventoryId DestinationInventory { get; }

        public LogisticsNodeId SourceNode { get; }

        public LogisticsNodeId DestinationNode { get; }

        public Vector3 SourcePosition { get; }

        public void AddPolicy(
            EntityId destination,
            double minimum,
            double target,
            double maximum,
            LogisticsStockPriority priority)
        {
            EntityId policy =
                Simulation.Entities.CreateEntity();
            Simulation.Entities.AddComponent(
                policy,
                new LogisticsStockPolicy(
                    destination,
                    ResourceIds.FerrousOre,
                    minimum,
                    target,
                    maximum,
                    priority));
        }

        public EntityId CreateTruck(Vector3 position) =>
            CargoTruckFactory.Create(
                Simulation.Entities,
                Inventories,
                position,
                LocalPlayer,
                Cargo);

        public void Connect(
            LogisticsNodeId source,
            LogisticsNodeId destination,
            double capacityPerSecond = 100.0)
        {
            Assert.True(
                Network.TryGetNode(
                    source,
                    out LogisticsNode sourceNode));
            Assert.True(
                Network.TryGetNode(
                    destination,
                    out LogisticsNode destinationNode));

            double distance =
                Vector3.Distance(
                    sourceNode.WorldPosition,
                    destinationNode.WorldPosition);

            Network.AddEdge(
                source,
                destination,
                LogisticsTransportMode.GroundRoad,
                distanceMeters: distance,
                baseCost: distance,
                capacityPerSecond: capacityPerSecond);
        }

        public EntityId CreateStorageNode(
            Vector3 position,
            out InventoryId inventoryId,
            out LogisticsNodeId nodeId) =>
            CreateStorageNode(
                position,
                LocalFaction,
                out inventoryId,
                out nodeId);

        public EntityId CreateStorageNode(
            Vector3 position,
            FactionId owner,
            out InventoryId inventoryId,
            out LogisticsNodeId nodeId)
        {
            EntityId entity =
                CreateStorageEntity(
                    Simulation,
                    Inventories,
                    position,
                    1_000.0,
                    out inventoryId,
                    owner);

            nodeId =
                Network.AddNode(
                    entity,
                    position,
                    LogisticsNodeKind.StorageDepot,
                    LogisticsNodeCapabilities.CargoSource |
                    LogisticsNodeCapabilities.CargoDestination |
                    LogisticsNodeCapabilities.Storage |
                    LogisticsNodeCapabilities.Distribution);
            return entity;
        }

        public double TotalConservedQuantity(
            EntityId truck)
        {
            double total =
                Inventories.GetTotalQuantity(SourceInventory) +
                Inventories.GetTotalQuantity(DestinationInventory);

            if (Simulation.Entities.IsAlive(truck) &&
                Simulation.Entities.TryGetComponent(
                    truck,
                    out CargoTransport transport) &&
                Inventories.Contains(transport.CargoInventory))
            {
                total +=
                    Inventories.GetTotalQuantity(
                        transport.CargoInventory);
            }

            return total;
        }
    }
}
