using System.Numerics;
using System.Diagnostics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Cue = ForgeLine.Presentation.WorldAudioCue;

namespace ForgeLine.Presentation;

internal interface IWorldAudioVisibility { bool Allows(EntityId entity, Vector3 position); }

internal readonly record struct WorldAudioEntityFacts(EntityId Entity, Vector3 Position,
    UnitFeaturePresentationMetadata Unit, BuildingFeaturePresentationMetadata Building,
    bool Owned, bool Working = false, CargoTransportLifecycleState Cargo = default,
    UnitSupplyState? Supply = null, double? HealthFraction = null, bool? Moving = null);

/// <summary>Single extraction-owner writer. No live simulation reference, device access or simulation RNG.</summary>
internal sealed class WorldAudioEventBridge(Func<long>? clock = null)
{
    private readonly Func<long> _clock = clock ?? Stopwatch.GetTimestamp;
    private long _capturedAtTimestamp;
    internal const int MaximumEvents = 256, MaximumLoops = 1024, HistoryCapacity = 2048;
    private readonly WorldAudioEvent[] _events = new WorldAudioEvent[MaximumEvents];
    private readonly WorldAudioLoop[] _loops = new WorldAudioLoop[MaximumLoops];
    private readonly History[] _history = new History[HistoryCapacity];
    private readonly byte[] _variants = new byte[4];
    private SimulationSessionId _session;
    private PlayerId _player;
    private SimulationTick _tick;
    private PlayerExperienceSnapshot _previousExperience;
    private WorldAudioSnapshot? _cached;
    private bool _hasTick, _accepting, _repeat, _baseline, _completed, _lowSupply, _previousLowSupply, _industrial;
    private int _eventCount, _loopCount;
    private ulong _sequence, _retentionTicks;
    private long _droppedEvents, _droppedLoops, _replacedHistory, _retracted;

    internal bool Begin(SimulationSessionId session, PlayerId player, SimulationTick tick, TimeSpan duration)
    {
        _accepting = _repeat = false;
        if (!session.IsSpecified || !player.IsSpecified || duration < TimeSpan.Zero ||
            _hasTick && (session.Value < _session.Value || session == _session && (player != _player || tick < _tick))) return false;
        if (_hasTick && session == _session && tick == _tick) { _repeat = true; return false; }
        if (session != _session)
        {
            Array.Clear(_history); Array.Clear(_variants);
            _eventCount = 0; _sequence = 0; _baseline = _completed = _previousLowSupply = false;
            _droppedEvents = _droppedLoops = _replacedHistory = _retracted = 0;
        }
        _capturedAtTimestamp = _clock();
        _session = session; _player = player; _tick = tick; _hasTick = _accepting = true;
        _lowSupply = _industrial = false; _loopCount = 0;
        _retentionTicks = duration > TimeSpan.Zero ? (ulong)Math.Clamp(Math.Ceiling(.25 / duration.TotalSeconds), 1, 32) : 1;
        int kept = 0;
        for (int i = 0; i < _eventCount; i++)
            if (_events[i].ExpiresAt > tick && _events[i].CreatedAtTimestamp <= _capturedAtTimestamp &&
                Stopwatch.GetElapsedTime(_events[i].CreatedAtTimestamp, _capturedAtTimestamp) <= TimeSpan.FromMilliseconds(250))
                _events[kept++] = _events[i];
        _eventCount = kept;
        return true;
    }

