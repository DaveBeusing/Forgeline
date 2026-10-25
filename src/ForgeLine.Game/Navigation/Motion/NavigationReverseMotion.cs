using System.Numerics;
using ForgeLine.Navigation;

namespace ForgeLine.Game;

/// <summary>Shared bounded reverse proposal for escape verification and the actual fixed-tick owner.</summary>
internal static class NavigationReverseMotion
{
    internal static bool TryStep(in NavigationVehicleObservation vehicle, Vector3 target, float maximumSpeed,
        float deltaSeconds, NavigationWorldSnapshot world, out NavigationPose end, out float speed, out float heading)
    {
        end = vehicle.Pose; speed = 0; heading = vehicle.Movement.HeadingRadians;
        if (!vehicle.Pose.IsValid || !vehicle.Profile.IsValid || !NavigationMotionCapture.Finite(target) ||
            !float.IsFinite(maximumSpeed) || maximumSpeed < 0 || !float.IsFinite(deltaSeconds) || deltaSeconds <= 0 ||
            !float.IsFinite(heading) || !NavigationMotionCapture.Finite(vehicle.Movement.Velocity)) return false;
        Vector3 forward = Vector3.Transform(Vector3.UnitZ, vehicle.Pose.Rotation);
        Vector3 up = Vector3.Transform(Vector3.UnitY, vehicle.Pose.Rotation);
        if (MathF.Abs(forward.Y) > 0.001f || up.Y < 0.999f ||
            MathF.Abs(GroundMovementState.NormalizeAngle(MathF.Atan2(forward.X, forward.Z) - heading)) > 0.001f ||
            Vector3.Dot(vehicle.Movement.Velocity, forward) > 0.01f) return false;
        Vector3 delta = target - vehicle.Pose.Position; delta.Y = 0;
        float distance = delta.Length();
        float current = new Vector2(vehicle.Movement.Velocity.X, vehicle.Movement.Velocity.Z).Length();
        if (!float.IsFinite(distance) || !float.IsFinite(current) || current > vehicle.Profile.MaximumSpeed + 0.01f) return false;
        if (distance <= 0.15f) return true;
        float desiredHeading = MathF.Atan2(-delta.X, -delta.Z);
        float angle = GroundMovementState.NormalizeAngle(desiredHeading - heading);
        heading = GroundMovementState.NormalizeAngle(heading + Math.Clamp(angle,
            -vehicle.Profile.MaximumYawRate * deltaSeconds, vehicle.Profile.MaximumYawRate * deltaSeconds));
        float remaining = MathF.Max(0, distance - 0.15f);
        float limit = MathF.Min(MathF.Min(1, maximumSpeed), vehicle.Profile.MaximumSpeed);
        limit = MathF.Min(limit, MathF.Sqrt(2 * vehicle.Profile.Deceleration * remaining));
        if (MathF.Abs(angle) > MathF.PI / 30) limit = MathF.Min(limit, remaining * vehicle.Profile.MaximumYawRate * 0.5f);
        float change = (limit > current ? vehicle.Profile.Acceleration : vehicle.Profile.Deceleration) * deltaSeconds;
        speed = current + Math.Clamp(limit - current, -change, change);
        float travel = MathF.Min(remaining, speed * deltaSeconds);
        Vector3 position = vehicle.Pose.Position - new Vector3(MathF.Sin(heading), 0, MathF.Cos(heading)) * travel;
        position = NavigationMotionNumerics.ConstrainTravel(vehicle.Pose.Position, position, (double)speed * deltaSeconds);
        if (!world.TrySampleHeight(vehicle.Pose.Position.X, vehicle.Pose.Position.Z, out float startHeight) ||
            !world.TrySampleHeight(position.X, position.Z, out float height)) return false;
        position.Y = height + vehicle.Pose.Position.Y - startHeight;
        if (!NavigationMotionCapture.Finite(position)) return false;
        speed = new Vector2(position.X - vehicle.Pose.Position.X, position.Z - vehicle.Pose.Position.Z).Length() / deltaSeconds;
        end = new(position, Quaternion.CreateFromAxisAngle(Vector3.UnitY, heading));
        return end.IsValid;
    }
}
