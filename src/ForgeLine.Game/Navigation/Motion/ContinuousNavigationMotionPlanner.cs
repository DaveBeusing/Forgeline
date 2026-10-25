using System.Numerics;
using ForgeLine.Navigation;

namespace ForgeLine.Game;

/// <summary>
/// Computes ordinary corridor motion from one copied tick. Precise local, dock and
/// queue motion stays with its existing owner. This class has no mutable history.
/// </summary>
public sealed class ContinuousNavigationMotionPlanner : INavigationMotionPlanner
{
    private const float DirectionEpsilon = 0.00001f;
    private readonly NavigationMotionPlannerOptions _options;

    public ContinuousNavigationMotionPlanner(NavigationMotionPlannerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
    }

    public NavigationModuleAvailability Availability => NavigationModuleAvailability.Available;

    public NavigationMotionProposal Propose(in NavigationMotionInput input,
        NavigationCollisionFrame collision, INavigationGeometryQuery geometry,
        int workAllowance, out int workConsumed) =>
        ProposeCore(input, collision, geometry, workAllowance, out workConsumed, out _, out _, out _);

    /// <summary>Simulation-only trace; waypoint indices are not permission to advance route ownership.</summary>
    internal NavigationMotionProposal ProposeCore(in NavigationMotionInput input,
        NavigationCollisionFrame collision, INavigationGeometryQuery geometry,
        int workAllowance, out int workConsumed, out Vector3 usedWaypoint,
        out int currentWaypointIndex, out int usedWaypointIndex)
    {
        ArgumentNullException.ThrowIfNull(collision);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentOutOfRangeException.ThrowIfNegative(workAllowance);
        workConsumed = 0;
        usedWaypoint = input.Vehicle.Pose.Position;
        currentWaypointIndex = -1;
        usedWaypointIndex = -1;

        if (!ValidInput(input, collision))
            return Hold(input, collision, NavigationModuleAvailability.Unavailable, NavigationBlockReason.StaleSnapshot);
        if (!input.CanMove)
            return Hold(input, collision, Availability, NavigationBlockReason.None);
        if (PreciseOwner(input))
            return Hold(input, collision, NavigationModuleAvailability.Unavailable, NavigationBlockReason.ReservedCorridor);
        if (!ValidTraffic(input))
            return Hold(input, collision, NavigationModuleAvailability.Unavailable, NavigationBlockReason.StaleSnapshot);
        if (input.Traffic.Availability != NavigationModuleAvailability.Available)
            return Hold(input, collision, NavigationModuleAvailability.Unavailable, NavigationBlockReason.MissingGeometry);
        if (input.Traffic.Action is NavigationTrafficAction.Hold or NavigationTrafficAction.Yield)
            return Hold(input, collision, Availability, input.Traffic.Reason);
        if (input.Traffic.Action == NavigationTrafficAction.Recovery)
            return Hold(input, collision, NavigationModuleAvailability.Unavailable, NavigationBlockReason.ReservedCorridor);
        if (geometry.Availability != NavigationModuleAvailability.Available || !collision.IsComplete || !collision.World.HasTerrain)
            return Hold(input, collision, NavigationModuleAvailability.Unavailable, NavigationBlockReason.MissingGeometry);
        if (workAllowance == 0)
            return Hold(input, collision, Availability, NavigationBlockReason.SearchWork);

        NavigationRouteProposal? route = input.Route;
        if (route is null || !ValidRoute(input, collision, route))
            return Hold(input, collision, NavigationModuleAvailability.Unavailable, NavigationBlockReason.StaleSnapshot);
        if (!TryFindCurrentWaypoint(input, route, workAllowance, ref workConsumed, out int currentWaypoint))
            return Hold(input, collision, NavigationModuleAvailability.Unavailable,
                workConsumed == workAllowance || route.Waypoints.Count > _options.MaximumRouteWaypoints
                    ? NavigationBlockReason.SearchWork : NavigationBlockReason.StaleSnapshot);
        currentWaypointIndex = currentWaypoint;

        float currentSpeed = HorizontalLength(input.Vehicle.Movement.Velocity);
        float maximumSpeed = MathF.Min(input.Vehicle.Profile.MaximumSpeed,
            MathF.Min(input.EffectiveMaximumSpeed, input.Traffic.MaximumSpeed));
        if (!float.IsFinite(currentSpeed) || currentSpeed > input.Vehicle.Profile.MaximumSpeed + DirectionEpsilon)
            return Hold(input, collision, NavigationModuleAvailability.Unavailable, NavigationBlockReason.Unknown);
        if (maximumSpeed <= 0)
            return Hold(input, collision, Availability, input.Traffic.Reason);

        float brakingDistance = currentSpeed * currentSpeed / (2 * input.Vehicle.Profile.Deceleration);
        float lookahead = MathF.Min(_options.MaximumLookaheadMeters, maximumSpeed * _options.PlanningHorizonSeconds);
        if (!float.IsFinite(brakingDistance) || !float.IsFinite(lookahead) || lookahead <= 0)
            return Hold(input, collision, NavigationModuleAvailability.Unavailable, NavigationBlockReason.Unknown);
        if (!Spend(workAllowance, ref workConsumed) ||
            !collision.World.TrySampleHeight(input.Vehicle.Pose.Position.X, input.Vehicle.Pose.Position.Z, out float height) ||
            !float.IsFinite(height))
            return Hold(input, collision, NavigationModuleAvailability.Unavailable,
                workConsumed == workAllowance ? NavigationBlockReason.SearchWork : NavigationBlockReason.Terrain);
        float groundOffset = input.Vehicle.Pose.Position.Y - height;
        if (!float.IsFinite(groundOffset))
            return Hold(input, collision, NavigationModuleAvailability.Unavailable, NavigationBlockReason.Terrain);

        if (!TryLookahead(input, collision, geometry, route, currentWaypoint, groundOffset,
            lookahead, maximumSpeed, workAllowance, ref workConsumed, out Vector3 steeringTarget,
            out float safeDistance, out NavigationBlockReason horizonReason, out int steeringIndex))
            return Hold(input, collision, Availability, horizonReason, brakingDistance);
        usedWaypoint = steeringTarget;
        usedWaypointIndex = steeringIndex;

        return ProposeStep(input, collision, geometry, route, steeringTarget, safeDistance,
            groundOffset, currentSpeed, maximumSpeed, brakingDistance, horizonReason,
            workAllowance, ref workConsumed);
    }

