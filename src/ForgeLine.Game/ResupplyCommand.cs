using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public sealed class ResupplyCommand : ISimulationCommand
{
    private readonly EntityId[] _targets;

    public ResupplyCommand(
        PlayerId issuer,
        ReadOnlySpan<EntityId> targets,
        SimulationTick submittedAtTick)
    {
        if (!issuer.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(issuer));
        }

        if (targets.IsEmpty)
        {
            throw new ArgumentException(
                "A resupply command requires at least one target entity.",
                nameof(targets));
        }

        Issuer = issuer;
        SubmittedAtTick = submittedAtTick;
        _targets = targets.ToArray();
    }

    public PlayerId Issuer { get; }

    public SimulationTick SubmittedAtTick { get; }

    public ReadOnlySpan<EntityId> Targets => _targets;

    public int AcceptedTargetCount { get; private set; }

    public int RejectedTargetCount { get; private set; }

    public SimulationTick ExecutedAtTick { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        int accepted = 0;
        int rejected = 0;

        for (int index = 0; index < _targets.Length; index++)
        {
            EntityId entity = _targets[index];

            if (!IsEligibleTarget(context, entity) ||
                !TryFindNearestProvider(
                    context,
                    entity,
                    out EntityId provider,
                    out WorldTransform providerTransform))
            {
                rejected++;
                continue;
            }

            ClearFormationMembership(context, entity);

            var resupplyOrder =
                new ResupplyOrder(
                    provider,
                    SubmittedAtTick,
                    context.Tick);

            if (context.Entities.HasComponent<ResupplyOrder>(entity))
            {
                context.Entities.SetComponent(entity, resupplyOrder);
            }
            else
            {
                context.Entities.AddComponent(entity, resupplyOrder);
            }

            var movementOrder =
                new MovementOrder(
                    Issuer,
                    providerTransform.Position,
                    SubmittedAtTick,
                    context.Tick);

            if (context.Entities.HasComponent<MovementOrder>(entity))
            {
                context.Entities.SetComponent(entity, movementOrder);
            }
            else
            {
                context.Entities.AddComponent(entity, movementOrder);
            }

            accepted++;
        }

        AcceptedTargetCount = accepted;
        RejectedTargetCount = rejected;
        ExecutedAtTick = context.Tick;
    }

    private bool IsEligibleTarget(
        SimulationContext context,
        EntityId entity)
    {
        if (!context.Entities.TryGetComponent(
                entity,
                out ControllableEntity controllable) ||
            !controllable.IsControllable ||
            controllable.Owner != Issuer ||
            !context.Entities.HasComponent<WorldTransform>(entity) ||
            !context.Entities.HasComponent<GroundMovement>(entity))
        {
            return false;
        }

        return
            context.Entities.HasComponent<UnitFuelState>(entity) ||
            context.Entities.HasComponent<AmmunitionState>(entity);
    }

    private bool TryFindNearestProvider(
        SimulationContext context,
        EntityId target,
        out EntityId providerEntity,
        out WorldTransform providerTransform)
    {
        providerEntity = EntityId.Invalid;
        providerTransform = default;

        WorldTransform targetTransform =
            context.Entities.GetComponent<WorldTransform>(target);
        float bestDistanceSquared = float.PositiveInfinity;

        foreach (EntityId candidate in
                 context.Entities.Query<SupplyProvider>(
                     ForgeLine.Ecs.QueryIterationOrder.StableByEntityIndex))
        {
            if (candidate == target ||
                !context.Entities.TryGetComponent(
                    candidate,
                    out SupplyProvider provider) ||
                !provider.Enabled ||
                provider.Owner != Issuer ||
                !context.Entities.TryGetComponent(
                    candidate,
                    out WorldTransform transform))
            {
                continue;
            }

            if (context.Entities.TryGetComponent(
                    candidate,
                    out SupplyDepot depot) &&
                depot.State != SupplyDepotState.Operational)
            {
                continue;
            }

            float distanceSquared =
                HorizontalDistanceSquared(
                    targetTransform.Position,
                    transform.Position);

            if (!providerEntity.IsValid ||
                distanceSquared < bestDistanceSquared ||
                (distanceSquared == bestDistanceSquared &&
                 candidate < providerEntity))
            {
                providerEntity = candidate;
                providerTransform = transform;
                bestDistanceSquared = distanceSquared;
            }
        }

        return providerEntity.IsValid;
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

    private static float HorizontalDistanceSquared(
        System.Numerics.Vector3 left,
        System.Numerics.Vector3 right)
    {
        float x = left.X - right.X;
        float z = left.Z - right.Z;
        return x * x + z * z;
    }
}
