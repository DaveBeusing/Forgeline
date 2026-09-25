using System.Numerics;
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
}
