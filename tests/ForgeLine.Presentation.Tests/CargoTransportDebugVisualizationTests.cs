using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Logistics;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class CargoTransportDebugVisualizationTests
{
    [Fact]
    public void CargoTransportSnapshotBuildsStateAndTargetDebugGeometry()
    {
        var inventories =
            new InventoryStore();
        var network =
            new LogisticsNetwork();
        var simulation =
            new SimulationCoordinator();
        var transportSystem =
            new CargoTransportSystem(
                network,
                inventories);

        simulation.RegisterSystem(
            transportSystem);

        EntityId truck =
            CargoTruckFactory.Create(
                simulation.Entities,
                inventories,
                new Vector3(
                    4.0f,
                    0.0f,
                    4.0f),
                new PlayerId(1),
                transportSystem:
                    transportSystem);

        simulation.AdvanceOneTick();

        CargoTransportDebugSnapshot snapshot =
            transportSystem.LastDebugSnapshot;

        Assert.Single(
            snapshot.Transports);
        Assert.Equal(
            truck,
            snapshot.Transports[0].Entity);

        var debugDraw =
            new DebugDraw
            {
                Enabled = true
            };

        CargoTransportDebugVisualization.Draw(
            debugDraw,
            snapshot,
            maximumTransports: 16,
            maximumLabels: 16);

        Assert.Single(
            debugDraw.Labels);
    }

    [Fact]
    public void EmptyCargoTransportSnapshotProducesNoLabels()
    {
        var debugDraw =
            new DebugDraw
            {
                Enabled = true
            };

        CargoTransportDebugVisualization.Draw(
            debugDraw,
            CargoTransportDebugSnapshot.Empty);

        Assert.Empty(
            debugDraw.Labels);
    }
}
