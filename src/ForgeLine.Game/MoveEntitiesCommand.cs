using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public sealed class MoveEntitiesCommand : ISimulationCommand
{
    private readonly EntityId[] _targets;
    private readonly bool _preserveCombatIntent;

    public MoveEntitiesCommand(
        PlayerId issuer,
        ReadOnlySpan<EntityId> targets,
        Vector3 worldTarget,
        SimulationTick submittedAtTick,
        FormationTemplate formation = FormationTemplate.Compact,
        bool preserveCombatIntent = false)
    {
        if (!issuer.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(issuer));
        }

        if (!float.IsFinite(worldTarget.X) ||
            !float.IsFinite(worldTarget.Y) ||
            !float.IsFinite(worldTarget.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(worldTarget));
        }

        if (targets.IsEmpty)
        {
            throw new ArgumentException(
                "A movement command requires at least one target entity.",
                nameof(targets));
        }

        if (!Enum.IsDefined(formation))
        {
            throw new ArgumentOutOfRangeException(nameof(formation));
        }

        Issuer = issuer;
        WorldTarget = worldTarget;
        SubmittedAtTick = submittedAtTick;
        Formation = formation;
        _preserveCombatIntent = preserveCombatIntent;
        _targets = targets.ToArray();
    }

    public PlayerId Issuer { get; }

    public Vector3 WorldTarget { get; }

    public SimulationTick SubmittedAtTick { get; }

    public FormationTemplate Formation { get; }

    public ReadOnlySpan<EntityId> Targets => _targets;

    public int AcceptedTargetCount { get; private set; }

    public int RejectedTargetCount { get; private set; }

    public SimulationTick ExecutedAtTick { get; private set; }

    public EntityId CreatedMovementGroup { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        int accepted = 0;
        int rejected = 0;
        var formationTargets = new List<EntityId>(_targets.Length);
        var formationTargetSet = new HashSet<EntityId>();
        var individualTargets = new List<EntityId>();

        for (int index = 0; index < _targets.Length; index++)
        {
            EntityId entity = _targets[index];

            if (!context.Entities.TryGetComponent(
                    entity,
                    out ControllableEntity controllable) ||
                !controllable.IsControllable ||
                controllable.Owner != Issuer ||
                controllable.Category ==
                    ControllableEntityCategory.Building)
            {
                rejected++;
                continue;
            }

            accepted++;

            if (!_preserveCombatIntent)
            {
                ClearCombatIntent(
                    context,
                    entity);
            }

            ClearFormationMembership(context, entity);

            if (IsMovementCapable(context, entity))
            {
                if (formationTargetSet.Add(entity))
                {
                    formationTargets.Add(entity);
                }
            }
            else
            {
                individualTargets.Add(entity);
            }
        }

        if (formationTargets.Count >= 2)
        {
            CreatedMovementGroup =
                CreateMovementGroup(context, formationTargets);
        }
        else
        {
            for (int index = 0; index < formationTargets.Count; index++)
            {
                individualTargets.Add(formationTargets[index]);
            }
        }

        for (int index = 0; index < individualTargets.Count; index++)
        {
            SetIndividualOrder(context, individualTargets[index]);
        }

        AcceptedTargetCount = accepted;
        RejectedTargetCount = rejected;
        ExecutedAtTick = context.Tick;
    }

    private EntityId CreateMovementGroup(
        SimulationContext context,
        List<EntityId> formationTargets)
    {
        EntityId group = context.Entities.CreateEntity();
        context.Entities.AddComponent(
            group,
            new MovementGroupOrder(
                Issuer,
                WorldTarget,
                Formation,
                SubmittedAtTick,
                context.Tick));
        context.Entities.AddComponent(
            group,
            MovementGroupState.Pending(context.Tick));

        for (int index = 0; index < formationTargets.Count; index++)
        {
            EntityId entity = formationTargets[index];

            if (context.Entities.HasComponent<MovementOrder>(entity))
            {
                context.Entities.RemoveComponent<MovementOrder>(entity);
            }

            context.Entities.AddComponent(
                entity,
                new MovementGroupMember(group));
        }

        return group;
    }

    private void SetIndividualOrder(
        SimulationContext context,
        EntityId entity)
    {
        var order = new MovementOrder(
            Issuer,
            WorldTarget,
            SubmittedAtTick,
            context.Tick);

        if (context.Entities.HasComponent<MovementOrder>(entity))
        {
            context.Entities.SetComponent(entity, order);
        }
        else
        {
            context.Entities.AddComponent(entity, order);
        }
    }

    private static bool IsMovementCapable(
        SimulationContext context,
        EntityId entity)
    {
        return context.Entities.HasComponent<WorldTransform>(entity) &&
               context.Entities.HasComponent<GroundMovement>(entity) &&
               context.Entities.HasComponent<GroundMovementState>(entity) &&
               context.Entities.HasComponent<NavigationAgent>(entity);
    }

    private static void ClearCombatIntent(
        SimulationContext context,
        EntityId entity)
    {
        TacticalCommandUtilities.RemoveIfPresent<CombatOrderState>(
            context,
            entity);
        TacticalCommandUtilities.RemoveIfPresent<TacticalCombatState>(
            context,
            entity);
        TacticalCommandUtilities.RemoveIfPresent<TacticalMovementConstraint>(
            context,
            entity);
        TacticalCommandUtilities.RemoveIfPresent<CombatGroupMember>(
            context,
            entity);
        TacticalCommandUtilities.RemoveIfPresent<AutoTargetState>(
            context,
            entity);
    }

    private static void ClearFormationMembership(
        SimulationContext context,
        EntityId entity)
    {
        if (context.Entities.HasComponent<MovementGroupMember>(entity))
        {
            context.Entities.RemoveComponent<MovementGroupMember>(entity);
        }

        if (context.Entities.HasComponent<FormationMovementConstraint>(entity))
        {
            context.Entities.RemoveComponent<FormationMovementConstraint>(entity);
        }
    }
}
