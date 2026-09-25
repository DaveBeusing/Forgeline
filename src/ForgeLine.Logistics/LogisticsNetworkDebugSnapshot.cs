using System.Numerics;

namespace ForgeLine.Logistics;

public readonly record struct LogisticsNodeDebugReadModel(
    LogisticsNodeId Id,
    LogisticsNodeKind Kind,
    LogisticsNodeCapabilities Capabilities,
    Vector3 WorldPosition,
    bool Enabled);

public readonly record struct LogisticsEdgeDebugReadModel(
    LogisticsEdgeId Id,
    LogisticsNodeId Source,
    LogisticsNodeId Destination,
    LogisticsTransportMode Mode,
    Vector3 SourcePosition,
    Vector3 DestinationPosition,
    double CapacityPerSecond,
    bool Enabled);

public readonly record struct LogisticsRouteDebugSegment(
    LogisticsEdgeId EdgeId,
    LogisticsNodeId From,
    LogisticsNodeId To,
    Vector3 FromPosition,
    Vector3 ToPosition,
    LogisticsTransportMode Mode,
    double Cost);

public sealed class LogisticsNetworkDebugSnapshot
{
    private readonly LogisticsNodeDebugReadModel[] _nodes;
    private readonly LogisticsEdgeDebugReadModel[] _edges;
    private readonly LogisticsRouteDebugSegment[] _routeSegments;

    private LogisticsNetworkDebugSnapshot(
        LogisticsNetworkVersion version,
        LogisticsNetworkMetrics metrics,
        LogisticsNodeDebugReadModel[] nodes,
        LogisticsEdgeDebugReadModel[] edges,
        LogisticsRouteDebugSegment[] routeSegments)
    {
        Version = version;
        Metrics = metrics;
        _nodes = nodes;
        _edges = edges;
        _routeSegments = routeSegments;
    }

    public LogisticsNetworkVersion Version { get; }

    public LogisticsNetworkMetrics Metrics { get; }

    public IReadOnlyList<LogisticsNodeDebugReadModel> Nodes =>
        _nodes;

    public IReadOnlyList<LogisticsEdgeDebugReadModel> Edges =>
        _edges;

    public IReadOnlyList<LogisticsRouteDebugSegment> RouteSegments =>
        _routeSegments;

    public static LogisticsNetworkDebugSnapshot Capture(
        LogisticsNetwork network,
        LogisticsRoute? route = null)
    {
        ArgumentNullException.ThrowIfNull(network);

        IReadOnlyList<LogisticsNode> nodes = network.GetNodes();
        var positions = new Dictionary<
            LogisticsNodeId,
            Vector3>(nodes.Count);
        var nodeModels =
            new LogisticsNodeDebugReadModel[nodes.Count];

        for (int index = 0; index < nodes.Count; index++)
        {
            LogisticsNode node = nodes[index];
            positions.Add(node.Id, node.WorldPosition);
            nodeModels[index] =
                new LogisticsNodeDebugReadModel(
                    node.Id,
                    node.Kind,
                    node.Capabilities,
                    node.WorldPosition,
                    node.Enabled);
        }

        IReadOnlyList<LogisticsEdge> edges = network.GetEdges();
        var edgeModels =
            new LogisticsEdgeDebugReadModel[edges.Count];

        for (int index = 0; index < edges.Count; index++)
        {
            LogisticsEdge edge = edges[index];
            edgeModels[index] =
                new LogisticsEdgeDebugReadModel(
                    edge.Id,
                    edge.Source,
                    edge.Destination,
                    edge.Mode,
                    positions[edge.Source],
                    positions[edge.Destination],
                    edge.CapacityPerSecond,
                    edge.Enabled);
        }

        LogisticsRouteDebugSegment[] routeSegments =
            CaptureRoute(route, positions);

        return new LogisticsNetworkDebugSnapshot(
            network.Version,
            network.Metrics,
            nodeModels,
            edgeModels,
            routeSegments);
    }

    private static LogisticsRouteDebugSegment[] CaptureRoute(
        LogisticsRoute? route,
        Dictionary<LogisticsNodeId, Vector3> positions)
    {
        if (route is null ||
            route.Segments.Count == 0)
        {
            return [];
        }

        var segments =
            new List<LogisticsRouteDebugSegment>(
                route.Segments.Count);

        for (int index = 0;
             index < route.Segments.Count;
             index++)
        {
            LogisticsRouteSegment segment =
                route.Segments[index];

            if (!positions.TryGetValue(
                    segment.From,
                    out Vector3 fromPosition) ||
                !positions.TryGetValue(
                    segment.To,
                    out Vector3 toPosition))
            {
                continue;
            }

            segments.Add(
                new LogisticsRouteDebugSegment(
                    segment.EdgeId,
                    segment.From,
                    segment.To,
                    fromPosition,
                    toPosition,
                    segment.Mode,
                    segment.Cost));
        }

        return segments.ToArray();
    }
}
