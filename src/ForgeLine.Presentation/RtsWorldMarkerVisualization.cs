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
        DrawRing(draw, instance, color, terrain, dashed: false, minimumRadius);
    }

    public static void DrawHover(
        DebugDraw draw,
        in RenderInstance instance,
        Vector4 color,
        ITerrainQuery? terrain = null,
        float minimumRadius = 1.0f,
        bool foreignOwned = false)
    {
        DrawRing(draw, instance, color, terrain, dashed: true, minimumRadius);
        if (foreignOwned && draw.Enabled && (instance.Visibility & RenderVisibilityMask.World) != 0 &&
            instance.Mesh.IsValid && instance.Material.IsValid)
        {
            Vector3 extents = PresentationBounds.ResolveLocalHalfExtents(instance);
            float radius = MathF.Max(minimumRadius, new Vector2(extents.X, extents.Z).Length() * 1.08f);
            Vector3 center = instance.Transform.Position;
            float fallbackHeight = PresentationBounds.ResolveGroundPlaneY(instance);
            for (int index = 0; index < 4; index++)
            {
                float angle = index * MathF.PI * 0.5f;
                Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
                draw.Line(GroundPoint(center.X + direction.X * radius, center.Z + direction.Y * radius, fallbackHeight, terrain),
                    GroundPoint(center.X + direction.X * radius * 1.2f, center.Z + direction.Y * radius * 1.2f, fallbackHeight, terrain), color);
            }
        }
    }

    private static void DrawRing(
        DebugDraw draw,
        in RenderInstance instance,
        Vector4 color,
        ITerrainQuery? terrain,
        bool dashed,
        float minimumRadius)
    {
        ArgumentNullException.ThrowIfNull(draw);
        if (!draw.Enabled || (instance.Visibility & RenderVisibilityMask.World) == 0 ||
            !instance.Mesh.IsValid || !instance.Material.IsValid)
        {
            return;
        }

        Vector3 extents = PresentationBounds.ResolveLocalHalfExtents(instance);
        float radius = MathF.Max(float.IsFinite(minimumRadius) ? MathF.Max(1.0f, minimumRadius) : 1.0f,
            new Vector2(extents.X, extents.Z).Length() * 1.08f);
        Vector3 center = instance.Transform.Position;
        float fallbackHeight = PresentationBounds.ResolveGroundPlaneY(instance);
        const int segments = 24;
        Vector3 previous = GroundPoint(center.X + radius, center.Z, fallbackHeight, terrain);
        for (int index = 1; index <= segments; index++)
        {
            float angle = index * (MathF.Tau / segments);
            Vector3 next = GroundPoint(
                center.X + MathF.Cos(angle) * radius,
                center.Z + MathF.Sin(angle) * radius,
                fallbackHeight,
                terrain);
            if (!dashed || (index & 1) != 0)
            {
                draw.Line(previous, next, color);
            }
            previous = next;
        }
    }

    private static Vector3 GroundPoint(float x, float z, float fallbackHeight, ITerrainQuery? terrain)
    {
        float height = terrain is not null && terrain.TrySampleHeight(x, z, out float sampled) && float.IsFinite(sampled)
            ? sampled
            : fallbackHeight;
        return new Vector3(x, height + 0.08f, z);
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
