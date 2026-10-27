using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public readonly record struct OperationsWindowSubject(EntityId Entity, InventoryId Inventory,
    ResourceId Resource, bool? Waiting, double? Backlog);

public readonly record struct OperationsWindowMeasurement(EntityId Entity, InventoryId Inventory,
    ResourceId Resource, SimulationTick StartExclusive, SimulationTick EndInclusive,
    int CoveredTicks, int WaitObservedTicks, int BacklogObservedTicks, double Seconds,
    double? DeliveredQuantity, double? DeliveredPerSecond, double? WaitingFraction,
    double? Backlog, double? MinimumBacklog, double? MaximumBacklog);

/// <summary>Detached observations. Quantities are per receiving inventory/resource, never net production.</summary>
public sealed class OperationsObservationSnapshot
{
    public OperationsObservationSnapshot(SimulationSessionId session, SimulationTick tick, PlayerId player,
        IReadOnlyList<OperationsWindowMeasurement> measurements, int omittedSubjects, bool limited = false)
    {
        Session = session; Tick = tick; Player = player;
        Measurements = Array.AsReadOnly(measurements.Take(GameplayProgressJournal.MaximumOperationsSubjects).ToArray());
        OmittedSubjects = omittedSubjects; Limited = limited || omittedSubjects > 0;
    }
    public SimulationSessionId Session { get; }
    public SimulationTick Tick { get; }
    public PlayerId Player { get; }
    public IReadOnlyList<OperationsWindowMeasurement> Measurements { get; }
    public int OmittedSubjects { get; }
    public bool Limited { get; }
}

/// <summary>Optional observation on the simulation owner; no commands, persisted state or decision inputs.</summary>
public sealed partial class GameplayProgressJournal
{
    public const int OperationsWindowTicks = 600;
    public const int MaximumOperationsSubjects = 128;
    private readonly Dictionary<(PlayerId Player, EntityId Entity, InventoryId Inventory, ResourceId Resource), OperationsWindow> _operations = [];

    // Called once for each successful physical inventory transfer, before milestone-event coalescing.
    internal void ObserveOperationsDelivery(SimulationTick tick, PlayerId player, EntityId target,
        InventoryId inventory, ResourceId resource, double quantity)
    {
        if (!double.IsFinite(quantity) || quantity <= 0 ||
            !_entities.TryGetComponent(target, out ControllableEntity owner) || owner.Owner != player) return;
        if (!_operations.TryGetValue((player, target, inventory, resource), out var window) ||
            tick.Value <= window.LastTick || tick.Value != window.LastTick + 1) return;
        int slot = (int)(tick.Value % OperationsWindowTicks);
        if (window.Samples[slot].Tick != tick.Value) window.Samples[slot] = new() { Tick = tick.Value };
        ref var sample = ref window.Samples[slot];
        double sum = sample.Quantity + quantity;
        if (double.IsFinite(sum)) sample.Quantity = sum;
        else sample.InvalidQuantity = true;
    }

    public OperationsObservationSnapshot ObserveOperations(PlayerId player, SimulationTick tick, TimeSpan duration,
        IReadOnlyList<OperationsWindowSubject> subjects)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        var retained = new HashSet<(PlayerId Player, EntityId Entity, InventoryId Inventory, ResourceId Resource)>();
        var measurements = new List<OperationsWindowMeasurement>();
        int omitted = Math.Max(0, subjects.Count - MaximumOperationsSubjects);
        if (!_observed || tick != _completed || !double.IsFinite(duration.TotalSeconds) || duration.TotalSeconds <= 0)
            return new(_session, tick, player, measurements, subjects.Count);
        foreach (var subject in subjects.Take(MaximumOperationsSubjects))
        {
            var key = (player, subject.Entity, subject.Inventory, subject.Resource);
            if (!subject.Inventory.IsSpecified || !subject.Resource.IsSpecified ||
                !_entities.TryGetComponent(subject.Entity, out ControllableEntity owner) ||
                owner.Owner != player || !owner.IsControllable || !retained.Add(key)) continue;
            if (!_operations.TryGetValue(key, out var window))
            {
                if (_operations.Count >= MaximumOperationsSubjects) { omitted++; continue; }
                _operations.Add(key, window = new(tick.Value));
            }
            if (tick.Value != window.LastTick && tick.Value != window.LastTick + 1)
                _operations[key] = window = new(tick.Value);
            if (tick.Value > window.Start)
            {
                int slot = (int)(tick.Value % OperationsWindowTicks);
                if (window.Samples[slot].Tick != tick.Value) window.Samples[slot] = new() { Tick = tick.Value };
                ref var sample = ref window.Samples[slot];
                sample.Seconds = duration.TotalSeconds;
                sample.Waiting = subject.Waiting;
                sample.Backlog = subject.Backlog is { } backlog && double.IsFinite(backlog) && backlog >= 0 ? backlog : null;
            }
            window.LastTick = tick.Value;
            measurements.Add(window.Copy(subject, tick));
        }
        foreach (var key in _operations.Keys.Where(key => key.Player == player && !retained.Contains(key)).ToArray())
            _operations.Remove(key);
        return new(_session, tick, player, measurements, omitted);
    }

    private sealed class OperationsWindow(ulong start)
    {
        public readonly OperationsSample[] Samples = new OperationsSample[OperationsWindowTicks];
        public readonly ulong Start = start;
        public ulong LastTick = start;
        public OperationsWindowMeasurement Copy(OperationsWindowSubject subject, SimulationTick tick)
        {
            ulong first = Math.Max(Start, tick.Value > OperationsWindowTicks ? tick.Value - OperationsWindowTicks : 0);
            double quantity = 0, seconds = 0, waitingSeconds = 0, knownWaitSeconds = 0;
            double? minimum = null, maximum = null, backlog = null;
            int covered = 0, knownWait = 0, knownBacklog = 0;
            bool invalid = false;
            int length = (int)(tick.Value - first);
            for (int offset = 0; offset < length; offset++)
            {
                ulong value = first + 1 + (ulong)offset;
                var sample = Samples[(int)(value % OperationsWindowTicks)];
                if (sample.Tick != value || sample.Seconds <= 0) continue;
                covered++; quantity += sample.Quantity; seconds += sample.Seconds; invalid |= sample.InvalidQuantity;
                if (sample.Waiting is { } waiting)
                { knownWait++; knownWaitSeconds += sample.Seconds; if (waiting) waitingSeconds += sample.Seconds; }
                if (sample.Backlog is { } current)
                { knownBacklog++; minimum = Math.Min(minimum ?? current, current); maximum = Math.Max(maximum ?? current, current); }
                if (value == tick.Value) backlog = sample.Backlog;
            }
            bool measured = covered > 0 && !invalid && double.IsFinite(quantity) && double.IsFinite(seconds) && seconds > 0;
            return new(subject.Entity, subject.Inventory, subject.Resource, new(first), tick,
                covered, knownWait, knownBacklog, seconds,
                measured ? quantity : null, measured ? quantity / seconds : null,
                knownWaitSeconds > 0 ? waitingSeconds / knownWaitSeconds : null, backlog, minimum, maximum);
        }
    }
    private struct OperationsSample
    {
        public ulong Tick;
        public double Quantity;
        public double Seconds;
        public bool InvalidQuantity;
        public bool? Waiting;
        public double? Backlog;
    }
}
