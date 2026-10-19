using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public static class RtsWorldMarkerVisualization
{
    public static void DrawSelected(
        DebugDraw draw,
        in RenderInstance instance,
        Vector4 color,
        ITerrainQuery? terrain = null,
        float minimumRadius = 1.0f)
    {
        DrawCorners(draw, instance, color, terrain, minimumRadius, hovered: false, foreignOwned: false);
    }

    public static void DrawHover(
        DebugDraw draw,
        in RenderInstance instance,
        Vector4 color,
        ITerrainQuery? terrain = null,
        float minimumRadius = 1.0f,
        bool foreignOwned = false)
    {
        DrawCorners(draw, instance, color, terrain, minimumRadius, hovered: true, foreignOwned);
    }

    // Selected members retain four oriented L-corners. Hover uses diagonal corner
    // notches, with an outward tick for foreign-owned objects. No ellipse is emitted.
    private static void DrawCorners(
        DebugDraw draw,
        in RenderInstance instance,
        Vector4 color,
        ITerrainQuery? terrain,
        float minimumRadius,
        bool hovered,
        bool foreignOwned)
    {
        ArgumentNullException.ThrowIfNull(draw);
        if (!draw.Enabled || (instance.Visibility & RenderVisibilityMask.World) == 0 ||
            !instance.Mesh.IsValid || !instance.Material.IsValid)
            return;

        Vector3 extents = PresentationBounds.ResolveLocalHalfExtents(instance);
        float minimum = float.IsFinite(minimumRadius) ? MathF.Max(0.5f, minimumRadius) : 1f;
        float insetX = MathF.Min(extents.X * 0.6f, MathF.Max(extents.X * 0.28f, minimum * 0.5f));
        float insetZ = MathF.Min(extents.Z * 0.6f, MathF.Max(extents.Z * 0.28f, minimum * 0.5f));

        for (int corner = 0; corner < 4; corner++)
        {
            Vector3 outer = TacticalFootprint.Corner(instance, corner, terrain: terrain);
            if (hovered)
            {
                draw.Line(outer,
                    TacticalFootprint.Corner(instance, corner, insetX, insetZ, terrain), color);
                if (foreignOwned)
                    draw.Line(outer,
                        TacticalFootprint.Corner(instance, corner, -insetX * 0.4f, -insetZ * 0.4f, terrain), color);
            }
            else
            {
                draw.Line(outer,
                    TacticalFootprint.Corner(instance, corner, insetX, 0, terrain), color);
                draw.Line(outer,
                    TacticalFootprint.Corner(instance, corner, 0, insetZ, terrain), color);
            }
        }
    }

    public static void DrawTarget(
        DebugDraw draw,
        Vector3 position,
        bool valid,
        Vector4 validColor,
        Vector4 invalidColor)
    {
        ArgumentNullException.ThrowIfNull(draw);
        Vector4 color = valid ? validColor : invalidColor;
        Vector3 elevated = position + Vector3.UnitY * 0.2f;
        if (valid)
        {
            draw.Circle(elevated, 4.0f, color, 20);
            draw.Line(elevated - Vector3.UnitX * 6.0f, elevated + Vector3.UnitX * 6.0f, color);
            draw.Line(elevated - Vector3.UnitZ * 6.0f, elevated + Vector3.UnitZ * 6.0f, color);
            return;
        }
        Vector3 diagonal = new(4.0f, 0.0f, 4.0f);
        Vector3 antiDiagonal = new(4.0f, 0.0f, -4.0f);
        draw.Line(elevated - diagonal, elevated + diagonal, color);
        draw.Line(elevated - antiDiagonal, elevated + antiDiagonal, color);
    }

    public static void DrawInvalidFootprint(DebugDraw draw, in AxisAlignedBounds bounds, Vector4 color)
    {
        ArgumentNullException.ThrowIfNull(draw);
        float y = bounds.Maximum.Y + 0.1f;
        draw.Line(new(bounds.Minimum.X, y, bounds.Minimum.Z), new(bounds.Maximum.X, y, bounds.Maximum.Z), color);
        draw.Line(new(bounds.Minimum.X, y, bounds.Maximum.Z), new(bounds.Maximum.X, y, bounds.Minimum.Z), color);
    }
}
