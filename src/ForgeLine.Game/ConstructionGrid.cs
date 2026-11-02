using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Game;

public static class ConstructionGrid
{
    public const float CellSizeMeters = 4;

    public static Vector3 Snap(Vector3 point) => new(
        MathF.Round(point.X / CellSizeMeters) * CellSizeMeters,
        point.Y,
        MathF.Round(point.Z / CellSizeMeters) * CellSizeMeters);

    // Physical building dimensions are not resized. Only the logical reservation
    // expands to complete grid cells, centered around the authored building pivot.
    public static (int Width, int Depth) BuildingCells(
        BuildingFootprint footprint, BuildingOrientation orientation)
    {
        Vector3 half = footprint.GetHalfExtents(orientation);
        return (
            checked((int)MathF.Ceiling(2.0f * half.X / CellSizeMeters)),
            checked((int)MathF.Ceiling(2.0f * half.Z / CellSizeMeters)));
    }

    public static Vector3 SnapBuilding(
        Vector3 point, BuildingFootprint footprint, BuildingOrientation orientation)
    {
        var cells = BuildingCells(footprint, orientation);
        return new Vector3(
            SnapAxis(point.X, cells.Width), point.Y, SnapAxis(point.Z, cells.Depth));
    }

    public static bool IsBuildingAligned(
        Vector3 point, BuildingFootprint footprint, BuildingOrientation orientation)
    {
        Vector3 snapped = SnapBuilding(point, footprint, orientation);
        return MathF.Abs(point.X - snapped.X) <= 0.001f &&
               MathF.Abs(point.Z - snapped.Z) <= 0.001f;
    }

    public static AxisAlignedBounds BuildingEnvelope(
        Vector3 ground, BuildingFootprint footprint, BuildingOrientation orientation)
    {
        var cells = BuildingCells(footprint, orientation);
        float halfWidth = cells.Width * CellSizeMeters * 0.5f;
        float halfDepth = cells.Depth * CellSizeMeters * 0.5f;
        return new AxisAlignedBounds(
            new Vector3(ground.X - halfWidth, ground.Y, ground.Z - halfDepth),
            new Vector3(ground.X + halfWidth, ground.Y + footprint.Height, ground.Z + halfDepth));
    }

    private static float SnapAxis(float position, int cells)
    {
        // Odd-sized envelopes use half-cell centers, so both edges still lie
        // on the same zero-origin grid as freely placed conveyor segments.
        float phase = (cells & 1) == 0 ? 0.0f : CellSizeMeters * 0.5f;
        return MathF.Round((position - phase) / CellSizeMeters) * CellSizeMeters + phase;
    }
}
