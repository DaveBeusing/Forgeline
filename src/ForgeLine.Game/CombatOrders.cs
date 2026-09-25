using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public sealed class AttackCommand : ISimulationCommand
{
    private readonly EntityId[] _units;

    public AttackCommand(
        PlayerId issuer,
        ReadOnlySpan<EntityId> units,
        EntityId target,
        SimulationTick submittedAtTick,
        float pursuitLeashMeters = 160.0f)
    {
        TacticalCommandUtilities.ValidateIssuerAndUnits(
            issuer,
            units);

        if (!target.IsValid)
        {
            throw new ArgumentException(
                "Attack commands require a valid target entity.",
                nameof(target));
        }

        TacticalCommandUtilities.ValidateLeash(
            pursuitLeashMeters);

        Issuer = issuer;
        Target = target;
        SubmittedAtTick = submittedAtTick;
        PursuitLeashMeters = pursuitLeashMeters;
        _units = units.ToArray();
    }

    public PlayerId Issuer { get; }

    public EntityId Target { get; }

    public SimulationTick SubmittedAtTick { get; }

    public float PursuitLeashMeters { get; }

    public int AcceptedTargetCount { get; private set; }

    public int RejectedTargetCount { get; private set; }

    public EntityId CreatedCombatGroup { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        List<EntityId> accepted =
            TacticalCommandUtilities.FilterOwnedCombatUnits(
                context,
                Issuer,
                _units,
                out int rejected);

        for (int index = 0; index < accepted.Count; index++)
        {
            EntityId entity = accepted[index];
            WorldTransform transform =
                context.Entities.GetComponent<WorldTransform>(
                    entity);

            TacticalCommandUtilities.ClearMovementIntent(
                context,
                entity);
            TacticalCommandUtilities.SetOrder(
                context,
                entity,
                new CombatOrderState(
                    CombatOrderKind.Attack,
                    Issuer,
                    Target,
                    Vector3.Zero,
                    hasDestination: false,
                    transform.Position,
                    PursuitLeashMeters,
                    FormationTemplate.Compact,
                    SubmittedAtTick,
                    context.Tick));
        }

        CreatedCombatGroup =
            TacticalCommandUtilities.CreateCombatGroup(
                context,
                accepted,
                CombatOrderKind.Attack,
                Issuer,
                Vector3.Zero,
                hasDestination: false,
                Target,
                FormationTemplate.Compact,
                PursuitLeashMeters);

        AcceptedTargetCount = accepted.Count;
        RejectedTargetCount = rejected;
    }
}

public sealed class AttackMoveCommand : ISimulationCommand
{
    private readonly EntityId[] _units;

    public AttackMoveCommand(
        PlayerId issuer,
        ReadOnlySpan<EntityId> units,
        Vector3 destination,
        SimulationTick submittedAtTick,
        FormationTemplate formation = FormationTemplate.Compact,
        float pursuitLeashMeters = 80.0f)
    {
        TacticalCommandUtilities.ValidateIssuerAndUnits(
            issuer,
            units);
        TacticalCommandUtilities.ValidatePosition(
            destination,
            nameof(destination));
        TacticalCommandUtilities.ValidateLeash(
            pursuitLeashMeters);

        if (!Enum.IsDefined(formation))
        {
            throw new ArgumentOutOfRangeException(nameof(formation));
        }

        Issuer = issuer;
        Destination = destination;
        SubmittedAtTick = submittedAtTick;
        Formation = formation;
        PursuitLeashMeters = pursuitLeashMeters;
        _units = units.ToArray();
    }

    public PlayerId Issuer { get; }

    public Vector3 Destination { get; }

    public SimulationTick SubmittedAtTick { get; }

    public FormationTemplate Formation { get; }

    public float PursuitLeashMeters { get; }

    public int AcceptedTargetCount { get; private set; }

    public int RejectedTargetCount { get; private set; }

    public EntityId CreatedCombatGroup { get; private set; }

