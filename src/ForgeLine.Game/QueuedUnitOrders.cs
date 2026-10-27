using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum QueuedOrderKind : byte { Move, AttackMove, Attack }
public enum QueuedOrderEdit : byte { Replace, Append, Cancel }
public enum QueuedOrderStatus : byte { WaitingForCurrent, Running, SuspendedForSupply, Blocked, Rejected }
public readonly record struct QueuedUnitOrder(QueuedOrderKind Kind, Vector3 Destination,
    EntityId Target, FormationTemplate Formation, SimulationTick SubmittedAtTick);

/// <summary>Immutable bounded intent. Movement, combat and supply remain with their existing systems.</summary>
public readonly record struct QueuedUnitOrders
{
    public const int MaximumEntries = 16;
    public const int MaximumUnits = 1024;
    public QueuedUnitOrders(PlayerId owner, IReadOnlyList<QueuedUnitOrder> entries, int cursor,
        bool repeating, bool active, bool waitForCurrent, bool resumeAfterSupply,
        QueuedOrderStatus status, SimulationTick issuedAt)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentOutOfRangeException.ThrowIfLessThan(entries.Count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(entries.Count, MaximumEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(cursor);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cursor, entries.Count);
        Owner = owner; Entries = Array.AsReadOnly(entries.ToArray()); Cursor = cursor;
        Repeating = repeating; Active = active; WaitForCurrent = waitForCurrent;
        ResumeAfterSupply = resumeAfterSupply; Status = status; IssuedAt = issuedAt;
    }
    public PlayerId Owner { get; init; }
    public IReadOnlyList<QueuedUnitOrder> Entries { get; }
    public int Cursor { get; init; }
    public bool Repeating { get; init; }
    public bool Active { get; init; }
    public bool WaitForCurrent { get; init; }
    public bool ResumeAfterSupply { get; init; }
    public QueuedOrderStatus Status { get; init; }
    public SimulationTick IssuedAt { get; init; }
    public int PendingCount => Repeating ? Entries.Count - (Active ? 1 : 0) : Entries.Count - Cursor - (Active ? 1 : 0);
}

