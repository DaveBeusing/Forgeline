using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Navigation;

namespace ForgeLine.Game;

/// <summary>A copied steering intent. The root binding remains the caller's commit token.</summary>
internal readonly record struct NavigationTrafficIntent(
    NavigationOrderBinding Binding, Vector3 SteeringTarget, float MaximumSpeed, bool CanMove,
    NavigationRouteProposal? Route = null, int CurrentWaypoint = 0);

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
        INavigationGeometryQuery geometry, float horizonSeconds, int workAllowance, out int workConsumed,
        ReadOnlySpan<NavigationPhysicalWait> physicalWaits = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentOutOfRangeException.ThrowIfNegative(workAllowance);
        if (!float.IsFinite(horizonSeconds) || horizonSeconds < 0.25f || horizonSeconds > 10)
            throw new ArgumentOutOfRangeException(nameof(horizonSeconds));
        if (physicalWaits.Length > 128)
            throw new ArgumentException("Physical wait evidence exceeds its capture bound.", nameof(physicalWaits));
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
        int predictionWork = ordered.Length * PredictionSteps * 2;
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
        var speedCaps = new float[ordered.Length];
        var waits = new NavigationBlockReason[ordered.Length];
        var hazards = new NavigationBlockReason[ordered.Length];
        Array.Fill(hazards, NavigationBlockReason.None);
        var waitingFor = new EntityId[ordered.Length];
        var localTargets = new Vector3?[ordered.Length];
        var actualBodies = new Dictionary<NavigationObstacleIdentity, NavigationCollisionBody>();
        if (ordered.Any(intent => frame.Tick.Value >= intent.Binding.OriginalOrder.SubmittedAtTick.Value &&
            frame.Tick.Value - intent.Binding.OriginalOrder.SubmittedAtTick.Value >= RecoveryWaitTicks))
        {
            if (frame.Collision.Bodies.Count > workAllowance - workConsumed) return Unavailable(NavigationBlockReason.SearchWork);
            workConsumed += frame.Collision.Bodies.Count;
            foreach (var body in frame.Collision.Bodies) actualBodies.Add(body.Identity, body);
        }
        float dt = horizonSeconds / PredictionSteps;
        for (int i = 0; i < ordered.Length; i++)
        {
            var intent = ordered[i];
            if (!vehicles.TryGetValue(intent.Binding.Requester, out var vehicle) ||
                vehicle.Owner != intent.Binding.OriginalOrder.Issuer ||
                (vehicle.RouteOrder ?? vehicle.DirectOrder) != intent.Binding.OriginalOrder ||
                (vehicle.DockVisit?.Generation ?? 0) != intent.Binding.DockGeneration ||
                (vehicle.QueueEntry?.Generation ?? 0) != intent.Binding.QueueGeneration ||
                !NavigationPrecisionOwnership.Matches(vehicle, intent.Binding) ||
                !Finite(vehicle.Movement.Velocity) || !float.IsFinite(vehicle.Movement.HeadingRadians) ||
                !Enum.IsDefined(vehicle.Movement.Status))
                return Unavailable(NavigationBlockReason.StaleSnapshot);
            if (!frame.GetVehicleCollision(vehicle.Entity).IsComplete)
                return Unavailable(NavigationBlockReason.MissingGeometry);
            profiles[i] = vehicle.Profile;
            speedCaps[i] = MathF.Min(intent.MaximumSpeed, vehicle.Profile.MaximumSpeed);
            if (intent.Route is { } route && (route.Request.RequestId != intent.Binding.RequestId ||
                route.Request.OriginalDestination != intent.Binding.OriginalOrder.WorldTarget ||
                route.Request.Vehicle != vehicle.Profile || route.Request.Revision != frame.Collision.World.Revision ||
                intent.CurrentWaypoint < 0 || intent.CurrentWaypoint >= route.Waypoints.Count ||
                vehicle.DirectOrder?.WorldTarget != route.Waypoints[intent.CurrentWaypoint]))
                return Unavailable(NavigationBlockReason.StaleSnapshot);
            bool escaping = _states.TryGetValue(vehicle.Entity, out var priorEscape) &&
                priorEscape.CommandGeneration == intent.Binding.CommandGeneration && priorEscape.LastTick <= frame.Tick.Value &&
                priorEscape.RecoveryTarget is Vector3 escapeTarget && HorizontalDistance(vehicle.Pose.Position, escapeTarget) > .2f &&
                intent.CanMove && !NavigationPrecisionOwnership.IsPrecise(vehicle);
            var predicted = escaping
                ? PredictReverse(vehicle, priorEscape.RecoveryTarget!.Value, intent.MaximumSpeed, dt, frame.Collision.World, out var predictionFailure)
                : Predict(vehicle, intent, dt, frame.Collision.World, out predictionFailure);
            if (predicted is null) return Unavailable(predictionFailure);
            paths[i] = predicted;
            // Precision owners retain their own admission and release rules.
            if (escaping || !intent.CanMove || NavigationPrecisionOwnership.IsPrecise(vehicle) ||
                intent.Binding.OriginalOrder.Kind != MovementOrderKind.Strategic)
                waits[i] = NavigationBlockReason.ReservedCorridor;
            else
                waits[i] = NavigationBlockReason.None;
        }

        // Optional local searches share a small slice of the same tick budget.
        // Rotate the starting identity deterministically so a crowded cohort cannot
        // spend the entire budget on the same first actors every tick.
        int detourBudget = Math.Min(32768, (workAllowance - workConsumed) / 4);
        int detourAttempts = 0;
        int detourStart = ordered.Length == 0 ? 0 : (int)(frame.Tick.Value % (ulong)ordered.Length);
        for (int rank = 0; rank < ordered.Length && detourAttempts < 4 && detourBudget > 0; rank++)
        {
            int i = (detourStart + rank) % ordered.Length;
            if (waits[i] != NavigationBlockReason.None) continue;
            var intent = ordered[i];
            var vehicle = vehicles[intent.Binding.Requester];
            int allowance = Math.Min(Math.Min(16384, frame.Limits.WorkPerSearch),
                Math.Min(detourBudget, (workAllowance - workConsumed) / 3));
            bool found = TryLocalDetour(frame, vehicle, intent, paths[i], actualBodies, geometry, allowance,
                out var localTarget, out int detourWork, out bool validDetour);
            workConsumed += detourWork; detourBudget -= detourWork;
            if (detourWork > 0) detourAttempts++;
            if (!validDetour) return Unavailable(NavigationBlockReason.MissingGeometry);
            if (!found) continue;
            var localIntent = intent with { Route = null, SteeringTarget = localTarget, MaximumSpeed = MathF.Min(1, intent.MaximumSpeed) };
            var predicted = Predict(vehicle, localIntent, dt, frame.Collision.World, out var predictionFailure);
            if (predicted is null) return Unavailable(predictionFailure);
            paths[i] = predicted;
            speedCaps[i] = localIntent.MaximumSpeed;
            localTargets[i] = localTarget;
            hazards[i] = NavigationBlockReason.MovingVehicle;
        }

        // A service-blocked approach cannot capture an unentered crossing against
        // the very service owner it must wait for. Occupied leases remain physical
        // ownership and are retained separately until complete rear clearance.
        var admissionAllowed = new bool[ordered.Length];
        for (int i = 0; i < ordered.Length; i++)
        {
            admissionAllowed[i] = waits[i] == NavigationBlockReason.None;
            if (!admissionAllowed[i]) continue;
            foreach (var reservation in frame.Reservations)
            {
                if (workConsumed == workAllowance) return Unavailable(NavigationBlockReason.SearchWork);
                workConsumed++;
                if (!ReservationApplies(vehicles[ordered[i].Binding.Requester], reservation)) continue;
                int reservationWork = PredictionSteps + ServiceReservationCheckWork(paths[i].Length);
                if (workAllowance - workConsumed < reservationWork)
                { workConsumed = workAllowance; return Unavailable(NavigationBlockReason.SearchWork); }
                workConsumed += reservationWork;
                if (!Touches(new(reservation.ReservationId, reservation.Pose, reservation.HalfExtents), paths[i], profiles[i])) continue;
                admissionAllowed[i] = false;
                if (!BlocksServiceReservation(vehicles[ordered[i].Binding.Requester], reservation, paths[i])) continue;
                waits[i] = NavigationBlockReason.ReservedCorridor;
                waitingFor[i] = reservation.Owner;
                break;
            }
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
        // Preserve FIFO at shared unentered crossings before a later queue member
        // parks inside an envelope that the head still needs to reach its front.
        // Reorder only each queue's existing priority slots, keeping a total
        // canonical order across unrelated queues and ordinary road users.
        if (ordered.Any(static intent => intent.Binding.QueueGeneration != 0))
        {
            int queueWork = ordered.Length * (ordered.Length + 1);
            if (queueWork > workAllowance - workConsumed) return Unavailable(NavigationBlockReason.SearchWork);
            workConsumed += queueWork;
            var queueSlots = priority.Select((index, rank) => (Index: index, Rank: rank, Vehicle: vehicles[ordered[index].Binding.Requester]))
                .Where(item => item.Vehicle.QueueEntry is { Generation: > 0, Position: > 0, ArrivedAtService: false } queue &&
                    queue.Owner == item.Vehicle.Owner && ordered[item.Index].Binding.OriginalOrder.WorldTarget == queue.Target)
                .GroupBy(static item => (item.Vehicle.QueueEntry!.Value.Building, item.Vehicle.Owner));
            foreach (var group in queueSlots)
            {
                var slots = group.ToArray();
                var fifo = slots.OrderBy(static item => item.Vehicle.QueueEntry!.Value.Position)
                    .ThenBy(static item => item.Vehicle.Entity).ToArray();
                for (int position = 0; position < slots.Length; position++)
                    priority[slots[position].Rank] = fifo[position].Index;
            }
        }
        var crossingPlan = DiscoverCrossings(frame, ordered, paths, profiles, workAllowance - workConsumed);
        workConsumed += crossingPlan.Work;
        if (!crossingPlan.Complete) return Unavailable(NavigationBlockReason.SearchWork);
        var passagePlan = PreparePassages(frame, ordered, vehicles, paths, priority, admissionAllowed, crossingPlan.Passages, waits, waitingFor,
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
                    if (!Conflicts(paths[i][step - 1], paths[i][step], profiles[i],
                        paths[j][step - 1], paths[j][step], profiles[j])) continue;
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
            var collision = frame.GetVehicleCollision(ordered[i].Binding.Requester);
            float clearDistance = 0;
            for (int step = 1; step <= PredictionSteps; step++)
            {
                if (workConsumed == workAllowance) return Unavailable(NavigationBlockReason.SearchWork);
                var sweep = new NavigationSweep(NavigationReadAdapter.GetObstacleIdentity(ordered[i].Binding.Requester),
                    paths[i][step - 1], paths[i][step], profiles[i], dt);
                var result = SweepGrounded(collision, geometry, sweep, workAllowance - workConsumed);
                if (result.WorkConsumed < 0 || result.WorkConsumed > workAllowance - workConsumed ||
                    !Enum.IsDefined(result.Status) || !Enum.IsDefined(result.Reason) ||
                    !float.IsFinite(result.AllowedFraction) || result.AllowedFraction < 0 || result.AllowedFraction > 1 ||
                    result.Status == NavigationGeometryStatus.Clear &&
                        (result.AllowedFraction != 1 || result.Reason != NavigationBlockReason.None || result.WorkConsumed == 0))
                {
                    workConsumed = workAllowance;
                    return Unavailable(NavigationBlockReason.MissingGeometry);
                }
                workConsumed += result.WorkConsumed;
                if (result.FrameTick != frame.Tick.Value || result.Revision != frame.Collision.World.Revision)
                    return Unavailable(NavigationBlockReason.StaleSnapshot);
                if (result.Status is NavigationGeometryStatus.Unavailable or NavigationGeometryStatus.InvalidInput or NavigationGeometryStatus.Stale)
                    return Unavailable(result.Reason);
                if (result.Status == NavigationGeometryStatus.BudgetExhausted)
                    return Unavailable(NavigationBlockReason.SearchWork);
                if (result.Status != NavigationGeometryStatus.Clear || result.AllowedFraction != 1)
                {
                    float availableDistance = clearDistance + HorizontalDistance(paths[i][step - 1].Position, paths[i][step].Position) * result.AllowedFraction;
                    // A turn may be blocked by a body behind us while forward clearance
                    // is available. This bounded preview grants only a slow ordinary proposal;
                    // the movement owner and final batch arbiter still sweep the actual tick.
                    bool localBlocker = false;
                    if (result.Blocker is { } observedBlocker)
                    {
                        foreach (var body in collision.Bodies)
                        {
                            if (workConsumed == workAllowance) return Unavailable(NavigationBlockReason.SearchWork);
                            workConsumed++;
                            if (body.Identity != observedBlocker) continue;
                            var currentVehicle = vehicles[ordered[i].Binding.Requester];
                            Vector3 forward = Vector3.Transform(Vector3.UnitZ, currentVehicle.Pose.Rotation);
                            bool behind = Vector3.Dot(body.Pose.Position - currentVehicle.Pose.Position, forward) < 0;
                            if (ordered[i].Route is { } turningRoute)
                            {
                                var origin = turningRoute.Request.Start;
                                Vector3 originalForward = Vector3.Transform(Vector3.UnitZ, origin.Rotation);
                                Vector3 goalDelta = ordered[i].Binding.OriginalOrder.WorldTarget - origin.Position;
                                goalDelta.Y = 0;
                                // Retain clearance throughout an initially backward-facing turn,
                                // even after its heading temporarily points toward the rear body.
                                behind |= Vector3.Dot(goalDelta, originalForward) < -.5f * goalDelta.Length() &&
                                    Vector3.Dot(body.Pose.Position - origin.Position, originalForward) < 0;
                            }
                            float localRange = profiles[i].HalfExtents.Length() + profiles[i].ClearanceMeters +
                                body.HalfExtents.Length() + 3;
                            localBlocker = behind && HorizontalDistance(body.Pose.Position, currentVehicle.Pose.Position) <= localRange;
                            break;
                        }
                    }
                    if ((availableDistance <= 0 || localBlocker) &&
                        result.Reason is NavigationBlockReason.MovingVehicle or NavigationBlockReason.StaticObstacle or NavigationBlockReason.Terrain)
                    {
                        int forwardWork = ForwardClearance(frame, vehicles[ordered[i].Binding.Requester],
                            ordered[i], geometry, passagePlan.Leases, workAllowance - workConsumed, out bool clearForward, out _);
                        if (forwardWork < 0) { workConsumed = workAllowance; return Unavailable(NavigationBlockReason.SearchWork); }
                        workConsumed += forwardWork;
                        if (clearForward)
                        {
                            speedCaps[i] = MathF.Min(1, ordered[i].MaximumSpeed);
                            hazards[i] = result.Reason;
                            break;
                        }
                    }
                    // A future obstruction is a braking horizon, not an immediate stop. The
                    // actual motion owner still sweeps its chosen steering step independently.
                    // Holding the entire horizon alternates acceleration/braking forever before
                    // a parked vehicle can enter local avoidance or the stopped recovery phase.
                    if (availableDistance > 0 && result.Status is NavigationGeometryStatus.Blocked or NavigationGeometryStatus.Limited &&
                        result.Reason is NavigationBlockReason.MovingVehicle or NavigationBlockReason.StaticObstacle or NavigationBlockReason.Terrain or NavigationBlockReason.OutsideWorld)
                    {
                        float deceleration = profiles[i].Deceleration;
                        float reserve = deceleration * dt;
                        float safe = MathF.Sqrt(reserve * reserve + 2 * deceleration * availableDistance) - reserve;
                        speedCaps[i] = MathF.Min(speedCaps[i], safe);
                        hazards[i] = result.Reason;
                        if (result.Blocker is NavigationObstacleIdentity distant && distant.Source == NavigationObstacleSource.Entity &&
                            distant.Value <= uint.MaxValue && distant.Generation <= uint.MaxValue)
                            waitingFor[i] = new((uint)distant.Value, (uint)distant.Generation);
                        if (vehicles[ordered[i].Binding.Requester].Movement.Velocity.LengthSquared() <= .0025f &&
                            WaitingAge(ordered[i].Binding, frame.Tick.Value) >= RecoveryWaitTicks)
                            waits[i] = result.Reason;
                        break;
                    }
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
                    if (!ReservationApplies(vehicles[ordered[i].Binding.Requester], reservation)) continue;
                    int checkWork = ServiceReservationCheckWork(2);
                    if (checkWork > workAllowance - workConsumed) return Unavailable(NavigationBlockReason.SearchWork);
                    workConsumed += checkWork;
                    if (BlocksServiceReservation(vehicles[ordered[i].Binding.Requester], reservation, [paths[i][step - 1], paths[i][step]]))
                    {
                        waits[i] = NavigationBlockReason.ReservedCorridor;
                        waitingFor[i] = reservation.Owner;
                        break;
                    }
                }
                if (waits[i] != NavigationBlockReason.None) break;
                clearDistance += HorizontalDistance(paths[i][step - 1].Position, paths[i][step].Position);
            }
        }
        // A stopped wait-for cycle may grant one fully swept reverse escape. Search is bounded,
        // deterministic and remains advisory: the movement owner must brake first and sweep each
        // accepted reverse step again. No lateral displacement is ever authorized here.
        var recovery = new Vector3?[ordered.Length];
        var outwardReverse = new bool[ordered.Length];
        var capturedDepartures = new bool[ordered.Length];
        var recoveryWork = FindRecoveries(frame, ordered, vehicles, waits, waitingFor, geometry, passagePlan.Leases,
            crossingPlan.Passages, workAllowance - workConsumed, recovery, outwardReverse, capturedDepartures, physicalWaits, localTargets, speedCaps, hazards);
        if (recoveryWork < 0)
        {
            workConsumed = workAllowance;
            return Unavailable(NavigationBlockReason.SearchWork);
        }
        workConsumed += recoveryWork;
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
                if (hazards[i] == NavigationBlockReason.None) _states.Remove(intent.Binding.Requester);
                else
                {
                    _ = Waiting(intent.Binding, frame.Tick, hazards[i], 0);
                    _states[intent.Binding.Requester] = _states[intent.Binding.Requester] with { WaitingFor = waitingFor[i], RecoveryTarget = null };
                }
                directives[i] = new(NavigationModuleAvailability.Available, intent.Binding, frame.Tick,
                    NavigationTrafficAction.Proceed, speedCaps[i], localTargets[i], hazards[i])
                { CapturedCycleDepartureOnly = capturedDepartures[i] };
            }
            else
            {
                var hold = Waiting(intent.Binding, frame.Tick, waits[i], 0);
                _states[intent.Binding.Requester] = _states[intent.Binding.Requester] with
                {
                    WaitingFor = waitingFor[i],
                    RecoveryTarget = outwardReverse[i] || capturedDepartures[i] ? null : recovery[i],
                    LastRecoveryTick = recovery[i] is not null ? frame.Tick.Value : _states[intent.Binding.Requester].LastRecoveryTick
                };
                directives[i] = recovery[i] is Vector3 escape
                    ? hold with { Action = NavigationTrafficAction.Recovery, SuggestedTarget = escape, MaximumSpeed = MathF.Min(1, profiles[i].MaximumSpeed), OutwardDepartureOnly = outwardReverse[i], CapturedCycleDepartureOnly = capturedDepartures[i] }
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

    private static bool ReservationApplies(in NavigationVehicleObservation vehicle, in NavigationTrafficReservation reservation)
    {
        if (reservation.Owner == vehicle.Entity) return false;
        // FIFO belongs to the service owner. A later vehicle's unoccupied future
        // waiting pose cannot reserve the approach against an earlier queue member.
        // Actual vehicle bodies remain mandatory physical blockers in every case.
        if (!reservation.PhysicallyOccupied && reservation.Kind == NavigationReservationKind.Queue && reservation.QueueBuilding.IsValid)
        {
            // A future waiting target has no occupancy authority over another service
            // queue or an unrelated road user. Its actual owner remains a physical body.
            if (vehicle.QueueEntry is not { } queue || queue.Building != reservation.QueueBuilding) return false;
            if (queue.Owner == vehicle.Owner && queue.Position > 0 && queue.Position < reservation.QueuePosition) return false;
        }
        return true;
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

    private static NavigationPose[]? PredictReverse(NavigationVehicleObservation vehicle, Vector3 target,
        float maximumSpeed, float dt, NavigationWorldSnapshot world, out NavigationBlockReason failure)
    {
        failure = NavigationBlockReason.Terrain;
        var poses = new NavigationPose[PredictionSteps + 1]; poses[0] = vehicle.Pose;
        for (int step = 1; step <= PredictionSteps; step++)
        {
            if (!NavigationReverseMotion.TryStep(vehicle, target, maximumSpeed, dt, world, out var end, out _, out float heading)) return null;
            poses[step] = end;
            vehicle = vehicle with
            {
                Pose = end,
                Movement = vehicle.Movement with
                { Velocity = (end.Position - vehicle.Pose.Position) / dt, HeadingRadians = heading }
            };
        }
        failure = NavigationBlockReason.None;
        return poses;
    }

    private static NavigationPose[]? Predict(in NavigationVehicleObservation vehicle, in NavigationTrafficIntent intent,
        float dt, NavigationWorldSnapshot world, out NavigationBlockReason failure)
    {
        failure = NavigationBlockReason.MissingGeometry;
        if (!world.TrySampleHeight(vehicle.Pose.Position.X, vehicle.Pose.Position.Z, out float startHeight)) return null;
        float groundOffset = vehicle.Pose.Position.Y - startHeight;
        if (!float.IsFinite(groundOffset)) return null;
        var poses = new NavigationPose[PredictionSteps + 1];
        poses[0] = vehicle.Pose;
        float speed = vehicle.Movement.Velocity.Length();
        float heading = vehicle.Movement.HeadingRadians;
        int waypoint = intent.CurrentWaypoint, advances = 0;
        for (int step = 1; step <= PredictionSteps; step++)
        {
            Vector3 target = intent.Route is { } route ? route.Waypoints[waypoint] : intent.SteeringTarget;
            if (intent.Route is { } forward)
            {
                while (waypoint < forward.Waypoints.Count - 1 &&
                    HorizontalDistance(poses[step - 1].Position, target) <= MathF.Max(0.2f, speed * dt))
                {
                    if (advances == PredictionSteps) { failure = NavigationBlockReason.SearchWork; return null; }
                    advances++; waypoint++; target = forward.Waypoints[waypoint];
                }
            }
            Vector3 delta = target - poses[step - 1].Position;
            delta.Y = 0;
            float distance = delta.Length();
            Vector3 goal = intent.Route?.ResolvedDestination ?? intent.SteeringTarget;
            float remaining = HorizontalDistance(poses[step - 1].Position, goal);
            float desired = intent.CanMove ? MathF.Min(intent.MaximumSpeed,
                MathF.Min(vehicle.Profile.MaximumSpeed, MathF.Sqrt(2 * vehicle.Profile.Deceleration * remaining))) : 0;
            float change = (desired > speed ? vehicle.Profile.Acceleration : vehicle.Profile.Deceleration) * dt;
            speed += Math.Clamp(desired - speed, -change, change);
            if (distance > 0.0001f)
            {
                float targetHeading = MathF.Atan2(delta.X, delta.Z);
                float angle = MathF.IEEERemainder(targetHeading - heading, MathF.Tau);
                heading += Math.Clamp(angle, -vehicle.Profile.MaximumYawRate * dt, vehicle.Profile.MaximumYawRate * dt);
            }
            Vector3 direction = new(MathF.Sin(heading), 0, MathF.Cos(heading));
            Vector3 position = poses[step - 1].Position + direction * MathF.Min(distance, speed * dt);
            if (!world.TrySampleHeight(position.X, position.Z, out float height))
            {
                failure = NavigationBlockReason.OutsideWorld;
                return null;
            }
            position.Y = height + groundOffset;
            if (!Finite(position)) return null;
            poses[step] = new(position, Quaternion.CreateFromAxisAngle(Vector3.UnitY, heading));
        }
        failure = NavigationBlockReason.None;
        return poses;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    // Circumscribed discs conservatively contain every yawed horizontal footprint. Minimize relative
    // separation over the whole interval instead of checking endpoints (which can tunnel at crossings).
    // Compare the complete swept physical boxes in the second vehicle's translating
    // frame. Equal motion cancels, so clear parallel lanes and safe following gaps
    // do not become fictitious crossings. Rotation remains conservatively enclosed.
    private static bool Conflicts(in NavigationPose a0, in NavigationPose a1, in NavigationVehicleProfile a,
        in NavigationPose b0, in NavigationPose b1, in NavigationVehicleProfile b)
    {
        var relativeStart = a0 with { Position = a0.Position - b0.Position };
        var relativeEnd = a1 with { Position = a1.Position - b1.Position };
        var stationaryStart = b0 with { Position = Vector3.Zero };
        var stationaryEnd = b1 with { Position = Vector3.Zero };
        if (!NavigationMotionArbitrator.TryBuildSweptOccupancy(relativeStart, relativeEnd, a,
                out var first, out var firstHalf) ||
            !NavigationMotionArbitrator.TryBuildSweptOccupancy(stationaryStart, stationaryEnd, b,
                out var second, out var secondHalf)) return true;
        firstHalf += new Vector3(a.ClearanceMeters);
        secondHalf += new Vector3(b.ClearanceMeters);
        Span<Vector3> firstAxes = stackalloc Vector3[3]
        {
            Vector3.Transform(Vector3.UnitX, first.Rotation),
            Vector3.Transform(Vector3.UnitY, first.Rotation),
            Vector3.Transform(Vector3.UnitZ, first.Rotation)
        };
        Span<Vector3> secondAxes = stackalloc Vector3[3]
        {
            Vector3.Transform(Vector3.UnitX, second.Rotation),
            Vector3.Transform(Vector3.UnitY, second.Rotation),
            Vector3.Transform(Vector3.UnitZ, second.Rotation)
        };
        Vector3 delta = first.Position - second.Position;
        for (int axisIndex = 0; axisIndex < 15; axisIndex++)
        {
            Vector3 axis = axisIndex < 3 ? firstAxes[axisIndex] : axisIndex < 6 ?
                secondAxes[axisIndex - 3] : Vector3.Cross(firstAxes[(axisIndex - 6) / 3], secondAxes[(axisIndex - 6) % 3]);
            float lengthSquared = axis.LengthSquared();
            if (lengthSquared < 1e-10f) continue;
            float support = firstHalf.X * MathF.Abs(Vector3.Dot(axis, firstAxes[0])) +
                firstHalf.Y * MathF.Abs(Vector3.Dot(axis, firstAxes[1])) +
                firstHalf.Z * MathF.Abs(Vector3.Dot(axis, firstAxes[2])) +
                secondHalf.X * MathF.Abs(Vector3.Dot(axis, secondAxes[0])) +
                secondHalf.Y * MathF.Abs(Vector3.Dot(axis, secondAxes[1])) +
                secondHalf.Z * MathF.Abs(Vector3.Dot(axis, secondAxes[2]));
            if (MathF.Abs(Vector3.Dot(delta, axis)) > support + 1e-5f * MathF.Sqrt(lengthSquared)) return false;
        }
        return true;
    }
}
