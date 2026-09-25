using System.Numerics;
using BenchmarkDotNet.Attributes;
using ForgeLine.Core;
using ForgeLine.Logistics;

namespace ForgeLine.Simulation.Benchmarks;

[MemoryDiagnoser]
public class LogisticsRoutingBenchmarks
{
    private LogisticsNetwork _network = null!;
    private LogisticsNodeId _source;
    private LogisticsNodeId _destination;

    [Params(1_000, 10_000)]
    public int NodeCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _network = new LogisticsNetwork();
        var nodes = new LogisticsNodeId[NodeCount];
        int width =
            checked((int)Math.Ceiling(Math.Sqrt(NodeCount)));

        for (int index = 0; index < NodeCount; index++)
        {
            int x = index % width;
            int z = index / width;

            nodes[index] = _network.AddNode(
                new EntityId(
                    checked((uint)index + 1),
                    1),
                new Vector3(
                    x * 10.0f,
                    0.0f,
                    z * 10.0f),
                LogisticsNodeKind.StorageDepot,
                LogisticsNodeCapabilities.CargoSource |
                LogisticsNodeCapabilities.CargoDestination |
                LogisticsNodeCapabilities.Storage);
        }

        for (int index = 0; index < NodeCount; index++)
        {
            int x = index % width;

            if (x + 1 < width &&
                index + 1 < NodeCount)
            {
                AddRoad(
                    nodes[index],
                    nodes[index + 1]);
            }

            if (index + width < NodeCount)
            {
                AddRoad(
                    nodes[index],
                    nodes[index + width]);
            }
        }

        _source = nodes[0];
        _destination = nodes[^1];
    }

    [Benchmark]
    public int FindColdRoute()
    {
        _network.InvalidateRoutes();

        LogisticsRouteSearchResult result =
            _network.FindRoute(
                _source,
                _destination);

        if (!result.Succeeded ||
            result.Route is null)
        {
            throw new InvalidOperationException(
                "Logistics routing benchmark failed to find a connected route.");
        }

        return result.Route.Segments.Count;
    }

    [Benchmark]
    public bool CheckReachability()
    {
        return _network.IsReachable(
            _source,
            _destination);
    }

    private void AddRoad(
        LogisticsNodeId source,
        LogisticsNodeId destination)
    {
        _network.AddEdge(
            source,
            destination,
            LogisticsTransportMode.GroundRoad,
            distanceMeters: 10.0,
            baseCost: 10.0,
            capacityPerSecond: 100.0);
    }
}
