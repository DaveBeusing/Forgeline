using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public enum AlertSeverity : byte { Information, Warning, Critical }
public readonly record struct AlertIdentity(SimulationSessionId Session, PlayerAlertState Kind, SimulationTick Started, PlayerId Player = default);
public readonly record struct ActionableAlert(AlertIdentity Identity, AlertSeverity Severity, EntityId Target,
    string Label, OperationsCategory Operations, RtsUiIcon Icon);

/// <summary>Current conditions only; immutable, bounded and authorized at the completed tick.</summary>
public sealed class ActionableAlertSnapshot
{
    public ActionableAlertSnapshot(SimulationSessionId session, SimulationTick tick, PlayerId player, IReadOnlyList<ActionableAlert> alerts)
    {
        Session = session; Tick = tick; Player = player;
        Alerts = Array.AsReadOnly(alerts.Take(5).ToArray());
    }
    public SimulationSessionId Session { get; }
    public SimulationTick Tick { get; }
    public PlayerId Player { get; }
    public IReadOnlyList<ActionableAlert> Alerts { get; }
    public static ActionableAlertSnapshot? Resolve(PresentationSnapshot? snapshot) =>
        snapshot is { SessionId.IsSpecified: true, PlayerExperience: { } experience, Alerts: { } alerts } &&
        alerts.Session == snapshot.SessionId && alerts.Tick == snapshot.Tick && experience.Tick == snapshot.Tick &&
        alerts.Player == experience.Player ? alerts : null;
}

/// <summary>Five aggregate identities avoid per-tick notification storms; resolution removes a condition immediately.</summary>
public sealed class AlertLifecycleTracker
{
    private readonly SimulationTick[] _started = new SimulationTick[5];
    private readonly bool[] _active = new bool[5];
    private SimulationSessionId _session;
    private PlayerId _player;
    private SimulationTick _tick;
    public static PlayerAlertState Kind(int index) => index switch
    {
        0 => PlayerAlertState.CommandCoreDestroyed,
        1 => PlayerAlertState.CommandCoreDamaged,
        2 => PlayerAlertState.SupplyCritical,
        3 => PlayerAlertState.ProductionBlocked,
        _ => PlayerAlertState.LowPower
    };
    public ActionableAlertSnapshot Capture(SimulationSessionId session, in PlayerExperienceSnapshot experience, ReadOnlySpan<EntityId> targets)
    {
        if (_session != session || _player != experience.Player || experience.Tick.Value < _tick.Value) Array.Clear(_active);
        _session = session; _player = experience.Player; _tick = experience.Tick;
        var alerts = new List<ActionableAlert>(5);
        for (int i = 0; i < 5; i++)
        {
            var kind = Kind(i); bool active = (experience.Alerts & kind) != 0;
            if (active && !_active[i]) _started[i] = experience.Tick;
            _active[i] = active;
            if (!active) continue;
            alerts.Add(new(new(session, kind, _started[i], experience.Player), i < 3 ? AlertSeverity.Critical : AlertSeverity.Warning,
                targets.Length > i ? targets[i] : default, Label(kind), Category(kind), Icon(kind)));
        }
        return new(session, experience.Tick, experience.Player, alerts);
    }
    public static string Label(PlayerAlertState kind) => kind switch
    {
        PlayerAlertState.CommandCoreDestroyed => "CORE DESTROYED",
        PlayerAlertState.CommandCoreDamaged => "CORE DAMAGED",
        PlayerAlertState.SupplyCritical => "SUPPLY CRITICAL",
        PlayerAlertState.ProductionBlocked => "PRODUCTION BLOCKED",
        _ => "POWER CONSTRAINED"
    };
    public static OperationsCategory Category(PlayerAlertState kind) => kind switch
    {
        PlayerAlertState.SupplyCritical => OperationsCategory.Supply,
        PlayerAlertState.LowPower => OperationsCategory.Power,
        _ => OperationsCategory.Blocked
    };
    public static RtsUiIcon Icon(PlayerAlertState kind) => kind switch
    {
        PlayerAlertState.SupplyCritical => RtsUiIcon.SupplyCritical,
        PlayerAlertState.LowPower => RtsUiIcon.StatusPower,
        PlayerAlertState.ProductionBlocked => RtsUiIcon.BuildingFactory,
        PlayerAlertState.CommandCoreDamaged => RtsUiIcon.StatusHealth,
        PlayerAlertState.CommandCoreDestroyed => RtsUiIcon.BuildingCommand,
        _ => RtsUiIcon.StatusAlert
    };

    internal ActionableAlertSnapshot Capture(SimulationContext context, PresentationExtractionContext extraction, in PlayerExperienceSnapshot experience)
    {
        Span<EntityId> targets = stackalloc EntityId[5];
        foreach (var entity in context.Entities.Query<ControllableEntity>(QueryIterationOrder.StableByEntityIndex))
        {
            var owner = context.Entities.GetComponent<ControllableEntity>(entity);
            if (owner.Owner != extraction.Player || !owner.IsControllable ||
                context.Entities.TryGetComponent(entity, out HealthState health) && health.IsDepleted) continue;
            if (entity == extraction.Side.CommandCore)
                targets[1] = entity;
            if (!targets[2].IsValid && context.Entities.TryGetComponent(entity, out UnitSupplyState supply) &&
                supply.Status is BattlefieldSupplyStatus.Critical or BattlefieldSupplyStatus.Unsupplied) targets[2] = entity;
            if (!targets[3].IsValid &&
                (context.Entities.TryGetComponent(entity, out ProductionFacility production) && production.Status is ProductionStatus.NoInput or ProductionStatus.NoPower or ProductionStatus.OutputFull or ProductionStatus.Paused ||
                context.Entities.TryGetComponent(entity, out UnitProductionFacility units) && units.Status is UnitProductionStatus.NoInput or UnitProductionStatus.NoPower or UnitProductionStatus.Paused)) targets[3] = entity;
            if (!targets[4].IsValid && context.Entities.TryGetComponent(entity, out PowerConsumer power) &&
                power.State is PowerOperationalState.Brownout or PowerOperationalState.Offline) targets[4] = entity;
        }
        return Capture(extraction.Scenario.Simulation.SessionId, experience, targets);
    }
}
