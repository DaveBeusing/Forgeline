using System.Numerics;
using ForgeLine.Navigation;

namespace ForgeLine.Game;

internal sealed partial class PredictiveTrafficPlanner
{
    private static int ForwardClearance(NavigationTrafficFrame frame, NavigationVehicleObservation vehicle,
        NavigationTrafficIntent intent, INavigationGeometryQuery geometry,
        IReadOnlyDictionary<ulong, PredictiveTrafficPassageLease> leases, int allowance, out bool clear)
    {
        clear = false;
        if (!intent.CanMove || NavigationPrecisionOwnership.IsPrecise(vehicle) ||
            vehicle.Movement.Velocity.LengthSquared() > 1.0001f || intent.MaximumSpeed <= 0) return 0;
        Vector3 originalForward = new(MathF.Sin(vehicle.Movement.HeadingRadians), 0, MathF.Cos(vehicle.Movement.HeadingRadians));
        Vector3 velocity = vehicle.Movement.Velocity; velocity.Y = 0;
        if (Vector3.Dot(velocity, originalForward) < -.001f ||
            (velocity - originalForward * Vector3.Dot(velocity, originalForward)).LengthSquared() > .0001f) return 0;
        int consumed = 0;
        const float previewSeconds = .25f;
        float speed = MathF.Min(MathF.Min(1, intent.MaximumSpeed), velocity.Length() + vehicle.Profile.Acceleration * previewSeconds);
        float yaw = vehicle.Profile.MaximumYawRate * previewSeconds;
        ReadOnlySpan<float> turns = [0, yaw, -yaw];
        foreach (float turn in turns)
        {
            if (consumed == allowance) return -1;
            consumed++;
            float heading = vehicle.Movement.HeadingRadians + turn;
            Vector3 forward = new(MathF.Sin(heading), 0, MathF.Cos(heading));
            Vector3 position = vehicle.Pose.Position + forward * (speed * previewSeconds);
            if (!frame.Collision.World.TrySampleHeight(vehicle.Pose.Position.X, vehicle.Pose.Position.Z, out float startHeight) ||
                !frame.Collision.World.TrySampleHeight(position.X, position.Z, out float height)) continue;
            position.Y = height + vehicle.Pose.Position.Y - startHeight;
            NavigationPose end = new(position, Quaternion.CreateFromAxisAngle(Vector3.UnitY, heading));
            var sweep = new NavigationSweep(NavigationReadAdapter.GetObstacleIdentity(vehicle.Entity),
                vehicle.Pose, end, vehicle.Profile, previewSeconds);
            var result = SweepGrounded(frame.GetVehicleCollision(vehicle.Entity), geometry, sweep, allowance - consumed);
            if (result.WorkConsumed < 0 || result.WorkConsumed > allowance - consumed) return -1;
            consumed += result.WorkConsumed;
            if (!Enum.IsDefined(result.Status) || !Enum.IsDefined(result.Reason) ||
                !float.IsFinite(result.AllowedFraction) || result.AllowedFraction < 0 || result.AllowedFraction > 1 ||
                result.FrameTick != frame.Tick.Value || result.Revision != frame.Collision.World.Revision ||
                result.Status is NavigationGeometryStatus.BudgetExhausted or NavigationGeometryStatus.InvalidInput or
                    NavigationGeometryStatus.Stale or NavigationGeometryStatus.Unavailable) return -1;
            if (result.Status != NavigationGeometryStatus.Clear || result.AllowedFraction != 1 ||
                result.Reason != NavigationBlockReason.None || result.Blocker.HasValue || result.WorkConsumed == 0) continue;
            bool reserved = false;
            foreach (var reservation in frame.Reservations)
            {
                int checkWork = ServiceReservationCheckWork(2);
                if (checkWork > allowance - consumed) return -1;
                consumed += checkWork;
                if (BlocksServiceReservation(vehicle, reservation, [vehicle.Pose, end])) reserved = true;
            }
            foreach (var lease in leases.Values)
            {
                if (consumed == allowance) return -1;
                consumed++;
                if (lease.Owner != vehicle.Entity && Touches(lease.Passage, [vehicle.Pose, end], vehicle.Profile)) reserved = true;
            }
            if (reserved) continue;
            clear = true;
            return consumed;
        }
        return consumed;
    }
}