    public EntityId CreatedMovementGroup { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        List<EntityId> accepted =
            TacticalCommandUtilities.FilterOwnedCombatUnits(
                context,
                Issuer,
                _units,
                out int rejected);

        for (int index = 0; index < accepted.Count; index++)
        {
            EntityId entity = accepted[index];
            WorldTransform transform =
                context.Entities.GetComponent<WorldTransform>(
                    entity);

            TacticalCommandUtilities.SetOrder(
                context,
                entity,
                new CombatOrderState(
                    CombatOrderKind.AttackMove,
                    Issuer,
                    EntityId.Invalid,
                    Destination,
                    hasDestination: true,
                    transform.Position,
                    PursuitLeashMeters,
                    Formation,
                    SubmittedAtTick,
                    context.Tick));
        }

        CreatedCombatGroup =
            TacticalCommandUtilities.CreateCombatGroup(
                context,
                accepted,
                CombatOrderKind.AttackMove,
                Issuer,
                Destination,
                hasDestination: true,
                EntityId.Invalid,
                Formation,
                PursuitLeashMeters);

        if (accepted.Count > 0)
        {
            var movement =
                new MoveEntitiesCommand(
                    Issuer,
                    accepted.ToArray(),
                    Destination,
                    SubmittedAtTick,
                    Formation);
            movement.Execute(context);
            CreatedMovementGroup =
                movement.CreatedMovementGroup;
        }

        AcceptedTargetCount = accepted.Count;
        RejectedTargetCount = rejected;
    }
}

public sealed class StopCombatCommand : ISimulationCommand
{
    private readonly EntityId[] _units;

    public StopCombatCommand(
        PlayerId issuer,
        ReadOnlySpan<EntityId> units,
        SimulationTick submittedAtTick)
    {
        TacticalCommandUtilities.ValidateIssuerAndUnits(
            issuer,
            units);

        Issuer = issuer;
        SubmittedAtTick = submittedAtTick;
        _units = units.ToArray();
    }

    public PlayerId Issuer { get; }

    public SimulationTick SubmittedAtTick { get; }

    public int AcceptedTargetCount { get; private set; }

    public int RejectedTargetCount { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        List<EntityId> accepted =
            TacticalCommandUtilities.FilterOwnedCombatUnits(
                context,
                Issuer,
                _units,
                out int rejected);

        for (int index = 0; index < accepted.Count; index++)
        {
            EntityId entity = accepted[index];
            WorldTransform transform =
                context.Entities.GetComponent<WorldTransform>(
                    entity);

            TacticalCommandUtilities.ClearMovementIntent(
                context,
                entity);
            TacticalCommandUtilities.SetOrder(
                context,
                entity,
                new CombatOrderState(
                    CombatOrderKind.Stop,
                    Issuer,
                    EntityId.Invalid,
                    transform.Position,
                    hasDestination: false,
                    transform.Position,
                    pursuitLeashMeters: 0.0f,
                    FormationTemplate.Compact,
                    SubmittedAtTick,
                    context.Tick));
        }

        AcceptedTargetCount = accepted.Count;
        RejectedTargetCount = rejected;
    }
}

public sealed class HoldPositionCommand : ISimulationCommand
{
    private readonly EntityId[] _units;

    public HoldPositionCommand(
        PlayerId issuer,
        ReadOnlySpan<EntityId> units,
        SimulationTick submittedAtTick)
    {
        TacticalCommandUtilities.ValidateIssuerAndUnits(
            issuer,
            units);

        Issuer = issuer;
        SubmittedAtTick = submittedAtTick;
        _units = units.ToArray();
    }

    public PlayerId Issuer { get; }

    public SimulationTick SubmittedAtTick { get; }

    public int AcceptedTargetCount { get; private set; }

    public int RejectedTargetCount { get; private set; }

