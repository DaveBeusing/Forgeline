using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public enum AlertSeverity : byte { Information, Warning, Critical }
public readonly record struct AlertIdentity(SimulationSessionId Session, PlayerAlertState Kind, SimulationTick Started, PlayerId Player = default, ulong Cause = 0);
public readonly record struct ActionableAlert(AlertIdentity Identity, AlertSeverity Severity, EntityId Target,
    string Label, OperationsCategory Operations, RtsUiIcon Icon,
    CargoFleetCapacityObservation? Fleet = null);

/// <summary>Current conditions only; immutable, bounded and authorized at the completed tick.</summary>
public enum AlertChangeKind : byte { Started, CauseChanged, Resolved }
public readonly record struct AlertChange(AlertIdentity Identity, AlertChangeKind Kind, SimulationTick Tick);
/// <summary>Adapter input must contain only current, player-authorized completed-tick facts.</summary>
public readonly record struct AlertObservation(PlayerAlertState Kind, ulong Cause, string Label);

public sealed class ActionableAlertSnapshot
{
    public ActionableAlertSnapshot(SimulationSessionId session, SimulationTick tick, PlayerId player, IReadOnlyList<ActionableAlert> alerts, IReadOnlyList<AlertChange>? changes = null, NotificationPreferences? preferences = null)
    {
        Session = session; Tick = tick; Player = player;
        Alerts = Array.AsReadOnly(alerts.Take(8).ToArray());
        Changes = Array.AsReadOnly((changes ?? []).Take(8).ToArray());
        Preferences = preferences ?? NotificationPreferences.Default;
    }
    public SimulationSessionId Session { get; }
    public SimulationTick Tick { get; }
    public PlayerId Player { get; }
    public IReadOnlyList<ActionableAlert> Alerts { get; }
    public IReadOnlyList<AlertChange> Changes { get; }
    public NotificationPreferences Preferences { get; }
    internal ActionableAlertSnapshot WithCauses(OperationsCauseSnapshot? causes)
    {
        if (causes is null || causes.Session != Session || causes.Tick != Tick || causes.Player != Player) return this;
        int powerGroups = 0;
        foreach (var group in causes.Groups) if (group.Reason == OperationsDiagnosisReason.NoPower) powerGroups++;
        if (powerGroups == 0) return this;
        return new(Session, Tick, Player, Alerts.Select(alert => alert.Identity.Kind == PlayerAlertState.LowPower
            ? alert with { Label = FormattableString.Invariant($"{alert.Label} / {powerGroups} SHARED CAUSE(S){(causes.Limited ? " / LIMITED" : "")}"),
                Target = causes.NextPowerMember(default) } : alert).ToArray(), Changes, Preferences);
    }
    public static ActionableAlertSnapshot? Resolve(PresentationSnapshot? snapshot) =>
        snapshot is { SessionId.IsSpecified: true, PlayerExperience: { } experience, Alerts: { } alerts } &&
        alerts.Session == snapshot.SessionId && alerts.Tick == snapshot.Tick && experience.Tick == snapshot.Tick &&
        alerts.Player == experience.Player ? alerts : null;
}

