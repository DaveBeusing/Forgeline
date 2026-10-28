using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;

namespace ForgeLine.Game;

public sealed record MatchPacingPlayerObservation(ulong Player, ulong FactoryTicks, ulong FactoryRunningTicks,
    ulong ResourceFacilityTicks, ulong ResourceShortfallFacilityTicks, ulong SupplyUnitTicks, ulong SupplyShortageTicks,
    ulong LongestSupplyShortageTicks, ulong? FirstSupplyShortageTick, ulong? FirstTechnologyUnlockTick, double? FrontTravelMeters, ulong FrontComparableTicks);

// Offline diagnostics only. These summaries are never a player-facing intelligence source.
public sealed record MatchPacingObservation(string ConfigurationFingerprint, string Scenario, string Profile,
    ulong StartedAtTick, ulong EndTick, ulong ObservedTicks, ulong MissedTicks, ulong TruncatedTicks,
    ulong LongestNoProgressTicks, bool ScoutAvailable, bool ResearchAvailable, bool TechnologyUnlockAvailable, IReadOnlyList<MatchPacingPlayerObservation> Players);

internal sealed class MatchPacingObserver
{
    internal const int MaximumEntitiesPerTick = 4096;
    private readonly MatchRuntime _runtime;
    private readonly Dictionary<PlayerId, PlayerCounters> _players = new();
    private readonly string _fingerprint;
    private readonly ulong _started;
    private ulong _lastTick, _observed, _missed, _truncated, _quiet, _longestQuiet;
    private double _lastDamage;
    private bool _activity;

