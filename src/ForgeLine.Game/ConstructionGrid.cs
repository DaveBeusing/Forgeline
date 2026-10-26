using System.Numerics;

namespace ForgeLine.Game;

public static class ConstructionGrid
{
    public const float CellSizeMeters = 4;

    public static Vector3 Snap(Vector3 point) => new(
        MathF.Round(point.X / CellSizeMeters) * CellSizeMeters,
        point.Y,
        MathF.Round(point.Z / CellSizeMeters) * CellSizeMeters);
}
