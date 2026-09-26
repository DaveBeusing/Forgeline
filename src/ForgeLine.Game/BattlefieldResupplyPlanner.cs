using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
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
        out EntityId providerEntity) =>
        TryIssueNearestProviderOrder(
            context,
            inventories: null,
            recipient,
            owner,
            submittedAtTick,
            BattlefieldSupplyResource.None,
            out providerEntity);

    public static bool TryIssueNearestProviderOrder(
        SimulationContext context,
        InventoryStore? inventories,
        EntityId recipient,
        PlayerId owner,
        SimulationTick submittedAtTick,
        BattlefieldSupplyResource requiredResources,
        out EntityId providerEntity)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (inventories is null)
        {
            if (requiredResources != BattlefieldSupplyResource.None)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requiredResources));
            }
        }
        else if (requiredResources == BattlefieldSupplyResource.None ||
                 (requiredResources & ~BattlefieldSupplyResource.All) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requiredResources));
        }

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
        SupplyProvider selectedProvider = default;
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
                (inventories is not null &&
                 (!inventories.Contains(provider.InventoryId) ||
                  !HasRequiredStock(
                      inventories,
                      provider.InventoryId,
                      requiredResources))) ||
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
                selectedProvider =
                    provider;
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

        BattlefieldSupplyResource requestedResources =
            requiredResources == BattlefieldSupplyResource.None
                ? BattlefieldSupplyResource.All
                : requiredResources;
        var resupplyOrder =
            new ResupplyOrder(
                providerEntity,
                submittedAtTick,
                context.Tick)
            {
                RequestedResources = requestedResources
            };

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

        Vector3 approachPosition =
            ResolveProviderApproachPosition(
                recipientTransform.Position,
                providerTransform.Position,
                selectedProvider.ResupplyRangeMeters);

        var movementOrder =
            new MovementOrder(
                owner,
                approachPosition,
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

    private static bool HasRequiredStock(
        InventoryStore inventories,
        InventoryId inventory,
        BattlefieldSupplyResource requiredResources)
    {
        const double QuantityEpsilon = 0.000000001;

        if (requiredResources.HasFlag(
                BattlefieldSupplyResource.Fuel) &&
            inventories.GetAvailableQuantity(
                inventory,
                ResourceIds.Fuel) <= QuantityEpsilon)
        {
            return false;
        }

        if (requiredResources.HasFlag(
                BattlefieldSupplyResource.Ammunition) &&
            inventories.GetAvailableQuantity(
                inventory,
                ResourceIds.Ammunition) <= QuantityEpsilon)
        {
            return false;
        }

        return true;
    }

    private static Vector3 ResolveProviderApproachPosition(
        Vector3 recipientPosition,
        Vector3 providerPosition,
        float resupplyRangeMeters)
    {
        Vector3 offset =
            recipientPosition -
            providerPosition;
        offset.Y = 0.0f;

        float distance =
            offset.Length();
        float approachRadius =
            MathF.Max(
                1.0f,
                resupplyRangeMeters * 0.75f);

        if (distance <=
            resupplyRangeMeters)
        {
            return recipientPosition;
        }

        Vector3 direction =
            distance > 0.0001f
                ? offset / distance
                : Vector3.UnitZ;

        return providerPosition +
            direction * approachRadius;
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
