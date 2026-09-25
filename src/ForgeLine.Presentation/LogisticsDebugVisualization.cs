using System.Numerics;
using ForgeLine.Logistics;

namespace ForgeLine.Presentation;

public static class LogisticsDebugVisualization
{
    public static void Draw(
        DebugDraw debugDraw,
        LogisticsNetworkDebugSnapshot snapshot,
        int maximumNodes = 128,
        int maximumEdges = 256,
        int maximumLabels = 16)
    {
        ArgumentNullException.ThrowIfNull(debugDraw);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumNodes);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumEdges);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLabels);

        Vector4 enabledNodeColor =
            new(0.2f, 0.9f, 0.55f, 1.0f);
        Vector4 disabledNodeColor =
            new(0.55f, 0.55f, 0.55f, 1.0f);
        Vector4 enabledEdgeColor =
            new(0.25f, 0.65f, 1.0f, 0.75f);
        Vector4 disabledEdgeColor =
            new(1.0f, 0.25f, 0.2f, 0.9f);
        Vector4 routeColor =
            new(1.0f, 0.8f, 0.15f, 1.0f);

        int edgeCount = Math.Min(
            snapshot.Edges.Count,
            maximumEdges);

        for (int index = 0; index < edgeCount; index++)
        {
            LogisticsEdgeDebugReadModel edge =
                snapshot.Edges[index];

            debugDraw.Line(
                edge.SourcePosition + Vector3.UnitY * 0.5f,
                edge.DestinationPosition + Vector3.UnitY * 0.5f,
                edge.Enabled
                    ? enabledEdgeColor
                    : disabledEdgeColor);
        }

        int routeCount =
            snapshot.RouteSegments.Count;

        for (int index = 0;
             index < routeCount;
             index++)
        {
            LogisticsRouteDebugSegment segment =
                snapshot.RouteSegments[index];

            debugDraw.Line(
                segment.FromPosition + Vector3.UnitY * 1.0f,
                segment.ToPosition + Vector3.UnitY * 1.0f,
                routeColor);
        }

        int nodeCount = Math.Min(
            snapshot.Nodes.Count,
            maximumNodes);
        int labels = 0;

        for (int index = 0; index < nodeCount; index++)
        {
            LogisticsNodeDebugReadModel node =
                snapshot.Nodes[index];

            Vector4 color = node.Enabled
                ? enabledNodeColor
                : disabledNodeColor;

            debugDraw.Point(
                node.WorldPosition + Vector3.UnitY * 0.75f,
                2.0f,
                color);

            if (labels >= maximumLabels)
            {
                continue;
            }

            debugDraw.Label(
                node.WorldPosition + Vector3.UnitY * 1.5f,
                $"L{node.Id.Value} {node.Kind}",
                color);
            labels++;
        }
    }
}