    internal MatchPacingObserver(MatchRuntime runtime)
    {
        _runtime = runtime;
        _started = _lastTick = runtime.Simulation.CurrentTick.Value;
        if (runtime.MatchConfiguration.Participants.Count > 16)
            throw new ArgumentException("Pacing observation supports at most 16 participants.", nameof(runtime));
        foreach (var participant in runtime.MatchConfiguration.Participants)
            _players.Add(participant.Player, new PlayerCounters());
        var scenario = runtime.RuntimeSettings.Scenario;
        string configuration = JsonSerializer.Serialize(new
        {
            runtime.RuntimeSettings.Composition.Key, runtime.RuntimeSettings.Composition.Identity,
            Scenario = scenario with { OpponentConfigurations = scenario.OpponentConfigurations.OrderBy(p => p.Key).ToDictionary(p => p.Key, p => p.Value) },
            Participants = runtime.MatchConfiguration.Participants.OrderBy(p => p.Player.Value).ToArray()
        });
        _fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(configuration)));
    }

    internal void NotifyActivity() => _activity = true;

    internal void Observe()
    {
        ulong tick = _runtime.Simulation.CurrentTick.Value;
        if (tick <= _lastTick) return; // Pause, slow host frames and repeated reads do not advance logical time.
        bool gap = tick != _lastTick + 1;
        _missed += tick - _lastTick - 1;
        _lastTick = tick;
        _observed++;
        var entities = _runtime.Simulation.Entities;
        bool truncated = (long)entities.GetComponentCount<ControllableEntity>() + entities.GetComponentCount<TechnologyCapabilityUnlock>() > MaximumEntitiesPerTick;
        if (truncated) _truncated++;
        foreach (var counters in _players.Values) counters.Begin();
        int inspected = 0;
        // Dense access bounds visits even when entity slots are sparse. Same version/commands retain the same order.
        foreach (var entity in entities.Query<ControllableEntity>())
        {
            if (inspected++ == MaximumEntitiesPerTick) break;
            var control = entities.GetComponent<ControllableEntity>(entity);
            if (!_players.TryGetValue(control.Owner, out var counters)) continue;
            if (entities.TryGetComponent(entity, out UnitProductionFacility factory))
            {
                counters.FactoryTicks++; counters.ResourceFacilityTicks++;
                if (factory.Status == UnitProductionStatus.Running) counters.FactoryRunningTicks++;
                if (factory.Status == UnitProductionStatus.NoInput) counters.ResourceShortfallFacilityTicks++;
            }
            if (entities.TryGetComponent(entity, out ProductionFacility processor))
            {
                counters.ResourceFacilityTicks++;
                if (processor.Status == ProductionStatus.NoInput) counters.ResourceShortfallFacilityTicks++;
            }
            if (entities.TryGetComponent(entity, out UnitSupplyState supply))
            {
                counters.SupplyUnitTicks++;
                counters.HasSupply = true;
                if (supply.Status != BattlefieldSupplyStatus.Supplied) counters.Shortage = true;
            }
            if (entities.TryGetComponent(entity, out UnitIdentity unit) &&
                _runtime.Services.UnitDefinitions.TryGet(unit.UnitId, out var definition) && definition.CargoCapacity <= 0 &&
                entities.HasComponent<Combatant>(entity) && entities.TryGetComponent(entity, out WorldTransform transform) &&
                (!entities.TryGetComponent(entity, out HealthState health) || !health.IsDepleted))
                counters.AddFront(entity, transform.Position);
        }
        foreach (var entity in entities.Query<TechnologyCapabilityUnlock>())
        {
            if (inspected++ >= MaximumEntitiesPerTick) break;
            var unlock = entities.GetComponent<TechnologyCapabilityUnlock>(entity);
            if (unlock.UnlockedAtTick.Value > _started && unlock.UnlockedAtTick.Value <= tick && unlock.SourceTechnology.IsSpecified &&
                _players.TryGetValue(unlock.Player, out var counters) && counters.ObserveUnlock(unlock.UnlockedAtTick.Value)) _activity = true;
        }
        foreach (var counters in _players.Values)
        {
            if (counters.End(tick, gap || truncated)) _activity = true;
        }
        double damage = _runtime.Services.CombatRuntime.Metrics.TotalDamageApplied;
        if (damage > _lastDamage) _activity = true;
        _lastDamage = damage;
        _quiet = gap || truncated || _activity ? 0 : _quiet + 1;
        _longestQuiet = Math.Max(_longestQuiet, _quiet);
        _activity = false;
    }

    internal MatchPacingObservation Capture() => new(_fingerprint, _runtime.RuntimeSettings.Composition.Key,
        _runtime.RuntimeSettings.Scenario.Profile.ToString(), _started, _lastTick, _observed, _missed, _truncated,
        _longestQuiet, _runtime.Services.UnitDefinitions.TryGet(UnitIds.ScoutVehicle, out _),
        _runtime.Services.TechnologyDefinitions.Count > 0, _runtime.Services.TechnologyDefinitions.Definitions.Any(d => d.Unlocks.Count > 0), Array.AsReadOnly(_players.OrderBy(p => p.Key.Value).Select(p => p.Value.Capture(p.Key.Value)).ToArray()));

    private sealed class PlayerCounters
    {
        internal ulong FactoryTicks, FactoryRunningTicks, ResourceFacilityTicks, ResourceShortfallFacilityTicks, SupplyUnitTicks;
        internal bool HasSupply, Shortage;
        private ulong _shortageTicks, _streak, _longest, _frontTicks;
        private ulong? _firstShortage, _firstUnlock;
        private double _x, _y, _z, _distance;
        private Vector3 _previousCentroid;
        private HashSet<EntityId> _current = new(), _previous = new();
        private bool _invalidFront;

        internal bool ObserveUnlock(ulong tick)
        {
            if (_firstUnlock is { } previous && previous <= tick) return false;
            _firstUnlock = tick; return true;
        }
        internal void Begin() { HasSupply = Shortage = _invalidFront = false; _x = _y = _z = 0; _current.Clear(); }
        internal void AddFront(EntityId entity, Vector3 position)
        {
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
            { _invalidFront = true; return; }
            _current.Add(entity); _x += position.X; _y += position.Y; _z += position.Z;
        }
        internal bool End(ulong tick, bool incomplete)
        {
            if (Shortage)
            {
                _firstShortage ??= tick; _shortageTicks++;
                _streak = incomplete ? 0 : _streak + 1;
                _longest = Math.Max(_longest, _streak);
            }
            else _streak = 0;
            bool moved = false;
            if (_current.Count > 0 && !_invalidFront && !incomplete)
            {
                var centroid = new Vector3((float)(_x / _current.Count), (float)(_y / _current.Count), (float)(_z / _current.Count));
                if (_current.SetEquals(_previous))
                {
                    double step = Vector3.Distance(centroid, _previousCentroid);
                    _distance += step; _frontTicks++; moved = step > .05;
                }
                _previousCentroid = centroid;
            }
            else _current.Clear();
            (_previous, _current) = (_current, _previous);
            return moved;
        }
        internal MatchPacingPlayerObservation Capture(ulong player) => new(player, FactoryTicks, FactoryRunningTicks,
            ResourceFacilityTicks, ResourceShortfallFacilityTicks, SupplyUnitTicks, _shortageTicks, _longest, _firstShortage, _firstUnlock,
            _frontTicks > 0 ? _distance : null, _frontTicks);
    }
}
