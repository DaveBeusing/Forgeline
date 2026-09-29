using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Jobs;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class VerticalSliceRuntimeCompositionTests
{
    [Fact]
    public void GameplayAndValidationKeepExplicitDistributionPolicies()
    {
        VerticalSliceScenarioSettings gameplay =
            VerticalSliceScenarioSettings.Create(
                VerticalSliceScenarioProfile.Gameplay);
        VerticalSliceScenarioSettings validation =
            VerticalSliceScenarioSettings.Create(
                VerticalSliceScenarioProfile.Validation);

        Assert.Equal(20UL, gameplay.DistributionRetryDelayTicks);
        Assert.Equal(4U, gameplay.DistributionMaximumTransportAttempts);
        Assert.Equal(200UL, gameplay.DistributionFairnessAgingTicks);

        Assert.Equal(10UL, validation.DistributionRetryDelayTicks);
        Assert.Equal(8U, validation.DistributionMaximumTransportAttempts);
        Assert.Equal(100UL, validation.DistributionFairnessAgingTicks);
    }

    [Fact]
    public void EquivalentGameplayConfigurationsShareSystemOrderWhileParticipantControlRemainsExplicit()
    {
        VerticalSliceRuntimeSettings computerRuntime =
            VerticalSliceRuntimeSettings.CreateHeadless(
                VerticalSliceScenarioProfile.Gameplay,
                seed: 91);
        VerticalSliceRuntimeSettings playerRuntime =
            computerRuntime with
            {
                Participants =
                    VerticalSliceRuntimeSettings.CreateDefaultParticipants(
                        westComputerControlled: false,
                        eastComputerControlled: true)
            };

        using VerticalSliceScenario computerScenario =
            VerticalSliceScenario.Create(
                computerRuntime,
                TestContext.Current.CancellationToken);
        using VerticalSliceScenario playerScenario =
            VerticalSliceScenario.Create(
                playerRuntime,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            computerScenario.Services.RegisteredSystemTypes,
            playerScenario.Services.RegisteredSystemTypes);
        Assert.Equal(
            computerScenario.Services.Navigation.World.Grid.Settings,
            playerScenario.Services.Navigation.World.Grid.Settings);
        Assert.Equal(
            computerScenario.RuntimeSettings.Scenario,
            playerScenario.RuntimeSettings.Scenario);

        Assert.True(
            computerScenario.Simulation.Entities.IsAlive(
                computerScenario.West.Controller));
        Assert.False(
            playerScenario.Simulation.Entities.IsAlive(
                playerScenario.West.Controller));
        Assert.True(
            playerScenario.Simulation.Entities.IsAlive(
                playerScenario.East.Controller));

        Assert.True(
            computerScenario.MatchConfiguration
                .GetParticipant(
                    computerScenario.West.Player)
                .IsComputerControlled);
        Assert.False(
            playerScenario.MatchConfiguration
                .GetParticipant(
                    playerScenario.West.Player)
                .IsComputerControlled);
    }

    [Fact]
    public void SharedRuntimeUsesNavigableApproachForBlockedStartingCore()
    {
        VerticalSliceRuntimeSettings runtime =
            VerticalSliceRuntimeSettings.CreateHeadless(
                VerticalSliceScenarioProfile.Gameplay,
                seed: 123) with
            {
                Participants =
                    VerticalSliceRuntimeSettings.CreateDefaultParticipants(
                        westComputerControlled: false,
                        eastComputerControlled: false)
            };

        using VerticalSliceScenario scenario =
            VerticalSliceScenario.Create(
                runtime,
                TestContext.Current.CancellationToken);

        NavigationGrid grid =
            scenario.Services.Navigation.World.Grid;
        NavigationCapabilities wheeled =
            NavigationCapabilities.For(
                NavigationMovementClass.Wheeled);
        WorldTransform coreTransform =
            scenario.Simulation.Entities.GetComponent<
                WorldTransform>(
                    scenario.West.CommandCore);

        Assert.True(
            grid.TryWorldToCell(
                coreTransform.Position,
                out NavigationCellCoordinate coreCell));
        Assert.False(
            grid.IsTraversable(
                coreCell,
                wheeled));

        NavigationCellCoordinate truckCell =
            FindTraversableCell(
                grid,
                coreCell,
                minimumRadius: 3);
        NavigationCellCoordinate destinationCell =
            FindTraversableCell(
                grid,
                coreCell,
                minimumRadius: 5,
                excluded: truckCell);
        Vector3 truckStart =
            grid.GetCellCenter(truckCell);
        Vector3 destinationPosition =
            grid.GetCellCenter(destinationCell);

        LogisticsNodeId sourceNode =
            GetOrCreateSourceNode(
                scenario,
                coreTransform.Position);

        InventoryId destinationInventory =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(
                    500.0));
        EntityId destination =
            scenario.Simulation.Entities.CreateEntity();
        scenario.Simulation.Entities.AddComponent(
            destination,
            new WorldTransform(
                destinationPosition,
                Quaternion.Identity,
                Vector3.One));
        scenario.Simulation.Entities.AddComponent(
            destination,
            new InventoryStorage(
                destinationInventory));

        LogisticsNodeId destinationNode =
            scenario.Logistics.AddNode(
                destination,
                destinationPosition,
                LogisticsNodeKind.StorageDepot,
                LogisticsNodeCapabilities.CargoDestination |
                LogisticsNodeCapabilities.Storage);

        scenario.Logistics.AddEdge(
            sourceNode,
            destinationNode,
            LogisticsTransportMode.GroundRoad,
            Vector3.Distance(
                coreTransform.Position,
                destinationPosition),
            baseCost: 1.0,
            capacityPerSecond: 100.0);

        EntityId truck =
            CargoTruckFactory.Create(
                scenario.Simulation.Entities,
                scenario.Inventories,
                truckStart,
                scenario.West.Player,
                scenario.CargoTransport);
        CargoTransport transport =
            scenario.Simulation.Entities.GetComponent<
                CargoTransport>(
                    truck);

        const double quantity = 25.0;
        double before =
            scenario.Inventories.GetQuantity(
                scenario.West.StartingInventory,
                ResourceIds.Steel) +
            scenario.Inventories.GetQuantity(
                destinationInventory,
                ResourceIds.Steel) +
            scenario.Inventories.GetQuantity(
                transport.CargoInventory,
                ResourceIds.Steel);

        Assert.True(
            scenario.CargoTransport.TryAssignOrder(
                scenario.Simulation.Entities,
                truck,
                new CargoTransportOrder(
                    sourceNode,
                    destinationNode,
                    ResourceIds.Steel,
                    quantity,
                    SimulationTick.Zero),
                SimulationTick.Zero));

        scenario.Simulation.AdvanceOneTick();

        CargoTransportMovementTarget approach =
            scenario.Simulation.Entities.GetComponent<
                CargoTransportMovementTarget>(
                    truck);

        Assert.NotEqual(
            coreTransform.Position,
            approach.WorldPosition);
        Assert.True(
            grid.TryWorldToCell(
                approach.WorldPosition,
                out NavigationCellCoordinate approachCell));
        Assert.True(
            grid.IsTraversable(
                approachCell,
                wheeled));

        for (int tick = 0;
             tick < 2_000 &&
             scenario.Simulation.Entities.HasComponent<
                 CargoTransportOrder>(
                     truck);
             tick++)
        {
            scenario.Simulation.AdvanceOneTick();
        }

        Assert.False(
            scenario.Simulation.Entities.HasComponent<
                CargoTransportOrder>(
                    truck));
        Assert.Equal(
            quantity,
            scenario.Inventories.GetQuantity(
                destinationInventory,
                ResourceIds.Steel),
            precision: 6);

        double after =
            scenario.Inventories.GetQuantity(
                scenario.West.StartingInventory,
                ResourceIds.Steel) +
            scenario.Inventories.GetQuantity(
                destinationInventory,
                ResourceIds.Steel) +
            scenario.Inventories.GetQuantity(
                transport.CargoInventory,
                ResourceIds.Steel);

        Assert.Equal(
            before,
            after,
            precision: 6);
    }

    [Fact]
    public void HostOwnedSchedulerSurvivesScenarioDisposal()
    {
        using var scheduler =
            new JobScheduler();
        VerticalSliceRuntimeSettings runtime =
            VerticalSliceRuntimeSettings.CreateClient(
                scheduler,
                seed: 77);
        VerticalSliceScenario scenario =
            VerticalSliceScenario.Create(
                runtime,
                TestContext.Current.CancellationToken);

        Assert.False(
            scenario.OwnsScheduler);
        Assert.Same(
            scheduler,
            scenario.Scheduler);

        scenario.Dispose();

        JobHandle handle =
            scheduler.Schedule(
                static _ => { });
        scheduler.Wait(handle);
    }

    [Fact]
    public void RuntimeOwnedSchedulerIsDisposedWithScenario()
    {
        VerticalSliceRuntimeSettings runtime =
            VerticalSliceRuntimeSettings.CreateOwned(
                VerticalSliceScenarioProfile.Gameplay,
                seed: 78,
                VerticalSliceRuntimeSettings.CreateDefaultParticipants(
                    westComputerControlled: true,
                    eastComputerControlled: true));
        VerticalSliceScenario scenario =
            VerticalSliceScenario.Create(
                runtime,
                TestContext.Current.CancellationToken);
        JobScheduler scheduler =
            Assert.IsType<JobScheduler>(
                scenario.Scheduler);

        Assert.True(
            scenario.OwnsScheduler);

        scenario.Dispose();

        Assert.Throws<InvalidOperationException>(
            () =>
                scheduler.Schedule(
                    static _ => { }));
    }

    [Fact]
    public void CanceledCreationDoesNotProduceRuntime()
    {
        using var cancellation =
            new CancellationTokenSource();
        cancellation.Cancel();

        VerticalSliceRuntimeSettings runtime =
            VerticalSliceRuntimeSettings.CreateOwned(
                VerticalSliceScenarioProfile.Gameplay,
                seed: 79,
                VerticalSliceRuntimeSettings.CreateDefaultParticipants(
                    westComputerControlled: true,
                    eastComputerControlled: true));

        Assert.Throws<OperationCanceledException>(
            () =>
                VerticalSliceScenario.Create(
                    runtime,
                    cancellation.Token));
    }

    private static LogisticsNodeId GetOrCreateSourceNode(
        VerticalSliceScenario scenario,
        Vector3 position)
    {
        if (scenario.Logistics.TryGetNodeForEntity(
                scenario.West.CommandCore,
                out LogisticsNodeId existing))
        {
            return existing;
        }

        return scenario.Logistics.AddNode(
            scenario.West.CommandCore,
            position,
            LogisticsNodeKind.StorageDepot,
            LogisticsNodeCapabilities.CargoSource |
            LogisticsNodeCapabilities.CargoDestination |
            LogisticsNodeCapabilities.Storage |
            LogisticsNodeCapabilities.Distribution);
    }

    private static NavigationCellCoordinate FindTraversableCell(
        NavigationGrid grid,
        NavigationCellCoordinate origin,
        int minimumRadius,
        NavigationCellCoordinate? excluded = null)
    {
        NavigationCapabilities capabilities =
            NavigationCapabilities.For(
                NavigationMovementClass.Wheeled);

        int maximumRadius =
            Math.Max(
                grid.Width,
                grid.Height);

        for (int radius = minimumRadius;
             radius <= maximumRadius;
             radius++)
        {
            for (int z = origin.Z - radius;
                 z <= origin.Z + radius;
                 z++)
            {
                for (int x = origin.X - radius;
                     x <= origin.X + radius;
                     x++)
                {
                    if (Math.Max(
                            Math.Abs(x - origin.X),
                            Math.Abs(z - origin.Z)) !=
                        radius)
                    {
                        continue;
                    }

                    var candidate =
                        new NavigationCellCoordinate(
                            x,
                            z);

                    if (excluded.HasValue &&
                        candidate == excluded.Value)
                    {
                        continue;
                    }

                    if (grid.IsTraversable(
                            candidate,
                            capabilities))
                    {
                        return candidate;
                    }
                }
            }
        }

        throw new InvalidOperationException(
            "No traversable navigation cell was found.");
    }
}
