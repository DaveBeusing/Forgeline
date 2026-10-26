using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Navigation;

namespace ForgeLine.Game;

internal sealed partial class PredictiveTrafficPlanner
{
    private const ulong RecoveryWaitTicks = 40;

    private int FindRecoveries(NavigationTrafficFrame frame, NavigationTrafficIntent[] intents,
        Dictionary<EntityId, NavigationVehicleObservation> vehicles, NavigationBlockReason[] waits,
        EntityId[] waitingFor, INavigationGeometryQuery geometry, IReadOnlyDictionary<ulong, PredictiveTrafficPassageLease> leases,
        int allowance, Vector3?[] targets, ReadOnlySpan<NavigationPhysicalWait> physicalWaits,
        Vector3?[] forwardTargets, float[] speedCaps, NavigationBlockReason[] hazards)
    {
        int consumed = 0;
        var physicalCycles = new Dictionary<EntityId, EntityId>();
        if (!physicalWaits.IsEmpty)
        {
            if (frame.Collision.Bodies.Count > allowance) return -1;
            consumed += frame.Collision.Bodies.Count;
            var bodies = frame.Collision.Bodies.ToDictionary(static body => body.Identity);
            foreach (var observation in physicalWaits)
            {
                if (allowance - consumed < 4) return -1;
                consumed += 4;
                if (observation.Tick != frame.Tick || observation.Revision != frame.Collision.World.Revision ||
                    observation.Owner.Entity == observation.Blocker.Entity ||
                    !vehicles.TryGetValue(observation.Blocker.Entity, out var blocker) || blocker != observation.Blocker ||
                    !bodies.TryGetValue(NavigationReadAdapter.GetObstacleIdentity(observation.Owner.Entity), out var ownerBody) ||
                    !bodies.TryGetValue(NavigationReadAdapter.GetObstacleIdentity(blocker.Entity), out var blockerBody) ||
                    ownerBody.Pose != observation.Owner.Pose || ownerBody.HalfExtents != observation.Owner.Profile.HalfExtents ||
                    blockerBody.Pose != blocker.Pose || blockerBody.HalfExtents != blocker.Profile.HalfExtents) continue;
                physicalCycles[observation.Owner.Entity] = blocker.Entity;
            }
        }
        var indices = new Dictionary<EntityId, int>();
        for (int i = 0; i < intents.Length; i++) indices.Add(intents[i].Binding.Requester, i);
        var visited = new bool[intents.Length];
        var claimedPaths = new List<(EntityId Owner, NavigationPose[] Path, NavigationVehicleProfile Profile)>();
        for (int start = 0; start < intents.Length; start++)
        {
            if (visited[start]) continue;
            var chain = new List<int>();
            var positions = new Dictionary<int, int>();
            int current = start;
            while (!visited[current])
            {
                if (consumed == allowance) return -1;
                consumed++;
                if (positions.TryGetValue(current, out int cycleStart))
                {
                    var cycle = chain.Skip(cycleStart).OrderBy(i => intents[i].Binding.Requester).ToArray();
                    bool activeEscape = cycle.Any(i => _states.TryGetValue(intents[i].Binding.Requester, out var state) &&
                        state.CommandGeneration == intents[i].Binding.CommandGeneration && state.RecoveryTarget is not null);
                    if (activeEscape) break;
                    foreach (int candidate in cycle)
                    {
                        if (!CanRecover(candidate)) continue;
                        if (TryTargets(candidate)) break;
                        if (consumed == allowance) return -1;
                    }
                    break;
                }
                positions.Add(current, chain.Count);
                chain.Add(current);
                if (!indices.TryGetValue(waitingFor[current], out int next)) break;
                current = next;
            }
            foreach (int index in chain) visited[index] = true;
        }
        // A verified escape remains bound to its original command until its physical endpoint.
        for (int i = 0; i < intents.Length; i++)
        {
            if (targets[i] is not null || !_states.TryGetValue(intents[i].Binding.Requester, out var state) ||
                state.CommandGeneration != intents[i].Binding.CommandGeneration || state.LastTick > frame.Tick.Value ||
                state.RecoveryTarget is not Vector3 target || !intents[i].CanMove) continue;
            var vehicle = vehicles[intents[i].Binding.Requester];
            if (Vector3.DistanceSquared(vehicle.Pose.Position, target) < 0.04f) continue;
            if (vehicle.Movement.Velocity.Length() > 1.01f || NavigationPrecisionOwnership.IsPrecise(vehicle)) continue;
            _ = TryEscape(i, target, verifyWholeRoute: false);
            if (consumed == allowance && targets[i] is null) return -1;
        }
        // A physical blocker outside the coarse wait graph can justify a verified
        // reverse arc. A service reservation alone is a logical admission wait; it
        // does not establish a physical deadlock. A copied same-tick physical veto
        // in the opposite direction closes a mixed precise/coarse cycle.
        for (int i = 0; i < intents.Length; i++)
        {
            if (targets[i] is not null || !CanRecover(i) || !waitingFor[i].IsValid ||
                indices.ContainsKey(waitingFor[i]) ||
                !(waits[i] is NavigationBlockReason.MovingVehicle or NavigationBlockReason.StaticObstacle ||
                  waits[i] == NavigationBlockReason.ReservedCorridor &&
                  physicalCycles.TryGetValue(waitingFor[i], out var physicallyWaitingFor) &&
                  physicallyWaitingFor == intents[i].Binding.Requester)) continue;
            // The precise owner is physically blocked by this coarse waiter.
            // A checked forward prefix can clear that owner even when the root's
            // planned turn would enter its reservation. Do not replace the root.
            if (waits[i] == NavigationBlockReason.ReservedCorridor &&
                physicalCycles.TryGetValue(waitingFor[i], out var opposite) && opposite == intents[i].Binding.Requester)
            {
                int forwardWork = ForwardClearance(frame, vehicles[intents[i].Binding.Requester], intents[i],
                    geometry, leases, allowance - consumed, out bool clear, out Vector3 forwardTarget);
                if (forwardWork < 0) return -1;
                consumed += forwardWork;
                if (clear)
                {
                    forwardTargets[i] = forwardTarget;
                    speedCaps[i] = MathF.Min(1, intents[i].MaximumSpeed);
                    hazards[i] = NavigationBlockReason.MovingVehicle;
                    waits[i] = NavigationBlockReason.None;
                    continue;
                }
            }
            _ = TryTargets(i);
            if (consumed == allowance && targets[i] is null) return -1;
        }
        return consumed;

        bool TryTargets(int index)
        {
            var vehicle = vehicles[intents[index].Binding.Requester];
            Vector3 back = Vector3.Transform(-Vector3.UnitZ, vehicle.Pose.Rotation); back.Y = 0;
            Vector3 side = Vector3.Transform(Vector3.UnitX, vehicle.Pose.Rotation); side.Y = 0;
            if (back.LengthSquared() < .0001f || side.LengthSquared() < .0001f) return false;
            back = Vector3.Normalize(back); side = Vector3.Normalize(side);
            float lateral = MathF.Max(6, vehicle.Profile.HalfExtents.Length() * 2 + 1);
            if (TryEscape(index, vehicle.Pose.Position + back + side * lateral)) return true;
            if (TryEscape(index, vehicle.Pose.Position + back - side * lateral)) return true;
            return TryEscape(index, vehicle.Pose.Position + back * MathF.Min(3, MathF.Max(1, vehicle.Profile.HalfExtents.Z * 2)));
        }

        bool CanRecover(int index)
        {
            var intent = intents[index];
            var vehicle = vehicles[intent.Binding.Requester];
            return intent.CanMove && intent.MaximumSpeed > 0 && waits[index] != NavigationBlockReason.None &&
                !NavigationPrecisionOwnership.IsPrecise(vehicle) &&
                vehicle.Movement.Velocity.LengthSquared() <= 0.0025f &&
                WaitingAge(intent.Binding, frame.Tick.Value) >= RecoveryWaitTicks;
        }

        bool TryEscape(int index, Vector3 target, bool verifyWholeRoute = true)
        {
            if (consumed == allowance) return false;
            var vehicle = vehicles[intents[index].Binding.Requester];
            if (!frame.Collision.World.TrySampleHeight(target.X, target.Z, out float targetHeight) ||
                !frame.Collision.World.TrySampleHeight(vehicle.Pose.Position.X, vehicle.Pose.Position.Z, out float startHeight)) return false;
            target.Y = targetHeight + vehicle.Pose.Position.Y - startHeight;
            const float dt = .125f;
            var path = new List<NavigationPose>(129) { vehicle.Pose };
            var simulated = vehicle;
            bool reached = false;
            for (int step = 0; step < (verifyWholeRoute ? 128 : 1); step++)
            {
                if (consumed == allowance) return false;
                consumed++;
                if (!NavigationReverseMotion.TryStep(simulated, target, MathF.Min(1, intents[index].MaximumSpeed), dt,
                    frame.Collision.World, out var end, out _, out float heading)) return false;
                var sweep = new NavigationSweep(NavigationReadAdapter.GetObstacleIdentity(vehicle.Entity), simulated.Pose,
                    end, vehicle.Profile, dt);
                var result = SweepGrounded(frame.GetVehicleCollision(vehicle.Entity), geometry, sweep, allowance - consumed);
                if (result.WorkConsumed <= 0 || result.WorkConsumed > allowance - consumed)
                { consumed = allowance; return false; }
                consumed += result.WorkConsumed;
                if (result.Status != NavigationGeometryStatus.Clear || result.AllowedFraction != 1 ||
                    result.FrameTick != frame.Tick.Value || result.Revision != frame.Collision.World.Revision) return false;
                foreach (var reservation in frame.Reservations)
                {
                    int checkWork = ServiceReservationCheckWork(2);
                    if (checkWork > allowance - consumed) { consumed = allowance; return false; }
                    consumed += checkWork;
                    if (BlocksServiceReservation(vehicle, reservation, [simulated.Pose, end])) return false;
                }
                foreach (var lease in leases.Values)
                {
                    if (consumed == allowance) return false;
                    consumed++;
                    if (lease.Owner != vehicle.Entity && Touches(lease.Passage, [simulated.Pose, end], vehicle.Profile)) return false;
                }
                foreach (var prior in claimedPaths)
                {
                    if (prior.Owner == vehicle.Entity) continue;
                    for (int segment = 1; segment < prior.Path.Length; segment++)
                    {
                        if (consumed == allowance) return false;
                        consumed++;
                        if (Conflicts(simulated.Pose, end, vehicle.Profile,
                            prior.Path[segment - 1], prior.Path[segment], prior.Profile)) return false;
                    }
                }
                path.Add(end);
                simulated = simulated with { Pose = end, Movement = simulated.Movement with
                    { Velocity = (end.Position - simulated.Pose.Position) / dt, HeadingRadians = heading } };
                if (HorizontalDistance(end.Position, target) <= .2f) { reached = true; break; }
            }
            if (verifyWholeRoute && !reached) return false;
            claimedPaths.Add((vehicle.Entity, path.ToArray(), vehicle.Profile));
            targets[index] = target;
            return true;
        }
    }
}
