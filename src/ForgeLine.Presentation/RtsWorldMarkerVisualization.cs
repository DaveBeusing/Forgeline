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

        Vector3 extents =
            Vector3.Max(
                Vector3.Abs(
                    instance.Transform.Scale) *
                0.5f,
                new Vector3(
                    0.5f));

        if (instance.Selectable.Category ==
            ForgeLine.Game.ControllableEntityCategory.Building)
        {
            DrawFootprint(
                draw,
                instance.Transform.Position,
                extents,
                color);
            return;
        }

        draw.Circle(
            instance.Transform.Position +
            Vector3.UnitY *
                0.15f,
            MathF.Max(
                extents.X,
                extents.Z) *
            1.15f,
            color,
            24);
    }

    public static void DrawHover(
        DebugDraw draw,
        in RenderInstance instance,
        Vector4 color)
    {
        ArgumentNullException.ThrowIfNull(draw);

        Vector3 center =
            instance.Transform.Position +
            Vector3.UnitY *
                MathF.Max(
                    0.25f,
                    MathF.Abs(
                        instance.Transform.Scale.Y) *
                    0.55f);
        float radius =
            MathF.Max(
                1.0f,
                MathF.Max(
                    MathF.Abs(
                        instance.Transform.Scale.X),
                    MathF.Abs(
                        instance.Transform.Scale.Z)) *
                0.35f);

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
        Vector3 extents,
        Vector4 color)
    {
        float y =
            center.Y +
            0.15f;
        Vector3 p0 =
            new(
                center.X -
                extents.X,
                y,
                center.Z -
                extents.Z);
        Vector3 p1 =
            new(
                center.X +
                extents.X,
                y,
                center.Z -
                extents.Z);
        Vector3 p2 =
            new(
                center.X +
                extents.X,
                y,
                center.Z +
                extents.Z);
        Vector3 p3 =
            new(
                center.X -
                extents.X,
                y,
                center.Z +
                extents.Z);

        draw.Line(p0, p1, color);
        draw.Line(p1, p2, color);
        draw.Line(p2, p3, color);
        draw.Line(p3, p0, color);
    }
}
