using System.Numerics;
using ForgeLine.Core;
using Xunit;

namespace ForgeLine.Logistics.Tests;

public sealed class LogisticsNetworkTests
{
    [Fact]
    public void FindsLowestCostRouteAcrossMultipleSegments()
    {
        var network = new LogisticsNetwork();
        LogisticsNodeId source = AddNode(network, 1, 0.0f);
        LogisticsNodeId relay = AddNode(network, 2, 10.0f);
        LogisticsNodeId destination = AddNode(network, 3, 20.0f);

        LogisticsEdgeId first = network.AddEdge(
            source,
            relay,
            LogisticsTransportMode.GroundRoad,
            distanceMeters: 10.0,
            baseCost: 1.0,
            capacityPerSecond: 100.0);
        LogisticsEdgeId second = network.AddEdge(
            relay,
            destination,
            LogisticsTransportMode.GroundRoad,
            distanceMeters: 10.0,
            baseCost: 1.0,
            capacityPerSecond: 100.0);
        _ = network.AddEdge(
            source,
            destination,
            LogisticsTransportMode.GroundRoad,
            distanceMeters: 20.0,
            baseCost: 5.0,
            capacityPerSecond: 100.0);

        LogisticsRouteSearchResult result =
            network.FindRoute(source, destination);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Route);
        Assert.Equal(2.0, result.Route.TotalCost);
        Assert.Equal(20.0, result.Route.TotalDistanceMeters);
        Assert.Collection(
            result.Route.Segments,
            segment => Assert.Equal(first, segment.EdgeId),
            segment => Assert.Equal(second, segment.EdgeId));
        Assert.True(network.IsReachable(source, destination));
        Assert.Equal(1, network.Metrics.ConnectedComponentCount);
    }

    [Fact]
    public void DisconnectedGraphReturnsExplicitNoRoute()
    {
        var network = new LogisticsNetwork();
        LogisticsNodeId source = AddNode(network, 1, 0.0f);
        LogisticsNodeId destination = AddNode(network, 2, 50.0f);

        LogisticsRouteSearchResult result =
            network.FindRoute(source, destination);

        Assert.False(result.Succeeded);
        Assert.Equal(
            LogisticsRouteFailureReason.NoRoute,
            result.FailureReason);
        Assert.False(network.IsReachable(source, destination));
        Assert.Equal(2, network.Metrics.ConnectedComponentCount);
        Assert.Equal(1, network.Metrics.FailedRouteCount);
    }

    [Fact]
    public void DisabledEdgeIsNeverUsedAndAlternateRouteWins()
    {
        var network = new LogisticsNetwork();
        LogisticsNodeId source = AddNode(network, 1, 0.0f);
        LogisticsNodeId blockedRelay = AddNode(network, 2, 10.0f);
        LogisticsNodeId alternateRelay = AddNode(network, 3, 10.0f);
        LogisticsNodeId destination = AddNode(network, 4, 20.0f);

        _ = network.AddEdge(
            source,
            blockedRelay,
            LogisticsTransportMode.GroundRoad,
            10.0,
            1.0,
            100.0);
        LogisticsEdgeId disabled = network.AddEdge(
            blockedRelay,
            destination,
            LogisticsTransportMode.GroundRoad,
            10.0,
            1.0,
            100.0,
            enabled: false);
        LogisticsEdgeId alternateFirst = network.AddEdge(
            source,
            alternateRelay,
            LogisticsTransportMode.GroundRoad,
            12.0,
            2.0,
            100.0);
        LogisticsEdgeId alternateSecond = network.AddEdge(
            alternateRelay,
            destination,
            LogisticsTransportMode.GroundRoad,
            12.0,
            2.0,
            100.0);

        LogisticsRouteSearchResult result =
            network.FindRoute(source, destination);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Route);
        Assert.DoesNotContain(
            result.Route.Segments,
            segment => segment.EdgeId == disabled);
        Assert.Collection(
            result.Route.Segments,
            segment => Assert.Equal(
                alternateFirst,
                segment.EdgeId),
            segment => Assert.Equal(
                alternateSecond,
                segment.EdgeId));
    }

    [Fact]
    public void EqualCostRoutesUseStableNodeTieBreak()
    {
        var network = new LogisticsNetwork();
        LogisticsNodeId source = AddNode(network, 1, 0.0f);
        LogisticsNodeId lowerIdRelay = AddNode(network, 2, 10.0f);
        LogisticsNodeId higherIdRelay = AddNode(network, 3, 10.0f);
        LogisticsNodeId destination = AddNode(network, 4, 20.0f);

        LogisticsEdgeId expectedFirst = network.AddEdge(
            source,
            lowerIdRelay,
            LogisticsTransportMode.GroundRoad,
            10.0,
            1.0,
            100.0);
        _ = network.AddEdge(
            lowerIdRelay,
            destination,
            LogisticsTransportMode.GroundRoad,
            10.0,
            1.0,
            100.0);
        _ = network.AddEdge(
            source,
            higherIdRelay,
            LogisticsTransportMode.GroundRoad,
            10.0,
            1.0,
            100.0);
        _ = network.AddEdge(
            higherIdRelay,
            destination,
            LogisticsTransportMode.GroundRoad,
            10.0,
            1.0,
            100.0);

        LogisticsRouteSearchResult first =
            network.FindRoute(source, destination);
        LogisticsRouteSearchResult cached =
            network.FindRoute(source, destination);

        Assert.True(first.Succeeded);
        Assert.NotNull(first.Route);
        Assert.Equal(
            expectedFirst,
            first.Route.Segments[0].EdgeId);
        Assert.True(cached.Succeeded);
        Assert.True(cached.Diagnostics.CacheHit);
        Assert.Equal(
            first.Route.Segments.Select(segment => segment.EdgeId),
            cached.Route!.Segments.Select(segment => segment.EdgeId));
    }

    [Fact]
    public void NodeRemovalInvalidatesCachedRoutes()
    {
        var network = new LogisticsNetwork();
        LogisticsNodeId source = AddNode(network, 1, 0.0f);
        LogisticsNodeId relay = AddNode(network, 2, 10.0f);
        LogisticsNodeId destination = AddNode(network, 3, 20.0f);

        _ = network.AddEdge(
            source,
            relay,
            LogisticsTransportMode.GroundRoad,
            10.0,
            1.0,
            100.0);
        _ = network.AddEdge(
            relay,
            destination,
            LogisticsTransportMode.GroundRoad,
            10.0,
            1.0,
            100.0);

        LogisticsRouteSearchResult first =
            network.FindRoute(source, destination);
        LogisticsRouteSearchResult cached =
            network.FindRoute(source, destination);
        LogisticsNetworkVersion cachedVersion =
            network.Version;

        Assert.True(first.Succeeded);
        Assert.True(cached.Diagnostics.CacheHit);
        Assert.Equal(1, network.RouteCacheEntryCount);

        Assert.True(network.RemoveNode(relay));

        Assert.True(network.Version.Value > cachedVersion.Value);
        Assert.Equal(0, network.RouteCacheEntryCount);
        Assert.Equal(0, network.EdgeCount);

        LogisticsRouteSearchResult afterRemoval =
            network.FindRoute(source, destination);

        Assert.False(afterRemoval.Succeeded);
        Assert.Equal(
            LogisticsRouteFailureReason.NoRoute,
            afterRemoval.FailureReason);
        Assert.NotEqual(
            first.Route!.Version,
            network.Version);
    }

    [Fact]
    public void RoutePolicyFiltersTransportModeAndCapacity()
    {
        var network = new LogisticsNetwork();
        LogisticsNodeId source = AddNode(network, 1, 0.0f);
        LogisticsNodeId destination = AddNode(network, 2, 20.0f);

        _ = network.AddEdge(
            source,
            destination,
            LogisticsTransportMode.GroundRoad,
            20.0,
            1.0,
            25.0);
        LogisticsEdgeId rail = network.AddEdge(
            source,
            destination,
            LogisticsTransportMode.Rail,
            20.0,
            2.0,
            1_000.0);

        var highCapacityRailPolicy =
            new LogisticsRouteCostPolicy(
                LogisticsTransportModeMask.Rail,
                minimumCapacityPerSecond: 500.0);

        LogisticsRouteSearchResult result =
            network.FindRoute(
                source,
                destination,
                highCapacityRailPolicy);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Route);
        Assert.Single(result.Route.Segments);
        Assert.Equal(
            rail,
            result.Route.Segments[0].EdgeId);

        var impossibleGroundPolicy =
            new LogisticsRouteCostPolicy(
                LogisticsTransportModeMask.GroundRoad,
                minimumCapacityPerSecond: 500.0);

        LogisticsRouteSearchResult blocked =
            network.FindRoute(
                source,
                destination,
                impossibleGroundPolicy);

        Assert.False(blocked.Succeeded);
    }

    [Fact]
    public void DisabledNodeBreaksReachabilityWithoutDeletingTopology()
    {
        var network = new LogisticsNetwork();
        LogisticsNodeId source = AddNode(network, 1, 0.0f);
        LogisticsNodeId relay = AddNode(network, 2, 10.0f);
        LogisticsNodeId destination = AddNode(network, 3, 20.0f);

        _ = network.AddEdge(
            source,
            relay,
            LogisticsTransportMode.GroundRoad,
            10.0,
            1.0,
            100.0);
        _ = network.AddEdge(
            relay,
            destination,
            LogisticsTransportMode.GroundRoad,
            10.0,
            1.0,
            100.0);

        Assert.True(
            network.UpdateNode(
                relay,
                new Vector3(10.0f, 0.0f, 0.0f),
                LogisticsNodeKind.StorageDepot,
                StandardCapabilities,
                enabled: false));

        Assert.False(network.IsReachable(source, destination));
        LogisticsRouteSearchResult result =
            network.FindRoute(source, destination);
        Assert.False(result.Succeeded);
        Assert.Equal(2, network.EdgeCount);
    }

    private const LogisticsNodeCapabilities StandardCapabilities =
        LogisticsNodeCapabilities.CargoSource |
        LogisticsNodeCapabilities.CargoDestination |
        LogisticsNodeCapabilities.Storage;

    private static LogisticsNodeId AddNode(
        LogisticsNetwork network,
        uint entityIndex,
        float x)
    {
        return network.AddNode(
            new EntityId(entityIndex, 1),
            new Vector3(x, 0.0f, 0.0f),
            LogisticsNodeKind.StorageDepot,
            StandardCapabilities);
    }
}