public sealed class QueuedOrdersCommand : ISimulationCommand
{
    private readonly EntityId[] _units;
    private readonly FactionIntelligenceStore _intelligence;
    public QueuedOrdersCommand(PlayerId issuer, ReadOnlySpan<EntityId> units, QueuedUnitOrder order,
        QueuedOrderEdit edit, bool patrol, SimulationTick submittedAtTick, FactionIntelligenceStore intelligence)
    {
        TacticalCommandUtilities.ValidateIssuerAndUnits(issuer, units);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(units.Length, QueuedUnitOrders.MaximumUnits);
        if (!Enum.IsDefined(edit) || !Enum.IsDefined(order.Kind) || !Enum.IsDefined(order.Formation))
            throw new ArgumentOutOfRangeException(nameof(edit));
        TacticalCommandUtilities.ValidatePosition(order.Destination, nameof(order));
        if (order.Kind == QueuedOrderKind.Attack && !order.Target.IsValid)
            throw new ArgumentException("An attack entry requires a complete target identity.", nameof(order));
        if (patrol && order.Kind != QueuedOrderKind.Move)
            throw new ArgumentException("Patrol uses movement waypoints.", nameof(patrol));
        _intelligence = intelligence ?? throw new ArgumentNullException(nameof(intelligence));
        _units = units.ToArray(); Issuer = issuer; Order = order with { SubmittedAtTick = submittedAtTick };
        Edit = edit; Patrol = patrol; SubmittedAtTick = submittedAtTick;
    }
    public PlayerId Issuer { get; }
    public ReadOnlySpan<EntityId> Units => _units;
    public QueuedUnitOrder Order { get; }
    public QueuedOrderEdit Edit { get; }
    public bool Patrol { get; }
    public SimulationTick SubmittedAtTick { get; }
    public SimulationTick ExecutedAtTick { get; private set; }
    public int AcceptedTargetCount { get; private set; }
    public int RejectedTargetCount { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ExecutedAtTick = context.Tick;
        AcceptedTargetCount = RejectedTargetCount = 0;
        bool knownTarget = Order.Kind != QueuedOrderKind.Attack || Issuer.Value <= uint.MaxValue &&
            _intelligence.TryResolveCurrentlyIdentifiedEntity(new FactionId((uint)Issuer.Value),
                IntelligenceContactKey.FromEntity(Order.Target), out var known) && known == Order.Target;
        var seen = new HashSet<EntityId>();
        foreach (var unit in _units)
        {
            if (!seen.Add(unit) || !QueuedOrderSystem.Eligible(context, unit, Issuer) || !knownTarget ||
                Order.Kind != QueuedOrderKind.Move && !context.Entities.HasComponent<Combatant>(unit))
            { RejectedTargetCount++; continue; }
            if (Patrol && Edit == QueuedOrderEdit.Replace &&
                VehicleDockPose.HorizontalDistanceSquared(context.Entities.GetComponent<WorldTransform>(unit).Position, Order.Destination) <= 4)
            { RejectedTargetCount++; continue; }
            bool existing = context.Entities.TryGetComponent(unit, out QueuedUnitOrders queue);
            if (Edit == QueuedOrderEdit.Cancel)
            {
                if (existing) context.Entities.RemoveComponent<QueuedUnitOrders>(unit);
                AcceptedTargetCount++; continue;
            }
            if (!existing && context.Entities.GetComponentCount<QueuedUnitOrders>() >= QueuedUnitOrders.MaximumUnits)
            { RejectedTargetCount++; continue; }
            if (Edit == QueuedOrderEdit.Append && existing)
            {
                // A finite command cannot be placed after an endless loop. Patrol append extends its fixed cycle.
                if (Patrol != queue.Repeating)
                { RejectedTargetCount++; continue; }
                var entries = queue.Repeating ? queue.Entries.ToList() : queue.Entries.Skip(queue.Cursor).ToList();
                int cursor = queue.Repeating ? queue.Cursor : 0;
                if (entries.Count >= QueuedUnitOrders.MaximumEntries) { RejectedTargetCount++; continue; }
                entries.Add(Order);
                context.Entities.SetComponent(unit, new QueuedUnitOrders(queue.Owner, entries, cursor, queue.Repeating,
                    queue.Active, queue.WaitForCurrent, queue.ResumeAfterSupply, queue.Status, queue.IssuedAt));
            }
            else
            {
                // Shift-patrol only extends a real patrol, never changes a finite sequence into a loop.
                if (Patrol && Edit == QueuedOrderEdit.Append) { RejectedTargetCount++; continue; }
                QueuedUnitOrder[] entries = Patrol
                    ? [Order with { Destination = context.Entities.GetComponent<WorldTransform>(unit).Position }, Order]
                    : [Order];
                var next = new QueuedUnitOrders(Issuer, entries, Patrol ? 1 : 0, Patrol, false,
                    Edit == QueuedOrderEdit.Append, false, QueuedOrderStatus.WaitingForCurrent, default);
                VehicleDockingService.Put(context.Entities, unit, next);
            }
            AcceptedTargetCount++;
        }
    }
}