    internal void ObserveEntity(in WorldAudioEntityFacts facts)
    {
        if (!_accepting || !facts.Owned || !facts.Entity.IsValid || !Finite(facts.Position)) return;
        int slot = (int)(facts.Entity.Index % HistoryCapacity);
        var old = _history[slot];
        bool same = old.Facts.Entity == facts.Entity && old.Facts.Unit.Unit == facts.Unit.Unit &&
            old.Facts.Building.Building == facts.Building.Building;
        bool adjacent = _baseline && same && old.Tick.Value < ulong.MaxValue && old.Tick.Value + 1 == _tick.Value;
        if (old.Facts.Entity.IsValid && !same) _replacedHistory++;
        bool moving = facts.Moving ?? (adjacent && Vector3.DistanceSquared(old.Facts.Position, facts.Position) > .0025f);
        if (facts.Unit.IsSpecified && !facts.Unit.IsWreck)
        {
            var idle = Vehicle(facts.Unit.Unit, false);
            if (idle != Cue.None)
            {
                AddLoop(Vehicle(facts.Unit.Unit, moving), facts.Entity, facts.Position);
                if (adjacent && moving != old.Moving)
                    Add(moving ? Cue.VehiclesEngineStart : Cue.VehiclesEngineStop, facts.Entity, facts.Position);
            }
        }
        if (facts.Building.IsSpecified && !facts.Building.IsDestroyed)
        {
            var industry = Industry(facts.Building.Building);
            if (industry != Cue.None && facts.Building.State == BuildingPresentationState.Operational &&
                (facts.Working || facts.Building.Building == BuildingIds.PowerPlant ||
                 facts.Building.Building == BuildingIds.StorageDepot || facts.Building.Building == BuildingIds.SupplyDepot))
            { AddLoop(industry, facts.Entity, facts.Position); _industrial = true; }
            if (adjacent && old.Facts.Building.IsConstruction && !facts.Building.IsConstruction)
                Add(Cue.ConstructionComplete, facts.Entity, facts.Position);
            // Unknown/colliding history never invents an entry transition.
            if (_baseline && !old.Facts.Entity.IsValid && facts.Building.IsConstruction)
                Add(Cue.ConstructionStart, facts.Entity, facts.Position);
        }
        if (adjacent && facts.Cargo != old.Facts.Cargo)
        {
            if (facts.Cargo == CargoTransportLifecycleState.Loading) Add(Cue.LogisticsCargoLoad, facts.Entity, facts.Position);
            if (facts.Cargo == CargoTransportLifecycleState.Unloading) Add(Cue.LogisticsCargoUnload, facts.Entity, facts.Position);
        }
        bool refueling = false, rearming = false, repairing = false;
        ulong refueledAt = same ? old.RefueledAt : 0, rearmedAt = same ? old.RearmedAt : 0, repairedAt = same ? old.RepairedAt : 0;
        bool Due(ulong at) => at == 0 || _tick.Value - at >= _retentionTicks * 2;
        if (facts.Supply is { } supply && supply.UpdatedAtTick <= _tick && ValidSupply(supply))
        {
            _lowSupply |= supply.Status == BattlefieldSupplyStatus.LowSupply;
            if (adjacent && old.Facts.Supply is { } previous && previous.UpdatedAtTick <= old.Tick && ValidSupply(previous))
            {
                refueling = supply.FuelFraction > previous.FuelFraction + .000001;
                rearming = supply.AmmunitionFraction > previous.AmmunitionFraction + .000001;
                if (refueling && !old.Refueling && Due(refueledAt)) { Add(Cue.LogisticsRefuel, facts.Entity, facts.Position); refueledAt = _tick.Value; }
                if (rearming && !old.Rearming && Due(rearmedAt)) { Add(Cue.LogisticsRearm, facts.Entity, facts.Position); rearmedAt = _tick.Value; }
                if (supply.Status == BattlefieldSupplyStatus.Supplied && previous.Status != BattlefieldSupplyStatus.Supplied)
                    Add(Cue.LogisticsSupplyComplete, facts.Entity, facts.Position);
            }
        }
        if (adjacent && facts.HealthFraction is { } health && old.Facts.HealthFraction is { } previousHealth &&
            double.IsFinite(health) && health is >= 0 and <= 1 && double.IsFinite(previousHealth) && previousHealth is > 0 and <= 1)
        {
            repairing = health > previousHealth + .000001;
            if (repairing && !old.Repairing && Due(repairedAt)) { Add(Cue.LogisticsRepair, facts.Entity, facts.Position); repairedAt = _tick.Value; }
        }
        _history[slot] = new(facts, _tick, moving, refueling, rearming, repairing, refueledAt, rearmedAt, repairedAt);
    }

