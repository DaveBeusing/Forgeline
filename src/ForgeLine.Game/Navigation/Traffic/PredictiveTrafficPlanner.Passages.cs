using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Navigation;

namespace ForgeLine.Game;

internal readonly record struct PredictiveTrafficPassageLease(
    NavigationTrafficPassage Passage, EntityId Owner, ulong CommandGeneration,
    int Direction, ulong GrantedAt, ulong LastTick, bool Entered);

internal sealed partial class PredictiveTrafficPlanner
{
    private const int MaximumTrackedPassages = 128;
    private sealed record PassagePlan(Dictionary<ulong, PredictiveTrafficPassageLease> Leases, int Work, bool Complete);

    private PassagePlan PreparePassages(NavigationTrafficFrame frame, NavigationTrafficIntent[] intents,
        Dictionary<EntityId, NavigationVehicleObservation> vehicles, NavigationPose[][] paths, int[] priority, bool[] admissionAllowed,
        IReadOnlyList<NavigationTrafficPassage> discovered,
        NavigationBlockReason[] waits, EntityId[] waitingFor, int allowance)
    {
        int consumed = 0;
        var passages = new SortedDictionary<ulong, NavigationTrafficPassage>();
        foreach (var passage in discovered) passages.Add(passage.Id, passage);
        // Changed or removed route geometry cannot discard a physically occupied old lease.
        foreach (var lease in _passageLeases.Values) passages[lease.Passage.Id] = lease.Passage;
        var leases = new Dictionary<ulong, PredictiveTrafficPassageLease>();
        if (passages.Count > MaximumTrackedPassages) return new(leases, consumed, false);
        foreach (var passage in passages.Values)
        {
            if (!Covered(frame.Collision, passage)) return new(leases, consumed, false);
            var physicalOccupants = new SortedSet<EntityId>();
            bool retained = false;
            PredictiveTrafficPassageLease active = default;
            if (_passageLeases.TryGetValue(passage.Id, out var prior))
            {
                if (prior.LastTick > frame.Tick.Value) return new(leases, consumed, false);
                bool occupied = false;
                if (vehicles.TryGetValue(prior.Owner, out var owner))
                    occupied = Occupies(passage, owner.Pose, owner.Profile.HalfExtents, owner.Profile.ClearanceMeters);
                foreach (var body in frame.Collision.Bodies)
                {
                    if (consumed == allowance) return new(leases, consumed, false);
                    consumed++;
                    if (body.Identity == NavigationReadAdapter.GetObstacleIdentity(prior.Owner))
                        occupied |= Occupies(passage, body.Pose, body.HalfExtents, 0);
                }
                bool entered = prior.Entered || occupied;
                // An entered lease ends only when the complete physical footprint clears. Before
                // entry, canceled/changed commands or an expired approach can yield the admission.
                if (allowance - consumed < intents.Length) return new(leases, allowance, false);
                consumed += intents.Length;
                bool stillApproaching = false;
                for (int i = 0; i < intents.Length; i++)
                    if (intents[i].Binding.Requester == prior.Owner && intents[i].Binding.CommandGeneration == prior.CommandGeneration &&
                        intents[i].CanMove && admissionAllowed[i]) { stillApproaching = true; break; }
                retained = occupied || !entered && stillApproaching && frame.Tick.Value - prior.GrantedAt < 80;
                if (retained) active = prior with { LastTick = frame.Tick.Value, Entered = entered };
            }
            if (!retained)
            {
                EntityId occupant = default;
                bool multiple = false;
                foreach (var body in frame.Collision.Bodies)
                {
                    if (consumed == allowance) return new(leases, consumed, false);
                    consumed++;
                    // Static flank geometry cannot acquire traffic admission merely
                    // by touching this coarse envelope. It remains in every physical
                    // sweep; an actually obstructed lane still cannot be traversed.
                    if (body.Kind == NavigationObstacleKind.Structure) continue;
                    if (!Occupies(passage, body.Pose, body.HalfExtents, 0)) continue;
                    if (body.Identity.Source != NavigationObstacleSource.Entity || body.Identity.Value > uint.MaxValue ||
                        body.Identity.Generation > uint.MaxValue) { multiple = true; break; }
                    EntityId identity = new((uint)body.Identity.Value, (uint)body.Identity.Generation);
                    if (occupant.IsValid && occupant != identity) multiple = true;
                    occupant = identity;
                    physicalOccupants.Add(identity);
                }
                foreach (var vehicle in vehicles.Values)
                {
                    if (consumed == allowance) return new(leases, consumed, false);
                    consumed++;
                    if (!Occupies(passage, vehicle.Pose, vehicle.Profile.HalfExtents, vehicle.Profile.ClearanceMeters)) continue;
                    if (occupant.IsValid && occupant != vehicle.Entity) multiple = true;
                    occupant = vehicle.Entity;
                    physicalOccupants.Add(vehicle.Entity);
                }
                foreach (int index in priority)
                {
                    if (allowance - consumed < PredictionSteps) return new(leases, allowance, false);
                    consumed += PredictionSteps;
                    var intent = intents[index];
                    var vehicle = vehicles[intent.Binding.Requester];
                    if (multiple || occupant.IsValid && occupant != vehicle.Entity || !intent.CanMove || !admissionAllowed[index] ||
                        NavigationPrecisionOwnership.IsPrecise(vehicle) ||
                        intent.Binding.OriginalOrder.Kind != MovementOrderKind.Strategic ||
                        !Touches(passage, paths[index], vehicle.Profile)) continue;
                    Vector3 local = Vector3.Transform(intent.SteeringTarget - vehicle.Pose.Position, Quaternion.Conjugate(passage.Pose.Rotation));
                    int direction = local.Z >= 0 ? 1 : -1;
                    active = new(passage, vehicle.Entity, intent.Binding.CommandGeneration, direction,
                        frame.Tick.Value, frame.Tick.Value, occupant == vehicle.Entity);
                    retained = true;
                    break;
                }
            }
            if (retained) leases.Add(passage.Id, active);
            for (int index = 0; index < intents.Length; index++)
            {
                if (allowance - consumed < PredictionSteps) return new(leases, allowance, false);
                consumed += PredictionSteps;
                var vehicle = vehicles[intents[index].Binding.Requester];
                if (!admissionAllowed[index] || retained && vehicle.Entity == active.Owner || !Touches(passage, paths[index], vehicle.Profile) ||
                    paths[index].Length > 1 && IsOutwardDeparture(passage, paths[index][0], paths[index][1], vehicle.Profile)) continue;
                waits[index] = NavigationBlockReason.ReservedCorridor;
                if (retained) waitingFor[index] = active.Owner;
                else foreach (var other in physicalOccupants)
                    if (other != vehicle.Entity) { waitingFor[index] = other; break; }
            }
        }
        return new(leases, consumed, true);
    }

