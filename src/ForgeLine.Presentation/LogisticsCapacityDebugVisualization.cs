using System.Numerics;
using ForgeLine.Logistics;

namespace ForgeLine.Presentation;

public static class LogisticsCapacityDebugVisualization
{
    public static void Draw(
        DebugDraw debugDraw,
        LogisticsCapacityDebugSnapshot snapshot,
        int maximumNodes = 128,
        int maximumEdges = 256,
        int maximumLabels = 16)
    {
        ArgumentNullException.ThrowIfNull(debugDraw);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumNodes);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumEdges);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLabels);

        int edgeCount =
            Math.Min(
                snapshot.Edges.Count,
                maximumEdges);

        for (int index = 0; index < edgeCount; index++)
        {
            LogisticsEdgeCapacityReadModel edge =
                snapshot.Edges[index];

            debugDraw.Line(
                edge.SourcePosition + Vector3.UnitY * 0.75f,
                edge.DestinationPosition + Vector3.UnitY * 0.75f,
                GetStateColor(edge.State));
        }

        int nodeCount =
            Math.Min(
                snapshot.Nodes.Count,
                maximumNodes);
        int labels = 0;

        for (int index = 0; index < nodeCount; index++)
        {
            LogisticsNodeCapacityReadModel node =
                snapshot.Nodes[index];
            Vector4 color =
                GetStateColor(node.State);

            debugDraw.Point(
                node.WorldPosition + Vector3.UnitY,
                node.State == LogisticsLoadState.Blocked
                    ? 5.0f
                    : 3.0f,
                color);

            if (labels >= maximumLabels ||
                (node.State == LogisticsLoadState.Healthy &&
                 node.Utilization < 0.01))
            {
                continue;
            }

            debugDraw.Label(
                node.WorldPosition + Vector3.UnitY * 2.25f,
                $"LOG {node.State} {node.Utilization:P0}",
                color);
            labels++;
        }

        if (snapshot.Nodes.Count > 0 &&
            labels < maximumLabels &&
            (snapshot.Health.BacklogRequestCount > 0 ||
             snapshot.Health.HasSaturation ||
             snapshot.Health.HasBlockedInfrastructure))
        {
            LogisticsNodeCapacityReadModel anchor =
                snapshot.Nodes[0];

            debugDraw.Label(
                anchor.WorldPosition + new Vector3(0.0f, 4.0f, 0.0f),
                $"LOGISTICS backlog={snapshot.Health.BacklogRequestCount} " +
                $"qty={snapshot.Health.BacklogQuantity:F1} " +
                $"denied={snapshot.Health.DeniedReservationCount}",
                GetSummaryColor(snapshot.Health));
        }
    }

    private static Vector4 GetStateColor(
        LogisticsLoadState state) =>
        state switch
        {
            LogisticsLoadState.Healthy =>
                new Vector4(0.25f, 0.85f, 0.35f, 0.75f),
            LogisticsLoadState.Busy =>
                new Vector4(0.95f, 0.8f, 0.2f, 0.9f),
            LogisticsLoadState.Saturated =>
                new Vector4(1.0f, 0.45f, 0.1f, 1.0f),
            LogisticsLoadState.Blocked =>
                new Vector4(1.0f, 0.15f, 0.15f, 1.0f),
            _ =>
                new Vector4(0.8f, 0.8f, 0.8f, 0.75f)
        };

    private static Vector4 GetSummaryColor(
        LogisticsHealthSummary health)
    {
        if (health.HasBlockedInfrastructure)
        {
            return GetStateColor(LogisticsLoadState.Blocked);
        }

        if (health.HasSaturation)
        {
            return GetStateColor(LogisticsLoadState.Saturated);
        }

        return GetStateColor(LogisticsLoadState.Busy);
    }
}
