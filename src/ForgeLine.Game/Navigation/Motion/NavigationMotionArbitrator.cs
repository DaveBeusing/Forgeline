using System.Numerics;
using ForgeLine.Navigation;
using ForgeLine.World;

namespace ForgeLine.Game;

internal readonly record struct NavigationMotionConflict(
    NavigationObstacleIdentity First, NavigationObstacleIdentity Second);

internal sealed record NavigationMotionArbitrationResult(
    IReadOnlyList<NavigationMotionProposal> Proposals,
    IReadOnlyList<NavigationMotionConflict> Conflicts, bool Complete, int WorkConsumed);

/// <summary>
/// Final bounded veto of same-tick proposals. It changes no transforms, velocity, reservations or priority history.
/// Every winner clears the original stationary occupants as well as every earlier winning swept envelope.
/// The caller must include every vehicle that will actually move in this frame, including precise dock/queue
/// motion, or hold that vehicle for the tick. Bodies absent from the input batch are treated as stationary.
/// An unavailable planner result cannot be followed by a separate moving fallback after this decision.
/// If the work budget ends, earlier approved winners remain safe only while all unapproved participants hold.
/// GroundMovement remains the sole commit owner and must revalidate command generations before applying results.
/// </summary>
internal sealed class NavigationMotionArbitrator
{
    private readonly int _maximumVehicles;
    private readonly int _maximumBodies;

