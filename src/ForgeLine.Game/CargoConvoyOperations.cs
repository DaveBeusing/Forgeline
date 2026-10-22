using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum CargoConvoyStatus : byte
{
    Rendezvous = 0,
    Holding = 1,
    Moving = 2,
    Disrupted = 3,
    Dissolved = 4
}

/// <summary>A generation-aware simulation entity owns one temporary regional convoy.</summary>
public readonly record struct CargoConvoyGroup(
    PlayerId Owner,
    EntityId Leader,
    LogisticsNodeId Origin,
    LogisticsNodeId Destination,
    LogisticsNetworkVersion RouteVersion,
    CargoConvoyStatus Status,
    SimulationTick AcceptedAtTick,
    SimulationTick UpdatedAtTick);

public readonly record struct CargoConvoyMember(EntityId Group);

public readonly record struct CargoConvoyEscort(
    EntityId Group,
    SimulationTick LastIssuedAtTick,
    Vector3 LastFollowTarget);

/// <summary>Only limits existing movement; never issues cargo or navigation orders.</summary>
public readonly record struct CargoConvoyMotionConstraint(float MaximumSpeed, bool Hold);

public readonly record struct CargoConvoyReadModel(
    EntityId Group,
    EntityId Leader,
    CargoConvoyStatus Status,
    int TruckCount,
    int EscortCount,
    LogisticsNodeId Origin,
    LogisticsNodeId Destination,
    SimulationTick Tick);

/// <summary>Coordinates independently dispatched trucks without taking their orders, routes or docks.</summary>
public sealed class CargoConvoySystem : ISimulationSystem
{
    private const float MaximumSeparationMeters = 32.0f;
    private const float RendezvousMeters = 22.0f;
    private const float DockReleaseMeters = 36.0f;
    private const float EscortFollowSpacingMeters = 12.0f;
    private const ulong EscortRefreshTicks = 12;

    private readonly LogisticsNetwork _network;
    private readonly List<EntityId> _members = new();
    private readonly List<EntityId> _escorts = new();
    private readonly List<EntityId> _groups = new();

    public CargoConvoySystem(LogisticsNetwork network) =>
        _network = network ?? throw new ArgumentNullException(nameof(network));

    public SimulationPhase Phase => SimulationPhase.NavigationRequests;

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _groups.Clear();
        foreach (EntityId group in context.Entities.Query<CargoConvoyGroup>(
                     QueryIterationOrder.StableByEntityIndex))
            _groups.Add(group);

