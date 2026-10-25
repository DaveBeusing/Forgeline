using System.Numerics;
using ForgeLine.Navigation;

namespace ForgeLine.Game;

/// <summary>
/// Simulation-thread handoff for an already arbitrated proposal, before the
/// authoritative commit. This does not accept motion, complete goals or mutate a
/// route. A failed or ambiguous calculation requires retaining the old state.
/// </summary>
internal static class NavigationAcceptedMotionProgress
{
    internal static bool TryGetNextWaypoint(in NavigationMotionInput input,
        in NavigationMotionProposal approved, int currentWaypoint, int usedWaypoint,
        int workAllowance, out int workConsumed, out int nextWaypoint)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workAllowance);
        workConsumed = 0;
        nextWaypoint = currentWaypoint;
        NavigationRouteProposal? route = input.Route;
        long count = route is null ? 0 : route.Waypoints.Count +
            (route.ResolvedDestination != input.Binding.OriginalOrder.WorldTarget ? 1L : 0L);
        if (route is null || approved.Availability != NavigationModuleAvailability.Available ||
            approved.Binding != input.Binding || approved.Tick != input.Tick ||
            approved.Revision != route.Request.Revision || approved.Start != input.Vehicle.Pose ||
            !approved.Start.IsValid || !approved.End.IsValid ||
            currentWaypoint < 0 || currentWaypoint >= count || usedWaypoint < currentWaypoint || usedWaypoint >= count)
            return false;
        if (approved.Start.Position == approved.End.Position) return true;

        var start = new Projection();
        var end = new Projection();
        for (int index = currentWaypoint; index <= usedWaypoint; index++)
        {
            if (workConsumed >= workAllowance) return false;
            workConsumed++;
            Vector3 first = index == 0 ? route.Request.Start.Position : route.Waypoints[index - 1];
            Vector3 last = index < route.Waypoints.Count ? route.Waypoints[index] : input.Binding.OriginalOrder.WorldTarget;
            if (!Project(approved.Start.Position, first, last, index, ref start) ||
                !Project(approved.End.Position, first, last, index, ref end))
                return false;
            if (index == usedWaypoint) break;
        }
        if (!start.Found || !end.Found || start.Ambiguous || end.Ambiguous || end.Progress + 0.000001 < start.Progress)
            return false;

        // The rank is a segment index plus its projected fraction. Reaching an
        // intermediate vertex permits choosing the next target, never removing
        // the original order. Count denotes the explicit original-target leg
        // when a route ended at an alternate projected destination.
        nextWaypoint = Math.Clamp((int)Math.Floor(end.Progress), currentWaypoint,
            (int)Math.Min(route.Waypoints.Count, (long)usedWaypoint + 1));
        if (nextWaypoint == route.Waypoints.Count && route.ResolvedDestination == input.Binding.OriginalOrder.WorldTarget)
            nextWaypoint--;
        return true;
    }

    private static bool Project(Vector3 point, Vector3 first, Vector3 last, int index, ref Projection best)
    {
        double dx = (double)last.X - first.X;
        double dz = (double)last.Z - first.Z;
        double squared = dx * dx + dz * dz;
        if (!double.IsFinite(squared)) return false;
        if (squared <= 0) return true;
        double px = (double)point.X - first.X;
        double pz = (double)point.Z - first.Z;
        double fraction = Math.Clamp((px * dx + pz * dz) / squared, 0, 1);
        double errorX = px - dx * fraction;
        double errorZ = pz - dz * fraction;
        double distance = errorX * errorX + errorZ * errorZ;
        double progress = index + fraction;
        if (!double.IsFinite(distance) || !double.IsFinite(progress)) return false;
        double tolerance = 0.00000001 * Math.Max(1, Math.Min(best.DistanceSquared, distance));
        if (!best.Found || distance < best.DistanceSquared - tolerance)
        {
            best = new Projection { Found = true, DistanceSquared = distance, Progress = progress, SegmentIndex = index };
        }
        else if (Math.Abs(distance - best.DistanceSquared) <= tolerance && Math.Abs(progress - best.Progress) > 0.000001)
        {
            // At an ordinary corner's bisector two adjacent segments can be
            // equally close. Keep the earlier progress and allow the accepted
            // step; refusing the step would reproduce that tie forever. A tie
            // with a nonadjacent segment is a crossing/loop and stays ambiguous.
            if (index - best.SegmentIndex > 1) best.Ambiguous = true;
        }
        return true;
    }

    private struct Projection
    {
        public bool Found;
        public bool Ambiguous;
        public double DistanceSquared;
        public double Progress;
        public int SegmentIndex;
    }
}
