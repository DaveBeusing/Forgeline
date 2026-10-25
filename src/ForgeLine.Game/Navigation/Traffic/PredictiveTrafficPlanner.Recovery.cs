using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Navigation;

namespace ForgeLine.Game;

internal sealed partial class PredictiveTrafficPlanner
{
    private const ulong RecoveryWaitTicks = 40;

    private int FindRecoveries(NavigationTrafficFrame frame, NavigationTrafficIntent[] intents,
        Dictionary<EntityId, NavigationVehicleObservation> vehicles, NavigationBlockReason[] waits,
        EntityId[] waitingFor, INavigationGeometryQuery geometry, int allowance, Vector3?[] targets)
    {
        int consumed = 0;
        var indices = new Dictionary<EntityId, int>();
        for (int i = 0; i < intents.Length; i++) indices.Add(intents[i].Binding.Requester, i);
        var visited = new bool[intents.Length];
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
                        Vector3 backwards = Vector3.Transform(-Vector3.UnitZ, vehicles[intents[candidate].Binding.Requester].Pose.Rotation);
                        backwards.Y = 0;
                        if (backwards.LengthSquared() < 0.0001f) continue;
                        backwards = Vector3.Normalize(backwards);
                        var vehicle = vehicles[intents[candidate].Binding.Requester];
                        Vector3 target = vehicle.Pose.Position + backwards * MathF.Min(3, MathF.Max(1, vehicle.Profile.HalfExtents.Z * 2));
                        if (TryEscape(candidate, target)) break;
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
            Vector3 direction = Vector3.Transform(-Vector3.UnitZ, vehicle.Pose.Rotation);
            Vector3 delta = target - vehicle.Pose.Position;
            if (Vector3.Dot(direction, delta) <= 0 || vehicle.Movement.Velocity.Length() > 1.01f) continue;
            _ = TryEscape(i, target);
            if (consumed == allowance && targets[i] is null) return -1;
        }
        return consumed;

        bool CanRecover(int index)
        {
            var intent = intents[index];
            var vehicle = vehicles[intent.Binding.Requester];
            return intent.CanMove && waits[index] != NavigationBlockReason.None &&
                vehicle.DockVisit is null && vehicle.QueueEntry is null &&
                vehicle.Movement.Velocity.LengthSquared() <= 0.0025f &&
                WaitingAge(intent.Binding, frame.Tick.Value) >= RecoveryWaitTicks;
        }

        bool TryEscape(int index, Vector3 target)
        {
            if (consumed == allowance) return false;
            var vehicle = vehicles[intents[index].Binding.Requester];
            var sweep = new NavigationSweep(NavigationReadAdapter.GetObstacleIdentity(vehicle.Entity), vehicle.Pose,
                new NavigationPose(target, vehicle.Pose.Rotation), vehicle.Profile,
                Vector3.Distance(vehicle.Pose.Position, target));
            var result = geometry.Sweep(frame.GetVehicleCollision(vehicle.Entity), sweep, allowance - consumed);
            if (result.WorkConsumed <= 0 || result.WorkConsumed > allowance - consumed)
            {
                consumed = allowance;
                return false;
            }
            consumed += result.WorkConsumed;
            if (result.Status != NavigationGeometryStatus.Clear || result.AllowedFraction != 1 ||
                result.FrameTick != frame.Tick.Value || result.Revision != frame.Collision.World.Revision) return false;
            foreach (var reservation in frame.Reservations)
            {
                if (consumed == allowance) return false;
                consumed++;
                if (reservation.Owner == vehicle.Entity) continue;
                var reserved = vehicle.Profile with { HalfExtents = reservation.HalfExtents, ClearanceMeters = 0 };
                if (Conflicts(vehicle.Pose.Position, target, vehicle.Profile,
                    reservation.Pose.Position, reservation.Pose.Position, reserved)) return false;
            }
            targets[index] = target;
            return true;
        }
    }
}
