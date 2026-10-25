using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Navigation;

public readonly record struct NavigationPose(Vector3 Position, Quaternion Rotation)
{
    public bool IsValid => float.IsFinite(Position.X) && float.IsFinite(Position.Y) && float.IsFinite(Position.Z) &&
        float.IsFinite(Rotation.LengthSquared()) && MathF.Abs(Rotation.LengthSquared() - 1) <= 0.001f;
}

public readonly record struct NavigationVehicleProfile(
    Vector3 HalfExtents, float ClearanceMeters, float MaximumSpeed,
    float Acceleration, float Deceleration, float MaximumYawRate)
{
    public bool IsValid => Positive(HalfExtents.X) && Positive(HalfExtents.Y) && Positive(HalfExtents.Z) &&
        float.IsFinite(ClearanceMeters) && ClearanceMeters >= 0 && Positive(MaximumSpeed) &&
        Positive(Acceleration) && Positive(Deceleration) && Positive(MaximumYawRate);
    private static bool Positive(float value) => float.IsFinite(value) && value > 0;
}

/// <summary>Opaque source identity; no ECS dependency or independent ownership authority.</summary>
public enum NavigationObstacleSource : byte { Unspecified = 0, Entity = 1, AuthoredGeometry = 2, TrafficReservation = 3 }
public readonly record struct NavigationObstacleIdentity(NavigationObstacleSource Source, ulong Value, ulong Generation);
public enum NavigationObstacleKind : byte { Unspecified = 0, Structure = 1, Vehicle = 2, Wreck = 3, Reservation = 4 }
public readonly record struct NavigationCollisionBody(
    NavigationObstacleIdentity Identity, NavigationObstacleKind Kind, NavigationPose Pose,
    Vector3 HalfExtents, Vector3 Velocity);

/// <summary>Bounded physical neighborhood; incomplete coverage must never authorize an unchecked sweep.</summary>
public sealed class NavigationCollisionFrame
{
    public NavigationCollisionFrame(NavigationWorldSnapshot world, ulong tick, AxisAlignedBounds coverage,
        bool isComplete, ReadOnlySpan<NavigationCollisionBody> bodies, int maximumBodies, bool bodyPosesAreFixed = false)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBodies);
        if (bodies.Length > maximumBodies)
            throw new ArgumentException("Collision frame exceeds its configured bound.", nameof(bodies));
        if (!Finite(coverage.Minimum) || !Finite(coverage.Maximum))
            throw new ArgumentException("Coverage bounds must be finite.", nameof(coverage));
        var identities = new HashSet<NavigationObstacleIdentity>();
        foreach (NavigationCollisionBody body in bodies)
        {
            if (!body.Pose.IsValid || !Finite(body.HalfExtents) ||
                body.HalfExtents.X <= 0 || body.HalfExtents.Y <= 0 || body.HalfExtents.Z <= 0 ||
                !Finite(body.Velocity) || bodyPosesAreFixed && body.Velocity != Vector3.Zero || body.Identity.Generation == 0 ||
                !Enum.IsDefined(body.Identity.Source) || body.Identity.Source == NavigationObstacleSource.Unspecified ||
                !Enum.IsDefined(body.Kind) || body.Kind == NavigationObstacleKind.Unspecified || !identities.Add(body.Identity))
                throw new ArgumentException("Collision bodies require unique valid identities and finite physical geometry.", nameof(bodies));
        }
        World = world;
        Tick = tick;
        Coverage = coverage;
        IsComplete = isComplete;
        BodyPosesAreFixed = bodyPosesAreFixed;
        Bodies = Array.AsReadOnly(bodies.ToArray());
    }

    public NavigationWorldSnapshot World { get; }
    public ulong Tick { get; }
    public AxisAlignedBounds Coverage { get; }
    public bool IsComplete { get; }
    public IReadOnlyList<NavigationCollisionBody> Bodies { get; }
    /// <summary>Static occupancy model for a sequential owner; every actual move still requires independent arbitration.</summary>
    public bool BodyPosesAreFixed { get; }
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

public enum NavigationGeometryStatus : byte
{
    Unavailable = 0, Clear = 1, Limited = 2, Blocked = 3, BudgetExhausted = 4, InvalidInput = 5, Stale = 6
}
public enum NavigationBlockReason : byte
{
    Unknown = 0, None = 1, StaticObstacle = 2, MovingVehicle = 3, ReservedCorridor = 4,
    Terrain = 5, OutsideWorld = 6, MissingGeometry = 7, SearchWork = 8, StaleSnapshot = 9
}
public readonly record struct NavigationSweep(
    NavigationObstacleIdentity Mover, NavigationPose Start, NavigationPose End,
    NavigationVehicleProfile Vehicle, float DurationSeconds);
public readonly record struct NavigationSweepResult(
    NavigationGeometryStatus Status, NavigationSnapshotRevision Revision, ulong FrameTick,
    float AllowedFraction, NavigationBlockReason Reason, NavigationObstacleIdentity? Blocker, int WorkConsumed);

/// <summary>Validates the complete translation/rotation prefix; no transform writes.</summary>
public interface INavigationGeometryQuery
{
    NavigationModuleAvailability Availability { get; }
    NavigationSweepResult Sweep(NavigationCollisionFrame frame, in NavigationSweep movement, int workAllowance);
}
