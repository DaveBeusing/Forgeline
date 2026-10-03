using System.Numerics;
using ForgeLine.Combat;
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
        int friendlyProviders = 0;
        int rejectedProviders = 0;
        ResupplyProviderRejection rejections = ResupplyProviderRejection.None;
        WorldTransform providerTransform = default;
        SupplyProvider selectedProvider = default;
        bool preferMobileProvider =
            ShouldPreferMobileProvider(
                context,
                recipient);
        int bestProviderRank =
            int.MaxValue;
        float bestDistanceSquared =
            float.PositiveInfinity;

        foreach (EntityId candidate in
                 context.Entities.Query<SupplyProvider>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            SupplyProvider provider = context.Entities.GetComponent<SupplyProvider>(candidate);
            if (provider.Owner != owner)
            {
                continue;
            }

            friendlyProviders++;
            ResupplyProviderRejection rejection = EvaluateProvider(
                context, inventories, candidate, recipient, provider, requiredResources);
            if (rejection != ResupplyProviderRejection.None)
            {
                rejections |= rejection;
                rejectedProviders++;
                continue;
            }

            WorldTransform transform = context.Entities.GetComponent<WorldTransform>(candidate);
            float distanceSquared =
                HorizontalDistanceSquared(
                    recipientTransform.Position,
                    transform.Position);

            bool canReachProvider = CanReachProvider(
                context, inventories, recipient, distanceSquared, provider.ResupplyRangeMeters);
            if (!canReachProvider)
            {
                rejection = EvaluateRescueTravel(
                    context, inventories, candidate, recipient,
                    distanceSquared, provider.ResupplyRangeMeters);
                if (rejection != ResupplyProviderRejection.None)
                {
                    rejections |= ResupplyProviderRejection.RecipientTravelUnavailable | rejection;
                    rejectedProviders++;
                    continue;
                }
            }

            int providerRank =
                preferMobileProvider &&
                !context.Entities.HasComponent<SupplyTruck>(
                    candidate)
                    ? 1
                    : 0;

            if (!providerEntity.IsValid ||
                providerRank < bestProviderRank ||
                (providerRank == bestProviderRank &&
                 (distanceSquared < bestDistanceSquared ||
                  (distanceSquared == bestDistanceSquared &&
                   candidate < providerEntity))))
            {
                providerEntity =
                    candidate;
                providerTransform =
                    transform;
                selectedProvider =
                    provider;
                bestProviderRank =
                    providerRank;
                bestDistanceSquared =
                    distanceSquared;
                recipientCanReachProvider = canReachProvider;
            }
        }

        var result = new ResupplyPlanningResult(
            context.Tick, friendlyProviders, rejectedProviders,
            friendlyProviders == 0 ? ResupplyProviderRejection.NoFriendlyProvider : rejections,
            providerEntity);
        if (context.Entities.HasComponent<ResupplyPlanningResult>(recipient))
        {
            context.Entities.SetComponent(recipient, result);
        }
        else
        {
            context.Entities.AddComponent(recipient, result);
        }

        if (!providerEntity.IsValid)
        {
            return false;
        }

        bool alreadyInProviderRange =
            bestDistanceSquared <=
            selectedProvider.ResupplyRangeMeters *
            selectedProvider.ResupplyRangeMeters;
        bool prioritizedMobileDelivery =
            preferMobileProvider &&
            !alreadyInProviderRange &&
            context.Entities.HasComponent<SupplyTruck>(
                providerEntity) &&
            CanPrioritizedMobileProviderTravel(
                context,
                inventories,
                providerEntity,
                recipient,
                bestDistanceSquared,
                selectedProvider.ResupplyRangeMeters);

        if (!alreadyInProviderRange)
        {
            ClearFormationMovement(
                context,
                recipient);
        }

        if (prioritizedMobileDelivery)
        {
            recipientCanReachProvider =
                false;
        }

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

        if (alreadyInProviderRange)
        {
            return true;
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

            MovementOrder movement = SetMovementOrder(
                context,
                providerEntity,
                owner,
                providerApproachPosition,
                submittedAtTick);
            SupplyRescueTravel.Assign(
                context, inventories, providerEntity, recipient, movement);
        }

        return true;
    }

    private static bool ShouldPreferMobileProvider(
        SimulationContext context,
        EntityId recipient)
    {
        if (!context.Entities.HasComponent<Combatant>(
                recipient) ||
            !context.Entities.TryGetComponent(
                recipient,
                out UnitSupplyPriority priority))
        {
            return false;
        }

        return priority.Priority is
            BattlefieldSupplyPriority.Critical or
            BattlefieldSupplyPriority.High;
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

    private static ResupplyProviderRejection EvaluateProvider(
        SimulationContext context,
        InventoryStore? inventories,
        EntityId candidate,
        EntityId recipient,
        in SupplyProvider provider,
        BattlefieldSupplyResource requiredResources)
    {
        if (candidate == recipient)
        {
            return ResupplyProviderRejection.SelfSupply;
        }

        if (!provider.Enabled)
        {
            return ResupplyProviderRejection.Disabled;
        }

        if (!context.Entities.HasComponent<WorldTransform>(candidate))
        {
            return ResupplyProviderRejection.MissingPosition;
        }

        if (context.Entities.TryGetComponent(candidate, out SupplyDepot depot) &&
            depot.State != SupplyDepotState.Operational)
        {
            return ResupplyProviderRejection.DepotNotOperational;
        }

        if (context.Entities.HasComponent<SupplyTruck>(candidate))
        {
            if (context.Entities.HasComponent<ResupplyOrder>(candidate))
            {
                return ResupplyProviderRejection.ProviderRefueling;
            }

            foreach (EntityId otherRecipient in
                     context.Entities.Query<ResupplyOrder>(
                         QueryIterationOrder.StableByEntityIndex))
            {
                if (otherRecipient == recipient ||
                    !context.Entities.IsAlive(otherRecipient))
                {
                    continue;
                }

                ResupplyOrder activeOrder =
                    context.Entities.GetComponent<ResupplyOrder>(
                        otherRecipient);

                if (activeOrder.Provider == candidate)
                {
                    return ResupplyProviderRejection.ProviderBusy;
                }
            }
        }

        if (inventories is null)
        {
            return ResupplyProviderRejection.None;
        }

        if (!inventories.Contains(provider.InventoryId))
        {
            return ResupplyProviderRejection.MissingInventory;
        }

        const double QuantityEpsilon = 0.000000001;
        ResupplyProviderRejection rejection = ResupplyProviderRejection.None;
        if (requiredResources.HasFlag(BattlefieldSupplyResource.Fuel) &&
            inventories.GetAvailableQuantity(provider.InventoryId, ResourceIds.Fuel) <= QuantityEpsilon)
        {
            rejection |= ResupplyProviderRejection.FuelStockUnavailable;
        }

        if (requiredResources.HasFlag(BattlefieldSupplyResource.Ammunition) &&
            inventories.GetAvailableQuantity(provider.InventoryId, ResourceIds.Ammunition) <= QuantityEpsilon)
        {
            rejection |= ResupplyProviderRejection.AmmunitionStockUnavailable;
        }

        return rejection;
    }

    private static bool CanPrioritizedMobileProviderTravel(
        SimulationContext context,
        InventoryStore? inventories,
        EntityId provider,
        EntityId recipient,
        float distanceSquared,
        float range)
    {
        if (!context.Entities.HasComponent<SupplyTruck>(
                provider) ||
            !context.Entities.HasComponent<GroundMovement>(
                provider) ||
            context.Entities.HasComponent<ResupplyOrder>(
                provider) ||
            SupplyRescueTravel.IsDeferred(
                context,
                inventories,
                provider,
                recipient) ||
            !CanReachProvider(
                context,
                inventories,
                provider,
                distanceSquared,
                range))
        {
            return false;
        }

        foreach (EntityId candidate in
                 context.Entities.Query<ResupplyOrder>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (candidate == recipient ||
                !context.Entities.IsAlive(
                    candidate))
            {
                continue;
            }

            if (context.Entities.GetComponent<ResupplyOrder>(
                    candidate).Provider ==
                provider)
            {
                return false;
            }
        }

        return true;
    }

    internal static bool CanProviderReachImmobileRecipient(
        SimulationContext context,
        InventoryStore? inventories,
        EntityId provider,
        EntityId recipient,
        float distanceSquared,
        float range) =>
        EvaluateRescueTravel(context, inventories, provider, recipient, distanceSquared, range) ==
        ResupplyProviderRejection.None;

    private static ResupplyProviderRejection EvaluateRescueTravel(
        SimulationContext context,
        InventoryStore? inventories,
        EntityId provider,
        EntityId recipient,
        float distanceSquared,
        float range)
    {
        if (!context.Entities.HasComponent<SupplyTruck>(provider) ||
            !context.Entities.HasComponent<GroundMovement>(provider))
        {
            return ResupplyProviderRejection.ProviderNotMobile;
        }

        if (context.Entities.HasComponent<ResupplyOrder>(provider))
        {
            return ResupplyProviderRejection.ProviderRefueling;
        }

        if (SupplyRescueTravel.IsDeferred(context, inventories, provider, recipient))
        {
            return ResupplyProviderRejection.RouteRetryDeferred;
        }

        if (SupplyRescueTravel.HasUnrelatedMovement(context, provider, recipient))
        {
            return ResupplyProviderRejection.ProviderBusy;
        }

        // Propulsion uses UnitFuelState, never the SupplyTruck cargo inventory.
        if (!CanReachProvider(context, inventories, provider, distanceSquared, range))
        {
            return ResupplyProviderRejection.ProviderFuelUnavailable;
        }

        foreach (EntityId candidate in
                 context.Entities.Query<ResupplyOrder>(QueryIterationOrder.StableByEntityIndex))
        {
            if (candidate == recipient)
            {
                continue;
            }

            ResupplyOrder order = context.Entities.GetComponent<ResupplyOrder>(candidate);
            if (order.Provider == provider && context.Entities.IsAlive(candidate))
            {
                return ResupplyProviderRejection.ProviderBusy;
            }
        }

        return ResupplyProviderRejection.None;
    }

    private static MovementOrder SetMovementOrder(
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

        if (context.Entities.HasComponent<MovementOrder>(entity))
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

        return movementOrder;
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