public sealed class QueuedOrderSystem(FactionIntelligenceStore intelligence, WeaponCatalog weapons,
    ArtilleryWeaponCatalog artilleryWeapons) : ISimulationSystem
{
    private readonly List<EntityId> _units = new(QueuedUnitOrders.MaximumUnits);
    public SimulationPhase Phase => SimulationPhase.OrderProcessing;
    internal static bool Eligible(SimulationContext context, EntityId unit, PlayerId owner) =>
        context.Entities.IsAlive(unit) && context.Entities.TryGetComponent(unit, out ControllableEntity control) &&
        control.Owner == owner && control.IsControllable && (control.Category & ControllableEntityCategory.Building) == 0 &&
        context.Entities.HasComponent<Combatant>(unit) &&
        context.Entities.HasComponent<WorldTransform>(unit) && context.Entities.HasComponent<GroundMovement>(unit) &&
        context.Entities.HasComponent<NavigationAgent>(unit) && context.Entities.HasComponent<GroundMovementState>(unit) &&
        (!context.Entities.TryGetComponent(unit, out HealthState health) || !health.IsDepleted) &&
        !context.Entities.HasComponent<CargoTransport>(unit) && !context.Entities.HasComponent<SupplyTruck>(unit) &&
        !context.Entities.HasComponent<CargoTransportOrder>(unit) && !context.Entities.HasComponent<SupplyLoadingOrder>(unit);

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _units.Clear();
        foreach (var unit in context.Entities.Query<QueuedUnitOrders>(QueryIterationOrder.StableByEntityIndex))
        { if (_units.Count == QueuedUnitOrders.MaximumUnits) break; _units.Add(unit); }
        foreach (var unit in _units)
        {
            var queue = context.Entities.GetComponent<QueuedUnitOrders>(unit);
            if (!Eligible(context, unit, queue.Owner))
            { context.Entities.RemoveComponent<QueuedUnitOrders>(unit); continue; }
            if (context.Entities.HasComponent<ResupplyOrder>(unit))
            {
                context.Entities.SetComponent(unit, queue with { ResumeAfterSupply = queue.Active,
                    Status = QueuedOrderStatus.SuspendedForSupply });
                continue;
            }
            if (queue.Status == QueuedOrderStatus.Rejected) continue;
            if (queue.Active && !queue.ResumeAfterSupply)
            {
                if (!Complete(context, unit, queue))
                {
                    if (context.Entities.HasComponent<NavigationFailureState>(unit))
                        context.Entities.SetComponent(unit, queue with { Status = QueuedOrderStatus.Blocked });
                    continue;
                }
                int cursor = queue.Cursor + 1;
                if (cursor >= queue.Entries.Count)
                {
                    if (!queue.Repeating) { context.Entities.RemoveComponent<QueuedUnitOrders>(unit); continue; }
                    cursor = 0;
                }
                queue = queue with { Cursor = cursor, Active = false, WaitForCurrent = false };
            }
            if (!queue.Active && queue.WaitForCurrent && HasCurrent(context, unit))
            {
                var waiting = context.Entities.HasComponent<NavigationFailureState>(unit)
                    ? QueuedOrderStatus.Blocked : QueuedOrderStatus.WaitingForCurrent;
                if (queue.Status != waiting) context.Entities.SetComponent(unit, queue with { Status = waiting });
                continue;
            }
            Activate(context, unit, queue);
        }
    }
    private static bool HasCurrent(SimulationContext context, EntityId unit)
    {
        if (context.Entities.HasComponent<FireMissionRequest>(unit) ||
            context.Entities.TryGetComponent(unit, out FireMissionState mission) &&
                mission.Status is not (FireMissionStatus.Complete or FireMissionStatus.Cancelled)) return true;
        if (context.Entities.HasComponent<MovementGroupMember>(unit) ||
            TacticalCommandUtilities.TryGetMovementIntent(context, unit, out _) ||
            context.Entities.HasComponent<NavigationFailureState>(unit)) return true;
        return context.Entities.HasComponent<CombatOrderState>(unit) &&
            (!context.Entities.TryGetComponent(unit, out TacticalCombatState state) ||
                state.Status is not (CombatOrderStatus.Complete or CombatOrderStatus.Cancelled));
    }
    private static bool Complete(SimulationContext context, EntityId unit, in QueuedUnitOrders queue)
    {
        var entry = queue.Entries[queue.Cursor];
        if (entry.Kind != QueuedOrderKind.Move)
            return context.Entities.TryGetComponent(unit, out TacticalCombatState state) &&
                state.Status == CombatOrderStatus.Complete;
        if (HasCurrent(context, unit)) return false;
        var position = context.Entities.GetComponent<WorldTransform>(unit).Position;
        return VehicleDockPose.HorizontalDistanceSquared(position, entry.Destination) <= 4;
    }
    private void Activate(SimulationContext context, EntityId unit, QueuedUnitOrders queue)
    {
        var entry = queue.Entries[queue.Cursor];
        context.Entities.RemoveComponent<QueuedUnitOrders>(unit);
        int accepted;
        if (entry.Kind == QueuedOrderKind.Move)
        {
            var command = new MoveEntitiesCommand(queue.Owner, [unit], entry.Destination, context.Tick, entry.Formation);
            command.Execute(context); accepted = command.AcceptedTargetCount;
        }
        else
        {
            var command = entry.Kind == QueuedOrderKind.Attack
                ? PlayerTacticalActionCommand.Attack(queue.Owner, [unit], entry.Target, context.Tick, intelligence, weapons, artilleryWeapons)
                : PlayerTacticalActionCommand.AttackMove(queue.Owner, [unit], entry.Destination, context.Tick,
                    entry.Formation, intelligence, weapons, artilleryWeapons);
            command.Execute(context); accepted = command.AcceptedTargetCount;
        }
        // Reuse the same bounded intent. No submitted commands or new groups are produced by recurrence.
        VehicleDockingService.Put(context.Entities, unit, queue with { Active = accepted > 0, WaitForCurrent = false,
            ResumeAfterSupply = false, IssuedAt = context.Tick,
            Status = accepted > 0 ? QueuedOrderStatus.Running : QueuedOrderStatus.Rejected });
    }
}
