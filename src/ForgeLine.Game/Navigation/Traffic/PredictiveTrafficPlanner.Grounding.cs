using System.Numerics;
using ForgeLine.Navigation;

namespace ForgeLine.Game;

internal sealed partial class PredictiveTrafficPlanner
{
    private static NavigationSweepResult SweepGrounded(NavigationCollisionFrame frame,
        INavigationGeometryQuery geometry, NavigationSweep sweep, int allowance, int depth = 0)
    {
        var result = geometry.Sweep(frame, sweep, allowance);
        if (result.WorkConsumed < 0 || result.WorkConsumed > allowance || result.FrameTick != frame.Tick ||
            result.Revision != frame.World.Revision || !Enum.IsDefined(result.Status) || !Enum.IsDefined(result.Reason) ||
            !float.IsFinite(result.AllowedFraction) || result.AllowedFraction < 0 || result.AllowedFraction > 1 ||
            result.Status == NavigationGeometryStatus.Clear &&
                (result.WorkConsumed == 0 || result.AllowedFraction != 1 || result.Reason != NavigationBlockReason.None))
            return new(NavigationGeometryStatus.Unavailable, frame.World.Revision, frame.Tick, 0,
                NavigationBlockReason.MissingGeometry, null, allowance);
        if (result.Status == NavigationGeometryStatus.Clear || result.Reason != NavigationBlockReason.Terrain || depth == 5 ||
            result.WorkConsumed == allowance || result.FrameTick != frame.Tick || result.Revision != frame.World.Revision) return result;
        Vector3 middle = Vector3.Lerp(sweep.Start.Position, sweep.End.Position, 0.5f);
        if (!frame.World.TrySampleHeight(sweep.Start.Position.X, sweep.Start.Position.Z, out float startHeight) ||
            !frame.World.TrySampleHeight(middle.X, middle.Z, out float height)) return result;
        middle.Y = height + sweep.Start.Position.Y - startHeight;
        var midpoint = new NavigationPose(middle, Quaternion.Normalize(Quaternion.Slerp(sweep.Start.Rotation, sweep.End.Rotation, 0.5f)));
        int used = result.WorkConsumed;
        var first = SweepGrounded(frame, geometry, sweep with { End = midpoint, DurationSeconds = sweep.DurationSeconds * 0.5f },
            allowance - used, depth + 1);
        used += first.WorkConsumed;
        if (first.Status != NavigationGeometryStatus.Clear || first.AllowedFraction != 1)
            return first with { WorkConsumed = used, AllowedFraction = 0 };
        if (used == allowance)
            return new(NavigationGeometryStatus.BudgetExhausted, frame.World.Revision, frame.Tick, 0,
                NavigationBlockReason.SearchWork, null, used);
        var last = SweepGrounded(frame, geometry, sweep with { Start = midpoint, DurationSeconds = sweep.DurationSeconds * 0.5f },
            allowance - used, depth + 1);
        return last with { WorkConsumed = used + last.WorkConsumed,
            AllowedFraction = last.Status == NavigationGeometryStatus.Clear && last.AllowedFraction == 1 ? 1 : 0 };
    }
}
