using System.Numerics;
using BenchmarkDotNet.Attributes;
using ForgeLine.Core;
using ForgeLine.Logistics;
using ForgeLine.Simulation;

namespace ForgeLine.Simulation.Benchmarks;

[MemoryDiagnoser]
public sealed class LogisticsCapacityBenchmarks
{
    private LogisticsNetwork _network = null!;
    private LogisticsRoute _route = null!;
    private LogisticsNodeId _source;
    private LogisticsNodeId _destination;

    [Params(100, 1_000)]
    public int RequestCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _network = new LogisticsNetwork();
        _source =
            _network.AddNode(
                new EntityId(1, 1),
                Vector3.Zero,
                LogisticsNodeKind.LogisticsHub,
                StandardCapabilities);
        _destination =
            _network.AddNode(
                new EntityId(2, 1),
                new Vector3(100.0f, 0.0f, 0.0f),
                LogisticsNodeKind.LogisticsHub,
                StandardCapabilities);

        _network.AddEdge(
            _source,
            _destination,
            LogisticsTransportMode.GroundRoad,
            distanceMeters: 100.0,
            baseCost: 1.0,
            capacityPerSecond: 10_000.0);

        _route =
            _network.FindRoute(
                _source,
                _destination).Route!;
    }

    [Benchmark]
    public int ReserveRoutesUnderLoad()
    {
        var capacity =
            new LogisticsCapacityTracker(
                ticksPerSecond: 20,
                windowTicks: 20);

        int accepted = 0;

        for (int index = 0; index < RequestCount; index++)
        {
            double quantity = 0.25;

            LogisticsRouteSearchResult route =
                _network.FindCapacityAwareRoute(
                    _source,
                    _destination,
                    LogisticsRouteCostPolicy.Default,
                    capacity,
                    quantity);

            if (!route.Succeeded ||
                route.Route is null)
            {
                continue;
            }

            if (capacity.TryReserveRoute(
                    _network,
                    route.Route,
                    quantity,
                    SimulationTick.Zero,
                    out _,
                    out _))
            {
                accepted++;
            }
        }

        return accepted;
    }

    [Benchmark]
    public int ReplanAcrossRepeatedTopologyChanges()
    {
        int successes = 0;
        LogisticsEdgeId edge =
            _route.Segments[0].EdgeId;

        for (int index = 0; index < RequestCount; index++)
        {
            bool enabled =
                (index & 1) == 0;
            _network.SetEdgeEnabled(
                edge,
                enabled);

            if (_network.FindRoute(
                    _source,
                    _destination).Succeeded)
            {
                successes++;
            }
        }

        _network.SetEdgeEnabled(edge, enabled: true);
        return successes;
    }

    private const LogisticsNodeCapabilities StandardCapabilities =
        LogisticsNodeCapabilities.CargoSource |
        LogisticsNodeCapabilities.CargoDestination |
        LogisticsNodeCapabilities.Storage |
        LogisticsNodeCapabilities.Distribution;
}