    private NavigationMotionProposal ProposeStep(in NavigationMotionInput input,
        NavigationCollisionFrame collision, INavigationGeometryQuery geometry,
        NavigationRouteProposal route, Vector3 target, float safeDistance, float groundOffset,
        float currentSpeed, float maximumSpeed, float brakingDistance, NavigationBlockReason horizonReason,
        int workAllowance, ref int workConsumed)
    {
        NavigationPose start = input.Vehicle.Pose;
        NavigationVehicleProfile profile = input.Vehicle.Profile;
        float finalDistance = HorizontalDistance(start.Position, input.Binding.OriginalOrder.WorldTarget);
        float tolerance = route.Request.Goal.PositionTolerance;
        if (finalDistance <= tolerance)
            return Hold(input, collision, Availability, NavigationBlockReason.None, brakingDistance);

        Vector3 difference = target - start.Position;
        float targetDistance = HorizontalLength(difference);
        if (!float.IsFinite(targetDistance) || targetDistance <= DirectionEpsilon)
            return Hold(input, collision, Availability, horizonReason, brakingDistance);
        float desiredHeading = MathF.Atan2(difference.X, difference.Z);
        float headingChange = GroundMovementState.NormalizeAngle(desiredHeading - input.Vehicle.Movement.HeadingRadians);
        float curvature = 2 * MathF.Sin(headingChange) / targetDistance;
        float turnSpeed = MathF.Abs(curvature) > DirectionEpsilon
            ? profile.MaximumYawRate / MathF.Abs(curvature) : maximumSpeed;
        // Pure-pursuit curvature tends to zero for a target directly behind the
        // vehicle. The heading bound also slows that case instead of reversing.
        if (MathF.Abs(headingChange) > MathF.PI * 0.5f)
            turnSpeed = MathF.Min(turnSpeed, profile.MaximumYawRate * targetDistance / MathF.Abs(headingChange));

        float remaining = MathF.Min(safeDistance, MathF.Max(0, finalDistance - tolerance));
        float brakingStep = profile.Deceleration * input.DeltaSeconds;
        // Reserve a complete semi-implicit step before the remaining stopping
        // distance: v*dt + v^2/(2*a) must fit inside the approved horizon.
        double freeSpeed = Math.Sqrt((double)brakingStep * brakingStep + 2.0 * profile.Deceleration * remaining) - brakingStep;
        float speedLimit = MathF.Min(maximumSpeed, MathF.Min(turnSpeed, (float)freeSpeed));
        if (!float.IsFinite(curvature) || !float.IsFinite(speedLimit) || speedLimit < 0 || !float.IsFinite(brakingStep))
            return Hold(input, collision, NavigationModuleAvailability.Unavailable, NavigationBlockReason.Unknown, brakingDistance);

        float change = (speedLimit >= currentSpeed ? profile.Acceleration : profile.Deceleration) * input.DeltaSeconds;
        float speed = MoveTowards(currentSpeed, speedLimit, change);
        // A newly imposed hard limit or unexpectedly shortened safe horizon can
        // be incompatible with normal braking. Safety retains priority; do not
        // authorize an overspeed step on the assumption that another tick exists.
        if (!float.IsFinite(speed) || speed > maximumSpeed + DirectionEpsilon ||
            (double)speed * input.DeltaSeconds + (double)speed * speed / (2 * profile.Deceleration) > remaining + DirectionEpsilon)
            return Hold(input, collision, Availability, horizonReason == NavigationBlockReason.None
                ? NavigationBlockReason.ReservedCorridor : horizonReason, brakingDistance);

        float yawStep = profile.MaximumYawRate * input.DeltaSeconds;
        if (!float.IsFinite(yawStep))
            return Hold(input, collision, NavigationModuleAvailability.Unavailable, NavigationBlockReason.Unknown, brakingDistance);
        float heading = GroundMovementState.NormalizeAngle(input.Vehicle.Movement.HeadingRadians +
            Math.Clamp(headingChange, -yawStep, yawStep));
        Vector3 forward = new(MathF.Sin(heading), 0, MathF.Cos(heading));
        Vector3 candidate = start.Position + forward * (speed * input.DeltaSeconds);
        candidate = GroundMovementStep.ClampArrival(start.Position, candidate,
            input.Binding.OriginalOrder.WorldTarget, tolerance);
        candidate = NavigationMotionNumerics.ConstrainTravel(start.Position, candidate, (double)speed * input.DeltaSeconds);
        if (!Spend(workAllowance, ref workConsumed))
            return Hold(input, collision, Availability, NavigationBlockReason.SearchWork, brakingDistance);
        if (!collision.World.TrySampleHeight(candidate.X, candidate.Z, out float height) || !float.IsFinite(height))
            return Hold(input, collision, Availability, NavigationBlockReason.Terrain, brakingDistance);
        candidate.Y = height + groundOffset;
        NavigationPose end = new(candidate, Quaternion.CreateFromAxisAngle(Vector3.UnitY, heading));
        if (!end.IsValid)
            return Hold(input, collision, NavigationModuleAvailability.Unavailable, NavigationBlockReason.Terrain, brakingDistance);
        var sweep = new NavigationSweep(NavigationReadAdapter.GetObstacleIdentity(input.Vehicle.Entity),
            start, end, profile, input.DeltaSeconds);
        if (!TrySweep(geometry, collision, sweep, workAllowance, ref workConsumed, out NavigationSweepResult result))
            return Hold(input, collision, NavigationModuleAvailability.Unavailable, NavigationBlockReason.MissingGeometry, brakingDistance);
        if (result.AllowedFraction <= 0)
            return Hold(input, collision, Availability, result.Reason, brakingDistance);

        // A verified prefix belongs to this exact translation/rotation. Shorten
        // both together; never snap sideways or approve only a safe endpoint.
        float fraction = result.AllowedFraction;
        NavigationPose approved = fraction == 1 ? end : new NavigationPose(
            Vector3.Lerp(start.Position, end.Position, fraction),
            Quaternion.Normalize(Quaternion.Slerp(start.Rotation, end.Rotation, fraction)));
        if (fraction < 1)
        {
            // Applying a shortened prefix over the whole tick retimes its
            // interaction with moving bodies. That is a different sweep and
            // must be fully approved before it becomes a proposal.
            var retimed = sweep with { End = approved };
            if (!TrySweep(geometry, collision, retimed, workAllowance, ref workConsumed, out NavigationSweepResult rechecked))
                return Hold(input, collision, NavigationModuleAvailability.Unavailable, NavigationBlockReason.MissingGeometry, brakingDistance);
            if (rechecked.Status != NavigationGeometryStatus.Clear || rechecked.AllowedFraction != 1)
                return Hold(input, collision, Availability, rechecked.Reason, brakingDistance);
        }
        float actualSpeed = HorizontalDistance(start.Position, approved.Position) / input.DeltaSeconds;
        if (!approved.IsValid || !float.IsFinite(actualSpeed))
            return Hold(input, collision, NavigationModuleAvailability.Unavailable, NavigationBlockReason.Unknown, brakingDistance);
        return new NavigationMotionProposal(Availability, input.Binding, input.Tick, collision.World.Revision,
            start, approved, actualSpeed, curvature, brakingDistance, safeDistance,
            fraction < 1 ? result.Reason : horizonReason);
    }

