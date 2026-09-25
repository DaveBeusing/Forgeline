using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Navigation;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum FormationTemplate : byte
{
    Line = 1,
    Column = 2,
    Wedge = 3,
    Compact = 4
}

public enum MovementGroupStatus : byte
{
    AwaitingRoute = 0,
    Moving = 1,
    Arrived = 2,
    Failed = 3
}

public readonly record struct MovementGroupOrder
{
    public MovementGroupOrder(
        PlayerId issuer,
        Vector3 destination,
        FormationTemplate formation,
        SimulationTick submittedAtTick,
        SimulationTick acceptedAtTick)
    {
        if (!issuer.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(issuer));
        }

        if (!IsFinite(destination))
        {
            throw new ArgumentOutOfRangeException(nameof(destination));
        }

        if (!Enum.IsDefined(formation))
        {
            throw new ArgumentOutOfRangeException(nameof(formation));
        }

        Issuer = issuer;
        Destination = destination;
        Formation = formation;
        SubmittedAtTick = submittedAtTick;
        AcceptedAtTick = acceptedAtTick;
    }

    public PlayerId Issuer { get; }

    public Vector3 Destination { get; }

    public FormationTemplate Formation { get; }

    public SimulationTick SubmittedAtTick { get; }

    public SimulationTick AcceptedAtTick { get; }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}

public readonly record struct MovementGroupMember
{
    public const int UnassignedSlot = -1;

    public MovementGroupMember(EntityId group, int slotIndex = UnassignedSlot)
    {
        if (!group.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(group));
        }

        if (slotIndex < UnassignedSlot)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex));
        }

        Group = group;
        SlotIndex = slotIndex;
    }

    public EntityId Group { get; }

    public int SlotIndex { get; }
}

public readonly record struct MovementGroupState(
    MovementGroupStatus Status,
    Vector3 Centroid,
    Vector3 BoundsMinimum,
    Vector3 BoundsMaximum,
    Vector3 Forward,
    int MemberCount,
    float RepresentativeRadius,
    float EffectiveSpeed,
    float CompressionScale,
    int SplitCohortCount,
    SimulationTick UpdatedAtTick)
{
    public static MovementGroupState Pending(SimulationTick acceptedAtTick) =>
        new(
            MovementGroupStatus.AwaitingRoute,
            Vector3.Zero,
            Vector3.Zero,
            Vector3.Zero,
            Vector3.UnitZ,
            0,
            0.0f,
            0.0f,
            1.0f,
            1,
            acceptedAtTick);
}

public readonly record struct MovementGroupPendingPath(
    NavigationPathRequest Request);

public readonly record struct MovementGroupRoute(
    NavigationPath Path,
    int NextWaypointIndex)
{
    public bool IsFinalWaypoint =>
        Path.Waypoints.Count > 0 &&
        NextWaypointIndex >= Path.Waypoints.Count - 1;
}

public readonly record struct MovementGroupFailureState(
    NavigationFailureReason FailureReason,
    NavigationVersion NavigationVersion,
    SimulationTick FailedAtTick);

public readonly record struct FormationMovementConstraint
{
    public FormationMovementConstraint(float maximumSpeed)
    {
        if (!float.IsFinite(maximumSpeed) || maximumSpeed <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSpeed));
        }

        MaximumSpeed = maximumSpeed;
    }

    public float MaximumSpeed { get; }
}

public readonly record struct FormationMovementDiagnosticsSnapshot(
    int ActiveGroupCount,
    int ActiveMemberCount,
    int LargestGroupSize,
    int CompressedGroupCount,
    ulong SharedPathRequestCount,
    ulong CompletedGroupCount,
    ulong FailedGroupCount,
    ulong SlotReassignmentCount,
    ulong CompressionEventCount,
    ulong SplitEventCount,
    ulong BlockedSlotProjectionCount);

public readonly record struct FormationMovementDebugGroup(
    EntityId Group,
    FormationTemplate Formation,
    Vector3 Centroid,
    Vector3 BoundsMinimum,
    Vector3 BoundsMaximum,
    Vector3 Forward,
    Vector3 ActiveWaypoint,
    int MemberCount,
    float CompressionScale,
    int SplitCohortCount);

public readonly record struct FormationMovementDebugSlot(
    EntityId Group,
    EntityId Member,
    int SlotIndex,
    Vector3 MemberPosition,
    Vector3 SlotTarget);

public readonly record struct FormationMovementDebugRoutePoint(
    EntityId Group,
    int Index,
    Vector3 Position);

public sealed class FormationMovementDebugSnapshot
{
    private readonly FormationMovementDebugGroup[] _groups;
    private readonly FormationMovementDebugSlot[] _slots;
    private readonly FormationMovementDebugRoutePoint[] _routePoints;

    public FormationMovementDebugSnapshot(
        ReadOnlySpan<FormationMovementDebugGroup> groups,
        ReadOnlySpan<FormationMovementDebugSlot> slots,
        ReadOnlySpan<FormationMovementDebugRoutePoint> routePoints)
    {
        _groups = groups.ToArray();
        _slots = slots.ToArray();
        _routePoints = routePoints.ToArray();
    }

    public IReadOnlyList<FormationMovementDebugGroup> Groups => _groups;

    public IReadOnlyList<FormationMovementDebugSlot> Slots => _slots;

    public IReadOnlyList<FormationMovementDebugRoutePoint> RoutePoints =>
        _routePoints;

    public static FormationMovementDebugSnapshot Empty { get; } =
        new(
            ReadOnlySpan<FormationMovementDebugGroup>.Empty,
            ReadOnlySpan<FormationMovementDebugSlot>.Empty,
            ReadOnlySpan<FormationMovementDebugRoutePoint>.Empty);
}

public sealed record FormationMovementSystemOptions
{
    public float SpacingMarginMeters { get; init; } = 1.5f;

    public float WaypointRadiusMeters { get; init; } = 4.0f;

    public float ArrivalToleranceMeters { get; init; } = 2.0f;

    public float StretchSlowdownDistanceMultiplier { get; init; } = 3.0f;

    public float StretchedSpeedFactor { get; init; } = 0.75f;

    public float MinimumCompressionScale { get; init; } = 0.45f;

    public int CorridorScanCells { get; init; } = 8;

    public int SplitCohortSize { get; init; } = 12;

    public float SplitCohortGapMultiplier { get; init; } = 2.0f;

    public void Validate()
    {
        RequirePositiveFinite(
            SpacingMarginMeters,
            nameof(SpacingMarginMeters));
        RequirePositiveFinite(
            WaypointRadiusMeters,
            nameof(WaypointRadiusMeters));
        RequirePositiveFinite(
            ArrivalToleranceMeters,
            nameof(ArrivalToleranceMeters));
        RequirePositiveFinite(
            StretchSlowdownDistanceMultiplier,
            nameof(StretchSlowdownDistanceMultiplier));

        if (!float.IsFinite(StretchedSpeedFactor) ||
            StretchedSpeedFactor <= 0.0f ||
            StretchedSpeedFactor > 1.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(StretchedSpeedFactor));
        }

        if (!float.IsFinite(MinimumCompressionScale) ||
            MinimumCompressionScale <= 0.0f ||
            MinimumCompressionScale > 1.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumCompressionScale));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(
            CorridorScanCells,
            1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            SplitCohortSize,
            2);
        RequirePositiveFinite(
            SplitCohortGapMultiplier,
            nameof(SplitCohortGapMultiplier));
    }

    private static void RequirePositiveFinite(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }
}
