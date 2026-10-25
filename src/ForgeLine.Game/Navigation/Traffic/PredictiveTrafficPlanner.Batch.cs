using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Navigation;

namespace ForgeLine.Game;

/// <summary>A copied steering intent. The root binding remains the caller's commit token.</summary>
internal readonly record struct NavigationTrafficIntent(
    NavigationOrderBinding Binding, Vector3 SteeringTarget, float MaximumSpeed, bool CanMove);

internal sealed partial class PredictiveTrafficPlanner
{
    private const int MaximumTrackedVehicles = 512;
    private const int PredictionSteps = 16;

    /// <summary>
    /// Computes the entire tick in a canonical order before publishing any directive. Work exhaustion
    /// invalidates the whole batch, so changing input order cannot select a different partial winner.
    /// Geometry remains a mandatory veto; a predicted right of way never permits contact.
    /// </summary>
    public IReadOnlyList<NavigationTrafficDirective> PlanBatch(
        NavigationTrafficFrame frame, ReadOnlySpan<NavigationTrafficIntent> intents,
        INavigationGeometryQuery geometry, float horizonSeconds, int workAllowance, out int workConsumed)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentOutOfRangeException.ThrowIfNegative(workAllowance);
        if (!float.IsFinite(horizonSeconds) || horizonSeconds < 0.25f || horizonSeconds > 10)
            throw new ArgumentOutOfRangeException(nameof(horizonSeconds));
        if (intents.Length > MaximumTrackedVehicles)
            throw new ArgumentException("Traffic batch exceeds its participant bound.", nameof(intents));
        var ordered = intents.ToArray();
        Array.Sort(ordered, static (a, b) => CompareIdentity(a.Binding.Requester, b.Binding.Requester));
        for (int i = 0; i < ordered.Length; i++)
            if (!Valid(ordered[i]) || i > 0 && ordered[i - 1].Binding.Requester == ordered[i].Binding.Requester)
                throw new ArgumentException("Traffic intents require distinct complete bindings and finite steering.", nameof(intents));

        var directives = new NavigationTrafficDirective[ordered.Length];
        workConsumed = 0;
        if (!frame.IsComplete || frame.Collision.Tick != frame.Tick.Value ||
            geometry.Availability != NavigationModuleAvailability.Available)
            return Unavailable(NavigationBlockReason.MissingGeometry);
        int predictionWork = ordered.Length * PredictionSteps;
        if (predictionWork > workAllowance)
        {
            workConsumed = workAllowance;
            return Unavailable(NavigationBlockReason.SearchWork);
        }
        workConsumed = predictionWork;
        var vehicles = new Dictionary<EntityId, NavigationVehicleObservation>();
        foreach (var vehicle in frame.Vehicles) vehicles.Add(vehicle.Entity, vehicle);
        var paths = new NavigationPose[ordered.Length][];
        var profiles = new NavigationVehicleProfile[ordered.Length];
        var waits = new NavigationBlockReason[ordered.Length];
        var waitingFor = new EntityId[ordered.Length];
        float dt = horizonSeconds / PredictionSteps;
        for (int i = 0; i < ordered.Length; i++)
        {
            var intent = ordered[i];
            if (!vehicles.TryGetValue(intent.Binding.Requester, out var vehicle) ||
                vehicle.Owner != intent.Binding.OriginalOrder.Issuer ||
                (vehicle.RouteOrder ?? vehicle.DirectOrder) != intent.Binding.OriginalOrder ||
                (vehicle.DockVisit?.Generation ?? 0) != intent.Binding.DockGeneration ||
                (vehicle.QueueEntry?.Generation ?? 0) != intent.Binding.QueueGeneration ||
                !Finite(vehicle.Movement.Velocity))
                return Unavailable(NavigationBlockReason.StaleSnapshot);
            profiles[i] = vehicle.Profile;
            paths[i] = Predict(vehicle, intent, dt);
            // Precision owners retain their own admission and release rules.
            if (!intent.CanMove || vehicle.DockVisit is not null || vehicle.QueueEntry is not null ||
                intent.Binding.OriginalOrder.Kind != MovementOrderKind.Strategic)
                waits[i] = NavigationBlockReason.ReservedCorridor;
            else
                waits[i] = NavigationBlockReason.None;
        }