    private bool TryLookahead(in NavigationMotionInput input, NavigationCollisionFrame collision,
        INavigationGeometryQuery geometry, NavigationRouteProposal route, int currentWaypoint,
        float groundOffset, float lookahead, float maximumSpeed, int allowance, ref int consumed,
        out Vector3 target, out float safeDistance, out NavigationBlockReason reason, out int usedWaypoint)
    {
        target = input.Vehicle.Pose.Position;
        safeDistance = 0;
        reason = NavigationBlockReason.SearchWork;
        usedWaypoint = -1;
        int pointCount = route.Waypoints.Count;
        bool finalSegment = route.ResolvedDestination != input.Binding.OriginalOrder.WorldTarget;
        if (finalSegment && pointCount == int.MaxValue) return false;
        int total = checked(pointCount + (finalSegment ? 1 : 0));
        int last = currentWaypoint;
        Vector3 previous = input.Vehicle.Pose.Position;
        Vector3 candidate = previous;
        float traversed = 0;
        int inspected = 0;
        for (int index = currentWaypoint; index < total && inspected < _options.MaximumLookaheadWaypoints; index++)
        {
            if (!Spend(allowance, ref consumed)) return false;
            Vector3 point = index < pointCount ? route.Waypoints[index] : input.Binding.OriginalOrder.WorldTarget;
            float length = HorizontalDistance(previous, point);
            if (!float.IsFinite(length)) { reason = NavigationBlockReason.Unknown; return false; }
            last = index;
            inspected++;
            if (traversed + length >= lookahead && length > DirectionEpsilon)
            {
                candidate = Vector3.Lerp(previous, point, (lookahead - traversed) / length);
                break;
            }
            candidate = point;
            traversed += length;
            previous = point;
        }

        // All candidate/refinement searches share a fixed budget. They cannot
        // repeatedly consume the work reserved for the actual tick sweep.
        int horizonWorkLimit = consumed + (allowance - consumed) / 2;
        NavigationBlockReason partialReason = NavigationBlockReason.SearchWork;
        // A far blocked shortcut must not hide a fully clear nearer corner.
        // Keep partial prefixes only as fallbacks after the bounded search.
        for (int index = last; index >= currentWaypoint; index--)
        {
            if (TryGroundedHorizon(input, collision, geometry, candidate, groundOffset, maximumSpeed,
                horizonWorkLimit, ref consumed, out Vector3 candidateTarget, out float candidateDistance,
                out reason, out bool fullyCleared))
            {
                if (fullyCleared)
                {
                    target = candidateTarget;
                    safeDistance = candidateDistance;
                    usedWaypoint = index;
                    return true;
                }
                if (candidateDistance > safeDistance)
                {
                    target = candidateTarget;
                    safeDistance = candidateDistance;
                    usedWaypoint = index;
                    partialReason = reason;
                }
            }
            if (reason == NavigationBlockReason.MissingGeometry) return false;
            // A child can exhaust only its slice while enough of the shared
            // horizon budget remains to prove a shorter candidate completely.
            if (consumed >= horizonWorkLimit) break;
            if (index > currentWaypoint)
                candidate = route.Waypoints[index - 1];
        }
        // A parked body can occupy an intermediate grid waypoint while a later
        // corridor target remains reachable. Inspect bounded forward directions
        // before settling for the blocked prefix. Full body/terrain clearance is
        // mandatory; this does not consume or replace the original waypoint.
        for (int index = last + 1, inspectedForward = 0; index < total && inspectedForward < _options.MaximumLookaheadWaypoints; index++, inspectedForward++)
        {
            if (!Spend(horizonWorkLimit, ref consumed)) break;
            Vector3 point = index < pointCount ? route.Waypoints[index] : input.Binding.OriginalOrder.WorldTarget;
            Vector3 delta = point - input.Vehicle.Pose.Position; delta.Y = 0;
            float length = delta.Length();
            if (!float.IsFinite(length) || length <= DirectionEpsilon) continue;
            Vector3 alternate = length > lookahead ? input.Vehicle.Pose.Position + delta * (lookahead / length) : point;
            if (TryGroundedHorizon(input, collision, geometry, alternate, groundOffset, maximumSpeed,
                horizonWorkLimit, ref consumed, out var nextTarget, out float nextDistance, out var nextReason, out bool clear) && clear)
            {
                target = nextTarget; safeDistance = nextDistance; usedWaypoint = index; reason = nextReason;
                return true;
            }
            if (nextReason == NavigationBlockReason.MissingGeometry) { reason = nextReason; return false; }
        }
        if (safeDistance <= 0) return false;
        reason = partialReason;
        return true;
    }

