using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public enum SelectedUnitOrderKind : byte
{
    Idle, Move, Attack, AttackMove, Retreat, Hold, Cargo, Resupply
}

public readonly record struct SelectedUnitOrderView(
    EntityId Entity,
    Vector3 Origin,
    SelectedUnitOrderKind Kind,
    string Status,
    Vector3 Target,
    bool HasTarget,
    EntityId TargetEntity = default);

/// <summary>Immutable copied selection and authoritative orders from one session/tick.</summary>
public sealed class SelectedUnitOrderSnapshot
{
    private readonly EntityId[] _selected;
    private readonly SelectedUnitOrderView[] _orders;

    internal SelectedUnitOrderSnapshot(
        SimulationSessionId session,
        SimulationTick tick,
        EntityId[] selected,
        SelectedUnitOrderView[] orders,
        string summary)
    {
        SessionId = session;
        Tick = tick;
        _selected = (EntityId[])selected.Clone();
        _orders = (SelectedUnitOrderView[])orders.Clone();
        Summary = summary;
    }

    public SimulationSessionId SessionId { get; }
    public SimulationTick Tick { get; }
    public string Summary { get; }
    public ReadOnlySpan<SelectedUnitOrderView> Orders => _orders;

    // The input thread can change selection before another simulation tick is
    // available; never show an old selection's order on a new selection.
    public bool MatchesSelection(
        SimulationSessionId session,
        IReadOnlyCollection<EntityId>? selected)
    {
        if (SessionId != session || selected is null || selected.Count != _selected.Length)
            return false;
        int index = 0;
        foreach (EntityId entity in selected)
            if (_selected[index++] != entity)
                return false;
        return true;
    }
}

internal static class SelectedUnitOrderSnapshotFactory
{
    internal static SelectedUnitOrderSnapshot Capture(
        SimulationContext context,
        PresentationExtractionContext extraction,
        IReadOnlyList<EntityId> selected)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(extraction);
        ArgumentNullException.ThrowIfNull(selected);

        var views = new List<SelectedUnitOrderView>(selected.Count);
        var seen = new HashSet<EntityId>();
        EntityRegistry entities = context.Entities;
        for (int i = 0; i < selected.Count; i++)
        {
            EntityId id = selected[i];
            if (!seen.Add(id) || !entities.IsAlive(id) ||
                !entities.TryGetComponent(id, out ControllableEntity control) ||
                control.Owner != extraction.Player ||
                (control.Category & ControllableEntityCategory.Building) != 0 ||
                (control.Category & (ControllableEntityCategory.Unit | ControllableEntityCategory.Logistics)) == 0 ||
                !entities.TryGetComponent(id, out WorldTransform world))
                continue;
            views.Add(Resolve(context, extraction, id, world.Position));
        }

