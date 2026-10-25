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
        int allowance, Vector3?[] targets)
    {
        int consumed = 0;
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
        // A precise service owner or a stationary non-participant has no outgoing edge in the
        // coarse wait graph. Its blocked approach can still yield through a verified reverse arc.
        for (int i = 0; i < intents.Length; i++)
        {
            if (targets[i] is not null || !CanRecover(i) || !waitingFor[i].IsValid ||
                indices.ContainsKey(waitingFor[i])) continue;
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
                    if (consumed == allowance) return false;
                    consumed++;
                    if (ReservationApplies(vehicle, reservation) && Touches(new(reservation.ReservationId, reservation.Pose,
                        reservation.HalfExtents), [simulated.Pose, end], vehicle.Profile)) return false;
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
