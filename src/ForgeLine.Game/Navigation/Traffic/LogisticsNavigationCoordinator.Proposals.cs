using System.Numerics;
using ForgeLine.Navigation;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

internal sealed partial class LogisticsNavigationCoordinator
{
    private LogisticsNavigationPlan PlanMotion(SimulationContext context, NavigationTrafficFrame frame,
        Candidate[] candidates, IReadOnlyList<NavigationTrafficDirective> directives, int consumed)
    {
        if (candidates.Length == 0) return new(Array.Empty<NavigationApprovedMotion>(), true, consumed, NavigationBlockReason.None);
        var byEntity = directives.ToDictionary(static d => d.Binding.Requester);
        var inputs = new NavigationMotionInput[candidates.Length];
        var proposals = new NavigationMotionProposal[candidates.Length];
        var current = new int[candidates.Length];
        var used = new int[candidates.Length];
        var horizons = DiagnosticObservationsEnabled ? new NavigationCheckedHorizon?[candidates.Length] : null;
        for (int i = 0; i < candidates.Length; i++)
        {
            var candidate = candidates[i];
            if (!byEntity.TryGetValue(candidate.Binding.Requester, out var traffic) || traffic.Binding != candidate.Binding)
                return Unavailable(NavigationBlockReason.StaleSnapshot, consumed);
            var input = new NavigationMotionInput(candidate.Binding, context.Tick, (float)context.TickDuration.TotalSeconds,
                candidate.Vehicle, traffic, candidate.Route, candidate.MaximumSpeed, candidate.CanMove);
            current[i] = used[i] = -1;
            var collision = FreezePoses(frame.GetVehicleCollision(candidate.Vehicle.Entity), ref consumed);
            if (collision is null) return Unavailable(NavigationBlockReason.SearchWork, consumed);
            NavigationMotionProposal proposal;
            float speed = new Vector2(candidate.Vehicle.Movement.Velocity.X, candidate.Vehicle.Movement.Velocity.Z).Length();
            Vector3 forward = Vector3.Transform(Vector3.UnitZ, candidate.Vehicle.Pose.Rotation);
            float signed = Vector3.Dot(candidate.Vehicle.Movement.Velocity, forward);
            bool brake = traffic.Availability == NavigationModuleAvailability.Available && candidate.CanMove && speed > 0.01f &&
                (traffic.Action is NavigationTrafficAction.Hold or NavigationTrafficAction.Yield ||
                 traffic.Action == NavigationTrafficAction.Recovery && signed > 0.01f ||
                 traffic.Action == NavigationTrafficAction.Proceed && signed < -0.01f);
            if (brake)
            {
                float next = MathF.Min(candidate.MaximumSpeed, MathF.Max(0, speed - candidate.Vehicle.Profile.Deceleration * input.DeltaSeconds));
                input = input with { Traffic = traffic with { Action = NavigationTrafficAction.Brake, MaximumSpeed = next, SuggestedTarget = null } };
                proposal = ProposeBrake(input, collision, speed, next, Limits.WorkPerTick - consumed, out int work);
                consumed += work;
            }
            else if (traffic.Action == NavigationTrafficAction.Recovery)
            {
                proposal = ProposeReverse(input, collision, Limits.WorkPerTick - consumed, out int work);
                consumed += work;
            }
            else
            {
                proposal = _motion.ProposeCore(input, collision, _geometry!, Limits.WorkPerTick - consumed,
                    out int work, out _, out current[i], out used[i], out var horizon, DiagnosticObservationsEnabled);
                if (horizons is not null) horizons[i] = horizon;
                consumed += work;
            }
            inputs[i] = input;
            if (proposal.End != proposal.Start)
            {
                if (!_traffic.TryCheckPassage(candidate.Vehicle.Entity, proposal.Start, proposal.End, candidate.Vehicle.Profile,
                    Limits.WorkPerTick - consumed, out int passageWork, out bool passageComplete))
                    proposal = Hold(input, collision, passageComplete ? NavigationBlockReason.ReservedCorridor : NavigationBlockReason.SearchWork);
                consumed += passageWork;
                int stageCost = Math.Max(1, frame.Collision.Bodies.Count);
                if (stageCost > Limits.WorkPerTick - consumed) return Unavailable(NavigationBlockReason.SearchWork, consumed);
                consumed += stageCost;
                var transform = context.Entities.GetComponent<WorldTransform>(candidate.Vehicle.Entity);
                var end = transform with { Position = proposal.End.Position, Rotation = proposal.End.Rotation };
                if (VehicleDockingService.BlocksStagingStep(context.Entities, candidate.Vehicle.Entity, transform, end, candidate.Vehicle))
                    proposal = Hold(input, collision, NavigationBlockReason.ReservedCorridor);
            }
            proposals[i] = proposal;
        }
        if (proposals.All(static p => p.Start == p.End))
        {
            var stationary = new NavigationApprovedMotion[candidates.Length];
            for (int i = 0; i < stationary.Length; i++) stationary[i] = new(inputs[i], proposals[i], current[i], used[i], horizons?[i]);
            bool complete = proposals.All(static p => p.Availability == NavigationModuleAvailability.Available);
            return new(Array.AsReadOnly(stationary), complete, consumed,
                complete ? NavigationBlockReason.None : proposals.First(static p => p.Availability != NavigationModuleAvailability.Available).Reason);
        }
        // Reserve the already bounded waypoint handoff before granting any actual translation.
        int progressReserve = candidates.Length * 17;
        if (progressReserve > Limits.WorkPerTick - consumed) return Unavailable(NavigationBlockReason.SearchWork, consumed);
        consumed += progressReserve;
        var arbitration = _arbitrator.Constrain(frame.Collision, inputs, proposals, _geometry!, Limits.WorkPerTick - consumed);
        consumed += arbitration.WorkConsumed;
        var motions = new NavigationApprovedMotion[candidates.Length];
        for (int i = 0; i < motions.Length; i++)
        {
            var final = proposals[i].Availability != NavigationModuleAvailability.Available && proposals[i].Start == proposals[i].End
                ? proposals[i] : arbitration.Proposals[i];
            if (final.Start != final.End)
            {
                foreach (var reservation in frame.Reservations)
                {
                    int checkWork = PredictiveTrafficPlanner.ServiceReservationCheckWork(2);
                    if (checkWork > Limits.WorkPerTick - consumed) return Unavailable(NavigationBlockReason.SearchWork, consumed);
                    consumed += checkWork;
                    if (!PredictiveTrafficPlanner.BlocksServiceReservation(candidates[i].Vehicle, reservation, [final.Start, final.End])) continue;
                    final = Hold(inputs[i], frame.Collision, NavigationBlockReason.ReservedCorridor);
                    break;
                }
            }
            motions[i] = new(inputs[i], final, current[i], used[i], horizons?[i]);
        }
        return new(Array.AsReadOnly(motions), arbitration.Complete, consumed,
            arbitration.Complete ? NavigationBlockReason.None : NavigationBlockReason.SearchWork);
    }

