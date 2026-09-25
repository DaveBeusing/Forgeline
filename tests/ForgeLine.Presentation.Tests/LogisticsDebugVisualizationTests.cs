using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Logistics;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class LogisticsDebugVisualizationTests
{
    [Fact]
    public void LogisticsSnapshotBuildsNetworkAndRouteDebugGeometry()
    {
        var network = new LogisticsNetwork();

        LogisticsNodeId source = network.AddNode(
            new EntityId(1, 1),
            new Vector3(0.0f, 0.0f, 0.0f),
            LogisticsNodeKind.StorageDepot,
            LogisticsNodeCapabilities.CargoSource |
            LogisticsNodeCapabilities.CargoDestination |
            LogisticsNodeCapabilities.Storage);
        LogisticsNodeId relay = network.AddNode(
            new EntityId(2, 1),
            new Vector3(10.0f, 0.0f, 0.0f),
            LogisticsNodeKind.LogisticsHub,
            LogisticsNodeCapabilities.CargoSource |
            LogisticsNodeCapabilities.CargoDestination |
            LogisticsNodeCapabilities.Distribution);
        LogisticsNodeId destination = network.AddNode(
            new EntityId(3, 1),
            new Vector3(20.0f, 0.0f, 0.0f),
            LogisticsNodeKind.ProcessingFacility,
            LogisticsNodeCapabilities.CargoSource |
            LogisticsNodeCapabilities.CargoDestination |
            LogisticsNodeCapabilities.Processing);

        _ = network.AddEdge(
            source,
            relay,
            LogisticsTransportMode.GroundRoad,
            distanceMeters: 10.0,
            baseCost: 1.0,
            capacityPerSecond: 100.0);
        _ = network.AddEdge(
            relay,
            destination,
            LogisticsTransportMode.GroundRoad,
            distanceMeters: 10.0,
            baseCost: 1.0,
            capacityPerSecond: 100.0);

        LogisticsRouteSearchResult route =
            network.FindRoute(source, destination);

        Assert.True(route.Succeeded);

        LogisticsNetworkDebugSnapshot snapshot =
            LogisticsNetworkDebugSnapshot.Capture(
                network,
                route.Route);
        var debugDraw = new DebugDraw
        {
            Enabled = true
        };

        LogisticsDebugVisualization.Draw(
            debugDraw,
            snapshot,
            maximumNodes: 16,
            maximumEdges: 16,
            maximumLabels: 16);

        Assert.True(debugDraw.Lines.Length > 0);
        Assert.Equal(3, debugDraw.Labels.Count);
    }

    [Fact]
    public void EmptyLogisticsSnapshotProducesNoGeometry()
    {
        var network = new LogisticsNetwork();
        LogisticsNetworkDebugSnapshot snapshot =
            LogisticsNetworkDebugSnapshot.Capture(network);
        var debugDraw = new DebugDraw
        {
            Enabled = true
        };

        LogisticsDebugVisualization.Draw(
            debugDraw,
            snapshot);

        Assert.True(debugDraw.Lines.IsEmpty);
        Assert.Empty(debugDraw.Labels);
    }
}