    internal bool TryCheckPassage(EntityId owner, NavigationPose start, NavigationPose end,
        NavigationVehicleProfile profile, int allowance, out int consumed, out bool complete)
    {
        consumed = 0; complete = true;
        foreach (var lease in _passageLeases.Values)
        {
            if (consumed == allowance) { complete = false; return false; }
            consumed++;
            if (lease.Owner != owner && Touches(lease.Passage, [start, end], profile) &&
                !IsOutwardDeparture(lease.Passage, start, end, profile)) return false;
        }
        return true;
    }

    private static bool IsOutwardDeparture(in NavigationTrafficPassage passage, NavigationPose start,
        NavigationPose end, in NavigationVehicleProfile profile)
    {
        if (!Touches(passage, [start, start], profile)) return false;
        Quaternion inverse = Quaternion.Conjugate(passage.Pose.Rotation);
        Vector3 a = Vector3.Transform(start.Position - passage.Pose.Position, inverse);
        Vector3 b = Vector3.Transform(end.Position - passage.Pose.Position, inverse);
        Vector2 first = new(a.X / passage.HalfExtents.X, a.Z / passage.HalfExtents.Z);
        Vector2 last = new(b.X / passage.HalfExtents.X, b.Z / passage.HalfExtents.Z);
        Vector2 change = last - first;
        // Distance from the reserved centre must increase over the entire segment.
        // No permission to enter, cross the centre, rotate in place or release the lease.
        return Vector2.Dot(first, change) >= 0 && last.LengthSquared() > first.LengthSquared() + .0000001f;
    }