    private NavigationMotionProposal ProposeBrake(in NavigationMotionInput input, NavigationCollisionFrame collision,
        float currentSpeed, float nextSpeed, int allowance, out int consumed)
    {
        consumed = 0;
        if (input.Traffic.Availability != NavigationModuleAvailability.Available || !input.CanMove || allowance == 0 ||
            !float.IsFinite(currentSpeed) || currentSpeed <= 0 || !float.IsFinite(nextSpeed) || nextSpeed < 0)
            return Hold(input, collision, NavigationBlockReason.SearchWork);
        Vector3 direction = input.Vehicle.Movement.Velocity; direction.Y = 0;
        direction /= currentSpeed;
        Vector3 position = input.Vehicle.Pose.Position + direction * (nextSpeed * input.DeltaSeconds);
        position = NavigationMotionNumerics.ConstrainTravel(input.Vehicle.Pose.Position, position, (double)nextSpeed * input.DeltaSeconds);
        if (!collision.World.TrySampleHeight(input.Vehicle.Pose.Position.X, input.Vehicle.Pose.Position.Z, out float startHeight) ||
            !collision.World.TrySampleHeight(position.X, position.Z, out float height)) return Hold(input, collision, NavigationBlockReason.Terrain);
        position.Y = height + input.Vehicle.Pose.Position.Y - startHeight;
        var end = new NavigationPose(position, input.Vehicle.Pose.Rotation);
        return ApproveSpecial(input, collision, end, nextSpeed, allowance, out consumed);
    }