    private bool TryGroundedHorizon(in NavigationMotionInput input, NavigationCollisionFrame collision,
        INavigationGeometryQuery geometry, Vector3 candidate, float groundOffset, float maximumSpeed,
        int allowance, ref int consumed, out Vector3 target, out float safeDistance,
        out NavigationBlockReason reason, out bool fullyCleared)
    {
        target = input.Vehicle.Pose.Position;
        safeDistance = 0;
        reason = NavigationBlockReason.Terrain;
        fullyCleared = false;
        NavigationBlockReason approvedReason = reason;
        int refinement = 0;
        while (true)
        {
            float distance = HorizontalDistance(input.Vehicle.Pose.Position, candidate);
            if (!float.IsFinite(distance) || distance <= DirectionEpsilon) break;
            if (!Spend(allowance, ref consumed))
            {
                reason = safeDistance > 0 ? approvedReason : NavigationBlockReason.SearchWork;
                return safeDistance > 0;
            }
            if (collision.World.TrySampleHeight(candidate.X, candidate.Z, out float height) && float.IsFinite(height))
            {
                candidate.Y = height + groundOffset;
                float desiredHeading = MathF.Atan2(candidate.X - input.Vehicle.Pose.Position.X, candidate.Z - input.Vehicle.Pose.Position.Z);
                float duration = MathF.Max(input.DeltaSeconds, distance / maximumSpeed);
                float yaw = input.Vehicle.Profile.MaximumYawRate * duration;
                if (!float.IsFinite(duration) || !float.IsFinite(yaw))
                { reason = NavigationBlockReason.Unknown; return false; }
                float change = GroundMovementState.NormalizeAngle(desiredHeading - input.Vehicle.Movement.HeadingRadians);
                float heading = GroundMovementState.NormalizeAngle(input.Vehicle.Movement.HeadingRadians + Math.Clamp(change, -yaw, yaw));
                NavigationPose end = new(candidate, Quaternion.CreateFromAxisAngle(Vector3.UnitY, heading));
                var sweep = new NavigationSweep(NavigationReadAdapter.GetObstacleIdentity(input.Vehicle.Entity),
                    input.Vehicle.Pose, end, input.Vehicle.Profile, duration);
                // Each retry starts at this tick's observed pose. No downstream
                // sub-sweep is mislabeled as starting at the same mobile time0.
                int horizonAllowance = (allowance - consumed) / 2;
                if (horizonAllowance <= 0)
                {
                    reason = safeDistance > 0 ? approvedReason : NavigationBlockReason.SearchWork;
                    return safeDistance > 0;
                }
                if (!TrySweep(geometry, collision, sweep, consumed + horizonAllowance, ref consumed, out NavigationSweepResult result))
                { reason = NavigationBlockReason.MissingGeometry; return false; }
                reason = result.Reason;
                float approved = distance * result.AllowedFraction;
                if (!float.IsFinite(approved)) { reason = NavigationBlockReason.Unknown; return false; }
                if (result.Status == NavigationGeometryStatus.Clear)
                {
                    // A shorter, completely grounded/swept candidate takes
                    // precedence over any longer partial prefix from this ray.
                    target = candidate;
                    safeDistance = distance;
                    fullyCleared = true;
                    return true;
                }
                if (approved > safeDistance)
                {
                    target = candidate;
                    safeDistance = approved;
                    approvedReason = reason;
                }
                if (reason != NavigationBlockReason.Terrain)
                {
                    if (safeDistance > 0) reason = approvedReason;
                    return safeDistance > 0;
                }
            }
            if (refinement >= _options.MaximumGroundedRefinements) break;
            refinement++;
            candidate = Vector3.Lerp(input.Vehicle.Pose.Position, candidate, 0.5f);
        }
        reason = approvedReason;
        return safeDistance > 0;
    }