        // Priority is waiting age followed by full identity, never call or dictionary order.
        var priority = Enumerable.Range(0, ordered.Length).ToArray();
        Array.Sort(priority, (a, b) =>
        {
            ulong ageA = WaitingAge(ordered[a].Binding, frame.Tick.Value);
            ulong ageB = WaitingAge(ordered[b].Binding, frame.Tick.Value);
            int age = ageB.CompareTo(ageA);
            return age != 0 ? age : CompareIdentity(ordered[a].Binding.Requester, ordered[b].Binding.Requester);
        });
        var passagePlan = PreparePassages(frame, ordered, vehicles, paths, priority, waits, waitingFor,
            workAllowance - workConsumed);
        workConsumed += passagePlan.Work;
        if (!passagePlan.Complete) return Unavailable(NavigationBlockReason.SearchWork);
        for (int rank = 0; rank < priority.Length; rank++)
        {
            int i = priority[rank];
            if (waits[i] != NavigationBlockReason.None) continue;
            for (int otherRank = 0; otherRank < rank; otherRank++)
            {
                int j = priority[otherRank];
                if (waits[j] != NavigationBlockReason.None) continue;
                for (int step = 1; step <= PredictionSteps; step++)
                {
                    if (workConsumed == workAllowance) return Unavailable(NavigationBlockReason.SearchWork);
                    workConsumed++;
                    if (!Conflicts(paths[i][step - 1].Position, paths[i][step].Position, profiles[i],
                        paths[j][step - 1].Position, paths[j][step].Position, profiles[j])) continue;
                    waits[i] = NavigationBlockReason.MovingVehicle;
                    waitingFor[i] = ordered[j].Binding.Requester;
                    break;
                }
                if (waits[i] != NavigationBlockReason.None) break;
            }
        }

        for (int i = 0; i < ordered.Length; i++)
        {
            if (waits[i] != NavigationBlockReason.None) continue;
            for (int step = 1; step <= PredictionSteps; step++)
            {
                if (workConsumed == workAllowance) return Unavailable(NavigationBlockReason.SearchWork);
                var sweep = new NavigationSweep(NavigationReadAdapter.GetObstacleIdentity(ordered[i].Binding.Requester),
                    paths[i][step - 1], paths[i][step], profiles[i], dt);
                var result = geometry.Sweep(frame.Collision, sweep, workAllowance - workConsumed);
                if (result.WorkConsumed < 0 || result.WorkConsumed > workAllowance - workConsumed)
                    return Unavailable(NavigationBlockReason.MissingGeometry);
                workConsumed += result.WorkConsumed;
                if (result.FrameTick != frame.Tick.Value || result.Revision != frame.Collision.World.Revision)
                    return Unavailable(NavigationBlockReason.StaleSnapshot);
                if (result.Status == NavigationGeometryStatus.BudgetExhausted)
                    return Unavailable(NavigationBlockReason.SearchWork);
                if (result.Status != NavigationGeometryStatus.Clear || result.AllowedFraction != 1)
                {
                    if (result.Blocker is NavigationObstacleIdentity blocker && blocker.Source == NavigationObstacleSource.Entity &&
                        blocker.Value <= uint.MaxValue && blocker.Generation <= uint.MaxValue)
                        waitingFor[i] = new EntityId((uint)blocker.Value, (uint)blocker.Generation);
                    waits[i] = result.Reason == NavigationBlockReason.None ? NavigationBlockReason.MissingGeometry : result.Reason;
                    break;
                }
                foreach (var reservation in frame.Reservations)
                {
                    if (workConsumed == workAllowance) return Unavailable(NavigationBlockReason.SearchWork);
                    workConsumed++;
                    if (reservation.Owner == ordered[i].Binding.Requester) continue;
                    var reservedProfile = profiles[i] with { HalfExtents = reservation.HalfExtents, ClearanceMeters = 0 };
                    if (Conflicts(paths[i][step - 1].Position, paths[i][step].Position, profiles[i],
                        reservation.Pose.Position, reservation.Pose.Position, reservedProfile))
                    {
                        waits[i] = NavigationBlockReason.ReservedCorridor;
                        break;
                    }
                }
                if (waits[i] != NavigationBlockReason.None) break;
            }
        }
        // A stopped wait-for cycle may grant one fully swept reverse escape. Search is bounded,
        // deterministic and remains advisory: the movement owner must brake first and sweep each
        // accepted reverse step again. No lateral displacement is ever authorized here.
        var recovery = new Vector3?[ordered.Length];
        var recoveryWork = FindRecoveries(frame, ordered, vehicles, waits, waitingFor, geometry,
            workAllowance - workConsumed, recovery);
        if (recoveryWork < 0) return Unavailable(NavigationBlockReason.SearchWork);
        workConsumed += recoveryWork;
        for (int i = 0; i < recovery.Length; i++)
        {
            if (recovery[i] is not Vector3 escape) continue;
            foreach (var lease in passagePlan.Leases.Values)
            {
                if (workConsumed == workAllowance) return Unavailable(NavigationBlockReason.SearchWork);
                workConsumed++;
                if (lease.Owner != ordered[i].Binding.Requester &&
                    Touches(lease.Passage, [vehicles[ordered[i].Binding.Requester].Pose,
                        new NavigationPose(escape, vehicles[ordered[i].Binding.Requester].Pose.Rotation)], profiles[i]))
                {
                    recovery[i] = null;
                    break;
                }
            }
        }