    private NavigationMotionProposal ProposeReverse(in NavigationMotionInput input, NavigationCollisionFrame collision,
        int allowance, out int consumed)
    {
        consumed = 0;
        if (input.Traffic.Availability != NavigationModuleAvailability.Available || !input.CanMove ||
            input.Traffic.SuggestedTarget is not Vector3 target || allowance == 0 ||
            !NavigationReverseMotion.TryStep(input.Vehicle, target,
                MathF.Min(input.EffectiveMaximumSpeed, input.Traffic.MaximumSpeed), input.DeltaSeconds, collision.World,
                out var end, out float speed, out _))
            return Hold(input, collision, NavigationBlockReason.ReservedCorridor);
        return ApproveSpecial(input, collision, end, speed, allowance, out consumed);
    }

    private NavigationMotionProposal ApproveSpecial(in NavigationMotionInput input, NavigationCollisionFrame collision,
        NavigationPose end, float speed, int allowance, out int consumed)
    {
        consumed = 0;
        if (end == input.Vehicle.Pose) return Hold(input, collision, input.Traffic.Reason);
        var sweep = new NavigationSweep(NavigationReadAdapter.GetObstacleIdentity(input.Vehicle.Entity),
            input.Vehicle.Pose, end, input.Vehicle.Profile, input.DeltaSeconds);
        var result = _geometry!.Sweep(collision, sweep, allowance);
        if (result.WorkConsumed < 0 || result.WorkConsumed > allowance ||
            result.FrameTick != input.Tick.Value || result.Revision != collision.World.Revision ||
            !Enum.IsDefined(result.Status) || !Enum.IsDefined(result.Reason) ||
            result.Status == NavigationGeometryStatus.Clear &&
                (result.AllowedFraction != 1 || result.Reason != NavigationBlockReason.None || result.WorkConsumed == 0))
        { consumed = allowance; return Hold(input, collision, NavigationBlockReason.MissingGeometry, NavigationModuleAvailability.Unavailable); }
        consumed = result.WorkConsumed;
        if (result.Status != NavigationGeometryStatus.Clear || result.AllowedFraction != 1)
            return Hold(input, collision, result.Reason);
        speed = new Vector2(end.Position.X - input.Vehicle.Pose.Position.X, end.Position.Z - input.Vehicle.Pose.Position.Z).Length() / input.DeltaSeconds;
        float current = new Vector2(input.Vehicle.Movement.Velocity.X, input.Vehicle.Movement.Velocity.Z).Length();
        return new(NavigationModuleAvailability.Available, input.Binding, input.Tick, collision.World.Revision,
            input.Vehicle.Pose, end, speed, 0, current * current / (2 * input.Vehicle.Profile.Deceleration),
            Vector3.Distance(input.Vehicle.Pose.Position, end.Position), input.Traffic.Reason);
    }

    private static NavigationMotionProposal Hold(in NavigationMotionInput input, NavigationCollisionFrame collision,
        NavigationBlockReason reason, NavigationModuleAvailability availability = NavigationModuleAvailability.Available) =>
        new(availability, input.Binding, input.Tick, collision.World.Revision, input.Vehicle.Pose, input.Vehicle.Pose, 0, 0, 0, 0, reason);
}