    private static bool ValidLease(in PredictiveTrafficPassageLease lease) =>
        lease.Passage.Id != 0 && lease.Passage.Pose.IsValid && Finite(lease.Passage.HalfExtents) &&
        lease.Passage.HalfExtents.X > 0 && lease.Passage.HalfExtents.Y > 0 && lease.Passage.HalfExtents.Z > 0 &&
        lease.Owner.IsValid && lease.CommandGeneration > 0 && lease.Direction is -1 or 1 && lease.GrantedAt <= lease.LastTick;

    private static bool Covered(NavigationCollisionFrame collision, in NavigationTrafficPassage passage)
    {
        Vector3 extent = Vector3.Abs(Vector3.Transform(Vector3.UnitX, passage.Pose.Rotation)) * passage.HalfExtents.X +
            Vector3.Abs(Vector3.Transform(Vector3.UnitY, passage.Pose.Rotation)) * passage.HalfExtents.Y +
            Vector3.Abs(Vector3.Transform(Vector3.UnitZ, passage.Pose.Rotation)) * passage.HalfExtents.Z;
        Vector3 min = passage.Pose.Position - extent;
        Vector3 max = passage.Pose.Position + extent;
        // Only horizontal coverage matters for a ground passage; terrain owns height validation.
        return min.X >= collision.Coverage.Minimum.X && max.X <= collision.Coverage.Maximum.X &&
            min.Z >= collision.Coverage.Minimum.Z && max.Z <= collision.Coverage.Maximum.Z;
    }

    private static bool Occupies(in NavigationTrafficPassage passage, NavigationPose pose, Vector3 half, float clearance)
    {
        Quaternion inverse = Quaternion.Conjugate(passage.Pose.Rotation);
        Vector3 local = Vector3.Transform(pose.Position - passage.Pose.Position, inverse);
        Vector3 x = Vector3.Transform(Vector3.Transform(Vector3.UnitX, pose.Rotation), inverse);
        Vector3 y = Vector3.Transform(Vector3.Transform(Vector3.UnitY, pose.Rotation), inverse);
        Vector3 z = Vector3.Transform(Vector3.Transform(Vector3.UnitZ, pose.Rotation), inverse);
        // Project the entire yawed/tilted box, including its rear. Long flank walls must
        // not become circles spanning an otherwise empty road.
        float extentX = MathF.Abs(x.X) * half.X + MathF.Abs(y.X) * half.Y + MathF.Abs(z.X) * half.Z + clearance;
        float extentZ = MathF.Abs(x.Z) * half.X + MathF.Abs(y.Z) * half.Y + MathF.Abs(z.Z) * half.Z + clearance;
        return MathF.Abs(local.X) <= passage.HalfExtents.X + extentX && MathF.Abs(local.Z) <= passage.HalfExtents.Z + extentZ;
    }

    private static bool Touches(in NavigationTrafficPassage passage, ReadOnlySpan<NavigationPose> path, in NavigationVehicleProfile profile)
    {
        for (int step = 1; step < path.Length; step++)
        {
            // Use the complete swept physical box rather than a circumscribed
            // circle. Otherwise a fully cleared rear can be captured again by
            // the wider circle after the outward-occupant exception has ended.
            if (!NavigationMotionArbitrator.TryBuildSweptOccupancy(path[step - 1], path[step], profile,
                out var pose, out var half)) return true;
            // Passage admission is horizontal; preserve full yaw/tilt projection
            // and clearance without turning ground-height changes into free entry.
            if (Occupies(passage, pose, half, profile.ClearanceMeters)) return true;
        }
        return false;
    }
    internal static bool TryCheckOutwardStep(in NavigationVehicleProfile profile, in NavigationPose start,
        in NavigationPose end, IReadOnlyList<NavigationTrafficPassage> passages, int allowance, out int work, out bool complete, bool requireOutward = true)
    {
        work = 0; complete = true;
        if (!requireOutward && start.Position.X == end.Position.X && start.Position.Z == end.Position.Z) return false;
        ReadOnlySpan<NavigationPose> path = [start, end];
        foreach (var passage in passages)
        {
            if (allowance - work < 2) { work = allowance; complete = false; return false; }
            work += 2;
            if (Touches(passage, path, profile) && (requireOutward
                ? !IsOutwardDeparture(passage, start, end, profile)
                : !Occupies(passage, start, profile.HalfExtents, profile.ClearanceMeters))) return false;
        }
        return true;
    }

}