    // The caller supplies only an already-authorized, sanitized location and identity.
    internal void ObserveCombat(VfxEffectKind kind, EntityId authorizedEntity, Vector3 authorizedPosition, SimulationTick tick)
    {
        if (!_accepting || !_baseline || tick != _tick) return;
        var cue = kind switch
        {
            VfxEffectKind.MuzzleInfantry => Variant(Cue.CombatRifleFire01, 0),
            VfxEffectKind.MuzzleMachineGun => Variant(Cue.CombatMachineGunFire01, 1),
            VfxEffectKind.MuzzleTankCannon => Variant(Cue.CombatTankFire01, 2),
            VfxEffectKind.MuzzleArtillery => Variant(Cue.CombatArtilleryFire01, 3),
            VfxEffectKind.ImpactDirt => Cue.CombatImpactDirt,
            VfxEffectKind.ImpactMetal or VfxEffectKind.ImpactArmor => Cue.CombatImpactMetal,
            VfxEffectKind.ImpactConcrete => Cue.CombatImpactConcrete,
            VfxEffectKind.ImpactExplosive or VfxEffectKind.ExplosionSmall or VfxEffectKind.ExplosionMedium or VfxEffectKind.ExplosionAmmunitionSecondary => Cue.CombatExplosionSmall,
            VfxEffectKind.ExplosionVehicle => Cue.CombatExplosionVehicle,
            VfxEffectKind.ExplosionBuilding => Cue.CombatExplosionBuilding,
            _ => Cue.None
        };
        if (cue != Cue.None) Add(cue, authorizedEntity, authorizedPosition);
    }

    internal WorldAudioSnapshot? Complete(PlayerExperienceSnapshot? experience, IWorldAudioVisibility? visibility = null)
    {
        if (_repeat) return _cached;
        if (!_accepting) return null;
        _accepting = false;
        if (experience is not { } current || current.Player != _player || current.Tick != _tick || current.MatchStatus != PlayerMatchStatus.Active && !current.IsMatchComplete || _completed && !current.IsMatchComplete)
        {
            _eventCount = _loopCount = 0; _baseline = false; Array.Clear(_history);
            return _cached = null;
        }
        if (visibility is not null)
        {
            int kept = 0;
            for (int i = 0; i < _eventCount; i++)
            {
                var item = _events[i];
                if (!item.Spatial || visibility.Allows(item.Entity, item.Position)) _events[kept++] = item;
                else _retracted++;
            }
            _eventCount = kept;
        }
        if (current.IsMatchComplete)
        {
            _loopCount = 0;
            if (!_completed)
            {
                _eventCount = 0;
                if (_baseline)
                {
                    if (current.MatchStatus == PlayerMatchStatus.Victory) Add(Cue.EventsVictory, default, default, false, true);
                    if (current.MatchStatus == PlayerMatchStatus.Defeat) Add(Cue.EventsDefeat, default, default, false, true);
                }
                _completed = true;
            }
        }
        else
        {
            if (_baseline)
            {
                AlertEdge(current.Alerts, PlayerAlertState.LowPower, Cue.AlertsPowerLow);
                AlertEdge(current.Alerts, PlayerAlertState.SupplyCritical, Cue.AlertsSupplyCritical);
                AlertEdge(current.Alerts, PlayerAlertState.CommandCoreDamaged, Cue.AlertsBaseUnderAttack);
                if ((_previousExperience.Alerts & PlayerAlertState.LowPower) != 0 && (current.Alerts & PlayerAlertState.LowPower) == 0)
                    Add(Cue.AlertsPowerRestored, default, default, false, true);
                if (_lowSupply && !_previousLowSupply) Add(Cue.AlertsSupplyLow, default, default, false, true);
                if (current.Statistics.UnitsProduced > _previousExperience.Statistics.UnitsProduced)
                    Add(Cue.AlertsUnitReady, default, default, false, true);
                if (current.Feedback != _previousExperience.Feedback && current.Feedback.Kind != PlayerCommandFeedbackKind.None &&
                    current.Feedback.ResolvedAtTick <= _tick && current.Feedback.ResolvedAtTick >= _previousExperience.Tick)
                {
                    if (current.Feedback.State is PlayerCommandFeedbackState.Rejected or PlayerCommandFeedbackState.Partial)
                        Add(Cue.AlertsInvalidOrder, default, default, false, true);
                    else if (current.Feedback.Kind == PlayerCommandFeedbackKind.Construction && current.Feedback.State == PlayerCommandFeedbackState.Accepted)
                        Add(Cue.ConstructionPlace, default, default, false, true);
                }
            }
            AddLoop(Cue.AmbienceWindLoop, default, default, false, true);
            if (_industrial) AddLoop(Cue.AmbienceIndustrialLoop, default, default, false, true);
            for (int i = 0; i < _eventCount; i++)
                if (_events[i].Cue is >= Cue.CombatRifleFire01 and <= Cue.CombatImpactConcrete)
                { AddLoop(Cue.AmbienceBattlefieldLoop, default, default, false, true); break; }
        }
        _previousExperience = current; _previousLowSupply = _lowSupply; _baseline = true;
        return _cached = new(_session, _player, _tick, _events.AsSpan(0, _eventCount), _loops.AsSpan(0, _loopCount),
            new(_droppedEvents, _droppedLoops, _replacedHistory, _eventCount, _loopCount, _retracted), _capturedAtTimestamp);
    }

