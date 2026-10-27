using System.Numerics;

namespace ForgeLine.Navigation;

public enum NavigationTargetSemantics : byte { Unspecified = 0, PlayerGoal = 1, DockAccess = 2, Transit = 3 }
public enum NavigationSearchStatus : byte
{
    InvalidInput = 0, Pending = 1, Found = 2, Unreachable = 3, BudgetExhausted = 4, Canceled = 5, Stale = 6
}

public readonly record struct NavigationGoalConstraints(
    float MaximumProjectionDistance, float PositionTolerance, float? RequiredHeadingRadians, float HeadingToleranceRadians)
{
    public bool IsValid => float.IsFinite(MaximumProjectionDistance) && MaximumProjectionDistance >= 0 &&
        float.IsFinite(PositionTolerance) && PositionTolerance >= 0 &&
        (!RequiredHeadingRadians.HasValue || float.IsFinite(RequiredHeadingRadians.Value)) &&
        float.IsFinite(HeadingToleranceRadians) && HeadingToleranceRadians >= 0 && HeadingToleranceRadians <= MathF.PI;
}

/// <summary>Opaque existing request ID; entity and command generations remain in the Game binding.</summary>
public sealed record NavigationSearchSpecification(
    ulong RequestId,
    NavigationPose Start,
    Vector3 OriginalDestination,
    NavigationTargetSemantics TargetSemantics,
    NavigationGoalConstraints Goal,
    NavigationCapabilities Capabilities,
    NavigationVehicleProfile Vehicle,
    NavigationSnapshotRevision Revision,
    ulong KnowledgeScope,
    int MaximumWork,
    long MaximumWorkspaceBytes,
    int MaximumEndpointCandidates)
{
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfZero(RequestId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumWork);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumWorkspaceBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumEndpointCandidates);
        if (!Start.IsValid || !Vehicle.IsValid || !Revision.IsValid || !Goal.IsValid ||
            !Enum.IsDefined(Capabilities.MovementClass) || !Enum.IsDefined(TargetSemantics) ||
            TargetSemantics == NavigationTargetSemantics.Unspecified ||
            !float.IsFinite(OriginalDestination.X) || !float.IsFinite(OriginalDestination.Y) || !float.IsFinite(OriginalDestination.Z))
            throw new InvalidOperationException("Search input must carry a valid pose, profile, revision and explicit target semantics.");
    }
}

/// <summary>Copied route output; an alternate endpoint never changes the original gameplay target.</summary>
public sealed class NavigationRouteProposal
{
    public NavigationRouteProposal(NavigationSearchSpecification request, Vector3 resolvedDestination,
        ReadOnlySpan<Vector3> waypoints, ReadOnlySpan<NavigationRegionRevision> regions,
        NavigationPathDiagnostics diagnostics, int maximumWaypoints)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumWaypoints);
        if (waypoints.IsEmpty || waypoints.Length > maximumWaypoints || waypoints[^1] != resolvedDestination)
            throw new ArgumentException("The bounded route must terminate at its resolved endpoint.", nameof(waypoints));
        foreach (Vector3 point in waypoints)
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z))
                throw new ArgumentException("Route points must be finite.", nameof(waypoints));
        if (regions.Length > request.MaximumWork)
            throw new ArgumentException("Route dependencies exceed the request work bound.", nameof(regions));
        foreach (NavigationRegionRevision region in regions)
            if (!region.Revision.IsValid || region.Revision > request.Revision.Geometry)
                throw new ArgumentException("Route dependency revision is invalid.", nameof(regions));
        Request = request;
        ResolvedDestination = resolvedDestination;
        Waypoints = Array.AsReadOnly(waypoints.ToArray());
        Regions = Array.AsReadOnly(regions.ToArray());
        Diagnostics = diagnostics;
    }

    // JSON metadata must not bind a ref-struct constructor parameter.
    [System.Text.Json.Serialization.JsonConstructor]
    public NavigationRouteProposal(NavigationSearchSpecification request, Vector3 resolvedDestination,
        IReadOnlyList<Vector3> waypoints, IReadOnlyList<NavigationRegionRevision> regions,
        NavigationPathDiagnostics diagnostics)
        : this(request, resolvedDestination,
            (waypoints ?? throw new ArgumentNullException(nameof(waypoints))).ToArray(),
            (regions ?? throw new ArgumentNullException(nameof(regions))).ToArray(),
            diagnostics, Math.Max(1, waypoints.Count))
    {
    }

    public NavigationSearchSpecification Request { get; }
    public Vector3 ResolvedDestination { get; }
    public IReadOnlyList<Vector3> Waypoints { get; }
    public IReadOnlyList<NavigationRegionRevision> Regions { get; }
    public NavigationPathDiagnostics Diagnostics { get; }
}

public readonly record struct NavigationSearchAdvance(
    NavigationSearchStatus Status, int WorkConsumed, long WorkspaceBytes,
    NavigationRouteProposal? Route, NavigationFailureReason FailureReason);

/// <summary>Single-owner resumable state; every search stage is charged to Advance, not CreateSession.</summary>
public interface INavigationSearchSession<TCheckpoint> : IDisposable where TCheckpoint : class
{
    NavigationSearchSpecification Request { get; }
    NavigationSearchAdvance Advance(int workAllowance);
    TCheckpoint CaptureState();
    void Cancel();
}

public interface INavigationSearchKernel<TCheckpoint> where TCheckpoint : class
{
    NavigationModuleAvailability Availability { get; }
    INavigationSearchSession<TCheckpoint> CreateSession(NavigationWorldSnapshot snapshot, NavigationSearchSpecification request);
    INavigationSearchSession<TCheckpoint> RestoreSession(NavigationWorldSnapshot snapshot, TCheckpoint checkpoint);
}
