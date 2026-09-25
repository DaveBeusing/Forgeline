using System.Numerics;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public static class IntelligenceDebugVisualization
{
    public static void Draw(
        DebugDraw debugDraw,
        FactionIntelligenceSnapshot snapshot,
        float planeY = 0.15f,
        int maximumCells = 1_024,
        int maximumContacts = 128)
    {
        ArgumentNullException.ThrowIfNull(debugDraw);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!float.IsFinite(planeY))
        {
            throw new ArgumentOutOfRangeException(nameof(planeY));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(maximumCells);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumContacts);

        int cellCount =
            Math.Min(
                snapshot.Cells.Count,
                maximumCells);

        for (int index = 0;
             index < cellCount;
             index++)
        {
            VisibilityCellSnapshot cell =
                snapshot.Cells[index];

            Vector4 color =
                cell.State switch
                {
                    IntelligenceState.Visible =>
                        new Vector4(0.15f, 0.85f, 0.35f, 0.65f),
                    IntelligenceState.Explored =>
                        new Vector4(0.35f, 0.45f, 0.55f, 0.45f),
                    IntelligenceState.Unexplored =>
                        new Vector4(0.4f, 0.12f, 0.12f, 0.35f),
                    _ =>
                        new Vector4(0.55f, 0.25f, 0.65f, 0.4f)
                };

            float size =
                snapshot.CellSizeMeters;
            float minimumX =
                cell.Cell.X * size;
            float minimumZ =
                cell.Cell.Z * size;

            debugDraw.Box(
                new AxisAlignedBounds(
                    new Vector3(
                        minimumX,
                        planeY,
                        minimumZ),
                    new Vector3(
                        minimumX + size,
                        planeY + 0.05f,
                        minimumZ + size)),
                color);
        }

        int contactCount =
            Math.Min(
                snapshot.Contacts.Count,
                maximumContacts);

        for (int index = 0;
             index < contactCount;
             index++)
        {
            IntelligenceContact contact =
                snapshot.Contacts[index];

            Vector4 color =
                contact.State == IntelligenceState.Identified
                    ? new Vector4(1.0f, 0.35f, 0.15f, 1.0f)
                    : new Vector4(0.85f, 0.3f, 1.0f, 1.0f);

            debugDraw.Point(
                contact.LastKnownPosition,
                contact.IsCurrent ? 2.0f : 1.0f,
                color);

            debugDraw.Label(
                contact.LastKnownPosition +
                    Vector3.UnitY * 2.0f,
                contact.State == IntelligenceState.Identified
                    ? $"ID {contact.IdentityKey}"
                    : "RADAR",
                color);
        }
    }

    public static void DrawSensors(
        DebugDraw debugDraw,
        IReadOnlyList<IntelligenceSensorDebugEntry> sensors,
        int maximumSensors = 64)
    {
        ArgumentNullException.ThrowIfNull(debugDraw);
        ArgumentNullException.ThrowIfNull(sensors);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumSensors);

        int count =
            Math.Min(
                sensors.Count,
                maximumSensors);

        for (int index = 0;
             index < count;
             index++)
        {
            IntelligenceSensorDebugEntry sensor =
                sensors[index];

            Vector4 color =
                sensor.IsRadar
                    ? new Vector4(0.65f, 0.25f, 1.0f, 0.55f)
                    : new Vector4(0.15f, 0.8f, 1.0f, 0.55f);

            debugDraw.Circle(
                sensor.Position,
                sensor.RangeMeters,
                color,
                segments: 32);

            if (sensor.IsRadar &&
                sensor.IdentificationRangeMeters > 0.0f)
            {
                debugDraw.Circle(
                    sensor.Position,
                    sensor.IdentificationRangeMeters,
                    new Vector4(1.0f, 0.55f, 0.15f, 0.65f),
                    segments: 24);
            }
        }
    }

    public static void DrawMetrics(
        DebugDraw debugDraw,
        in BattlefieldIntelligenceMetrics metrics,
        Vector3 position)
    {
        ArgumentNullException.ThrowIfNull(debugDraw);

        string text =
            $"INT scans={metrics.SensorScansThisTick} " +
            $"contacts={metrics.DetectedContactsThisTick + metrics.IdentifiedContactsThisTick} " +
            $"visible={metrics.VisibleCellsThisTick} " +
            $"ms={metrics.SensorUpdateDuration.TotalMilliseconds:F3}";

        debugDraw.Label(
            position,
            text,
            new Vector4(0.75f, 0.9f, 1.0f, 1.0f));
    }
}