    private void AlertEdge(PlayerAlertState state, PlayerAlertState flag, Cue cue)
    {
        if ((state & flag) != 0 && (_previousExperience.Alerts & flag) == 0) Add(cue, default, default, false, true);
    }
    private Cue Variant(Cue first, int family)
    {
        byte next = _variants[family]; _variants[family] = (byte)((next + 1) % 3);
        return (Cue)((int)first + next);
    }
    private void Add(Cue cue, EntityId entity, Vector3 position, bool spatial = true, bool finalizing = false)
    {
        if ((!_accepting && !finalizing) || !Finite(position)) return;
        bool priority = WorldAudioCatalog.Get(cue).Bus == WorldAudioBus.Alerts;
        if (!priority && _eventCount >= MaximumEvents - 16) { _droppedEvents++; return; }
        if (_eventCount == MaximumEvents)
        {
            int replace = -1;
            for (int i = 0; i < _eventCount; i++)
                if (WorldAudioCatalog.Get(_events[i].Cue).Bus != WorldAudioBus.Alerts) { replace = i; break; }
            if (replace < 0) { _droppedEvents++; return; }
            _events.AsSpan(replace + 1, _eventCount - replace - 1).CopyTo(_events.AsSpan(replace));
            _eventCount--; _droppedEvents++;
        }
        ulong expiry = _tick.Value > ulong.MaxValue - _retentionTicks ? ulong.MaxValue : _tick.Value + _retentionTicks;
        _events[_eventCount++] = new(++_sequence, _tick, new(expiry), cue, entity, position, spatial, _capturedAtTimestamp);
    }
    private void AddLoop(Cue cue, EntityId entity, Vector3 position, bool spatial = true, bool ambience = false)
    {
        int limit = ambience ? MaximumLoops : MaximumLoops - 3;
        if (_loopCount >= limit) { _droppedLoops++; return; }
        _loops[_loopCount++] = new(cue, entity, position, spatial);
    }
    private static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    private static bool ValidSupply(UnitSupplyState s) => double.IsFinite(s.FuelFraction) && s.FuelFraction is >= 0 and <= 1 &&
        double.IsFinite(s.AmmunitionFraction) && s.AmmunitionFraction is >= 0 and <= 1 && Enum.IsDefined(s.Status);
    private static Cue Vehicle(UnitId id, bool moving) => id == UnitIds.ScoutVehicle ? moving ? Cue.VehiclesScoutMoveLoop : Cue.VehiclesScoutIdleLoop :
        id == UnitIds.CargoTruck || id == UnitIds.SupplyTruck ? moving ? Cue.VehiclesTruckMoveLoop : Cue.VehiclesTruckIdleLoop :
        id == UnitIds.MainBattleTank || id == UnitIds.MobileArtillery ? moving ? Cue.VehiclesTankMoveLoop : Cue.VehiclesTankIdleLoop : Cue.None;
    private static Cue Industry(BuildingId id) => id == BuildingIds.Extractor ? Cue.IndustryExtractorLoop :
        id == BuildingIds.Smelter ? Cue.IndustrySmelterLoop : id == BuildingIds.Refinery ? Cue.IndustryRefineryLoop :
        id == BuildingIds.PowerPlant ? Cue.IndustryPowerPlantLoop :
        id == BuildingIds.StorageDepot || id == BuildingIds.SupplyDepot || id == BuildingIds.LogisticsHub ? Cue.IndustryDepotLoop :
        id == BuildingIds.VehicleFactory || id == BuildingIds.Barracks || id == BuildingIds.ElectronicsPlant || id == BuildingIds.AmmunitionPlant ? Cue.IndustryFactoryLoop : Cue.None;
    private readonly record struct History(WorldAudioEntityFacts Facts, SimulationTick Tick, bool Moving,
        bool Refueling, bool Rearming, bool Repairing, ulong RefueledAt, ulong RearmedAt, ulong RepairedAt);
}