/// <summary>Eight live categories and tick-based attention transitions; no persistent incident log.</summary>
public sealed class AlertLifecycleTracker
{
    private readonly SimulationTick[] _started = new SimulationTick[8], _lastSeen = new SimulationTick[8], _resolvedAt = new SimulationTick[8];
    private readonly bool[] _active = new bool[8], _known = new bool[8], _resolved = new bool[8];
    private readonly ulong[] _causes = new ulong[8];
    private readonly AlertObservation[] _observations = new AlertObservation[4];
    private SimulationSessionId _session;
    private PlayerId _player;
    private SimulationTick _tick, _producerTick;
    private bool _producerBaseline;
    private PlayerMatchStatistics _statistics;
    private ulong _attackUntil, _completionUntil, _batchDue, _completionCause;
    private ulong _completedUnits, _pendingUnits;
    private int _completedBuildings, _pendingBuildings;
    private string _completionLabel = "WORK COMPLETED";
    public static PlayerAlertState Kind(int index) => index switch
    {
        0 => PlayerAlertState.CommandCoreDestroyed, 1 => PlayerAlertState.CommandCoreDamaged,
        2 => PlayerAlertState.SupplyCritical, 3 => PlayerAlertState.ProductionBlocked,
        4 => PlayerAlertState.LowPower, 5 => PlayerAlertState.CargoFleetShortage,
        6 => PlayerAlertState.CommandCoreUnderAttack, 7 => PlayerAlertState.WorkCompleted,
        _ => PlayerAlertState.None
    };
    public static AlertSeverity Severity(PlayerAlertState kind) => kind switch
    {
        PlayerAlertState.CommandCoreDestroyed or PlayerAlertState.CommandCoreUnderAttack or
            PlayerAlertState.CommandCoreDamaged or PlayerAlertState.SupplyCritical => AlertSeverity.Critical,
        PlayerAlertState.WorkCompleted => AlertSeverity.Information,
        _ => AlertSeverity.Warning
    };
    private void Prepare(SimulationSessionId session, PlayerId player, SimulationTick tick)
    {
        if (_session != session || _player != player || tick.Value < _tick.Value)
        {
            Array.Clear(_active); Array.Clear(_known); Array.Clear(_resolved); Array.Clear(_causes);
            _producerBaseline = false; _attackUntil = _completionUntil = _batchDue = 0;
            _completedUnits = _pendingUnits = 0; _completedBuildings = _pendingBuildings = 0;
        }
        _session = session; _player = player; _tick = tick;
    }
    public ActionableAlertSnapshot Capture(SimulationSessionId session, in PlayerExperienceSnapshot experience,
        ReadOnlySpan<EntityId> targets, CargoFleetCapacityObservation? fleet = null,
        ReadOnlySpan<AlertObservation> observations = default, int ticksPerSecond = 20,
        NotificationPreferences? preferences = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ticksPerSecond, 1);
        Prepare(session, experience.Player, experience.Tick);
        var alerts = new List<ActionableAlert>(8);
        var changes = new List<AlertChange>(8);
        ulong recovery = 3UL * (ulong)ticksPerSecond, cooldown = 5UL * (ulong)ticksPerSecond;
        for (int i = 0; i < 8; i++)
        {
            var kind = Kind(i);
            bool active = (experience.Alerts & kind) != 0;
            ulong cause = 0;
            string label = Label(kind);
            for (int item = 0; item < Math.Min(8, observations.Length); item++)
                if (observations[item].Kind == kind) { cause = observations[item].Cause; label = observations[item].Label; break; }
            if (active)
            {
                bool changedCause = _known[i] && cause != _causes[i];
                bool fresh = !_known[i] || changedCause || !_active[i] &&
                    (Severity(kind) == AlertSeverity.Critical || _resolved[i] &&
                        experience.Tick.Value - _resolvedAt[i].Value >= cooldown);
                if (fresh)
                {
                    _started[i] = experience.Tick;
                    changes.Add(new(new(session, kind, _started[i], experience.Player, cause),
                        changedCause ? AlertChangeKind.CauseChanged : AlertChangeKind.Started, experience.Tick));
                }
                _known[i] = true; _causes[i] = cause; _lastSeen[i] = experience.Tick; _resolved[i] = false;
                alerts.Add(new(new(session, kind, _started[i], experience.Player, cause), Severity(kind),
                    targets.Length > i ? targets[i] : default, label, Category(kind), Icon(kind),
                    kind == PlayerAlertState.CargoFleetShortage ? fleet : null));
            }
            else if (_known[i] && !_resolved[i] && experience.Tick.Value - _lastSeen[i].Value >= recovery)
            {
                _resolved[i] = true; _resolvedAt[i] = experience.Tick;
                changes.Add(new(new(session, kind, _started[i], experience.Player, _causes[i]), AlertChangeKind.Resolved, experience.Tick));
            }
            _active[i] = active;
        }
        return new(session, experience.Tick, experience.Player, alerts, changes, preferences);
    }
    public static string Label(PlayerAlertState kind) => kind switch
    {
        PlayerAlertState.CommandCoreDestroyed => "CORE DESTROYED",
        PlayerAlertState.CommandCoreUnderAttack => "CORE UNDER ATTACK",
        PlayerAlertState.WorkCompleted => "WORK COMPLETED",
        PlayerAlertState.CommandCoreDamaged => "CORE DAMAGED",
        PlayerAlertState.SupplyCritical => "SUPPLY CRITICAL",
        PlayerAlertState.ProductionBlocked => "PRODUCTION BLOCKED",
        PlayerAlertState.CargoFleetShortage => "INSUFFICIENT CARGO TRANSPORT CAPACITY",
        _ => "POWER CONSTRAINED"
    };
    public static OperationsCategory Category(PlayerAlertState kind) => kind switch
    {
        PlayerAlertState.SupplyCritical => OperationsCategory.Supply,
        PlayerAlertState.CargoFleetShortage => OperationsCategory.Logistics,
        PlayerAlertState.LowPower => OperationsCategory.Power,
        _ => OperationsCategory.Blocked
    };
    public static RtsUiIcon Icon(PlayerAlertState kind) => kind switch
    {
        PlayerAlertState.SupplyCritical => RtsUiIcon.SupplyCritical,
        PlayerAlertState.LowPower => RtsUiIcon.StatusPower,
        PlayerAlertState.CommandCoreUnderAttack => RtsUiIcon.StatusHealth,
        PlayerAlertState.WorkCompleted => RtsUiIcon.BuildingFactory,
        PlayerAlertState.ProductionBlocked => RtsUiIcon.BuildingFactory,
        PlayerAlertState.CargoFleetShortage => RtsUiIcon.UnitLogistics,
        PlayerAlertState.CommandCoreDamaged => RtsUiIcon.StatusHealth,
        PlayerAlertState.CommandCoreDestroyed => RtsUiIcon.BuildingCommand,
        _ => RtsUiIcon.StatusAlert
    };

    internal ActionableAlertSnapshot Capture(SimulationContext context, PresentationExtractionContext extraction,
        in PlayerExperienceSnapshot experience, CargoFleetCapacityObservation? fleet = null, NotificationPreferences? preferences = null)
    {
        var session = extraction.Scenario.Simulation.SessionId;
        Prepare(session, experience.Player, context.Tick);
        int rate = extraction.Scenario.Simulation.Clock.TicksPerSecond;
        Span<EntityId> targets = stackalloc EntityId[8];
        targets.Clear();
        ulong productionCause = 0, powerCause = 0, supplyCause = 0;
        foreach (var entity in context.Entities.Query<ControllableEntity>(QueryIterationOrder.StableByEntityIndex))
        {
            var owner = context.Entities.GetComponent<ControllableEntity>(entity);
            if (owner.Owner != extraction.Player || !owner.IsControllable ||
                context.Entities.TryGetComponent(entity, out HealthState health) && health.IsDepleted) continue;
            if (entity == extraction.Side.CommandCore) { targets[1] = entity; targets[6] = entity; }
            if (context.Entities.TryGetComponent(entity, out UnitSupplyState supply) &&
                supply.Status is BattlefieldSupplyStatus.Critical or BattlefieldSupplyStatus.Unsupplied)
            {
                if (!targets[2].IsValid) targets[2] = entity;
                supplyCause |= supply.Status == BattlefieldSupplyStatus.Unsupplied ? 2UL : 1UL;
            }
            ulong cause = 0;
            if (context.Entities.TryGetComponent(entity, out ProductionFacility production))
                cause |= production.Status switch { ProductionStatus.NoInput => 1UL, ProductionStatus.NoPower => 2UL,
                    ProductionStatus.OutputFull => 4UL, ProductionStatus.Paused => 8UL, _ => 0UL };
            if (context.Entities.TryGetComponent(entity, out UnitProductionFacility units))
                cause |= units.Status switch { UnitProductionStatus.NoInput => 1UL, UnitProductionStatus.NoPower => 2UL,
                    UnitProductionStatus.OutputBlocked => 4UL, UnitProductionStatus.Paused => 8UL, _ => 0UL };
            if (cause != 0) { productionCause |= cause; if (!targets[3].IsValid) targets[3] = entity; }
            if (context.Entities.TryGetComponent(entity, out PowerConsumer power) &&
                power.State is PowerOperationalState.Brownout or PowerOperationalState.Offline)
            {
                powerCause |= power.State == PowerOperationalState.Offline ? 2UL : 1UL;
                if (!targets[4].IsValid) targets[4] = entity;
            }
        }
        var effective = AdaptEvents(session, experience, targets[6], extraction.Scenario.Services.CombatRuntime.Events, rate, preferences);
        if (fleet is { } shortage) targets[5] = shortage.AffectedFacility;
        Span<AlertObservation> observations = _observations;
        observations[0] = new(PlayerAlertState.ProductionBlocked, productionCause, ProductionLabel(productionCause));
        observations[1] = new(PlayerAlertState.LowPower, powerCause, powerCause == 1 ? "POWER BROWNOUT" : powerCause == 2 ? "POWER OFFLINE" : "POWER CONSTRAINED / MULTIPLE STATES");
        observations[2] = new(PlayerAlertState.SupplyCritical, supplyCause, supplyCause == 2 ? "SUPPLY CRITICAL / UNSUPPLIED" : "SUPPLY CRITICAL");
        observations[3] = new(PlayerAlertState.WorkCompleted, _completionCause, _completionLabel);
        return Capture(session, effective, targets, fleet, observations, rate, preferences);
    }
    internal PlayerExperienceSnapshot AdaptEvents(SimulationSessionId session, in PlayerExperienceSnapshot experience,
        EntityId ownedCore, IReadOnlyList<CombatEvent> events, int rate, NotificationPreferences? preferences = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rate, 1);
        Prepare(session, experience.Player, experience.Tick);
        var effective = experience;
        foreach (var item in events)
            if (item.Tick == experience.Tick && item.Type == CombatEventType.DamageApplied &&
                item.Target == ownedCore && ownedCore.IsValid && double.IsFinite(item.Amount) && item.Amount > 0)
                _attackUntil = Add(experience.Tick.Value, 5UL * (ulong)rate);
        if (ownedCore.IsValid && experience.Tick.Value < _attackUntil) effective = effective with { Alerts = effective.Alerts | PlayerAlertState.CommandCoreUnderAttack };
        if (!_producerBaseline || _producerTick != experience.Tick)
        {
            if (_producerBaseline && experience.Tick > _producerTick)
            {
                ulong units = experience.Statistics.UnitsProduced > _statistics.UnitsProduced
                    ? experience.Statistics.UnitsProduced - _statistics.UnitsProduced : 0;
                int buildings = Math.Max(0, experience.Statistics.BuildingsConstructed - _statistics.BuildingsConstructed);
                if (units > 0 || buildings > 0)
                {
                    _pendingUnits = Add(_pendingUnits, units);
                    _pendingBuildings = (int)Math.Min(int.MaxValue, (long)_pendingBuildings + buildings);
                    if (_batchDue == 0) _batchDue = Add(experience.Tick.Value, 5UL * (ulong)rate);
                }
                if ((_pendingUnits > 0 || _pendingBuildings > 0) &&
                    (preferences?.Mode != NotificationMode.Batched || experience.Tick.Value >= _batchDue))
                {
                    if (experience.Tick.Value >= _completionUntil) { _completedUnits = 0; _completedBuildings = 0; _completionCause++; }
                    _completedUnits = Add(_completedUnits, _pendingUnits);
                    _completedBuildings = (int)Math.Min(int.MaxValue, (long)_completedBuildings + _pendingBuildings);
                    _pendingUnits = 0; _pendingBuildings = 0; _batchDue = 0;
                    _completionUntil = Add(experience.Tick.Value, 5UL * (ulong)rate);
                    _completionLabel = FormattableString.Invariant($"WORK COMPLETED / {_completedUnits} UNIT(S) / {_completedBuildings} BUILDING(S)");
                }
            }
            _producerBaseline = true; _producerTick = experience.Tick; _statistics = experience.Statistics;
        }
        if (experience.Tick.Value < _completionUntil) effective = effective with { Alerts = effective.Alerts | PlayerAlertState.WorkCompleted };
        return effective;
    }
    private static ulong Add(ulong value, ulong amount) => value + Math.Min(ulong.MaxValue - value, amount);
    private static string ProductionLabel(ulong cause) => cause switch
    {
        1 => "PRODUCTION BLOCKED / NO INPUT", 2 => "PRODUCTION BLOCKED / NO POWER",
        4 => "PRODUCTION BLOCKED / OUTPUT FULL", 8 => "PRODUCTION BLOCKED / PAUSED",
        _ => "PRODUCTION BLOCKED / MULTIPLE CURRENT CAUSES"
    };
}
