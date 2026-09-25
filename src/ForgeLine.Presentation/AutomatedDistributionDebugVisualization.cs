using System.Numerics;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

public static class AutomatedDistributionDebugVisualization
{
    public static void Draw(
        DebugDraw debugDraw,
        AutomatedDistributionDebugSnapshot snapshot,
        int maximumRequests = 128,
        int maximumLabels = 16)
    {
        ArgumentNullException.ThrowIfNull(debugDraw);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRequests);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLabels);

        int count = Math.Min(
            snapshot.Requests.Count,
            maximumRequests);
        int labels = 0;

        for (int index = 0; index < count; index++)
        {
            LogisticsTransportRequestReadModel request =
                snapshot.Requests[index];
            Vector4 color = GetStateColor(request.State);

            debugDraw.Point(
                request.DestinationPosition + Vector3.UnitY * 1.25f,
                2.5f,
                color);

            if (request.Origin.IsSpecified)
            {
                debugDraw.Line(
                    request.OriginPosition + Vector3.UnitY * 1.5f,
                    request.DestinationPosition + Vector3.UnitY * 1.5f,
                    color);
            }

            if (labels >= maximumLabels)
            {
                continue;
            }

            Vector3 labelPosition =
                request.Origin.IsSpecified
                    ? Vector3.Lerp(
                        request.OriginPosition,
                        request.DestinationPosition,
                        0.5f) + Vector3.UnitY * 2.0f
                    : request.DestinationPosition + Vector3.UnitY * 2.0f;

            string quantity =
                request.RequestedQuantity.ToString(
                    "F1",
                    System.Globalization.CultureInfo.InvariantCulture);

            debugDraw.Label(
                labelPosition,
                $"DIST R{request.RequestId.Value} {request.State} q={quantity}",
                color);
            labels++;
        }
    }

    private static Vector4 GetStateColor(
        LogisticsTransportRequestState state) =>
        state switch
        {
            LogisticsTransportRequestState.Assigned =>
                new Vector4(0.2f, 0.85f, 1.0f, 1.0f),
            LogisticsTransportRequestState.InTransit =>
                new Vector4(0.2f, 1.0f, 0.45f, 1.0f),
            LogisticsTransportRequestState.RetryPending =>
                new Vector4(1.0f, 0.75f, 0.15f, 1.0f),
            LogisticsTransportRequestState.Failed =>
                new Vector4(1.0f, 0.2f, 0.15f, 1.0f),
            LogisticsTransportRequestState.Completed =>
                new Vector4(0.55f, 0.85f, 0.55f, 0.85f),
            _ =>
                new Vector4(0.75f, 0.75f, 1.0f, 1.0f)
        };
}
