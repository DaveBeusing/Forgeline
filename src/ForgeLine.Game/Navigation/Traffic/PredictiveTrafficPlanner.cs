using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Navigation;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

/// <summary>Detached, versioned traffic state. Physical reservations are owned by the Game integration.</summary>
internal sealed record PredictiveTrafficCheckpoint(
    int Revision, PredictiveTrafficVehicleState[] Vehicles);

internal readonly record struct PredictiveTrafficVehicleState(
    EntityId Entity, ulong CommandGeneration, ulong LastTick, ulong WaitingSince,
    ulong LastRecoveryTick, NavigationBlockReason LastReason);

/// <summary>
/// Conservative fixed-tick advisory planner. Does not update transforms, orders, docks or inventories.
/// The integration owner must supply a complete immutable frame and reconcile every binding.
/// </summary>
internal sealed class PredictiveTrafficPlanner : INavigationTrafficPlanner<PredictiveTrafficCheckpoint>
{
    private const int CheckpointRevision = 1;
    private readonly Dictionary<EntityId, PredictiveTrafficVehicleState> _states = new();

    public NavigationModuleAvailability Availability => NavigationModuleAvailability.Available;

    public NavigationTrafficDirective Plan(
        NavigationTrafficFrame frame, in NavigationOrderBinding binding,
        INavigationGeometryQuery geometry, int workAllowance, out int workConsumed)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentOutOfRangeException.ThrowIfNegative(workAllowance);
        workConsumed = 0;
        var hold = new NavigationTrafficDirective(
            NavigationModuleAvailability.Unavailable, binding, frame.Tick,
            NavigationTrafficAction.Hold, 0, null, NavigationBlockReason.MissingGeometry);

        if (!binding.Requester.IsValid || binding.CommandGeneration == 0 ||
            !frame.IsComplete || !frame.Collision.IsComplete ||
            frame.Collision.Tick != frame.Tick.Value ||
            geometry.Availability != NavigationModuleAvailability.Available ||
            workAllowance == 0)
            return hold;

        NavigationVehicleObservation? found = null;
        foreach (NavigationVehicleObservation vehicle in frame.Vehicles)
        {
            if (vehicle.Entity == binding.Requester)
            {
                found = vehicle;
                break;
            }
        }
        if (found is not NavigationVehicleObservation mover || !mover.Pose.IsValid ||
            !mover.Profile.IsValid || !binding.OriginalOrder.Issuer.IsSpecified ||
            mover.Owner != binding.OriginalOrder.Issuer)
            return hold;

        bool dockOccupied = mover.DockVisit is VehicleDockVisit visit && visit.Generation > 0;
        if (dockOccupied || mover.QueueEntry is VehicleQueueEntry queue && queue.Position > 1)
            return Waiting(binding, frame.Tick, NavigationBlockReason.ReservedCorridor, workConsumed);

        Vector3 toTarget = binding.OriginalOrder.WorldTarget - mover.Pose.Position;
        toTarget.Y = 0;
        if (!Finite(toTarget))
            return hold;

        float speed = MathF.Min(mover.Profile.MaximumSpeed, mover.Movement.Velocity.Length());
        if (!float.IsFinite(speed))
            return hold;
        // At rest, inspect the first acceleration-limited movement rather than a zero-length sweep.
        float inspectSpeed = MathF.Min(mover.Profile.MaximumSpeed,
            MathF.Max(speed, mover.Profile.Acceleration * 0.05f));
        float distance = toTarget.Length();
        if (!float.IsFinite(distance))
            return hold;
        if (distance <= 0.0001f)
            return new(NavigationModuleAvailability.Available, binding, frame.Tick,
                NavigationTrafficAction.Hold, 0, null, NavigationBlockReason.None);

        Vector3 direction = toTarget / distance;
        // The fixed, conservative 50 ms lookahead is not a complete multi-second predictor.
        float travel = MathF.Min(distance, inspectSpeed * 0.05f);
        Vector3 end = mover.Pose.Position + direction * travel;
        var sweep = new NavigationSweep(
            NavigationReadAdapter.GetObstacleIdentity(mover.Entity), mover.Pose,
            new NavigationPose(end, mover.Pose.Rotation), mover.Profile, 0.05f);
        var result = geometry.Sweep(frame.Collision, sweep, workAllowance);
        if (result.WorkConsumed < 0 || result.WorkConsumed > workAllowance)
            return hold;
        workConsumed = result.WorkConsumed;
        if (result.FrameTick != frame.Tick.Value ||
            result.Revision != frame.Collision.World.Revision)
            return hold;

        if (result.Status != NavigationGeometryStatus.Clear || result.AllowedFraction != 1)
            return Waiting(binding, frame.Tick,
                result.Reason == NavigationBlockReason.None
                    ? NavigationBlockReason.MissingGeometry : result.Reason, workConsumed);

        // Separate dock and passage reservations are authoritative, even if the geometric query
        // has a clear result for this instantaneous movement prefix.
        foreach (NavigationTrafficReservation reservation in frame.Reservations)
        {
            workConsumed++;
            if (workConsumed > workAllowance)
            {
                workConsumed = workAllowance;
                return hold;
            }
            if (reservation.Owner == mover.Entity)
                continue;
            Vector3 delta = reservation.Pose.Position - end;
            delta.Y = 0;
            float threshold = mover.Profile.HalfExtents.Length() + reservation.HalfExtents.Length();
            if (delta.LengthSquared() <= threshold * threshold)
                return Waiting(binding, frame.Tick, NavigationBlockReason.ReservedCorridor, workConsumed);
        }

        _states.Remove(binding.Requester);
        return new(NavigationModuleAvailability.Available, binding, frame.Tick,
            NavigationTrafficAction.Proceed, inspectSpeed, null, NavigationBlockReason.None);
    }

    public PredictiveTrafficCheckpoint CaptureState()
    {
        var values = _states.Values.OrderBy(static v => v.Entity.Index)
            .ThenBy(static v => v.Entity.Generation).ToArray();
        return new(CheckpointRevision, values);
    }

    public void RestoreState(PredictiveTrafficCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.Revision != CheckpointRevision || checkpoint.Vehicles is null)
            throw new InvalidOperationException("Invalid traffic checkpoint revision.");
        var rebuilt = new Dictionary<EntityId, PredictiveTrafficVehicleState>();
        foreach (PredictiveTrafficVehicleState state in checkpoint.Vehicles)
        {
            if (!state.Entity.IsValid || state.CommandGeneration == 0 ||
                state.WaitingSince > state.LastTick || state.LastRecoveryTick > state.LastTick ||
                !Enum.IsDefined(state.LastReason) || !rebuilt.TryAdd(state.Entity, state))
                throw new InvalidOperationException("Invalid traffic checkpoint participant.");
        }
        _states.Clear();
        foreach (var pair in rebuilt)
            _states.Add(pair.Key, pair.Value);
    }

    private NavigationTrafficDirective Waiting(
        in NavigationOrderBinding binding, SimulationTick tick, NavigationBlockReason reason, int consumed)
    {
        if (!_states.TryGetValue(binding.Requester, out var state) ||
            state.CommandGeneration != binding.CommandGeneration || tick.Value < state.LastTick)
            state = new(binding.Requester, binding.CommandGeneration, tick.Value, tick.Value, 0, reason);
        else
            state = state with { LastTick = tick.Value, LastReason = reason };
        _states[binding.Requester] = state;
        _ = consumed;
        return new(NavigationModuleAvailability.Available, binding, tick,
            NavigationTrafficAction.Hold, 0, null, reason);
    }

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
