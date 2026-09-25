using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Logistics;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class AutomatedDistributionDebugVisualizationTests
{
    [Fact]
    public void PendingDistributionRequestProducesDestinationDebugGeometry()
    {
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
                retryDelayTicks: 5);
        var simulation =
            new SimulationCoordinator();

        simulation.RegisterSystem(distribution);

        InventoryId destinationInventory =
            inventories.CreateInventory(
                new InventorySpecification(500.0));
        EntityId destination =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            destination,
            new InventoryStorage(destinationInventory));
        simulation.Entities.AddComponent(
            destination,
            new StorageDepot(
                destinationInventory,
                new FactionId(1)));

        Vector3 destinationPosition =
            new(30.0f, 0.0f, 40.0f);
        network.AddNode(
            destination,
            destinationPosition,
            LogisticsNodeKind.StorageDepot,
            LogisticsNodeCapabilities.CargoDestination |
            LogisticsNodeCapabilities.Storage |
            LogisticsNodeCapabilities.Distribution);

        EntityId policy =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            policy,
            new LogisticsStockPolicy(
                destination,
                ResourceIds.FerrousOre,
                desiredMinimum: 20.0,
                desiredTarget: 60.0,
                desiredMaximum: 100.0));

        simulation.AdvanceOneTick();

        AutomatedDistributionDebugSnapshot snapshot =
            distribution.LastDebugSnapshot;
        Assert.Single(snapshot.Requests);
        Assert.Equal(
            LogisticsTransportRequestState.RetryPending,
            snapshot.Requests[0].State);

        var debugDraw =
            new DebugDraw
            {
                Enabled = true
            };

        AutomatedDistributionDebugVisualization.Draw(
            debugDraw,
            snapshot,
            maximumRequests: 16,
            maximumLabels: 16);

        Assert.Single(debugDraw.Labels);
        Assert.True(debugDraw.Lines.Length >= 3);
        Assert.Contains(
            "DIST R",
            debugDraw.Labels[0].Text);
    }

    [Fact]
    public void EmptyDistributionSnapshotProducesNoGeometry()
    {
        var debugDraw =
            new DebugDraw
            {
                Enabled = true
            };

        AutomatedDistributionDebugVisualization.Draw(
            debugDraw,
            AutomatedDistributionDebugSnapshot.Empty);

        Assert.Empty(debugDraw.Labels);
        Assert.Equal(0, debugDraw.Lines.Length);
    }
}