    private bool TryFindCurrentWaypoint(in NavigationMotionInput input, NavigationRouteProposal route,
        int allowance, ref int consumed, out int waypoint)
    {
        waypoint = -1;
        if (route.Waypoints.Count > _options.MaximumRouteWaypoints ||
            input.Vehicle.DirectOrder is not MovementOrder active ||
            input.Vehicle.RouteOrder != input.Binding.OriginalOrder ||
            active.Issuer != input.Binding.OriginalOrder.Issuer ||
            active.SubmittedAtTick != input.Binding.OriginalOrder.SubmittedAtTick ||
            active.AcceptedAtTick.Value < input.Binding.OriginalOrder.AcceptedAtTick.Value ||
            active.AcceptedAtTick.Value > input.Tick.Value || active.Kind != MovementOrderKind.Strategic)
            return false;
        bool duplicate = false;
        bool finalSegment = route.ResolvedDestination != input.Binding.OriginalOrder.WorldTarget;
        long count = route.Waypoints.Count + (finalSegment ? 1L : 0L);
        for (long index = 0; index < count; index++)
        {
            if (!Spend(allowance, ref consumed)) return false;
            Vector3 point = index < route.Waypoints.Count ? route.Waypoints[(int)index] : input.Binding.OriginalOrder.WorldTarget;
            if (point != active.WorldTarget) continue;
            duplicate |= waypoint >= 0;
            waypoint = (int)index;
        }
        return waypoint >= 0 && !duplicate;
    }

