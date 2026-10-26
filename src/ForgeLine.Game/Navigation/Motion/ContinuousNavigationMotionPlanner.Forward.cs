using System.Numerics;
using ForgeLine.Navigation;

namespace ForgeLine.Game;

public sealed partial class ContinuousNavigationMotionPlanner
{
    private NavigationMotionProposal ProposeForwardClearance(in NavigationMotionInput sourceInput,
        NavigationCollisionFrame collision, INavigationGeometryQuery geometry, NavigationRouteProposal route,
        float groundOffset, int allowance, ref int consumed, NavigationBlockReason reason, float brakingDistance)
    {
        var input = sourceInput;
        // The independently swept motion horizon can discover a parked body that
        // the traffic prediction missed. A stopped or slowly advancing mover may probe
        // the same slow clearance fan; every candidate and actual tick is swept.
        if (reason == NavigationBlockReason.MovingVehicle &&
            input.Traffic.Availability == NavigationModuleAvailability.Available &&
            input.Traffic.Action == NavigationTrafficAction.Proceed &&
            input.Traffic.Reason == NavigationBlockReason.None &&
            input.Vehicle.Movement.Velocity.LengthSquared() <= 1.0001f)
            input = input with { Traffic = input.Traffic with { MaximumSpeed = MathF.Min(1, input.Traffic.MaximumSpeed), Reason = reason } };
        if (reason is not (NavigationBlockReason.MovingVehicle or NavigationBlockReason.StaticObstacle or NavigationBlockReason.Terrain) ||
            input.Traffic.Reason is not (NavigationBlockReason.MovingVehicle or NavigationBlockReason.StaticObstacle or NavigationBlockReason.Terrain) ||
            input.Traffic.MaximumSpeed > 1 || input.Traffic.MaximumSpeed <= 0 ||
            HorizontalDistance(input.Vehicle.Pose.Position, input.Binding.OriginalOrder.WorldTarget) <= route.Request.Goal.PositionTolerance)
            return Hold(input, collision, Availability, reason, brakingDistance);
        Vector3 forward = new(MathF.Sin(input.Vehicle.Movement.HeadingRadians), 0, MathF.Cos(input.Vehicle.Movement.HeadingRadians));
        Vector3 velocity = input.Vehicle.Movement.Velocity; velocity.Y = 0;
        float currentSpeed = velocity.Length();
        if (currentSpeed > 1.0001f || Vector3.Dot(velocity, forward) < -.001f ||
            (velocity - forward * Vector3.Dot(velocity, forward)).LengthSquared() > .0001f)
            return Hold(input, collision, Availability, reason, brakingDistance);
        // Choose a fully swept local direction without consuming route ownership.
        // The fan includes straight clearance when every turn would clip a rear neighbor.
        float selectedHeading = input.Vehicle.Movement.HeadingRadians;
        float bestDistance = float.PositiveInfinity;
        bool found = false;
        ReadOnlySpan<float> angles = [0, MathF.PI / 4, -MathF.PI / 4, MathF.PI / 2, -MathF.PI / 2,
            3 * MathF.PI / 4, -3 * MathF.PI / 4, MathF.PI];
        int searchLimit = consumed + (allowance - consumed) / 2;
        float previewCap = MathF.Min(input.EffectiveMaximumSpeed, MathF.Min(input.Vehicle.Profile.MaximumSpeed, input.Traffic.MaximumSpeed));
        float previewSpeed = MoveTowards(currentSpeed, previewCap, (previewCap >= currentSpeed
            ? input.Vehicle.Profile.Acceleration : input.Vehicle.Profile.Deceleration) * input.DeltaSeconds);
        if (!float.IsFinite(previewSpeed) || previewSpeed > previewCap + DirectionEpsilon)
            return Hold(input, collision, Availability, reason, brakingDistance);
        foreach (float angle in angles)
        {
            if (!Spend(searchLimit, ref consumed)) break;
            float heading = GroundMovementState.NormalizeAngle(input.Vehicle.Movement.HeadingRadians + angle);
            Vector3 direction = new(MathF.Sin(heading), 0, MathF.Cos(heading));
            Vector3 target = input.Vehicle.Pose.Position + direction * 3;
            if (!collision.World.TrySampleHeight(target.X, target.Z, out float targetHeight)) continue;
            target.Y = targetHeight + groundOffset;
            var preview = new NavigationSweep(NavigationReadAdapter.GetObstacleIdentity(input.Vehicle.Entity),
                input.Vehicle.Pose, new(target, Quaternion.CreateFromAxisAngle(Vector3.UnitY, heading)),
                input.Vehicle.Profile, 3);
            if (!TrySweep(geometry, collision, preview, searchLimit, ref consumed, out var checkedDirection))
                return Hold(input, collision, NavigationModuleAvailability.Unavailable, NavigationBlockReason.MissingGeometry, brakingDistance);
            if (checkedDirection.Status != NavigationGeometryStatus.Clear || checkedDirection.AllowedFraction != 1) continue;
            float turn = GroundMovementState.NormalizeAngle(heading - input.Vehicle.Movement.HeadingRadians);
            float actualHeading = GroundMovementState.NormalizeAngle(input.Vehicle.Movement.HeadingRadians +
                Math.Clamp(turn, -input.Vehicle.Profile.MaximumYawRate * input.DeltaSeconds,
                    input.Vehicle.Profile.MaximumYawRate * input.DeltaSeconds));
            Vector3 actualForward = new(MathF.Sin(actualHeading), 0, MathF.Cos(actualHeading));
            Vector3 actualPosition = NavigationMotionNumerics.ConstrainTravel(input.Vehicle.Pose.Position,
                input.Vehicle.Pose.Position + actualForward * (previewSpeed * input.DeltaSeconds),
                (double)previewSpeed * input.DeltaSeconds);
            if (!Spend(searchLimit, ref consumed)) break;
            if (!collision.World.TrySampleHeight(actualPosition.X, actualPosition.Z, out float actualHeight)) continue;
            actualPosition.Y = actualHeight + groundOffset;
            var actualSweep = new NavigationSweep(NavigationReadAdapter.GetObstacleIdentity(input.Vehicle.Entity),
                input.Vehicle.Pose, new(actualPosition, Quaternion.CreateFromAxisAngle(Vector3.UnitY, actualHeading)),
                input.Vehicle.Profile, input.DeltaSeconds);
            if (!TrySweep(geometry, collision, actualSweep, searchLimit, ref consumed, out var checkedTick))
                return Hold(input, collision, NavigationModuleAvailability.Unavailable, NavigationBlockReason.MissingGeometry, brakingDistance);
            if (checkedTick.Status != NavigationGeometryStatus.Clear || checkedTick.AllowedFraction != 1) continue;
            float distance = HorizontalDistance(target, input.Binding.OriginalOrder.WorldTarget);
            if (distance >= bestDistance) continue;
            bestDistance = distance; selectedHeading = heading; found = true;
        }
        // A short straight departure can be safe even when no complete three-metre
        // turn is yet possible. The actual tick is still independently swept below.
        if (found)
        {
            float change = GroundMovementState.NormalizeAngle(selectedHeading - input.Vehicle.Movement.HeadingRadians);
            selectedHeading = GroundMovementState.NormalizeAngle(input.Vehicle.Movement.HeadingRadians +
                Math.Clamp(change, -input.Vehicle.Profile.MaximumYawRate * input.DeltaSeconds,
                    input.Vehicle.Profile.MaximumYawRate * input.DeltaSeconds));
            forward = new(MathF.Sin(selectedHeading), 0, MathF.Cos(selectedHeading));
        }
        float cap = MathF.Min(input.EffectiveMaximumSpeed, MathF.Min(input.Vehicle.Profile.MaximumSpeed, input.Traffic.MaximumSpeed));
        float speed = MoveTowards(currentSpeed, cap, (cap >= currentSpeed
            ? input.Vehicle.Profile.Acceleration : input.Vehicle.Profile.Deceleration) * input.DeltaSeconds);
        if (!float.IsFinite(speed) || speed > cap + DirectionEpsilon)
            return Hold(input, collision, Availability, reason, brakingDistance);
        Vector3 position = NavigationMotionNumerics.ConstrainTravel(input.Vehicle.Pose.Position,
            input.Vehicle.Pose.Position + forward * (speed * input.DeltaSeconds), (double)speed * input.DeltaSeconds);
        if (!Spend(allowance, ref consumed))
            return Hold(input, collision, Availability, NavigationBlockReason.SearchWork, brakingDistance);
        if (!collision.World.TrySampleHeight(position.X, position.Z, out float height) || !float.IsFinite(height))
            return Hold(input, collision, Availability, NavigationBlockReason.Terrain, brakingDistance);
        position.Y = height + groundOffset;
        NavigationPose end = new(position, Quaternion.CreateFromAxisAngle(Vector3.UnitY, selectedHeading));
        if (!end.IsValid)
            return Hold(input, collision, NavigationModuleAvailability.Unavailable, NavigationBlockReason.Terrain, brakingDistance);
        var sweep = new NavigationSweep(NavigationReadAdapter.GetObstacleIdentity(input.Vehicle.Entity),
            input.Vehicle.Pose, end, input.Vehicle.Profile, input.DeltaSeconds);
        if (!TrySweep(geometry, collision, sweep, allowance, ref consumed, out var result))
            return Hold(input, collision, NavigationModuleAvailability.Unavailable, NavigationBlockReason.MissingGeometry, brakingDistance);
        if (result.Status != NavigationGeometryStatus.Clear || result.AllowedFraction != 1)
            return Hold(input, collision, Availability, result.Reason, brakingDistance);
        return new(Availability, input.Binding, input.Tick, collision.World.Revision, input.Vehicle.Pose, end,
            HorizontalDistance(input.Vehicle.Pose.Position, end.Position) / input.DeltaSeconds, 0, brakingDistance,
            HorizontalDistance(input.Vehicle.Pose.Position, end.Position), reason);
    }
}
