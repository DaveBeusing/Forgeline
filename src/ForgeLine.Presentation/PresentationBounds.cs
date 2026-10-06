using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public static class PresentationBounds
{
    private static readonly Vector3 MinimumHalfExtents =
        new(
            0.05f);

    public static Vector3 ResolveLocalHalfExtents(
        in RenderInstance instance)
    {
        Vector3 halfExtents =
            Vector3.Max(
                Vector3.Abs(
                    instance.Transform.Scale) *
                0.5f,
                MinimumHalfExtents);

        if (!instance.UnitFeature.IsSpecified)
        {
            return halfExtents;
        }

        return halfExtents *
            UnitPresentationCatalog.ResolveReadabilityBoundsScale(
                instance.UnitFeature.Unit);
    }

    public static Vector3 ResolveWorldAxisAlignedHalfExtents(
        in RenderInstance instance)
    {
        Vector3 local =
            ResolveLocalHalfExtents(
                instance);
        Quaternion rotation =
            ResolveRotation(
                instance.Transform.Rotation);

        Vector3 xAxis =
            Vector3.Transform(
                new Vector3(
                    local.X,
                    0.0f,
                    0.0f),
                rotation);
        Vector3 yAxis =
            Vector3.Transform(
                new Vector3(
                    0.0f,
                    local.Y,
                    0.0f),
                rotation);
        Vector3 zAxis =
            Vector3.Transform(
                new Vector3(
                    0.0f,
                    0.0f,
                    local.Z),
                rotation);

        return Vector3.Abs(
                   xAxis) +
               Vector3.Abs(
                   yAxis) +
               Vector3.Abs(
                   zAxis);
    }

    public static AxisAlignedBounds ResolveAxisAlignedBounds(
        in RenderInstance instance)
    {
        Vector3 extents =
            ResolveWorldAxisAlignedHalfExtents(
                instance);

        return new AxisAlignedBounds(
            instance.Transform.Position -
                extents,
            instance.Transform.Position +
                extents);
    }

    public static float ResolveGroundPlaneY(
        in RenderInstance instance) =>
        instance.Transform.Position.Y -
        MathF.Max(
            MathF.Abs(
                instance.Transform.Scale.Y) *
            0.5f,
            MinimumHalfExtents.Y);

    private static Quaternion ResolveRotation(
        Quaternion rotation)
    {
        float lengthSquared =
            rotation.LengthSquared();

        if (!float.IsFinite(
                lengthSquared) ||
            lengthSquared <=
            1e-8f)
        {
            return Quaternion.Identity;
        }

        return Quaternion.Normalize(
            rotation);
    }
}