    private static bool ValidInput(in NavigationMotionInput input, NavigationCollisionFrame collision)
    {
        NavigationVehicleObservation vehicle = input.Vehicle;
        MovementOrder order = input.Binding.OriginalOrder;
        if (!vehicle.Entity.IsValid || vehicle.Entity != input.Binding.Requester ||
            !vehicle.Owner.IsSpecified || order.Issuer != vehicle.Owner ||
            input.Binding.RequestId == 0 || input.Binding.CommandGeneration == 0 ||
            !vehicle.Pose.IsValid || !vehicle.Profile.IsValid || !Finite(vehicle.Movement.Velocity) ||
            !NavigationPrecisionOwnership.Matches(vehicle, input.Binding) ||
            !float.IsFinite(vehicle.Movement.HeadingRadians) || !Enum.IsDefined(vehicle.Movement.Status) ||
            !float.IsFinite(input.DeltaSeconds) || input.DeltaSeconds <= 0 ||
            !float.IsFinite(input.EffectiveMaximumSpeed) || input.EffectiveMaximumSpeed < 0 ||
            !Finite(order.WorldTarget) || !Enum.IsDefined(order.Kind) ||
            order.SubmittedAtTick.Value > order.AcceptedAtTick.Value || order.AcceptedAtTick.Value > input.Tick.Value ||
            collision.Tick != input.Tick.Value || !collision.World.Revision.IsValid)
            return false;
        Vector3 forward = Vector3.Transform(Vector3.UnitZ, vehicle.Pose.Rotation);
        Vector3 up = Vector3.Transform(Vector3.UnitY, vehicle.Pose.Rotation);
        return MathF.Abs(forward.Y) <= 0.001f && up.Y >= 0.999f &&
            MathF.Abs(GroundMovementState.NormalizeAngle(MathF.Atan2(forward.X, forward.Z) -
                vehicle.Movement.HeadingRadians)) <= 0.001f;
    }

    private static bool ValidRoute(in NavigationMotionInput input, NavigationCollisionFrame collision,
        NavigationRouteProposal route) =>
        route.Request.RequestId == input.Binding.RequestId &&
        route.Request.OriginalDestination == input.Binding.OriginalOrder.WorldTarget &&
        route.Request.Revision == collision.World.Revision &&
        route.Request.KnowledgeScope == collision.World.KnowledgeScope &&
        route.Request.Capabilities == collision.World.Capabilities &&
        route.Request.Vehicle == input.Vehicle.Profile &&
        route.Request.TargetSemantics is NavigationTargetSemantics.PlayerGoal or NavigationTargetSemantics.Transit &&
        WithinProjectionDistance(route.ResolvedDestination, route.Request.OriginalDestination, route.Request.Goal.MaximumProjectionDistance) &&
        !route.Request.Goal.RequiredHeadingRadians.HasValue &&
        input.Vehicle.Pending is null;