        // State is committed only after a complete successful computation.
        _passageLeases.Clear();
        foreach (var pair in passagePlan.Leases) _passageLeases.Add(pair.Key, pair.Value);
        var live = ordered.Select(static v => v.Binding.Requester).ToHashSet();
        foreach (var entity in _states.Keys.Where(e => !live.Contains(e)).ToArray()) _states.Remove(entity);
        for (int i = 0; i < ordered.Length; i++)
        {
            var intent = ordered[i];
            if (waits[i] == NavigationBlockReason.None)
            {
                _states.Remove(intent.Binding.Requester);
                directives[i] = new(NavigationModuleAvailability.Available, intent.Binding, frame.Tick,
                    NavigationTrafficAction.Proceed, MathF.Min(intent.MaximumSpeed, profiles[i].MaximumSpeed), null, NavigationBlockReason.None);
            }
            else
            {
                var hold = Waiting(intent.Binding, frame.Tick, waits[i], 0);
                _states[intent.Binding.Requester] = _states[intent.Binding.Requester] with
                {
                    WaitingFor = waitingFor[i], RecoveryTarget = recovery[i],
                    LastRecoveryTick = recovery[i] is not null ? frame.Tick.Value : _states[intent.Binding.Requester].LastRecoveryTick
                };
                directives[i] = recovery[i] is Vector3 escape
                    ? hold with { Action = NavigationTrafficAction.Recovery, SuggestedTarget = escape, MaximumSpeed = MathF.Min(1, profiles[i].MaximumSpeed) }
                    : hold with { Action = waits[i] == NavigationBlockReason.MovingVehicle ? NavigationTrafficAction.Yield : NavigationTrafficAction.Hold };
            }
        }
        return Array.AsReadOnly(directives);

        IReadOnlyList<NavigationTrafficDirective> Unavailable(NavigationBlockReason reason)
        {
            for (int i = 0; i < ordered.Length; i++)
                directives[i] = new(NavigationModuleAvailability.Unavailable, ordered[i].Binding, frame.Tick,
                    NavigationTrafficAction.Hold, 0, null, reason);
            return Array.AsReadOnly(directives);
        }
    }

    private ulong WaitingAge(in NavigationOrderBinding binding, ulong tick) =>
        _states.TryGetValue(binding.Requester, out var state) && state.CommandGeneration == binding.CommandGeneration &&
        state.LastTick <= tick ? tick - state.WaitingSince : 0;

    private static bool Valid(in NavigationTrafficIntent intent) =>
        intent.Binding.Requester.IsValid && intent.Binding.CommandGeneration > 0 &&
        intent.Binding.OriginalOrder.Issuer.IsSpecified && Finite(intent.SteeringTarget) &&
        float.IsFinite(intent.MaximumSpeed) && intent.MaximumSpeed >= 0;

    private static int CompareIdentity(EntityId a, EntityId b)
    {
        int index = a.Index.CompareTo(b.Index);
        return index != 0 ? index : a.Generation.CompareTo(b.Generation);
    }

    private static NavigationPose[] Predict(in NavigationVehicleObservation vehicle, in NavigationTrafficIntent intent, float dt)
    {
        var poses = new NavigationPose[PredictionSteps + 1];
        poses[0] = vehicle.Pose;
        float speed = vehicle.Movement.Velocity.Length();
        float heading = vehicle.Movement.HeadingRadians;
        for (int step = 1; step <= PredictionSteps; step++)
        {
            Vector3 delta = intent.SteeringTarget - poses[step - 1].Position;
            delta.Y = 0;
            float distance = delta.Length();
            float desired = intent.CanMove ? MathF.Min(intent.MaximumSpeed,
                MathF.Min(vehicle.Profile.MaximumSpeed, MathF.Sqrt(2 * vehicle.Profile.Deceleration * distance))) : 0;
            float change = (desired > speed ? vehicle.Profile.Acceleration : vehicle.Profile.Deceleration) * dt;
            speed += Math.Clamp(desired - speed, -change, change);
            if (distance > 0.0001f)
            {
                float targetHeading = MathF.Atan2(delta.X, delta.Z);
                float angle = MathF.IEEERemainder(targetHeading - heading, MathF.Tau);
                heading += Math.Clamp(angle, -vehicle.Profile.MaximumYawRate * dt, vehicle.Profile.MaximumYawRate * dt);
            }
            Vector3 forward = new(MathF.Sin(heading), 0, MathF.Cos(heading));
            Vector3 position = poses[step - 1].Position + forward * MathF.Min(distance, speed * dt);
            poses[step] = new(position, Quaternion.CreateFromAxisAngle(Vector3.UnitY, heading));
        }
        return poses;
    }

    // Circumscribed discs conservatively contain every yawed horizontal footprint. Minimize relative
    // separation over the whole interval instead of checking endpoints (which can tunnel at crossings).
    private static bool Conflicts(Vector3 a0, Vector3 a1, in NavigationVehicleProfile a,
        Vector3 b0, Vector3 b1, in NavigationVehicleProfile b)
    {
        Vector2 relative = new(a0.X - b0.X, a0.Z - b0.Z);
        Vector2 velocity = new(a1.X - a0.X - b1.X + b0.X, a1.Z - a0.Z - b1.Z + b0.Z);
        float t = velocity.LengthSquared() > 0 ? Math.Clamp(-Vector2.Dot(relative, velocity) / velocity.LengthSquared(), 0, 1) : 0;
        float radius = a.HalfExtents.Length() + a.ClearanceMeters +
            b.HalfExtents.Length() + b.ClearanceMeters;
        return (relative + velocity * t).LengthSquared() <= radius * radius;
    }
}
