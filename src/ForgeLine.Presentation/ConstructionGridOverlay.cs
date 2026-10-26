using System.Numerics;
using ForgeLine.Game;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public static class ConstructionGridOverlay
{
    public const int HalfCells = 6;
    public const int MaximumLines = 2 * (HalfCells * 2 + 1) * HalfCells * 2;

    public static void Draw(DebugDraw draw, ITerrainQuery terrain, Vector3 center)
    {
        ArgumentNullException.ThrowIfNull(draw);
        ArgumentNullException.ThrowIfNull(terrain);
        if (!draw.Enabled || !float.IsFinite(center.X) || !float.IsFinite(center.Y) || !float.IsFinite(center.Z)) return;
        center = ConstructionGrid.Snap(center);
        float cell = ConstructionGrid.CellSizeMeters;
        for (int line = -HalfCells; line <= HalfCells; line++)
            for (int segment = -HalfCells; segment < HalfCells; segment++)
            {
                Add(center.X + line * cell, center.Z + segment * cell, center.X + line * cell, center.Z + (segment + 1) * cell);
                Add(center.X + segment * cell, center.Z + line * cell, center.X + (segment + 1) * cell, center.Z + line * cell);
            }

        void Add(float ax, float az, float bx, float bz)
        {
            if (terrain.TrySampleHeight(ax, az, out float ay) && terrain.TrySampleHeight(bx, bz, out float by) &&
                float.IsFinite(ay) && float.IsFinite(by))
                draw.Line(new(ax, ay + .12f, az), new(bx, by + .12f, bz), new(.72f, .84f, .92f, .3f));
        }
    }
}