    public EntityId CreatedCombatGroup { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        List<EntityId> accepted =
            TacticalCommandUtilities.FilterOwnedCombatUnits(
                context,
                Issuer,
                _units,
                out int rejected);

        for (int index = 0; index < accepted.Count; index++)
        {
            EntityId entity = accepted[index];
            WorldTransform transform =
                context.Entities.GetComponent<WorldTransform>(
                    entity);

            TacticalCommandUtilities.ClearMovementIntent(
                context,
                entity);
            TacticalCommandUtilities.SetOrder(
                context,
                entity,
                new CombatOrderState(
                    CombatOrderKind.HoldPosition,
                    Issuer,
                    EntityId.Invalid,
                    transform.Position,
                    hasDestination: false,
                    transform.Position,
                    pursuitLeashMeters: 0.0f,
                    FormationTemplate.Compact,
                    SubmittedAtTick,
                    context.Tick));
        }

        CreatedCombatGroup =
            TacticalCommandUtilities.CreateCombatGroup(
                context,
                accepted,
                CombatOrderKind.HoldPosition,
                Issuer,
                Vector3.Zero,
                hasDestination: false,
                EntityId.Invalid,
                FormationTemplate.Compact,
                pursuitLeashMeters: 0.0f);

        AcceptedTargetCount = accepted.Count;
        RejectedTargetCount = rejected;
    }
}

public sealed class RetreatCommand : ISimulationCommand
{
    private readonly EntityId[] _units;

    public RetreatCommand(
        PlayerId issuer,
        ReadOnlySpan<EntityId> units,
        Vector3 destination,
        SimulationTick submittedAtTick,
        FormationTemplate formation = FormationTemplate.Column)
    {
        TacticalCommandUtilities.ValidateIssuerAndUnits(
            issuer,
            units);
        TacticalCommandUtilities.ValidatePosition(
            destination,
            nameof(destination));

        if (!Enum.IsDefined(formation))
        {
            throw new ArgumentOutOfRangeException(nameof(formation));
        }

        Issuer = issuer;
        Destination = destination;
        SubmittedAtTick = submittedAtTick;
        Formation = formation;
        _units = units.ToArray();
    }

    public PlayerId Issuer { get; }

    public Vector3 Destination { get; }

    public SimulationTick SubmittedAtTick { get; }

    public FormationTemplate Formation { get; }

    public int AcceptedTargetCount { get; private set; }

    public int RejectedTargetCount { get; private set; }

    public EntityId CreatedCombatGroup { get; private set; }

    public EntityId CreatedMovementGroup { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        List<EntityId> accepted =
            TacticalCommandUtilities.FilterOwnedCombatUnits(
                context,
                Issuer,
                _units,
                out int rejected);

        for (int index = 0; index < accepted.Count; index++)
        {
            EntityId entity = accepted[index];
            WorldTransform transform =
                context.Entities.GetComponent<WorldTransform>(
                    entity);

            TacticalCommandUtilities.SetOrder(
                context,
                entity,
                new CombatOrderState(
                    CombatOrderKind.Retreat,
                    Issuer,
                    EntityId.Invalid,
                    Destination,
                    hasDestination: true,
                    transform.Position,
                    pursuitLeashMeters: 0.0f,
                    Formation,
                    SubmittedAtTick,
                    context.Tick));
        }

        CreatedCombatGroup =
            TacticalCommandUtilities.CreateCombatGroup(
                context,
                accepted,
                CombatOrderKind.Retreat,
                Issuer,
                Destination,
                hasDestination: true,
                EntityId.Invalid,
                Formation,
                pursuitLeashMeters: 0.0f);

        if (accepted.Count > 0)
        {
            var movement =
                new MoveEntitiesCommand(
                    Issuer,
                    accepted.ToArray(),
                    Destination,
                    SubmittedAtTick,
                    Formation);
            movement.Execute(context);
            CreatedMovementGroup =
                movement.CreatedMovementGroup;
        }

        AcceptedTargetCount = accepted.Count;
        RejectedTargetCount = rejected;
    }
}

