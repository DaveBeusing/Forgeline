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

        bool recipientCanReachProvider = false;

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

            bool canReachProvider = CanReachProvider(
                context, inventories, recipient, distanceSquared, provider.ResupplyRangeMeters);
            if (!canReachProvider &&
                !CanProviderReachImmobileRecipient(
                    context, inventories, candidate, recipient,
                    distanceSquared, provider.ResupplyRangeMeters))
            {
                continue;
            }

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
                recipientCanReachProvider = canReachProvider;
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

        if (recipientCanReachProvider)
        {
            Vector3 approachPosition =
                ResolveProviderApproachPosition(
                    recipientTransform.Position,
                    providerTransform.Position,
                    selectedProvider.ResupplyRangeMeters);

            SetMovementOrder(
                context,
                recipient,
                owner,
                approachPosition,
                submittedAtTick);
        }
        else
        {
            TacticalCommandUtilities.ClearMovementIntent(
                context,
                recipient);
            ClearFormationMovement(
                context,
                providerEntity);

            Vector3 providerApproachPosition =
                ResolveProviderApproachPosition(
                    providerTransform.Position,
                    recipientTransform.Position,
                    selectedProvider.ResupplyRangeMeters);

            SetMovementOrder(
                context,
                providerEntity,
                owner,
                providerApproachPosition,
                submittedAtTick);
        }

        return true;
    }

    internal static bool CanReachProvider(
        SimulationContext context,
        InventoryStore? inventories,
        EntityId recipient,
        float distanceSquared,
        float range)
    {
        if (distanceSquared <= range * range)
        {
            return true;
        }

        if (context.Entities.TryGetComponent(recipient, out SupplyMovementConstraint movement) &&
            !movement.CanMove)
        {
            return false;
        }

        if (inventories is null ||
            !context.Entities.TryGetComponent(recipient, out UnitFuelState fuel) ||
            fuel.ConsumptionPerMeter <= 0.0)
        {
            return true;
        }

        // This is only a direct-distance rejection bound. Navigation must still
        // establish a traversable route; passing it does not certify a detour.
        double requiredFuel = (MathF.Sqrt(distanceSquared) - range * 0.75f) * fuel.ConsumptionPerMeter;
        return inventories.Contains(fuel.InventoryId) &&
            inventories.GetAvailableQuantity(fuel.InventoryId, ResourceIds.Fuel) >= requiredFuel;
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

    internal static bool CanProviderReachImmobileRecipient(
        SimulationContext context,
        InventoryStore? inventories,
        EntityId provider,
        EntityId recipient,
        float distanceSquared,
        float range)
    {
        if (!context.Entities.HasComponent<SupplyTruck>(
                provider) ||
            context.Entities.HasComponent<ResupplyOrder>(provider) ||
            !context.Entities.HasComponent<GroundMovement>(
                provider))
        {
            return false;
        }

        // Propulsion uses UnitFuelState, never the SupplyTruck cargo inventory.
        if (!CanReachProvider(
                context, inventories, provider, distanceSquared, range))
        {
            return false;
        }

        foreach (EntityId candidate in
                 context.Entities.Query<ResupplyOrder>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (candidate == recipient)
            {
                continue;
            }

            ResupplyOrder order =
                context.Entities.GetComponent<ResupplyOrder>(
                    candidate);

            if (order.Provider == provider &&
                context.Entities.IsAlive(candidate))
            {
                return false;
            }
        }

        return true;
    }

    private static void SetMovementOrder(
        SimulationContext context,
        EntityId entity,
        PlayerId owner,
        Vector3 destination,
        SimulationTick submittedAtTick)
    {
        var movementOrder =
            new MovementOrder(
                owner,
                destination,
                submittedAtTick,
                context.Tick);

        if (context.Entities.HasComponent<MovementOrder>(
                entity))
        {
            context.Entities.SetComponent(
                entity,
                movementOrder);
        }
        else
        {
            context.Entities.AddComponent(
                entity,
                movementOrder);
        }
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