    public NavigationMotionArbitrator(int maximumVehicles, int maximumBodies)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumVehicles);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBodies, maximumVehicles);
        _maximumVehicles = maximumVehicles;
        _maximumBodies = maximumBodies;
    }

    public NavigationMotionArbitrationResult Constrain(NavigationCollisionFrame collision,
        ReadOnlySpan<NavigationMotionInput> inputs, ReadOnlySpan<NavigationMotionProposal> proposals,
        INavigationGeometryQuery geometry, int workAllowance)
    {
        ArgumentNullException.ThrowIfNull(collision);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentOutOfRangeException.ThrowIfNegative(workAllowance);
        if (inputs.Length != proposals.Length || inputs.Length > _maximumVehicles)
            throw new ArgumentException("Motion batches must match their bounded observations.", nameof(inputs));

        var output = new NavigationMotionProposal[inputs.Length];
        var order = new int[inputs.Length];
        var envelopes = new AxisAlignedBounds[inputs.Length];
        var selected = new bool[inputs.Length];
        var conflicts = new List<NavigationMotionConflict>(inputs.Length);
        int consumed = 0;
        for (int index = 0; index < inputs.Length; index++)
        {
            output[index] = Hold(inputs[index], collision, NavigationBlockReason.SearchWork);
            order[index] = index;
        }
        if (workAllowance == 0) return Finish(output, conflicts, false, consumed);
        if (!collision.IsComplete || collision.Bodies.Count > _maximumBodies ||
            geometry.Availability != NavigationModuleAvailability.Available)
        {
            FillHolds(inputs, collision, output, NavigationBlockReason.MissingGeometry);
            return Finish(output, conflicts, false, consumed);
        }

        // Sorting is explicit and charged; task completion and input enumeration never pick the winner.
        for (int index = 1; index < order.Length; index++)
        {
            int item = order[index], previous = index - 1;
            while (previous >= 0)
            {
                if (!NavigationMotionCapture.Spend(workAllowance, ref consumed))
                    return Finish(output, conflicts, false, consumed);
                if (inputs[order[previous]].Vehicle.Entity.CompareTo(inputs[item].Vehicle.Entity) <= 0) break;
                order[previous + 1] = order[previous];
                previous--;
            }
            order[previous + 1] = item;
        }
        // Establish unique owners before any result can become an approved winner.
        for (int index = 1; index < order.Length; index++)
        {
            if (!NavigationMotionCapture.Spend(workAllowance, ref consumed))
                return Finish(output, conflicts, false, consumed);
            if (inputs[order[index]].Vehicle.Entity != inputs[order[index - 1]].Vehicle.Entity) continue;
            FillHolds(inputs, collision, output, NavigationBlockReason.StaleSnapshot);
            return Finish(output, conflicts, false, consumed);
        }

        var stationaryBodies = new NavigationCollisionBody[collision.Bodies.Count];
        var originals = new Dictionary<NavigationObstacleIdentity, NavigationCollisionBody>(collision.Bodies.Count);
        for (int index = 0; index < collision.Bodies.Count; index++)
        {
            if (!NavigationMotionCapture.Spend(workAllowance, ref consumed))
                return Finish(output, conflicts, false, consumed);
            NavigationCollisionBody body = collision.Bodies[index];
            stationaryBodies[index] = body with { Velocity = Vector3.Zero };
            originals.Add(body.Identity, body);
        }
        var stationary = new NavigationCollisionFrame(collision.World, collision.Tick, collision.Coverage,
            true, stationaryBodies, _maximumBodies);
        float delta = inputs.IsEmpty ? 0 : inputs[0].DeltaSeconds;

        for (int position = 0; position < order.Length; position++)
        {
            if (!NavigationMotionCapture.Spend(workAllowance, ref consumed))
                return Finish(output, conflicts, false, consumed);
            int index = order[position];
            NavigationMotionInput input = inputs[index];
            NavigationMotionProposal proposal = proposals[index];
            if (!Valid(input, proposal, collision, delta) ||
                !originals.TryGetValue(NavigationReadAdapter.GetObstacleIdentity(input.Vehicle.Entity), out var body) ||
                body.Pose != input.Vehicle.Pose || body.HalfExtents != input.Vehicle.Profile.HalfExtents ||
                body.Velocity != input.Vehicle.Movement.Velocity)
            {
                output[index] = Hold(input, collision, NavigationBlockReason.StaleSnapshot);
                continue;
            }
            if (proposal.End == proposal.Start)
            {
                output[index] = proposal.DesiredSpeed == 0 ? proposal :
                    Hold(input, collision, NavigationBlockReason.Unknown);
                continue;
            }
            if (!MayMove(input, proposal) || !TryEnvelope(proposal, input.Vehicle.Profile, out AxisAlignedBounds envelope))
            {
                output[index] = Hold(input, collision, NavigationBlockReason.Unknown);
                continue;
            }

            var sweep = new NavigationSweep(NavigationReadAdapter.GetObstacleIdentity(input.Vehicle.Entity),
                proposal.Start, proposal.End, input.Vehicle.Profile, input.DeltaSeconds);
            int remaining = workAllowance - consumed;
            if (remaining == 0) return Finish(output, conflicts, false, consumed);
            NavigationSweepResult approval = geometry.Sweep(stationary, sweep, remaining);
            if (!ValidApproval(approval, stationary, remaining))
            {
                // A malformed child work report cannot grant additional work to later candidates.
                consumed = workAllowance;
                output[index] = Hold(input, collision, NavigationBlockReason.MissingGeometry);
                return Finish(output, conflicts, false, consumed);
            }
            consumed += approval.WorkConsumed;
            if (approval.Status != NavigationGeometryStatus.Clear || approval.AllowedFraction != 1)
            {
                output[index] = Hold(input, collision, approval.Reason);
                if (approval.Blocker is NavigationObstacleIdentity blocker)
                    conflicts.Add(new(sweep.Mover, blocker));
                continue;
            }

            bool conflict = false;
            for (int earlier = 0; earlier < position; earlier++)
            {
                int other = order[earlier];
                if (!selected[other]) continue;
                if (!NavigationMotionCapture.Spend(workAllowance, ref consumed))
                    return Finish(output, conflicts, false, consumed);
                if (!Overlaps(envelope, envelopes[other])) continue;
                conflicts.Add(new(sweep.Mover, NavigationReadAdapter.GetObstacleIdentity(inputs[other].Vehicle.Entity)));
                conflict = true;
                break;
            }
            if (conflict)
            {
                output[index] = Hold(input, collision, NavigationBlockReason.MovingVehicle);
                continue;
            }
            selected[index] = true;
            envelopes[index] = envelope;
            output[index] = proposal;
        }
        return Finish(output, conflicts, true, consumed);
    }

    private static bool Valid(in NavigationMotionInput input, in NavigationMotionProposal proposal,
        NavigationCollisionFrame collision, float delta)
    {
        NavigationVehicleObservation vehicle = input.Vehicle;
        NavigationOrderBinding binding = input.Binding;
        return vehicle.Entity.IsValid && vehicle.Entity == binding.Requester && vehicle.Owner.IsSpecified &&
            binding.CommandGeneration != 0 && binding.OriginalOrder.Issuer == vehicle.Owner &&
            NavigationMotionCapture.Finite(binding.OriginalOrder.WorldTarget) && Enum.IsDefined(binding.OriginalOrder.Kind) &&
            (!binding.OriginalOrder.TargetHeadingRadians.HasValue || float.IsFinite(binding.OriginalOrder.TargetHeadingRadians.Value)) &&
            binding.OriginalOrder.SubmittedAtTick <= binding.OriginalOrder.AcceptedAtTick &&
            binding.OriginalOrder.AcceptedAtTick <= input.Tick &&
            (vehicle.RouteOrder == binding.OriginalOrder || vehicle.DirectOrder == binding.OriginalOrder) &&
            vehicle.Pose.IsValid && vehicle.Profile.IsValid && NavigationMotionCapture.Finite(vehicle.Movement.Velocity) &&
            float.IsFinite(vehicle.Movement.HeadingRadians) && Enum.IsDefined(vehicle.Movement.Status) &&
            float.IsFinite(input.DeltaSeconds) && input.DeltaSeconds > 0 && input.DeltaSeconds == delta &&
            input.Tick.Value == collision.Tick && proposal.Tick == input.Tick && proposal.Binding == binding &&
            proposal.Revision == collision.World.Revision && proposal.Start == vehicle.Pose && proposal.End.IsValid &&
            proposal.Availability == NavigationModuleAvailability.Available &&
            float.IsFinite(proposal.DesiredSpeed) && proposal.DesiredSpeed >= 0 && float.IsFinite(proposal.Curvature) &&
            float.IsFinite(proposal.BrakingDistance) && proposal.BrakingDistance >= 0 &&
            float.IsFinite(proposal.SafeRemainingDistance) && proposal.SafeRemainingDistance >= 0 &&
            Enum.IsDefined(proposal.Reason) &&
            NavigationPrecisionOwnership.Matches(vehicle, binding);
    }

    private static bool MayMove(in NavigationMotionInput input, in NavigationMotionProposal proposal)
    {
        NavigationTrafficDirective traffic = input.Traffic;
        if (!input.CanMove || !float.IsFinite(input.EffectiveMaximumSpeed) || input.EffectiveMaximumSpeed < 0 ||
            traffic.Availability != NavigationModuleAvailability.Available || traffic.Binding != input.Binding ||
            traffic.Tick != input.Tick || traffic.Action is not (NavigationTrafficAction.Proceed or NavigationTrafficAction.Recovery or NavigationTrafficAction.Brake) ||
            !float.IsFinite(traffic.MaximumSpeed) || traffic.MaximumSpeed < 0) return false;
        double maximum = Math.Min(input.Vehicle.Profile.MaximumSpeed,
            Math.Min(input.EffectiveMaximumSpeed, traffic.MaximumSpeed));
        double x = (double)proposal.End.Position.X - proposal.Start.Position.X;
        double z = (double)proposal.End.Position.Z - proposal.Start.Position.Z;
        double speed = Math.Sqrt(x * x + z * z) / input.DeltaSeconds;
        double angle = RotationAngle(proposal.Start.Rotation, proposal.End.Rotation);
        return speed <= maximum + 0.00001 && proposal.DesiredSpeed <= maximum + 0.00001 &&
            Math.Abs(speed - proposal.DesiredSpeed) <= 0.0001 * Math.Max(1, speed) &&
            angle <= input.Vehicle.Profile.MaximumYawRate * (double)input.DeltaSeconds + 0.0001;
    }

    private static bool ValidApproval(in NavigationSweepResult result, NavigationCollisionFrame frame, int allowance) =>
        Enum.IsDefined(result.Status) && Enum.IsDefined(result.Reason) &&
        result.Revision == frame.World.Revision && result.FrameTick == frame.Tick &&
        result.WorkConsumed >= 0 && result.WorkConsumed <= allowance &&
        float.IsFinite(result.AllowedFraction) && result.AllowedFraction >= 0 && result.AllowedFraction <= 1 &&
        (result.AllowedFraction == 0 || result.WorkConsumed > 0) &&
        (result.Status != NavigationGeometryStatus.Clear || result.AllowedFraction == 1 &&
            result.Reason == NavigationBlockReason.None && result.WorkConsumed > 0) &&
        (result.Status is not (NavigationGeometryStatus.Unavailable or NavigationGeometryStatus.Blocked or
            NavigationGeometryStatus.InvalidInput or NavigationGeometryStatus.Stale) || result.AllowedFraction == 0);

    private static bool TryEnvelope(in NavigationMotionProposal proposal, in NavigationVehicleProfile profile,
        out AxisAlignedBounds bounds)
    {
        bounds = default;
        Vector3 first = Extents(profile.HalfExtents, proposal.Start.Rotation);
        Vector3 last = Extents(profile.HalfExtents, proposal.End.Rotation);
        double radius = Math.Sqrt((double)profile.HalfExtents.X * profile.HalfExtents.X +
            (double)profile.HalfExtents.Y * profile.HalfExtents.Y + (double)profile.HalfExtents.Z * profile.HalfExtents.Z);
        double angle = RotationAngle(proposal.Start.Rotation, proposal.End.Rotation);
        // Any intermediate orientation is at most half the full arc from one endpoint.
        Vector3 absolute = Vector3.Max(Vector3.Abs(proposal.Start.Position), Vector3.Abs(proposal.End.Position));
        float padding = (float)(2 * radius * Math.Sin(angle * 0.25) + profile.ClearanceMeters +
            0.000002 * (Math.Max(Math.Max(absolute.X, absolute.Y), absolute.Z) + radius + 1));
        Vector3 half = Vector3.Max(first, last) + new Vector3(padding);
        Vector3 minimum = Vector3.Min(proposal.Start.Position, proposal.End.Position) - half;
        Vector3 maximum = Vector3.Max(proposal.Start.Position, proposal.End.Position) + half;
        if (!NavigationMotionCapture.Finite(minimum) || !NavigationMotionCapture.Finite(maximum)) return false;
        bounds = new AxisAlignedBounds(minimum, maximum);
        return true;
    }

    private static Vector3 Extents(Vector3 half, Quaternion rotation)
    {
        rotation = Quaternion.Normalize(rotation);
        return Vector3.Abs(Vector3.Transform(Vector3.UnitX, rotation)) * half.X +
            Vector3.Abs(Vector3.Transform(Vector3.UnitY, rotation)) * half.Y +
            Vector3.Abs(Vector3.Transform(Vector3.UnitZ, rotation)) * half.Z;
    }

    private static double RotationAngle(Quaternion first, Quaternion second)
    {
        first = Quaternion.Normalize(first);
        second = Quaternion.Normalize(second);
        double dot = (double)first.X * second.X + (double)first.Y * second.Y +
            (double)first.Z * second.Z + (double)first.W * second.W;
        if (dot < 0) second = -second;
        double x = (double)second.X - first.X;
        double y = (double)second.Y - first.Y;
        double z = (double)second.Z - first.Z;
        double w = (double)second.W - first.W;
        return 4 * Math.Asin(Math.Clamp(Math.Sqrt(x * x + y * y + z * z + w * w) * 0.5, 0, 1));
    }

    private static bool Overlaps(in AxisAlignedBounds first, in AxisAlignedBounds second) =>
        first.Minimum.X <= second.Maximum.X && first.Maximum.X >= second.Minimum.X &&
        first.Minimum.Y <= second.Maximum.Y && first.Maximum.Y >= second.Minimum.Y &&
        first.Minimum.Z <= second.Maximum.Z && first.Maximum.Z >= second.Minimum.Z;

    private static NavigationMotionProposal Hold(in NavigationMotionInput input,
        NavigationCollisionFrame collision, NavigationBlockReason reason) =>
        new(NavigationModuleAvailability.Unavailable, input.Binding, input.Tick, collision.World.Revision,
            input.Vehicle.Pose, input.Vehicle.Pose, 0, 0, 0, 0, reason);

    private static void FillHolds(ReadOnlySpan<NavigationMotionInput> inputs, NavigationCollisionFrame collision,
        NavigationMotionProposal[] output, NavigationBlockReason reason)
    {
        for (int index = 0; index < output.Length; index++) output[index] = Hold(inputs[index], collision, reason);
    }

    private static NavigationMotionArbitrationResult Finish(NavigationMotionProposal[] proposals,
        List<NavigationMotionConflict> conflicts, bool complete, int consumed) =>
        new(Array.AsReadOnly(proposals), conflicts.AsReadOnly(), complete, consumed);
}
