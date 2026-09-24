using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public static class SpatialIndexDebugVisualization
{
    public static void DrawOccupiedCells(
        DebugDraw debugDraw,
        SpatialIndexDebugSnapshot snapshot,
        Vector4 normalColor,
        Vector4 denseColor,
        int maximumCells = 256,
        int maximumLabels = 16)
    {
        ArgumentNullException.ThrowIfNull(debugDraw);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCells);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLabels);

        int cellCount = Math.Min(snapshot.Cells.Count, maximumCells);
        for (int index = 0; index < cellCount; index++)
        {
            SpatialDebugCell cell = snapshot.Cells[index];
            Vector4 color = cell.Occupancy > 4
                ? denseColor
                : normalColor;

            debugDraw.Box(cell.Bounds, color);

            if (index < maximumLabels)
            {
                debugDraw.Label(
                    cell.Bounds.Center,
                    $"{cell.Address} n={cell.Occupancy}",
                    color);
            }
        }
    }

    public static void DrawAabbQuery(
        DebugDraw debugDraw,
        AxisAlignedBounds bounds,
        Vector4 color)
    {
        ArgumentNullException.ThrowIfNull(debugDraw);
        debugDraw.Box(bounds, color);
    }

    public static void DrawRadiusQuery(
        DebugDraw debugDraw,
        Vector3 center,
        float radius,
        Vector4 color)
    {
        ArgumentNullException.ThrowIfNull(debugDraw);
        debugDraw.Circle(center, radius, color, segments: 48);
    }
}
