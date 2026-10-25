using System.Numerics;

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
}