        foreach (EntityId groupId in _groups)
        {
            if (!context.Entities.TryGetComponent(groupId, out CargoConvoyGroup group))
                continue;

            _members.Clear();
            foreach (EntityId truck in context.Entities.Query<CargoConvoyMember>(
                         QueryIterationOrder.StableByEntityIndex))
                if (context.Entities.GetComponent<CargoConvoyMember>(truck).Group == groupId)
                    _members.Add(truck);

            _escorts.Clear();
            foreach (EntityId unit in context.Entities.Query<CargoConvoyEscort>(
                         QueryIterationOrder.StableByEntityIndex))
                if (context.Entities.GetComponent<CargoConvoyEscort>(unit).Group == groupId)
                    _escorts.Add(unit);

            if (group.RouteVersion != _network.Version)
            {
                Dissolve(context, groupId);
                continue;
            }

            for (int i = _members.Count - 1; i >= 0; i--)
                if (!CargoConvoyEligibility.TryGetTruck(context.Entities,
                        group.Owner, _members[i], group.Origin, group.Destination,
                        group.RouteVersion, out _))
                {
                    DetachTruck(context.Entities, _members[i]);
                    _members.RemoveAt(i);
                }

            if (_members.Count < 2)
            {
                Dissolve(context, groupId);
                continue;
            }

            EntityId leader = _members[0];
            if (!_network.TryGetNode(group.Destination, out LogisticsNode destination) ||
                !context.Entities.TryGetComponent(leader, out WorldTransform leaderTransform))
            {
                Dissolve(context, groupId);
                continue;
            }

            float maximumDistanceSquared = 0;
            float slowestSpeed = float.PositiveInfinity;
            bool nearDock = false;
            int segment = -1;
            foreach (EntityId truck in _members)
            {
                var transform = context.Entities.GetComponent<WorldTransform>(truck);
                var movement = context.Entities.GetComponent<GroundMovement>(truck);
                var route = context.Entities.GetComponent<CargoTransportRouteState>(truck);
                if (segment < 0) segment = route.NextSegmentIndex;
                if (Math.Abs(route.NextSegmentIndex - segment) > 1)
                {
                    nearDock = true;
                    break;
                }
                float separation = Vector3.DistanceSquared(
                    leaderTransform.Position, transform.Position);
                maximumDistanceSquared = Math.Max(maximumDistanceSquared, separation);
                slowestSpeed = Math.Min(slowestSpeed, movement.MaximumSpeed);
                if (context.Entities.HasComponent<VehicleDockVisit>(truck) ||
                    context.Entities.TryGetComponent(truck, out SupplyMovementConstraint movementSupply) &&
                    !movementSupply.CanMove ||
                    context.Entities.HasComponent<CargoFuelServiceState>(truck) ||
                    context.Entities.HasComponent<ResupplyOrder>(truck) ||
                    context.Entities.HasComponent<SupplyRescueAssignment>(truck))
                {
                    nearDock = true;
                    break;
                }
                var next = route.Route!.Segments[route.NextSegmentIndex].To;
                if (_network.TryGetNode(next, out LogisticsNode node) &&
                    context.Entities.HasComponent<CompletedBuilding>(node.Entity) &&
                    Vector3.DistanceSquared(transform.Position, node.WorldPosition) <
                        DockReleaseMeters * DockReleaseMeters)
                {
                    nearDock = true;
                    break;
                }
            }

            // Physical bay access always wins over grouping and rendezvous.
            if (nearDock)
            {
                Dissolve(context, groupId);
                continue;
            }

            bool holding = maximumDistanceSquared >
                MaximumSeparationMeters * MaximumSeparationMeters;
            var status = group.Leader != leader ? CargoConvoyStatus.Disrupted :
                holding ? CargoConvoyStatus.Holding :
                maximumDistanceSquared > RendezvousMeters * RendezvousMeters
                    ? CargoConvoyStatus.Rendezvous : CargoConvoyStatus.Moving;
            foreach (EntityId truck in _members)
            {
                // A leader can briefly yield to distant followers; the convoy
                // never owns the existing cargo order or issues a movement target.
                var constraint = new CargoConvoyMotionConstraint(
                    slowestSpeed, holding && truck == leader);
                if (context.Entities.HasComponent<CargoConvoyMotionConstraint>(truck))
                    context.Entities.SetComponent(truck, constraint);
                else
                    context.Entities.AddComponent(truck, constraint);
            }

            for (int i = _escorts.Count - 1; i >= 0; i--)
            {
                EntityId escort = _escorts[i];
                if (!CargoConvoyEligibility.IsEscort(context.Entities, group.Owner, escort) ||
                    context.Entities.HasComponent<CombatOrderState>(escort) ||
                    context.Entities.HasComponent<ResupplyOrder>(escort) ||
                    context.Entities.HasComponent<SupplyRescueAssignment>(escort))
                {
                    DetachEscort(context, escort);
                    _escorts.RemoveAt(i);
                    continue;
                }

                var assignment = context.Entities.GetComponent<CargoConvoyEscort>(escort);
                if (assignment.LastIssuedAtTick != SimulationTick.Zero &&
                    TacticalCommandUtilities.TryGetMovementIntent(context, escort, out MovementOrder current) &&
                    current.AcceptedAtTick != assignment.LastIssuedAtTick)
                {
                    DetachEscort(context, escort);
                    _escorts.RemoveAt(i);
                    continue;
                }

                Vector3 approach = destination.WorldPosition - leaderTransform.Position;
                approach.Y = 0;
                Vector3 behind = approach.LengthSquared() > 0.0001f
                    ? -Vector3.Normalize(approach) : -Vector3.UnitZ;
                Vector3 target = leaderTransform.Position +
                    behind * (EscortFollowSpacingMeters + i * 6.0f);

                if (assignment.LastIssuedAtTick != SimulationTick.Zero &&
                    (context.Tick.Value < assignment.LastIssuedAtTick.Value + EscortRefreshTicks ||
                     Vector3.DistanceSquared(target, assignment.LastFollowTarget) < 100))
                    continue;

                var command = new MoveEntitiesCommand(group.Owner, [escort], target, context.Tick);
                command.Execute(context);
                if (command.AcceptedTargetCount == 1)
                    context.Entities.SetComponent(escort,
                        assignment with { LastIssuedAtTick = context.Tick,
                            LastFollowTarget = target });
                else
                {
                    DetachEscort(context, escort);
                    _escorts.RemoveAt(i);
                }
            }

            context.Entities.SetComponent(groupId,
                group with { Leader = leader, Status = status,
                    UpdatedAtTick = context.Tick });
        }
    }

    public IReadOnlyList<CargoConvoyReadModel> CaptureOwned(
        EntityRegistry entities, PlayerId owner, SimulationTick tick)
    {
        ArgumentNullException.ThrowIfNull(entities);
        var result = new List<CargoConvoyReadModel>();
        foreach (EntityId id in entities.Query<CargoConvoyGroup>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            CargoConvoyGroup group = entities.GetComponent<CargoConvoyGroup>(id);
            if (group.Owner != owner) continue;
            int trucks = 0, escorts = 0;
            foreach (EntityId entity in entities.Query<CargoConvoyMember>())
                if (entities.GetComponent<CargoConvoyMember>(entity).Group == id)
                    trucks++;
            foreach (EntityId entity in entities.Query<CargoConvoyEscort>())
                if (entities.GetComponent<CargoConvoyEscort>(entity).Group == id)
                    escorts++;
            if (result.Count >= 64) break;
            result.Add(new CargoConvoyReadModel(
                id, group.Leader, group.Status, trucks, escorts,
                group.Origin, group.Destination, tick));
        }
        return Array.AsReadOnly(result.ToArray());
    }

    public static void Dissolve(SimulationContext context, EntityId groupId)
    {
        // Enumerate a fixed identity copy: detaching removes the queried component.
        EntityId[] trucks = context.Entities.Query<CargoConvoyMember>(
            QueryIterationOrder.StableByEntityIndex).ToArray();
        foreach (EntityId truck in trucks)
            if (context.Entities.TryGetComponent(truck, out CargoConvoyMember member) &&
                member.Group == groupId)
                DetachTruck(context.Entities, truck);
        EntityId[] escorts = context.Entities.Query<CargoConvoyEscort>(
            QueryIterationOrder.StableByEntityIndex).ToArray();
        foreach (EntityId escort in escorts)
            if (context.Entities.TryGetComponent(escort, out CargoConvoyEscort member) &&
                member.Group == groupId)
                DetachEscort(context, escort);
        if (context.Entities.IsAlive(groupId))
            context.Entities.DestroyEntity(groupId);
    }

    private static void DetachTruck(EntityRegistry entities, EntityId entity)
    {
        entities.RemoveComponent<CargoConvoyMember>(entity);
        if (entities.HasComponent<CargoConvoyMotionConstraint>(entity))
            entities.RemoveComponent<CargoConvoyMotionConstraint>(entity);
    }

    private static void DetachEscort(SimulationContext context, EntityId escort)
    {
        if (!context.Entities.TryGetComponent(escort, out CargoConvoyEscort assignment))
            return;
        context.Entities.RemoveComponent<CargoConvoyEscort>(escort);
        // Never cancel a newer manual/tactical order.
        if (assignment.LastIssuedAtTick != SimulationTick.Zero &&
            TacticalCommandUtilities.TryGetMovementIntent(context, escort, out MovementOrder order) &&
            order.AcceptedAtTick == assignment.LastIssuedAtTick)
            TacticalCommandUtilities.ClearMovementIntent(context, escort);
    }
}