        return new SelectedUnitOrderSnapshot(
            extraction.Scenario.Simulation.SessionId, context.Tick,
            selected.ToArray(), views.ToArray(), Summary(views));
    }

    private static SelectedUnitOrderView Resolve(
        SimulationContext context,
        PresentationExtractionContext extraction,
        EntityId id,
        Vector3 origin)
    {
        EntityRegistry entities = context.Entities;
        bool outOfFuel = entities.TryGetComponent(id, out UnitFuelState fuel) &&
            extraction.Scenario.Inventories.Contains(fuel.InventoryId) &&
            extraction.Scenario.Inventories.GetAvailableQuantity(
                fuel.InventoryId, ResourceIds.Fuel) <= 0.0;

        SelectedUnitOrderView View(
            SelectedUnitOrderKind kind, string status, Vector3 target = default,
            bool hasTarget = false, EntityId targetEntity = default) =>
            new(id, origin, kind, outOfFuel && kind is SelectedUnitOrderKind.Move or
                SelectedUnitOrderKind.AttackMove or SelectedUnitOrderKind.Retreat or
                SelectedUnitOrderKind.Cargo ? status + " — OUT OF FUEL" : status,
                target, hasTarget && Finite(target), targetEntity);

        // Logistics owns its current destination even when navigation has
        // temporarily issued an internal dock/queue approach.
        if (entities.TryGetComponent(id, out CargoTransportOrder cargo))
        {
            CargoTransportRuntimeState state = entities.TryGetComponent(
                id, out CargoTransportRuntimeState live)
                ? live : CargoTransportRuntimeState.Idle;
            bool goOrigin = state.Lifecycle is CargoTransportLifecycleState.ToOrigin or
                CargoTransportLifecycleState.Loading;
            bool queued = entities.HasComponent<VehicleQueueEntry>(id);
            string verb = state.Lifecycle switch
            {
                CargoTransportLifecycleState.ToOrigin => queued ? "COLLECT — WAITING FOR BAY" : "COLLECT",
                CargoTransportLifecycleState.Loading => "LOADING",
                CargoTransportLifecycleState.ToDestination => queued ? "DELIVER — WAITING FOR BAY" : "DELIVER",
                CargoTransportLifecycleState.Unloading => "UNLOADING",
                CargoTransportLifecycleState.Waiting => state.WaitReason switch
                {
                    CargoTransportWaitReason.OriginResourceUnavailable => "COLLECT — NO STOCK",
                    CargoTransportWaitReason.DestinationCapacity => "DELIVER — DESTINATION FULL",
                    CargoTransportWaitReason.RouteUnavailable or CargoTransportWaitReason.RouteInvalidated => "DELIVERY — ROUTE BLOCKED",
                    _ => "DELIVERY — WAITING"
                },
                CargoTransportLifecycleState.Failed => "DELIVERY — BLOCKED",
                _ => "CARGO"
            };
            var network = extraction.Scenario.Logistics;
            var nodeId = goOrigin ? cargo.Origin : cargo.Destination;
            if (state.Lifecycle is CargoTransportLifecycleState.Waiting or CargoTransportLifecycleState.Failed)
                nodeId = state.AnchorNode.IsSpecified ? state.AnchorNode : cargo.Destination;
            if (network.TryGetNode(nodeId, out var node) &&
                entities.IsAlive(node.Entity) &&
                entities.TryGetComponent(node.Entity, out ControllableEntity owner) &&
                owner.Owner == extraction.Player)
                return View(SelectedUnitOrderKind.Cargo, verb, node.WorldPosition, true, node.Entity);
            return View(SelectedUnitOrderKind.Cargo, verb);
        }

        if (entities.TryGetComponent(id, out ResupplyOrder resupply))
        {
            if (entities.IsAlive(resupply.Provider) &&
                entities.TryGetComponent(resupply.Provider, out WorldTransform provider) &&
                entities.TryGetComponent(resupply.Provider, out ControllableEntity providerOwner) &&
                providerOwner.Owner == extraction.Player)
                return View(SelectedUnitOrderKind.Resupply, "RESUPPLY", provider.Position, true, resupply.Provider);
            return View(SelectedUnitOrderKind.Resupply, "RESUPPLY — WAITING");
        }

        if (entities.TryGetComponent(id, out CombatOrderState combat))
        {
            CombatOrderStatus phase = entities.TryGetComponent(id, out TacticalCombatState liveCombat)
                ? liveCombat.Status : CombatOrderStatus.Pending;
            if (phase is CombatOrderStatus.Complete or CombatOrderStatus.Cancelled)
                return View(SelectedUnitOrderKind.Idle, "IDLE");

            string status = phase switch
            {
                CombatOrderStatus.Resupplying => "RESUPPLYING",
                CombatOrderStatus.WaitingForIntelligence => "WAITING FOR INTELLIGENCE",
                CombatOrderStatus.Engaging => "ENGAGING",
                CombatOrderStatus.Pursuing => "PURSUING",
                CombatOrderStatus.Retreating => "RETREATING",
                _ => phase.ToString().ToUpperInvariant()
            };
            switch (combat.Kind)
            {
                case CombatOrderKind.Attack:
                    if (entities.IsAlive(combat.ExplicitTarget))
                    {
                        EntityId targetId = combat.ExplicitTarget;
                        // Never read enemy WorldTransform: only current faction-
                        // authorized intelligence contact locations are published.
                        if (extraction.Scenario.Intelligence.TryGetContact(
                                new FactionId(checked((uint)extraction.Player.Value)),
                                IntelligenceContactKey.FromEntity(targetId),
                                out IntelligenceContact contact) &&
                            contact.IsCurrent &&
                            contact.State is IntelligenceState.Detected or IntelligenceState.Identified)
                            return View(SelectedUnitOrderKind.Attack, "ATTACK — " + status,
                                contact.LastKnownPosition, true, targetId);
                    }
                    return View(SelectedUnitOrderKind.Attack, "ATTACK — " + status);
                case CombatOrderKind.AttackMove:
                    return View(SelectedUnitOrderKind.AttackMove, "ATTACK MOVE — " + status,
                        combat.Destination, combat.HasDestination);
                case CombatOrderKind.Retreat:
                    return View(SelectedUnitOrderKind.Retreat, "RETREAT — " + status,
                        combat.Destination, combat.HasDestination);
                case CombatOrderKind.HoldPosition:
                    return View(SelectedUnitOrderKind.Hold, "HOLD POSITION");
                case CombatOrderKind.Stop:
                    return View(SelectedUnitOrderKind.Idle, "STOPPED");
            }
        }

        if (entities.TryGetComponent(id, out MovementGroupMember member) &&
            entities.IsAlive(member.Group) &&
            entities.TryGetComponent(member.Group, out MovementGroupOrder groupOrder) &&
            groupOrder.Issuer == extraction.Player &&
            (!entities.TryGetComponent(member.Group, out MovementGroupState groupState) ||
             groupState.Status is MovementGroupStatus.AwaitingRoute or MovementGroupStatus.Moving))
            return View(SelectedUnitOrderKind.Move, "MOVE — FORMATION", groupOrder.Destination, true);

        MovementOrder movement;
        if (entities.TryGetComponent(id, out NavigationPendingPath pending))
            movement = pending.OriginalOrder;
        else if (entities.TryGetComponent(id, out NavigationRouteState route))
            movement = route.OriginalOrder;
        else if (entities.TryGetComponent(id, out NavigationFailureState failure))
            return View(SelectedUnitOrderKind.Move, "MOVE — ROUTE BLOCKED",
                failure.OriginalOrder.WorldTarget,
                failure.OriginalOrder.Kind == MovementOrderKind.Strategic);
        else if (!entities.TryGetComponent(id, out movement))
            return View(SelectedUnitOrderKind.Idle, "IDLE");

        if (movement.Kind != MovementOrderKind.Strategic)
            return View(SelectedUnitOrderKind.Idle, "IDLE");
        return View(SelectedUnitOrderKind.Move, "MOVE", movement.WorldTarget, true);
    }

    private static string Summary(List<SelectedUnitOrderView> orders)
    {
        if (orders.Count == 0) return "";
        if (orders.Count == 1) return orders[0].Status;

        var counts = new int[Enum.GetValues<SelectedUnitOrderKind>().Length];
        foreach (SelectedUnitOrderView order in orders)
            counts[(int)order.Kind]++;
        var labels = new List<string>(counts.Length);
        foreach (SelectedUnitOrderKind kind in Enum.GetValues<SelectedUnitOrderKind>())
        {
            int count = counts[(int)kind];
            if (count == 0) continue;
            string name = kind switch
            {
                SelectedUnitOrderKind.AttackMove => "ATTACK MOVE",
                SelectedUnitOrderKind.Resupply => "RESUPPLY",
                _ => kind.ToString().ToUpperInvariant()
            };
            labels.Add(name + " " + count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        return string.Join(" / ", labels);
    }

    private static bool Finite(Vector3 point) =>
        float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
}
