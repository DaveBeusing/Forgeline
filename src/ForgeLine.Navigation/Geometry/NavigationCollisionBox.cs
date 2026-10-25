using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Navigation;

/// <summary>Body-centered OBB used only by bounded physical clearance queries.</summary>
internal readonly record struct NavigationCollisionBox(
    Vector3 Center, Vector3 AxisX, Vector3 AxisY, Vector3 AxisZ, Vector3 HalfExtents)
{
    public AxisAlignedBounds Bounds
    {
        get
        {
            Vector3 extent = Vector3.Abs(AxisX) * HalfExtents.X +
                Vector3.Abs(AxisY) * HalfExtents.Y + Vector3.Abs(AxisZ) * HalfExtents.Z;
            return new AxisAlignedBounds(Center - extent, Center + extent);
        }
    }

    public static NavigationCollisionBox At(in NavigationPose pose, Vector3 halfExtents)
    {
        Quaternion rotation = Quaternion.Normalize(pose.Rotation);
        return new NavigationCollisionBox(pose.Position,
            Vector3.Transform(Vector3.UnitX, rotation),
            Vector3.Transform(Vector3.UnitY, rotation),
            Vector3.Transform(Vector3.UnitZ, rotation), halfExtents);
    }

    public static NavigationCollisionBox AxisAligned(Vector3 center, Vector3 halfExtents) =>
        new(center, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, halfExtents);

    public static bool Intersects(in NavigationCollisionBox left, in NavigationCollisionBox right)
    {
        AxisAlignedBounds a = left.Bounds;
        AxisAlignedBounds b = right.Bounds;
        if (a.Maximum.X < b.Minimum.X || a.Minimum.X > b.Maximum.X ||
            a.Maximum.Y < b.Minimum.Y || a.Minimum.Y > b.Maximum.Y ||
            a.Maximum.Z < b.Minimum.Z || a.Minimum.Z > b.Maximum.Z)
            return false;

        // Test the three axes of both boxes and their nine cross products.
        // An almost parallel cross axis may be omitted conservatively.
        Span<Vector3> axes = stackalloc Vector3[15]
        {
            left.AxisX, left.AxisY, left.AxisZ, right.AxisX, right.AxisY, right.AxisZ,
            Vector3.Cross(left.AxisX, right.AxisX), Vector3.Cross(left.AxisX, right.AxisY),
            Vector3.Cross(left.AxisX, right.AxisZ), Vector3.Cross(left.AxisY, right.AxisX),
            Vector3.Cross(left.AxisY, right.AxisY), Vector3.Cross(left.AxisY, right.AxisZ),
            Vector3.Cross(left.AxisZ, right.AxisX), Vector3.Cross(left.AxisZ, right.AxisY),
            Vector3.Cross(left.AxisZ, right.AxisZ)
        };
        Vector3 delta = right.Center - left.Center;
        foreach (Vector3 axis in axes)
        {
            if (axis.LengthSquared() <= 1e-12f) continue;
            double distance = Math.Abs(Dot(delta, axis));
            double radius = Projection(left, axis) + Projection(right, axis);
            // Round-off must enlarge the occupied interval, never open a gap.
            double tolerance = (distance + radius + 1) * 2e-6;
            if (distance > radius + tolerance) return false;
        }
        return true;
    }

    public static double Radius(Vector3 halfExtents) => Math.Sqrt(Dot(halfExtents, halfExtents));

    public static float NumericalPadding(Vector3 position, double radius) =>
        (float)((Math.Max(Math.Max(Math.Abs(position.X), Math.Abs(position.Y)), Math.Abs(position.Z)) + radius + 1) * 2e-6);

    private static double Projection(in NavigationCollisionBox box, Vector3 axis) =>
        Math.Abs(Dot(box.AxisX, axis)) * box.HalfExtents.X +
        Math.Abs(Dot(box.AxisY, axis)) * box.HalfExtents.Y +
        Math.Abs(Dot(box.AxisZ, axis)) * box.HalfExtents.Z;

    internal static double Dot(Vector3 left, Vector3 right) =>
        (double)left.X * right.X + (double)left.Y * right.Y + (double)left.Z * right.Z;
}

internal struct NavigationGeometryWork(int allowance)
{
    public int Consumed { get; private set; }

    public bool TrySpend()
    {
        if (Consumed >= allowance) return false;
        Consumed++;
        return true;
    }
}
