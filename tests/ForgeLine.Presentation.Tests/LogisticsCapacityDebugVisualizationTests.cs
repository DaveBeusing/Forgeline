using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Logistics;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class LogisticsCapacityDebugVisualizationTests
{
    [Fact]
    public void SaturatedAndBlockedInfrastructureProducesDebugGeometry()
    {
        var network = new LogisticsNetwork();
        LogisticsNodeId source =
            network.AddNode(
                new EntityId(1, 1),
                Vector3.Zero,
                LogisticsNodeKind.StorageDepot,
                StandardCapabilities);
        LogisticsNodeId destination =
            network.AddNode(
                new EntityId(2, 1),
                new Vector3(20.0f, 0.0f, 0.0f),
                LogisticsNodeKind.StorageDepot,
                StandardCapabilities);
        LogisticsEdgeId edge =
            network.AddEdge(
                source,
                destination,
                LogisticsTransportMode.GroundRoad,
                distanceMeters: 20.0,
                baseCost: 1.0,
                capacityPerSecond: 100.0);

        var capacity = new LogisticsCapacityTracker();
        LogisticsRoute route =
            network.FindRoute(source, destination).Route!;

        Assert.True(
            capacity.TryReserveRoute(
                network,
                route,
                quantity: 95.0,
                new SimulationTick(1),
                out _,
                out _));
        Assert.True(
            network.SetEdgeEnabled(
                edge,
                enabled: false));

        LogisticsCapacityDebugSnapshot snapshot =
            capacity.CaptureDebugSnapshot(
                network,
                new SimulationTick(2),
                backlogRequestCount: 2,
                backlogQuantity: 75.0);

        var debugDraw =
            new DebugDraw
            {
                Enabled = true
            };

        LogisticsCapacityDebugVisualization.Draw(
            debugDraw,
            snapshot,
            maximumNodes: 8,
            maximumEdges: 8,
            maximumLabels: 8);

        Assert.True(debugDraw.Lines.Length >= 1);
        Assert.True(debugDraw.Labels.Length >= 1);
        Assert.Contains(
            debugDraw.Labels,
            label =>
                label.Text.Contains(
                    "LOGISTICS",
                    StringComparison.Ordinal));
        Assert.Equal(
            LogisticsLoadState.Blocked,
            Assert.Single(snapshot.Edges).State);
    }

    [Fact]
    public void EmptySnapshotProducesNoGeometry()
    {
        var debugDraw =
            new DebugDraw
            {
                Enabled = true
            };

        LogisticsCapacityDebugVisualization.Draw(
            debugDraw,
            LogisticsCapacityDebugSnapshot.Empty);

        Assert.Equal(0, debugDraw.Lines.Length);
        Assert.Empty(debugDraw.Labels);
    }

    private const LogisticsNodeCapabilities StandardCapabilities =
        LogisticsNodeCapabilities.CargoSource |
        LogisticsNodeCapabilities.CargoDestination |
        LogisticsNodeCapabilities.Storage |
        LogisticsNodeCapabilities.Distribution;
}
