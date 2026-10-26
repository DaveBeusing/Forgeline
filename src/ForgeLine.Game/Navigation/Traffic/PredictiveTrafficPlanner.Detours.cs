using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Navigation;

namespace ForgeLine.Game;

internal sealed partial class PredictiveTrafficPlanner
{
    private readonly record struct DetourNode(int X, int Z, NavigationPose Pose, float Cost, int Parent);

    // A small advisory search around observed stationary bodies. It owns no route,
    // command, queue, saved state or transform. Every accepted tick is swept again.
    private bool TryLocalDetour(NavigationTrafficFrame frame, in NavigationVehicleObservation vehicle,
        in NavigationTrafficIntent intent, ReadOnlySpan<NavigationPose> nominal,
        Dictionary<NavigationObstacleIdentity, NavigationCollisionBody> actualBodies,
        INavigationGeometryQuery geometry, int allowance, out Vector3 target, out int consumed, out bool valid)
    {
        target = default; consumed = 0; valid = true;
        if (allowance == 0 || frame.Tick.Value < intent.Binding.OriginalOrder.SubmittedAtTick.Value ||
            frame.Tick.Value - intent.Binding.OriginalOrder.SubmittedAtTick.Value < RecoveryWaitTicks ||
            !intent.CanMove || intent.MaximumSpeed <= 0 || NavigationPrecisionOwnership.IsPrecise(vehicle) ||
            vehicle.Movement.Velocity.LengthSquared() > 1.01f) return false;
        var collision = frame.GetVehicleCollision(vehicle.Entity);
        bool blocked = false;
        foreach (var body in collision.Bodies)
        {
            if (consumed == allowance) return false;
            consumed++;
            if (body.Identity == NavigationReadAdapter.GetObstacleIdentity(vehicle.Entity) ||
                body.Kind != NavigationObstacleKind.Vehicle || !actualBodies.TryGetValue(body.Identity, out var currentBody) ||
                currentBody.Pose != body.Pose || currentBody.Velocity.LengthSquared() > .0025f) continue;
            var bodyProfile = new NavigationVehicleProfile(body.HalfExtents, 0, 1, 1, 1, 1);
            for (int step = 1; step < nominal.Length; step++)
            {
                if (consumed == allowance) return false;
                consumed++;
                if (Conflicts(nominal[step - 1], nominal[step], vehicle.Profile, body.Pose, body.Pose, bodyProfile))
                { blocked = true; break; }
            }
            foreach (var reservation in frame.Reservations)
            {
                if (allowance - consumed < PredictionSteps + 1) return false;
                consumed += PredictionSteps + 1;
                if (NavigationReadAdapter.GetObstacleIdentity(reservation.Owner) == body.Identity &&
                    ReservationApplies(vehicle, reservation) &&
                    Touches(new(reservation.ReservationId, reservation.Pose, reservation.HalfExtents), nominal, vehicle.Profile))
                { blocked = true; break; }
            }
            if (blocked) break;
        }
        if (!blocked) return false;
        Vector3 origin = vehicle.Pose.Position, goal = intent.Binding.OriginalOrder.WorldTarget;
        float initialDistance = HorizontalDistance(origin, goal);
        if (!float.IsFinite(initialDistance) || initialDistance <= 2) return false;
        if (!collision.World.TrySampleHeight(origin.X, origin.Z, out float ground)) return false;
        float offset = origin.Y - ground;
        const float spacing = 4;
        const int radius = 6, maximumNodes = 256, maximumExpanded = 64;
        var nodes = new List<DetourNode>(maximumNodes) { new(0, 0, vehicle.Pose, 0, -1) };
        var best = new Dictionary<(int X, int Z, int Heading), float>();
        var open = new PriorityQueue<int, (float Score, int Sequence)>();
        open.Enqueue(0, (initialDistance, 0));
        int expanded = 0;
        ReadOnlySpan<(int X, int Z)> directions = [(0, 1), (1, 1), (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1)];
        while (open.TryDequeue(out int current, out _) && expanded++ < maximumExpanded)
        {
            var node = nodes[current];
            if (current > 0 && HorizontalDistance(node.Pose.Position, goal) <= MathF.Max(2, initialDistance - 12))
            {
                int first = current;
                while (nodes[first].Parent > 0) first = nodes[first].Parent;
                target = nodes[first].Pose.Position;
                return true;
            }
            for (int direction = 0; direction < directions.Length; direction++)
            {
                if (consumed == allowance) return false;
                if (nodes.Count == maximumNodes) break;
                consumed++;
                var delta = directions[direction];
                int x = node.X + delta.X, z = node.Z + delta.Z;
                if (Math.Abs(x) > radius || Math.Abs(z) > radius) continue;
                Vector3 forward = Vector3.Transform(Vector3.UnitZ, node.Pose.Rotation);
                if (forward.X * delta.X + forward.Z * delta.Z < -.001f) continue;
                Vector3 position = origin + new Vector3(x * spacing, 0, z * spacing);
                if (!collision.World.TrySampleHeight(position.X, position.Z, out float height)) continue;
                position.Y = height + offset;
                float heading = MathF.Atan2(delta.X, delta.Z);
                float turn = MathF.Abs(GroundMovementState.NormalizeAngle(heading - MathF.Atan2(forward.X, forward.Z)));
                float length = HorizontalDistance(node.Pose.Position, position);
                float cost = node.Cost + length + vehicle.Profile.HalfExtents.Z * turn;
                var key = (x, z, direction);
                if (best.TryGetValue(key, out float prior) && prior <= cost) continue;
                var end = new NavigationPose(position, Quaternion.CreateFromAxisAngle(Vector3.UnitY, heading));
                float duration = MathF.Max(length, turn / vehicle.Profile.MaximumYawRate);
                var sweep = new NavigationSweep(NavigationReadAdapter.GetObstacleIdentity(vehicle.Entity),
                    node.Pose, end, vehicle.Profile, duration, PrefilterFixedBodies: true);
                // Exhausting advisory bookkeeping cannot invalidate the captured geometry.
                if (consumed == allowance) return false;
                var result = SweepGrounded(collision, geometry, sweep, allowance - consumed);
                if (result.WorkConsumed <= 0 || result.WorkConsumed > allowance - consumed ||
                    result.FrameTick != frame.Tick.Value || result.Revision != collision.World.Revision ||
                    !Enum.IsDefined(result.Status) || !Enum.IsDefined(result.Reason) ||
                    !float.IsFinite(result.AllowedFraction) || result.AllowedFraction < 0 || result.AllowedFraction > 1 ||
                    result.Status == NavigationGeometryStatus.Clear &&
                    (result.Reason != NavigationBlockReason.None || result.AllowedFraction != 1 || result.Blocker.HasValue))
                { valid = false; return false; }
                consumed += result.WorkConsumed;
                if (result.Status == NavigationGeometryStatus.BudgetExhausted) return false;
                if (result.Status is NavigationGeometryStatus.InvalidInput or NavigationGeometryStatus.Stale or NavigationGeometryStatus.Unavailable)
                { valid = false; return false; }
                if (result.Status != NavigationGeometryStatus.Clear || result.AllowedFraction != 1) continue;
                bool reserved = false;
                foreach (var reservation in frame.Reservations)
                {
                    int work = ServiceReservationCheckWork(2);
                    if (work > allowance - consumed) return false;
                    consumed += work;
                    if (BlocksServiceReservation(vehicle, reservation, [node.Pose, end])) { reserved = true; break; }
                }
                if (reserved) continue;
                if (!TryCheckPassage(vehicle.Entity, node.Pose, end, vehicle.Profile,
                    allowance - consumed, out int passageWork, out bool complete))
                {
                    consumed += passageWork;
                    if (!complete) return false;
                    continue;
                }
                consumed += passageWork;
                if (allowance - consumed < 16) return false;
                consumed += 16; // bounded priority-queue insertion/removal and state bookkeeping
                best[key] = cost;
                int next = nodes.Count;
                nodes.Add(new(x, z, end, cost, current));
                open.Enqueue(next, (cost + HorizontalDistance(position, goal), next));
            }
        }
        return false;
    }
}
