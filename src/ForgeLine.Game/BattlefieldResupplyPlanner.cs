using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public static class BattlefieldResupplyPlanner
{
    public static bool TryIssueNearestProviderOrder(
        SimulationContext context,
        EntityId recipient,
        PlayerId owner,
        SimulationTick submittedAtTick,
        out EntityId providerEntity)
    {
        ArgumentNullException.ThrowIfNull(context);

        providerEntity = EntityId.Invalid;

        if (!owner.IsSpecified ||
            !context.Entities.IsAlive(recipient) ||
            !context.Entities.TryGetComponent(
                recipient,
                out WorldTransform recipientTransform))
        {
            return false;
        }

        WorldTransform providerTransform = default;
        float bestDistanceSquared =
            float.PositiveInfinity;

        foreach (EntityId candidate in
                 context.Entities.Query<SupplyProvider>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (candidate == recipient ||
                !context.Entities.TryGetComponent(
                    candidate,
                    out SupplyProvider provider) ||
                !provider.Enabled ||
                provider.Owner != owner ||
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
                    recipientTransform.Position,
                    transform.Position);

            if (!providerEntity.IsValid ||
                distanceSquared < bestDistanceSquared ||
                (distanceSquared == bestDistanceSquared &&
                 candidate < providerEntity))
            {
                providerEntity =
                    candidate;
                providerTransform =
                    transform;
                bestDistanceSquared =
                    distanceSquared;
            }
        }

        if (!providerEntity.IsValid)
        {
            return false;
        }

        ClearFormationMovement(
            context,
            recipient);

        var resupplyOrder =
            new ResupplyOrder(
                providerEntity,
                submittedAtTick,
                context.Tick);

        if (context.Entities.HasComponent<ResupplyOrder>(
                recipient))
        {
            context.Entities.SetComponent(
                recipient,
                resupplyOrder);
        }
        else
        {
            context.Entities.AddComponent(
                recipient,
                resupplyOrder);
        }

        var movementOrder =
            new MovementOrder(
                owner,
                providerTransform.Position,
                submittedAtTick,
                context.Tick);

        if (context.Entities.HasComponent<MovementOrder>(
                recipient))
        {
            context.Entities.SetComponent(
                recipient,
                movementOrder);
        }
        else
        {
            context.Entities.AddComponent(
                recipient,
                movementOrder);
        }

        return true;
    }

    private static void ClearFormationMovement(
        SimulationContext context,
        EntityId entity)
    {
        TacticalCommandUtilities.RemoveIfPresent<
            MovementGroupMember>(
                context,
                entity);
        TacticalCommandUtilities.RemoveIfPresent<
            FormationMovementConstraint>(
                context,
                entity);
        TacticalCommandUtilities.RemoveIfPresent<
            NavigationPendingPath>(
                context,
                entity);
        TacticalCommandUtilities.RemoveIfPresent<
            NavigationRouteState>(
                context,
                entity);
        TacticalCommandUtilities.RemoveIfPresent<
            NavigationFailureState>(
                context,
                entity);
    }

    private static float HorizontalDistanceSquared(
        Vector3 left,
        Vector3 right)
    {
        float x =
            left.X - right.X;
        float z =
            left.Z - right.Z;
        return x * x + z * z;
    }
}
