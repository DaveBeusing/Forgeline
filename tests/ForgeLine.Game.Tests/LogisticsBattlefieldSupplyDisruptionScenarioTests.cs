using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class LogisticsBattlefieldSupplyDisruptionScenarioTests
{
    private static readonly PlayerId LocalPlayer = new(1);
    private static readonly FactionId LocalFaction = new(1);

    [Fact]
    public void DisconnectedSupplyDepotLeavesUnitUnsuppliedAndRestoreRecoversReadiness()
    {
        TerrainWorld terrain = CreateFlatWorld();
        NavigationWorld navigation =
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
        var inventories = new InventoryStore();
        var network = new LogisticsNetwork();
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
        var disruption =
            new LogisticsDisruptionSystem(network);
        var battlefieldSupply =
            new BattlefieldSupplySystem(inventories);

        simulation.RegisterSystem(disruption);
        simulation.RegisterSystem(
            new HierarchicalNavigationSystem(
                new HierarchicalPathfinder(navigation)));
        simulation.RegisterSystem(
            new GroundMovementSystem(terrain));
        simulation.RegisterSystem(battlefieldSupply);
        simulation.RegisterSystem(distribution);
        simulation.RegisterSystem(cargo);

        InventoryId sourceInventory =
            inventories.CreateInventory(
                new InventorySpecification(500.0));
        InventoryId depotInventory =
            inventories.CreateInventory(
                new InventorySpecification(500.0));

        Assert.True(
            inventories.Add(
                sourceInventory,
                ResourceIds.Fuel,
                120.0).Succeeded);
        Assert.True(
            inventories.Add(
                sourceInventory,
                ResourceIds.Ammunition,
                120.0).Succeeded);

        Vector3 sourcePosition =
            new(4.0f, 0.0f, 4.0f);
        Vector3 depotPosition =
            new(52.0f, 0.0f, 4.0f);
        Vector3 unitPosition =
            new(56.0f, 0.0f, 4.0f);

        EntityId source =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            source,
            new WorldTransform(
                sourcePosition,
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            source,
            new InventoryStorage(sourceInventory));
        simulation.Entities.AddComponent(
            source,
            new StorageDepot(
                sourceInventory,
                LocalFaction));

        EntityId depot =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            depot,
            new WorldTransform(
                depotPosition,
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            depot,
            new InventoryStorage(depotInventory));
        simulation.Entities.AddComponent(
            depot,
            new SupplyDepot(
                depotInventory,
                LocalPlayer));
        simulation.Entities.AddComponent(
            depot,
            new SupplyProvider(
                depotInventory,
                LocalPlayer,
                resupplyRangeMeters: 12.0f));

        LogisticsNodeId sourceNode =
            network.AddNode(
                source,
                sourcePosition,
                LogisticsNodeKind.StorageDepot,
                StandardCapabilities);
        LogisticsNodeId depotNode =
            network.AddNode(
                depot,
                depotPosition,
                LogisticsNodeKind.SupplyDepot,
                StandardCapabilities);
        LogisticsEdgeId criticalEdge =
            network.AddEdge(
                sourceNode,
                depotNode,
                LogisticsTransportMode.GroundRoad,
                distanceMeters:
                    Vector3.Distance(
                        sourcePosition,
                        depotPosition),
                baseCost: 1.0,
                capacityPerSecond: 100.0);

        AddPolicy(
            simulation,
            depot,
            ResourceIds.Fuel);
        AddPolicy(
            simulation,
            depot,
            ResourceIds.Ammunition);

        EntityId cargoTruck =
            CargoTruckFactory.Create(
                simulation.Entities,
                inventories,
                sourcePosition,
                LocalPlayer,
                cargo);

        EntityId unit =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            unit,
            new WorldTransform(
                unitPosition,
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            unit,
            new ControllableEntity(
                LocalPlayer,
                ControllableEntityCategory.Unit));

        InventoryId unitInventory =
            BattlefieldSupplyFactory.AttachUnitSupply(
                simulation.Entities,
                inventories,
                unit,
                fuelCapacity: 20.0,
                ammunitionCapacity: 20.0,
                fuelConsumptionPerMeter: 0.0,
                initialFuel: 0.0,
                initialAmmunition: 0.0);

        var disable =
            new SetLogisticsEdgeAvailabilityCommand(
                criticalEdge,
                enabled: false,
                SimulationTick.Zero);
        simulation.SubmitCommand(
            disable,
            new SimulationTick(1));

        simulation.AdvanceOneTick();
        simulation.RunTicks(
            40,
            TestContext.Current.CancellationToken);

        Assert.True(disable.Accepted);
        Assert.Equal(
            BattlefieldSupplyStatus.Unsupplied,
            simulation.Entities.GetComponent<UnitSupplyState>(
                unit).Status);
        Assert.Equal(
            0.0,
            inventories.GetQuantity(
                depotInventory,
                ResourceIds.Fuel));
        Assert.Equal(
            0.0,
            inventories.GetQuantity(
                depotInventory,
                ResourceIds.Ammunition));
        Assert.True(
            distribution.Metrics.BacklogRequestCount >= 2);
        Assert.Contains(
            distribution.LastDebugSnapshot.Requests,
            request =>
                request.BottleneckReason ==
                LogisticsBottleneckReason.DisconnectedRoute);

        var restore =
            new SetLogisticsEdgeAvailabilityCommand(
                criticalEdge,
                enabled: true,
                simulation.CurrentTick);
        simulation.SubmitCommand(
            restore,
            simulation.CurrentTick.Next());

        RunUntil(
            simulation,
            () =>
                simulation.Entities.GetComponent<UnitSupplyState>(
                    unit).Status ==
                    BattlefieldSupplyStatus.Supplied &&
                distribution.Metrics.CompletedRequestCount >= 2,
            maximumTicks: 2_000);

        Assert.True(restore.Accepted);
        Assert.Equal(
            BattlefieldSupplyStatus.Supplied,
            simulation.Entities.GetComponent<UnitSupplyState>(
                unit).Status);
        Assert.Equal(
            20.0,
            inventories.GetQuantity(
                unitInventory,
                ResourceIds.Fuel));
        Assert.Equal(
            20.0,
            inventories.GetQuantity(
                unitInventory,
                ResourceIds.Ammunition));
        Assert.True(
            inventories.GetQuantity(
                depotInventory,
                ResourceIds.Fuel) >= 40.0);
        Assert.True(
            inventories.GetQuantity(
                depotInventory,
                ResourceIds.Ammunition) >= 40.0);

        CargoTransport transport =
            simulation.Entities.GetComponent<CargoTransport>(
                cargoTruck);

        Assert.Equal(
            120.0,
            inventories.GetQuantity(
                sourceInventory,
                ResourceIds.Fuel) +
            inventories.GetQuantity(
                depotInventory,
                ResourceIds.Fuel) +
            inventories.GetQuantity(
                unitInventory,
                ResourceIds.Fuel) +
            inventories.GetQuantity(
                transport.CargoInventory,
                ResourceIds.Fuel),
            precision: 6);
        Assert.Equal(
            120.0,
            inventories.GetQuantity(
                sourceInventory,
                ResourceIds.Ammunition) +
            inventories.GetQuantity(
                depotInventory,
                ResourceIds.Ammunition) +
            inventories.GetQuantity(
                unitInventory,
                ResourceIds.Ammunition) +
            inventories.GetQuantity(
                transport.CargoInventory,
                ResourceIds.Ammunition),
            precision: 6);
    }

    private static void AddPolicy(
        SimulationCoordinator simulation,
        EntityId target,
        ResourceId resource)
    {
        EntityId policy =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            policy,
            new LogisticsStockPolicy(
                target,
                resource,
                desiredMinimum: 10.0,
                desiredTarget: 40.0,
                desiredMaximum: 100.0,
                LogisticsStockPriority.Critical));
    }

    private static void RunUntil(
        SimulationCoordinator simulation,
        Func<bool> condition,
        int maximumTicks)
    {
        for (int tick = 0;
             tick < maximumTicks && !condition();
             tick++)
        {
            simulation.AdvanceOneTick();
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

    private const LogisticsNodeCapabilities StandardCapabilities =
        LogisticsNodeCapabilities.CargoSource |
        LogisticsNodeCapabilities.CargoDestination |
        LogisticsNodeCapabilities.Storage |
        LogisticsNodeCapabilities.Distribution;
}
