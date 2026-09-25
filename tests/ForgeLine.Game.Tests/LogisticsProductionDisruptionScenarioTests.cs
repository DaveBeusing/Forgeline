using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class LogisticsProductionDisruptionScenarioTests
{
    private static readonly PowerNetworkId IndustrialPowerNetwork = new(1);

    [Fact]
    public void DisabledSupplyLinkStarvesFactoryAndRestoreRecoversProduction()
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
        var power =
            new PowerNetworkSystem();
        var production =
            new ProductionSystem(
                InitialProductionRecipes.CreateCatalog(),
                inventories);

        simulation.RegisterSystem(disruption);
        simulation.RegisterSystem(
            new HierarchicalNavigationSystem(
                new HierarchicalPathfinder(navigation)));
        simulation.RegisterSystem(
            new GroundMovementSystem(terrain));
        simulation.RegisterSystem(power);
        simulation.RegisterSystem(distribution);
        simulation.RegisterSystem(cargo);
        simulation.RegisterSystem(production);

        InventoryId sourceInventory =
            inventories.CreateInventory(
                new InventorySpecification(1_000.0));
        InventoryId factoryInput =
            inventories.CreateInventory(
                new InventorySpecification(1_000.0));
        InventoryId factoryOutput =
            inventories.CreateInventory(
                new InventorySpecification(1_000.0));

        Assert.True(
            inventories.Add(
                sourceInventory,
                ResourceIds.FerrousOre,
                100.0).Succeeded);

        Vector3 sourcePosition =
            new(4.0f, 0.0f, 4.0f);
        Vector3 factoryPosition =
            new(52.0f, 0.0f, 4.0f);

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
                new FactionId(1)));

        EntityId factory =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            factory,
            new WorldTransform(
                factoryPosition,
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            factory,
            new ProductionFacility(
                factoryInput,
                factoryOutput,
                ProductionCapability.SteelProcessing,
                SimulationTick.Zero));
        simulation.Entities.AddComponent(
            factory,
            new PowerNetworkMembership(
                IndustrialPowerNetwork));
        simulation.Entities.AddComponent(
            factory,
            new PowerConsumer(
                demand: 10.0,
                PowerPriority.Industrial));

        EntityId generator =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            generator,
            new PowerNetworkMembership(
                IndustrialPowerNetwork));
        simulation.Entities.AddComponent(
            generator,
            new PowerGenerator(1_000.0));

        LogisticsNodeId sourceNode =
            network.AddNode(
                source,
                sourcePosition,
                LogisticsNodeKind.StorageDepot,
                LogisticsNodeCapabilities.CargoSource |
                LogisticsNodeCapabilities.CargoDestination |
                LogisticsNodeCapabilities.Storage |
                LogisticsNodeCapabilities.Distribution);
        LogisticsNodeId factoryNode =
            network.AddNode(
                factory,
                factoryPosition,
                LogisticsNodeKind.ProcessingFacility,
                LogisticsNodeCapabilities.CargoSource |
                LogisticsNodeCapabilities.CargoDestination |
                LogisticsNodeCapabilities.Processing |
                LogisticsNodeCapabilities.Distribution);
        LogisticsEdgeId criticalEdge =
            network.AddEdge(
                sourceNode,
                factoryNode,
                LogisticsTransportMode.GroundRoad,
                distanceMeters:
                    Vector3.Distance(
                        sourcePosition,
                        factoryPosition),
                baseCost: 1.0,
                capacityPerSecond: 100.0);

        EntityId stockPolicy =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            stockPolicy,
            new LogisticsStockPolicy(
                factory,
                ResourceIds.FerrousOre,
                desiredMinimum: 10.0,
                desiredTarget: 20.0,
                desiredMaximum: 100.0,
                LogisticsStockPriority.High));

        _ = CargoTruckFactory.Create(
            simulation.Entities,
            inventories,
            sourcePosition,
            new PlayerId(1),
            cargo);

        var productionCommand =
            new QueueProductionCommand(
                factory,
                RecipeIds.Steel,
                SimulationTick.Zero,
                ProductionPriority.High,
                ProductionRequestMode.OneShot);
        simulation.SubmitCommand(
            productionCommand,
            new SimulationTick(1));

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
            20,
            TestContext.Current.CancellationToken);

        ProductionFacility starved =
            simulation.Entities.GetComponent<
                ProductionFacility>(factory);

        Assert.True(productionCommand.Accepted);
        Assert.True(disable.Accepted);
        Assert.Equal(
            ProductionStatus.NoInput,
            starved.Status);
        Assert.Equal(
            ProductionBlockReason.NoInput,
            starved.BlockReason);
        Assert.Equal(
            0.0,
            inventories.GetQuantity(
                factoryInput,
                ResourceIds.FerrousOre));
        Assert.Equal(
            0.0,
            inventories.GetQuantity(
                factoryOutput,
                ResourceIds.Steel));
        Assert.True(
            distribution.Metrics.BacklogRequestCount >= 1);

        SimulationTick restoreTick =
            simulation.CurrentTick.Next();
        var restore =
            new SetLogisticsEdgeAvailabilityCommand(
                criticalEdge,
                enabled: true,
                simulation.CurrentTick);
        simulation.SubmitCommand(
            restore,
            restoreTick);

        RunUntil(
            simulation,
            () =>
                inventories.GetQuantity(
                    factoryOutput,
                    ResourceIds.Steel) >= 10.0,
            maximumTicks: 900);

        ProductionFacility recovered =
            simulation.Entities.GetComponent<
                ProductionFacility>(factory);

        Assert.True(restore.Accepted);
        Assert.Equal(1UL, recovered.CompletedCycles);
        Assert.Equal(
            10.0,
            inventories.GetQuantity(
                factoryOutput,
                ResourceIds.Steel));
        Assert.True(
            inventories.GetQuantity(
                factoryInput,
                ResourceIds.FerrousOre) >= 10.0);
        Assert.True(
            distribution.Metrics.CompletedRequestCount >= 1);
        Assert.True(
            network.TryGetEdge(
                criticalEdge,
                out LogisticsEdge restoredEdge));
        Assert.True(restoredEdge.Enabled);
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
}
