using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Navigation;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

/// <summary>Detached, versioned traffic state. Physical reservations are owned by the Game integration.</summary>
internal sealed record PredictiveTrafficCheckpoint(
    int Revision, PredictiveTrafficVehicleState[] Vehicles, PredictiveTrafficPassageLease[] Leases);

internal readonly record struct PredictiveTrafficVehicleState(
    EntityId Entity, ulong CommandGeneration, ulong LastTick, ulong WaitingSince,
    ulong LastRecoveryTick, NavigationBlockReason LastReason, EntityId WaitingFor = default,
    Vector3? RecoveryTarget = null);

/// <summary>
/// Conservative fixed-tick advisory planner. Does not update transforms, orders, docks or inventories.
/// The integration owner must supply a complete immutable frame and reconcile every binding.
/// </summary>
internal sealed partial class PredictiveTrafficPlanner : INavigationTrafficPlanner<PredictiveTrafficCheckpoint>
{
    private const int CheckpointRevision = 3;
    private readonly Dictionary<ulong, PredictiveTrafficPassageLease> _passageLeases = new();
    private readonly Dictionary<EntityId, PredictiveTrafficVehicleState> _states = new();

    public NavigationModuleAvailability Availability => NavigationModuleAvailability.Available;

    public NavigationTrafficDirective Plan(
        NavigationTrafficFrame frame, in NavigationOrderBinding binding,
        INavigationGeometryQuery geometry, int workAllowance, out int workConsumed)
    {
        ArgumentNullException.ThrowIfNull(frame);
        EntityId requester = binding.Requester;
        var vehicle = frame.Vehicles.FirstOrDefault(v => v.Entity == requester);
        var intent = new NavigationTrafficIntent(binding,
            vehicle.DirectOrder?.WorldTarget ?? binding.OriginalOrder.WorldTarget,
            vehicle.Profile.MaximumSpeed, true);
        if (frame.Vehicles.Count != 1 || vehicle.Entity != requester || !Valid(intent))
        {
            workConsumed = 0;
            return new(NavigationModuleAvailability.Unavailable, binding, frame.Tick,
                NavigationTrafficAction.Hold, 0, null, NavigationBlockReason.StaleSnapshot);
        }
        return PlanBatch(frame, [intent], geometry, Math.Clamp(frame.Limits.PlanningHorizonSeconds, 0.25f, 10), workAllowance, out workConsumed)[0];
    }

    public PredictiveTrafficCheckpoint CaptureState()
    {
        var values = _states.Values.OrderBy(static v => v.Entity.Index)
            .ThenBy(static v => v.Entity.Generation).ToArray();
        return new(CheckpointRevision, values, _passageLeases.Values.OrderBy(static v => v.Passage.Id).ToArray());
    }

    public void RestoreState(PredictiveTrafficCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.Revision != CheckpointRevision || checkpoint.Vehicles is null ||
            checkpoint.Vehicles.Length > MaximumTrackedVehicles || checkpoint.Leases is null ||
            checkpoint.Leases.Length > MaximumTrackedPassages)
            throw new InvalidOperationException("Invalid traffic checkpoint revision.");
        var rebuilt = new Dictionary<EntityId, PredictiveTrafficVehicleState>();
        foreach (PredictiveTrafficVehicleState state in checkpoint.Vehicles)
        {
            if (!state.Entity.IsValid || state.CommandGeneration == 0 ||
                state.WaitingSince > state.LastTick || state.LastRecoveryTick > state.LastTick ||
                !Enum.IsDefined(state.LastReason) ||
                state.RecoveryTarget is Vector3 target && !Finite(target) ||
                state.WaitingFor == state.Entity || !rebuilt.TryAdd(state.Entity, state))
                throw new InvalidOperationException("Invalid traffic checkpoint participant.");
        }
        var rebuiltLeases = new Dictionary<ulong, PredictiveTrafficPassageLease>();
        foreach (var lease in checkpoint.Leases)
            if (!ValidLease(lease) || !rebuiltLeases.TryAdd(lease.Passage.Id, lease))
                throw new InvalidOperationException("Invalid traffic passage checkpoint.");
        _passageLeases.Clear();
        foreach (var pair in rebuiltLeases) _passageLeases.Add(pair.Key, pair.Value);
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
        if (!_states.ContainsKey(binding.Requester) && _states.Count == MaximumTrackedVehicles)
            return new(NavigationModuleAvailability.Unavailable, binding, tick,
                NavigationTrafficAction.Hold, 0, null, NavigationBlockReason.SearchWork);
        _states[binding.Requester] = state;
        _ = consumed;
        return new(NavigationModuleAvailability.Available, binding, tick,
            NavigationTrafficAction.Hold, 0, null, reason);
    }

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
