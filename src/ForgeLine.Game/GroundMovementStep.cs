using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Game;

/// <summary>Allocation-free horizontal arrival and swept circle geometry.</summary>
internal static class GroundMovementStep
{
    public static Vector3 ClampArrival(Vector3 start, Vector3 candidate, Vector3 target, float radius)
    {
        double dx = (double)candidate.X - start.X;
        double dz = (double)candidate.Z - start.Z;
        double lengthSquared = dx * dx + dz * dz;
        if (lengthSquared <= 0) return candidate;
        double t = (((double)target.X - start.X) * dx + ((double)target.Z - start.Z) * dz) / lengthSquared;
        if (t <= 0 || t >= 1) return candidate;
        double x = start.X + dx * t;
        double z = start.Z + dz * t;
        double errorX = x - target.X;
        double errorZ = z - target.Z;
        if (errorX * errorX + errorZ * errorZ > (double)radius * radius) return candidate;

        // Shorten the proposed segment; never snap sideways to the target.
        // Collision and terrain validation still run before arrival is accepted.
        return new Vector3((float)x, start.Y + (candidate.Y - start.Y) * (float)t, (float)z);
    }

    public static bool TrySweepCircle(Vector3 start, Vector3 candidate,
        in AxisAlignedBounds bounds, float radius, out float fraction)
    {
        fraction = 1;
        double dx = (double)candidate.X - start.X;
        double dz = (double)candidate.Z - start.Z;
        double lengthSquared = dx * dx + dz * dz;
        if (lengthSquared <= 0) return false;
        double radiusSquared = (double)radius * radius;
        double awayX = start.X - Math.Clamp((double)start.X, bounds.Minimum.X, bounds.Maximum.X);
        double awayZ = start.Z - Math.Clamp((double)start.Z, bounds.Minimum.Z, bounds.Maximum.Z);
        if (awayX * awayX + awayZ * awayZ < radiusSquared)
        {
            // Already penetrating: retain the existing explicit depenetration
            // policy instead of trapping a spawning or displaced unit forever.
            return false;
        }

        double earliest = double.PositiveInfinity;
        if (dx > 0)
            ConsiderFace((bounds.Minimum.X - (double)radius - start.X) / dx,
                start.Z, dz, bounds.Minimum.Z, bounds.Maximum.Z, ref earliest);
        else if (dx < 0)
            ConsiderFace((bounds.Maximum.X + (double)radius - start.X) / dx,
                start.Z, dz, bounds.Minimum.Z, bounds.Maximum.Z, ref earliest);
        if (dz > 0)
            ConsiderFace((bounds.Minimum.Z - (double)radius - start.Z) / dz,
                start.X, dx, bounds.Minimum.X, bounds.Maximum.X, ref earliest);
        else if (dz < 0)
            ConsiderFace((bounds.Maximum.Z + (double)radius - start.Z) / dz,
                start.X, dx, bounds.Minimum.X, bounds.Maximum.X, ref earliest);

        // Circular corner caps preserve the same rounded clearance as the
        // endpoint circle/AABB solver, unlike an oversized expanded box.
        ConsiderCorner(start, dx, dz, lengthSquared, bounds.Minimum.X, bounds.Minimum.Z, radiusSquared, ref earliest);
        ConsiderCorner(start, dx, dz, lengthSquared, bounds.Minimum.X, bounds.Maximum.Z, radiusSquared, ref earliest);
        ConsiderCorner(start, dx, dz, lengthSquared, bounds.Maximum.X, bounds.Minimum.Z, radiusSquared, ref earliest);
        ConsiderCorner(start, dx, dz, lengthSquared, bounds.Maximum.X, bounds.Maximum.Z, radiusSquared, ref earliest);
        if (!double.IsFinite(earliest)) return false;
        fraction = (float)earliest;
        return true;
    }

    private static void ConsiderFace(double t, double position, double displacement,
        double minimum, double maximum, ref double earliest)
    {
        if (t < 0 || t > 1 || t >= earliest) return;
        double crossing = position + displacement * t;
        if (crossing >= minimum && crossing <= maximum) earliest = t;
    }

    private static void ConsiderCorner(Vector3 start, double dx, double dz, double lengthSquared,
        double cornerX, double cornerZ, double radiusSquared, ref double earliest)
    {
        double x = start.X - cornerX;
        double z = start.Z - cornerZ;
        double b = x * dx + z * dz;
        if (b >= 0) return;
        double c = x * x + z * z - radiusSquared;
        double discriminant = b * b - lengthSquared * c;
        if (discriminant < 0) return;
        double t = (-b - Math.Sqrt(discriminant)) / lengthSquared;
        if (t >= 0 && t <= 1 && t < earliest) earliest = t;
    }
}