internal static class TacticalCommandUtilities
{
    public static void ValidateIssuerAndUnits(
        PlayerId issuer,
        ReadOnlySpan<EntityId> units)
    {
        if (!issuer.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(issuer));
        }

        if (units.IsEmpty)
        {
            throw new ArgumentException(
                "Combat commands require at least one unit.",
                nameof(units));
        }
    }

    public static void ValidatePosition(
        Vector3 position,
        string parameterName)
    {
        if (!float.IsFinite(position.X) ||
            !float.IsFinite(position.Y) ||
            !float.IsFinite(position.Z))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    public static void ValidateLeash(float leashMeters)
    {
        if (!float.IsFinite(leashMeters) ||
            leashMeters < 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leashMeters));
        }
    }

    public static List<EntityId> FilterOwnedCombatUnits(
        SimulationContext context,
        PlayerId issuer,
        ReadOnlySpan<EntityId> requested,
        out int rejected)
    {
        var accepted =
            new List<EntityId>(requested.Length);
        var unique =
            new HashSet<EntityId>();
        rejected = 0;

        for (int index = 0; index < requested.Length; index++)
        {
            EntityId entity = requested[index];

            if (!unique.Add(entity) ||
                !context.Entities.TryGetComponent(
                    entity,
                    out ControllableEntity controllable) ||
                !controllable.IsControllable ||
                controllable.Owner != issuer ||
                !context.Entities.HasComponent<WorldTransform>(entity) ||
                !context.Entities.HasComponent<Combatant>(entity))
            {
                rejected++;
                continue;
            }

            accepted.Add(entity);
        }

        return accepted;
    }

    public static void SetOrder(
        SimulationContext context,
        EntityId entity,
        in CombatOrderState order)
    {
        ClearCombatGroupMembership(
            context,
            entity);
        RemoveIfPresent<ResupplyOrder>(
            context,
            entity);

        if (context.Entities.HasComponent<CombatOrderState>(entity))
        {
            context.Entities.SetComponent(
                entity,
                order);
        }
        else
        {
            context.Entities.AddComponent(
                entity,
                order);
        }
    }

    public static EntityId CreateCombatGroup(
        SimulationContext context,
        List<EntityId> members,
        CombatOrderKind kind,
        PlayerId issuer,
        Vector3 destination,
        bool hasDestination,
        EntityId explicitTarget,
        FormationTemplate formation,
        float pursuitLeashMeters)
    {
        if (members.Count <= 1)
        {
            return EntityId.Invalid;
        }

        EntityId group =
            context.Entities.CreateEntity();
        context.Entities.AddComponent(
            group,
            new CombatGroupIntent(
                kind,
                issuer,
                destination,
                hasDestination,
                explicitTarget,
                formation,
                members.Count,
                pursuitLeashMeters,
                context.Tick));

        for (int index = 0; index < members.Count; index++)
        {
            EntityId member = members[index];
            ClearCombatGroupMembership(
                context,
                member);
            context.Entities.AddComponent(
                member,
                new CombatGroupMember(group));
        }

        return group;
    }

    public static void ClearMovementIntent(
        SimulationContext context,
        EntityId entity)
    {
        RemoveIfPresent<MovementOrder>(
            context,
            entity);
        RemoveIfPresent<NavigationPendingPath>(
            context,
            entity);
        RemoveIfPresent<NavigationRouteState>(
            context,
            entity);
        RemoveIfPresent<NavigationFailureState>(
            context,
            entity);
        RemoveIfPresent<MovementGroupMember>(
            context,
            entity);
        RemoveIfPresent<FormationMovementConstraint>(
            context,
            entity);
    }

    public static void ClearCombatGroupMembership(
        SimulationContext context,
        EntityId entity)
    {
        RemoveIfPresent<CombatGroupMember>(
            context,
            entity);
    }

    public static void RemoveIfPresent<T>(
        SimulationContext context,
        EntityId entity)
        where T : struct
    {
        if (context.Entities.HasComponent<T>(entity))
        {
            context.Entities.RemoveComponent<T>(entity);
        }
    }
}
