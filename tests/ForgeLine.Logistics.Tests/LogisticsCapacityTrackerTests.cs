using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Logistics.Tests;

public sealed class LogisticsCapacityTrackerTests
{
    private const LogisticsNodeCapabilities Capabilities =
        LogisticsNodeCapabilities.CargoSource |
        LogisticsNodeCapabilities.CargoDestination |
        LogisticsNodeCapabilities.Storage |
        LogisticsNodeCapabilities.Distribution;

    [Fact]
    public void ThroughputReservationEnforcesEdgeCapacityAndExpiresBySimulationTick()
    {
        var network = new LogisticsNetwork();
        LogisticsNodeId source = AddNode(network, 1, 0.0f);
        LogisticsNodeId destination = AddNode(network, 2, 10.0f);
        LogisticsEdgeId edge = network.AddEdge(
            source,
            destination,
            LogisticsTransportMode.GroundRoad,
            distanceMeters: 10.0,
            baseCost: 1.0,
            capacityPerSecond: 100.0);

        LogisticsRoute route =
            Assert.IsType<LogisticsRoute>(
                network.FindRoute(source, destination).Route);

        var capacity =
            new LogisticsCapacityTracker(
                ticksPerSecond: 20,
                windowTicks: 20);

        Assert.True(
            capacity.TryReserveRoute(
                network,
                route,
                quantity: 80.0,
                new SimulationTick(1),
                out LogisticsThroughputReservationId first,
                out LogisticsCapacityBottleneck firstBottleneck));
        Assert.True(first.IsSpecified);
        Assert.False(firstBottleneck.IsSpecified);

        Assert.False(
            capacity.TryReserveRoute(
                network,
                route,
                quantity: 30.0,
                new SimulationTick(1),
                out _,
                out LogisticsCapacityBottleneck blocked));
        Assert.Equal(
            LogisticsCapacityBottleneckKind.Edge,
            blocked.Kind);
        Assert.Equal(edge, blocked.EdgeId);

        LogisticsCapacityDebugSnapshot saturated =
            capacity.CaptureDebugSnapshot(
                network,
                new SimulationTick(1),
                backlogRequestCount: 1,
                backlogQuantity: 30.0);

        LogisticsEdgeCapacityReadModel model =
            Assert.Single(saturated.Edges);
        Assert.Equal(80.0, model.ScheduledLoad);
        Assert.Equal(0.8, model.Utilization, precision: 10);
        Assert.Equal(LogisticsLoadState.Busy, model.State);
        Assert.Equal(1, saturated.Health.BacklogRequestCount);
        Assert.Equal(30.0, saturated.Health.BacklogQuantity);

        capacity.Advance(
            network,
            new SimulationTick(21));

        Assert.False(capacity.Contains(first));
        Assert.True(
            capacity.TryReserveRoute(
                network,
                network.FindRoute(source, destination).Route!,
                quantity: 100.0,
                new SimulationTick(21),
                out _,
                out _));
    }

    [Fact]
    public void CapacityAwareRoutingAvoidsSaturatedCheapLink()
    {
        var network = new LogisticsNetwork();
        LogisticsNodeId source = AddNode(network, 1, 0.0f);
        LogisticsNodeId relay = AddNode(network, 2, 10.0f);
        LogisticsNodeId destination = AddNode(network, 3, 20.0f);

        LogisticsEdgeId direct = network.AddEdge(
            source,
            destination,
            LogisticsTransportMode.GroundRoad,
            distanceMeters: 20.0,
            baseCost: 1.0,
            capacityPerSecond: 100.0);
        LogisticsEdgeId alternateFirst = network.AddEdge(
            source,
            relay,
            LogisticsTransportMode.GroundRoad,
            distanceMeters: 10.0,
            baseCost: 2.0,
            capacityPerSecond: 100.0);
        LogisticsEdgeId alternateSecond = network.AddEdge(
            relay,
            destination,
            LogisticsTransportMode.GroundRoad,
            distanceMeters: 10.0,
            baseCost: 2.0,
            capacityPerSecond: 100.0);

        var capacity = new LogisticsCapacityTracker();

        LogisticsRoute directRoute =
            network.FindRoute(source, destination).Route!;
        Assert.Single(directRoute.Segments);
        Assert.Equal(direct, directRoute.Segments[0].EdgeId);

        Assert.True(
            capacity.TryReserveRoute(
                network,
                directRoute,
                quantity: 90.0,
                new SimulationTick(1),
                out _,
                out _));

        LogisticsRouteSearchResult rerouted =
            network.FindCapacityAwareRoute(
                source,
                destination,
                LogisticsRouteCostPolicy.Default,
                capacity,
                requestedQuantity: 20.0);

        Assert.True(rerouted.Succeeded);
        Assert.NotNull(rerouted.Route);
        Assert.Collection(
            rerouted.Route.Segments,
            segment => Assert.Equal(alternateFirst, segment.EdgeId),
            segment => Assert.Equal(alternateSecond, segment.EdgeId));
    }

    [Fact]
    public void DisabledInfrastructureIsBlockedAndInvalidatesReservations()
    {
        var network = new LogisticsNetwork();
        LogisticsNodeId source = AddNode(network, 1, 0.0f);
        LogisticsNodeId destination = AddNode(network, 2, 10.0f);
        LogisticsEdgeId edge = network.AddEdge(
            source,
            destination,
            LogisticsTransportMode.GroundRoad,
            distanceMeters: 10.0,
            baseCost: 1.0,
            capacityPerSecond: 100.0);

        var capacity = new LogisticsCapacityTracker();
        LogisticsRoute route =
            network.FindRoute(source, destination).Route!;

        Assert.True(
            capacity.TryReserveRoute(
                network,
                route,
                quantity: 50.0,
                new SimulationTick(1),
                out LogisticsThroughputReservationId reservation,
                out _));

        Assert.True(network.SetEdgeEnabled(edge, enabled: false));
        capacity.Advance(network, new SimulationTick(2));

        Assert.False(capacity.Contains(reservation));
        Assert.False(network.IsReachable(source, destination));

        LogisticsCapacityDebugSnapshot blocked =
            capacity.CaptureDebugSnapshot(
                network,
                new SimulationTick(2));

        Assert.Equal(
            LogisticsLoadState.Blocked,
            Assert.Single(blocked.Edges).State);

        Assert.True(network.SetEdgeEnabled(edge, enabled: true));
        Assert.True(network.IsReachable(source, destination));
    }

    private static LogisticsNodeId AddNode(
        LogisticsNetwork network,
        uint entityIndex,
        float x) =>
        network.AddNode(
            new EntityId(entityIndex, 1),
            new Vector3(x, 0.0f, 0.0f),
            LogisticsNodeKind.StorageDepot,
            Capabilities);
}
