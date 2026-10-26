using System.Numerics;
using ForgeLine.Navigation;

namespace ForgeLine.Game;

internal static class NavigationMotionNumerics
{
    // World-coordinate rounding must not turn an authored speed cap into an overspeed endpoint.
    internal static Vector3 ConstrainTravel(Vector3 start, Vector3 end, double maximumDistance)
    {
        double x = (double)end.X - start.X, z = (double)end.Z - start.Z;
        if (x * x + z * z <= maximumDistance * maximumDistance) return end;
        if (end.X > start.X) end.X = MathF.BitDecrement(end.X);
        else if (end.X < start.X) end.X = MathF.BitIncrement(end.X);
        if (end.Z > start.Z) end.Z = MathF.BitDecrement(end.Z);
        else if (end.Z < start.Z) end.Z = MathF.BitIncrement(end.Z);
        x = (double)end.X - start.X; z = (double)end.Z - start.Z;
        return x * x + z * z <= maximumDistance * maximumDistance ? end : start;
    }
    internal static bool IsCapturedForwardTarget(NavigationPose pose, Vector3 target)
    {
        Vector3 forward = Vector3.Transform(Vector3.UnitZ, pose.Rotation);
        double dx = (double)target.X - pose.Position.X, dz = (double)target.Z - pose.Position.Z;
        double lateral = dx * forward.Z - dz * forward.X;
        double uncertainty = (CoordinateSpacing(target.X) + CoordinateSpacing(pose.Position.X)) * Math.Abs(forward.Z) +
            (CoordinateSpacing(target.Z) + CoordinateSpacing(pose.Position.Z)) * Math.Abs(forward.X);
        return dx * forward.X + dz * forward.Z > 0 && double.IsFinite(uncertainty) && Math.Abs(lateral) <= uncertainty;
    }

    private static double CoordinateSpacing(float value) =>
        Math.Max((double)MathF.BitIncrement(value) - value, (double)value - MathF.BitDecrement(value));
}
