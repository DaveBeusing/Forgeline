using System.Numerics;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

public static class CargoTransportDebugVisualization
{
    public static void Draw(
        DebugDraw debugDraw,
        CargoTransportDebugSnapshot snapshot,
        int maximumTransports = 128,
        int maximumLabels = 16)
    {
        ArgumentNullException.ThrowIfNull(debugDraw);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumTransports);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLabels);

        int count = Math.Min(
            snapshot.Transports.Count,
            maximumTransports);
        int labels = 0;

        for (int index = 0;
             index < count;
             index++)
        {
            CargoTransportReadModel transport =
                snapshot.Transports[index];
            Vector4 color =
                GetStateColor(
                    transport.Lifecycle);

            debugDraw.Point(
                transport.WorldPosition +
                    Vector3.UnitY * 1.5f,
                3.0f,
                color);

            if (transport.HasMovementTarget)
            {
                debugDraw.Line(
                    transport.WorldPosition +
                        Vector3.UnitY * 1.0f,
                    transport.MovementTarget +
                        Vector3.UnitY * 1.0f,
                    color);
            }

            if (labels >= maximumLabels)
            {
                continue;
            }

            string quantity =
                transport.CargoQuantity.ToString(
                    "F1",
                    System.Globalization.CultureInfo.InvariantCulture);

            debugDraw.Label(
                transport.WorldPosition +
                    Vector3.UnitY * 2.5f,
                $"CARGO E{transport.Entity.Index} {transport.Lifecycle} q={quantity}",
                color);
            labels++;
        }
    }

    private static Vector4 GetStateColor(
        CargoTransportLifecycleState state) =>
        state switch
        {
            CargoTransportLifecycleState.Idle =>
                new Vector4(
                    0.55f,
                    0.75f,
                    0.95f,
                    1.0f),
            CargoTransportLifecycleState.Loading or
            CargoTransportLifecycleState.Unloading =>
                new Vector4(
                    0.2f,
                    1.0f,
                    0.45f,
                    1.0f),
            CargoTransportLifecycleState.Waiting =>
                new Vector4(
                    1.0f,
                    0.75f,
                    0.15f,
                    1.0f),
            CargoTransportLifecycleState.Failed =>
                new Vector4(
                    1.0f,
                    0.2f,
                    0.15f,
                    1.0f),
            _ =>
                new Vector4(
                    0.35f,
                    0.8f,
                    1.0f,
                    1.0f)
        };
}
