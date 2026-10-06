using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public static class RtsWorldMarkerVisualization
{
    public static void DrawSelected(
        DebugDraw draw,
        in RenderInstance instance,
        Vector4 color)
    {
        ArgumentNullException.ThrowIfNull(draw);

        Vector3 localExtents =
            PresentationBounds.ResolveLocalHalfExtents(
                instance);
        AxisAlignedBounds bounds =
            PresentationBounds.ResolveAxisAlignedBounds(
                instance);
        Vector3 worldExtents =
            (bounds.Maximum -
             bounds.Minimum) *
            0.5f;
        float markerY =
            PresentationBounds.ResolveGroundPlaneY(
                instance) +
            0.08f;

        if (instance.Selectable.Category ==
            ForgeLine.Game.ControllableEntityCategory.Building)
        {
            DrawFootprint(
                draw,
                instance.Transform.Position,
                instance.Transform.Rotation,
                localExtents,
                markerY,
                color);
            return;
        }

        draw.Circle(
            new Vector3(
                instance.Transform.Position.X,
                markerY,
                instance.Transform.Position.Z),
            MathF.Max(
                worldExtents.X,
                worldExtents.Z) *
            1.10f,
            color,
            24);
    }

    public static void DrawHover(
        DebugDraw draw,
        in RenderInstance instance,
        Vector4 color)
    {
        ArgumentNullException.ThrowIfNull(draw);

        AxisAlignedBounds bounds =
            PresentationBounds.ResolveAxisAlignedBounds(
                instance);
        Vector3 worldExtents =
            (bounds.Maximum -
             bounds.Minimum) *
            0.5f;
        Vector3 center =
            new(
                instance.Transform.Position.X,
                bounds.Maximum.Y +
                    0.20f,
                instance.Transform.Position.Z);
        float radius =
            MathF.Max(
                1.0f,
                MathF.Max(
                    worldExtents.X,
                    worldExtents.Z) *
                0.70f);

        Vector3 north =
            center +
            new Vector3(
                0.0f,
                0.0f,
                radius);
        Vector3 east =
            center +
            new Vector3(
                radius,
                0.0f,
                0.0f);
        Vector3 south =
            center -
            new Vector3(
                0.0f,
                0.0f,
                radius);
        Vector3 west =
            center -
            new Vector3(
                radius,
                0.0f,
                0.0f);

        draw.Line(
            north,
            east,
            color);
        draw.Line(
            east,
            south,
            color);
        draw.Line(
            south,
            west,
            color);
        draw.Line(
            west,
            north,
            color);
    }

    public static void DrawTarget(
        DebugDraw draw,
        Vector3 position,
        bool valid,
        Vector4 validColor,
        Vector4 invalidColor)
    {
        ArgumentNullException.ThrowIfNull(draw);

        Vector4 color =
            valid
                ? validColor
                : invalidColor;
        Vector3 elevated =
            position +
            Vector3.UnitY *
                0.2f;

        if (valid)
        {
            draw.Circle(
                elevated,
                4.0f,
                color,
                20);
            draw.Line(
                elevated -
                Vector3.UnitX *
                    6.0f,
                elevated +
                Vector3.UnitX *
                    6.0f,
                color);
            draw.Line(
                elevated -
                Vector3.UnitZ *
                    6.0f,
                elevated +
                Vector3.UnitZ *
                    6.0f,
                color);
            return;
        }

        Vector3 diagonal =
            new(
                4.0f,
                0.0f,
                4.0f);
        Vector3 antiDiagonal =
            new(
                4.0f,
                0.0f,
                -4.0f);

        draw.Line(
            elevated -
            diagonal,
            elevated +
            diagonal,
            color);
        draw.Line(
            elevated -
            antiDiagonal,
            elevated +
            antiDiagonal,
            color);
    }

    public static void DrawInvalidFootprint(
        DebugDraw draw,
        in AxisAlignedBounds bounds,
        Vector4 color)
    {
        ArgumentNullException.ThrowIfNull(draw);

        float y =
            bounds.Maximum.Y +
            0.1f;
        Vector3 first =
            new(
                bounds.Minimum.X,
                y,
                bounds.Minimum.Z);
        Vector3 second =
            new(
                bounds.Maximum.X,
                y,
                bounds.Maximum.Z);
        Vector3 third =
            new(
                bounds.Minimum.X,
                y,
                bounds.Maximum.Z);
        Vector3 fourth =
            new(
                bounds.Maximum.X,
                y,
                bounds.Minimum.Z);

        draw.Line(
            first,
            second,
            color);
        draw.Line(
            third,
            fourth,
            color);
    }

    private static void DrawFootprint(
        DebugDraw draw,
        Vector3 center,
        Quaternion rotation,
        Vector3 localExtents,
        float markerY,
        Vector4 color)
    {
        Vector3 p0 =
            RotateGroundCorner(
                center,
                rotation,
                -localExtents.X,
                -localExtents.Z,
                markerY);
        Vector3 p1 =
            RotateGroundCorner(
                center,
                rotation,
                localExtents.X,
                -localExtents.Z,
                markerY);
        Vector3 p2 =
            RotateGroundCorner(
                center,
                rotation,
                localExtents.X,
                localExtents.Z,
                markerY);
        Vector3 p3 =
            RotateGroundCorner(
                center,
                rotation,
                -localExtents.X,
                localExtents.Z,
                markerY);

        draw.Line(p0, p1, color);
        draw.Line(p1, p2, color);
        draw.Line(p2, p3, color);
        draw.Line(p3, p0, color);
    }

    private static Vector3 RotateGroundCorner(
        Vector3 center,
        Quaternion rotation,
        float localX,
        float localZ,
        float markerY)
    {
        Vector3 offset =
            Vector3.Transform(
                new Vector3(
                    localX,
                    0.0f,
                    localZ),
                rotation);

        return new Vector3(
            center.X +
                offset.X,
            markerY,
            center.Z +
                offset.Z);
    }
}
