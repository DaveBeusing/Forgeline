using System.Numerics;

namespace ForgeLine.World;

internal static class SpatialGeometry
{
    public static bool Contains(
        in AxisAlignedBounds bounds,
        Vector3 point)
    {
        return point.X >= bounds.Minimum.X &&
               point.X <= bounds.Maximum.X &&
               point.Y >= bounds.Minimum.Y &&
               point.Y <= bounds.Maximum.Y &&
               point.Z >= bounds.Minimum.Z &&
               point.Z <= bounds.Maximum.Z;
    }

    public static bool Intersects(
        in AxisAlignedBounds left,
        in AxisAlignedBounds right)
    {
        return left.Minimum.X <= right.Maximum.X &&
               left.Maximum.X >= right.Minimum.X &&
               left.Minimum.Y <= right.Maximum.Y &&
               left.Maximum.Y >= right.Minimum.Y &&
               left.Minimum.Z <= right.Maximum.Z &&
               left.Maximum.Z >= right.Minimum.Z;
    }

    public static float HorizontalDistanceSquared(
        Vector3 point,
        in AxisAlignedBounds bounds)
    {
        float closestX = Math.Clamp(
            point.X,
            bounds.Minimum.X,
            bounds.Maximum.X);
        float closestZ = Math.Clamp(
            point.Z,
            bounds.Minimum.Z,
            bounds.Maximum.Z);

        float deltaX = point.X - closestX;
        float deltaZ = point.Z - closestZ;
        return deltaX * deltaX + deltaZ * deltaZ;
    }
}