internal static class CargoConvoyEligibility
{
    public static bool TryGetTruck(
        EntityRegistry entities, PlayerId issuer,
        EntityId entity, LogisticsNodeId origin, LogisticsNodeId destination,
        LogisticsNetworkVersion networkVersion, out CargoTransportRouteState route)
    {
        route = default;
        if (!entities.TryGetComponent(entity, out ControllableEntity control) ||
            !control.IsControllable || control.Owner != issuer ||
            !entities.TryGetComponent(entity, out CargoTransport transport) ||
            transport.Owner != issuer ||
            !entities.TryGetComponent(entity, out CargoTransportOrder order) ||
            order.Origin != origin || order.Destination != destination ||
            !entities.TryGetComponent(entity, out CargoTransportRuntimeState state) ||
            state.Lifecycle != CargoTransportLifecycleState.ToDestination ||
            state.ObservedNetworkVersion != networkVersion ||
            !entities.TryGetComponent(entity, out CargoTransportRouteState candidate) ||
            candidate.Route is null || candidate.Route.Version != networkVersion ||
            candidate.NextSegmentIndex < 0 ||
            candidate.NextSegmentIndex >= candidate.Route.Segments.Count ||
            !entities.HasComponent<GroundMovement>(entity) ||
            !entities.HasComponent<GroundMovementState>(entity) ||
            !entities.HasComponent<WorldTransform>(entity) ||
            !entities.HasComponent<NavigationAgent>(entity) ||
            entities.HasComponent<VehicleDockVisit>(entity) ||
            entities.HasComponent<CargoFuelServiceState>(entity) ||
            entities.HasComponent<ResupplyOrder>(entity) ||
            entities.HasComponent<SupplyRescueAssignment>(entity))
            return false;
        route = candidate;
        return true;
    }

    public static bool IsEscort(EntityRegistry entities, PlayerId issuer, EntityId entity) =>
        entities.TryGetComponent(entity, out ControllableEntity control) &&
        control.IsControllable && control.Owner == issuer &&
        entities.HasComponent<Combatant>(entity) &&
        entities.HasComponent<WorldTransform>(entity) &&
        entities.HasComponent<GroundMovement>(entity) &&
        entities.HasComponent<GroundMovementState>(entity) &&
        entities.HasComponent<NavigationAgent>(entity) &&
        !entities.HasComponent<CargoTransport>(entity);
}