    private static bool ValidTraffic(in NavigationMotionInput input) =>
        Enum.IsDefined(input.Traffic.Availability) && Enum.IsDefined(input.Traffic.Action) &&
        Enum.IsDefined(input.Traffic.Reason) && input.Traffic.Binding == input.Binding &&
        input.Traffic.Tick == input.Tick && float.IsFinite(input.Traffic.MaximumSpeed) && input.Traffic.MaximumSpeed >= 0 &&
        (!input.Traffic.SuggestedTarget.HasValue || Finite(input.Traffic.SuggestedTarget.Value));

    private static bool PreciseOwner(in NavigationMotionInput input) =>
        input.Binding.OriginalOrder.Kind == MovementOrderKind.LocalApproach ||
        input.Binding.OriginalOrder.TargetHeadingRadians.HasValue ||
        input.Vehicle.DirectOrder is MovementOrder order &&
            (order.Kind == MovementOrderKind.LocalApproach || order.TargetHeadingRadians.HasValue) ||
        NavigationPrecisionOwnership.IsPrecise(input.Vehicle) ||
        input.Route?.Request.TargetSemantics == NavigationTargetSemantics.DockAccess ||
        input.Route?.Request.Goal.RequiredHeadingRadians.HasValue == true;

    private static bool TrySweep(INavigationGeometryQuery geometry, NavigationCollisionFrame collision,
        in NavigationSweep sweep, int allowance, ref int consumed, out NavigationSweepResult result)
    {
        int remaining = allowance - consumed;
        if (remaining <= 0)
        {
            result = new(NavigationGeometryStatus.BudgetExhausted, collision.World.Revision, collision.Tick,
                0, NavigationBlockReason.SearchWork, null, 0);
            return true;
        }
        result = geometry.Sweep(collision, sweep, remaining);
        if (result.WorkConsumed < 0 || result.WorkConsumed > remaining)
        {
            consumed = allowance;
            return false;
        }
        consumed += result.WorkConsumed;
        if (!Enum.IsDefined(result.Status) || !Enum.IsDefined(result.Reason) ||
            result.Revision != collision.World.Revision || result.FrameTick != collision.Tick ||
            !float.IsFinite(result.AllowedFraction) || result.AllowedFraction < 0 || result.AllowedFraction > 1)
            return false;
        return result.Status switch
        {
            NavigationGeometryStatus.Clear => result.AllowedFraction == 1 && result.WorkConsumed > 0 &&
                result.Reason == NavigationBlockReason.None && !result.Blocker.HasValue,
            NavigationGeometryStatus.Limited or NavigationGeometryStatus.BudgetExhausted =>
                result.AllowedFraction == 0 || result.WorkConsumed > 0,
            _ => result.AllowedFraction == 0
        };
    }

    private static NavigationMotionProposal Hold(in NavigationMotionInput input, NavigationCollisionFrame collision,
        NavigationModuleAvailability availability, NavigationBlockReason reason, float brakingDistance = 0) =>
        new(availability, input.Binding, input.Tick, collision.World.Revision, input.Vehicle.Pose,
            input.Vehicle.Pose, 0, 0, brakingDistance, 0, reason);

    private static bool Spend(int allowance, ref int consumed)
    {
        if (consumed >= allowance) return false;
        consumed++;
        return true;
    }

    private static float MoveTowards(float value, float target, float change) =>
        target >= value ? MathF.Min(target, value + change) : MathF.Max(target, value - change);

    private static float HorizontalLength(Vector3 value) =>
        (float)Math.Sqrt((double)value.X * value.X + (double)value.Z * value.Z);

    private static float HorizontalDistance(Vector3 first, Vector3 second) => HorizontalLength(first - second);

    private static bool WithinProjectionDistance(Vector3 first, Vector3 second, float maximum)
    {
        double x = (double)first.X - second.X;
        double y = (double)first.Y - second.Y;
        double z = (double)first.Z - second.Z;
        return x * x + y * y + z * z <= (double)maximum * maximum;
    }

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
