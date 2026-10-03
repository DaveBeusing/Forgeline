using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public readonly record struct AutomaticResupplyDecisionMetrics(
    int EvaluatedUnits,
    int LowSupplyUnits,
    int ActiveResupplyOrders,
    int OrdersIssuedThisTick,
    int ProviderUnavailableThisTick,
    ulong TotalOrdersIssued,
    ulong TotalProviderUnavailable);

public sealed class AutomaticResupplyDecisionSystem : ISimulationSystem
{
    private readonly InventoryStore _inventories;
    private readonly List<ResupplyCandidate> _candidates = new();
    private readonly List<EntityId> _rescueProviders = new();
    private ulong _totalOrdersIssued;
    private ulong _totalProviderUnavailable;

    public AutomaticResupplyDecisionSystem(
        InventoryStore inventories)
    {
        _inventories = inventories ??
            throw new ArgumentNullException(nameof(inventories));
    }

    public SimulationPhase Phase =>
        SimulationPhase.AiDecisions;

    public AutomaticResupplyDecisionMetrics Metrics
    {
        get;
        private set;
    }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        RefreshRescueAssignments(context);
        _candidates.Clear();

        foreach (EntityId entity in
                 context.Entities.Query<
                     AutomaticResupplyPolicy,
                     ControllableEntity>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            BattlefieldSupplyPriority priority =
                context.Entities.TryGetComponent(
                    entity,
                    out UnitSupplyPriority configuredPriority)
                    ? configuredPriority.Priority
                    : BattlefieldSupplyPriority.Normal;

            _candidates.Add(
                new ResupplyCandidate(
                    entity,
                    priority));
        }

        _candidates.Sort(
            static (left, right) =>
            {
                int priority =
                    left.Priority.CompareTo(
                        right.Priority);

                return priority != 0
                    ? priority
                    : left.Entity.CompareTo(
                        right.Entity);
            });

        int lowSupply = 0;
        int activeOrders = 0;
        int issued = 0;
        int unavailable = 0;

        for (int index = 0;
             index < _candidates.Count;
             index++)
        {
            EntityId entity =
                _candidates[index].Entity;

            if (!context.Entities.IsAlive(entity) ||
                !context.Entities.TryGetComponent(
                    entity,
                    out AutomaticResupplyPolicy policy) ||
                !policy.Enabled ||
                !context.Entities.TryGetComponent(
                    entity,
                    out ControllableEntity controllable) ||
                !controllable.IsControllable)
            {
                continue;
            }

            bool needsAmmunition =
                !context.Entities.HasComponent<CargoTransport>(entity) &&
                NeedsAmmunition(
                    context.Entities,
                    entity,
                    policy.AmmunitionThreshold);
            bool needsFuel =
                (NeedsFuel(
                     context.Entities,
                     entity,
                     policy.FuelThreshold) ||
                 CargoDeliveryFuelPolicy.RequiresRefueling(
                     context.Entities,
                     _inventories,
                     entity)) &&
                !CargoDeliveryFuelPolicy.ShouldDeferRefueling(
                    context.Entities, _inventories, entity);

            if (!needsAmmunition &&
                !needsFuel)
            {
                ClearResupplyIntentIfPresent(
                    context,
                    entity);
                MarkResupplyRequested(
                    context,
                    entity,
                    requested: false);
                continue;
            }

            lowSupply++;

            BattlefieldSupplyResource requiredResources =
                BattlefieldSupplyResource.None;

            if (needsFuel)
            {
                requiredResources |=
                    BattlefieldSupplyResource.Fuel;
            }

            if (needsAmmunition)
            {
                requiredResources |=
                    BattlefieldSupplyResource.Ammunition;
            }

            if (context.Entities.TryGetComponent(
                    entity,
                    out ResupplyOrder activeOrder))
            {
                if (CanContinueWithProvider(
                        context,
                        entity,
                        controllable.Owner,
                        activeOrder.Provider,
                        requiredResources))
                {
                    activeOrders++;
                    MarkResupplyRequested(
                        context,
                        entity,
                        requested: true);
                    continue;
                }

                ClearResupplyIntentIfPresent(
                    context,
                    entity);
            }

            if (BattlefieldResupplyPlanner.TryIssueNearestProviderOrder(
                    context,
                    _inventories,
                    entity,
                    controllable.Owner,
                    context.Tick,
                    requiredResources,
                    out _))
            {
                issued++;
                activeOrders++;
                _totalOrdersIssued++;
                MarkResupplyRequested(
                    context,
                    entity,
                    requested: true);
            }
            else
            {
                unavailable++;
                _totalProviderUnavailable++;
                MarkResupplyRequested(
                    context,
                    entity,
                    requested: false);
            }
        }

        Metrics =
            new AutomaticResupplyDecisionMetrics(
                _candidates.Count,
                lowSupply,
                activeOrders,
                issued,
                unavailable,
                _totalOrdersIssued,
                _totalProviderUnavailable);
    }

    private void RefreshRescueAssignments(SimulationContext context)
    {
        _rescueProviders.Clear();
        foreach (EntityId provider in context.Entities.Query<SupplyRescueAssignment>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            _rescueProviders.Add(provider);
        }

        foreach (EntityId provider in _rescueProviders)
        {
            if (!context.Entities.TryGetComponent(provider, out SupplyRescueAssignment assignment))
            {
                continue;
            }

            if (!context.Entities.IsAlive(assignment.Recipient) ||
                !context.Entities.TryGetComponent(assignment.Recipient, out ResupplyOrder order) ||
                order.Provider != provider)
            {
                SupplyRescueTravel.Release(context, provider, assignment.Recipient);
                continue;
            }

            // Both deliberate and automatic rescue orders use real current stock.
            // The provider need not itself have an automatic resupply policy.
            SupplyRescueTravel.RefreshBudget(context, _inventories, provider);
        }
    }

    private bool CanContinueWithProvider(
        SimulationContext context,
        EntityId recipient,
        PlayerId owner,
        EntityId providerEntity,
        BattlefieldSupplyResource requiredResources)
    {
        if (!providerEntity.IsValid ||
            !context.Entities.IsAlive(providerEntity) ||
            !context.Entities.TryGetComponent(
                providerEntity,
                out SupplyProvider provider) ||
            !provider.Enabled ||
            provider.Owner != owner ||
            !_inventories.Contains(provider.InventoryId))
        {
            return false;
        }

        if (context.Entities.TryGetComponent(
                providerEntity,
                out SupplyDepot depot) &&
            depot.State != SupplyDepotState.Operational)
        {
            return false;
        }

        const double quantityEpsilon = 0.000000001;

        if (requiredResources.HasFlag(
                BattlefieldSupplyResource.Fuel) &&
            _inventories.GetAvailableQuantity(
                provider.InventoryId,
                ResourceIds.Fuel) <= quantityEpsilon)
        {
            return false;
        }

        if (requiredResources.HasFlag(
                BattlefieldSupplyResource.Ammunition) &&
            _inventories.GetAvailableQuantity(
                provider.InventoryId,
                ResourceIds.Ammunition) <= quantityEpsilon)
        {
            return false;
        }

        if (context.Entities.TryGetComponent(
                recipient,
                out WorldTransform recipientTransform) &&
            context.Entities.TryGetComponent(
                providerEntity,
                out WorldTransform providerTransform))
        {
            float deltaX =
                recipientTransform.Position.X -
                providerTransform.Position.X;
            float deltaZ =
                recipientTransform.Position.Z -
                providerTransform.Position.Z;
            float distanceSquared =
                deltaX * deltaX +
                deltaZ * deltaZ;
            float rangeSquared =
                provider.ResupplyRangeMeters *
                provider.ResupplyRangeMeters;

            if (distanceSquared > rangeSquared)
            {
                bool recipientCanMove =
                    BattlefieldResupplyPlanner.CanReachProvider(
                        context, _inventories, recipient, distanceSquared, provider.ResupplyRangeMeters);
                EntityId traveler = recipientCanMove ? recipient : providerEntity;
                WorldTransform destination = recipientCanMove ? providerTransform : recipientTransform;

                if (!recipientCanMove &&
                    (!BattlefieldResupplyPlanner.CanProviderReachImmobileRecipient(
                         context, _inventories, providerEntity, recipient,
                         distanceSquared, provider.ResupplyRangeMeters) ||
                     !SupplyRescueTravel.ValidateRemainingRoute(context, providerEntity, recipient)))
                {
                    return false;
                }

                if (!TacticalCommandUtilities.TryGetMovementIntent(context, traveler, out MovementOrder intent))
                {
                    return false;
                }

                float targetDeltaX = intent.WorldTarget.X - destination.Position.X;
                float targetDeltaZ = intent.WorldTarget.Z - destination.Position.Z;
                return targetDeltaX * targetDeltaX + targetDeltaZ * targetDeltaZ <= rangeSquared;
            }
        }

        return true;
    }

    private static void ClearResupplyIntentIfPresent(
        SimulationContext context,
        EntityId entity)
    {
        if (!context.Entities.TryGetComponent(entity, out ResupplyOrder order))
        {
            return;
        }

        SupplyRescueTravel.Release(context, order.Provider, entity);
        TacticalCommandUtilities.RemoveIfPresent<ResupplyOrder>(
            context,
            entity);
        TacticalCommandUtilities.ClearMovementIntent(
            context,
            entity);
    }

    private bool NeedsAmmunition(
        EntityRegistry entities,
        EntityId entity,
        double threshold)
    {
        if (!entities.TryGetComponent(
                entity,
                out AmmunitionState ammunition))
        {
            return false;
        }

        if (!_inventories.Contains(
                ammunition.InventoryId))
        {
            return true;
        }

        double fraction =
            ammunition.Capacity <= 0.0
                ? 0.0
                : _inventories.GetQuantity(
                    ammunition.InventoryId,
                    ResourceIds.Ammunition) /
                  ammunition.Capacity;

        return fraction <= threshold;
    }

    private bool NeedsFuel(
        EntityRegistry entities,
        EntityId entity,
        double threshold)
    {
        if (!entities.TryGetComponent(
                entity,
                out UnitFuelState fuel))
        {
            return false;
        }

        if (!_inventories.Contains(
                fuel.InventoryId))
        {
            return true;
        }

        double fraction =
            fuel.Capacity <= 0.0
                ? 0.0
                : _inventories.GetQuantity(
                    fuel.InventoryId,
                    ResourceIds.Fuel) /
                  fuel.Capacity;

        return fraction <= threshold;
    }

    private readonly record struct ResupplyCandidate(
        EntityId Entity,
        BattlefieldSupplyPriority Priority);

    private static void MarkResupplyRequested(
        SimulationContext context,
        EntityId entity,
        bool requested)
    {
        if (!context.Entities.TryGetComponent(
                entity,
                out TacticalCombatState state))
        {
            if (!requested)
            {
                return;
            }

            context.Entities.AddComponent(
                entity,
                new TacticalCombatState(
                    CombatOrderStatus.Resupplying,
                    EntityId.Invalid,
                    default,
                    HasLastLegitimateTargetPosition: false,
                    MovementPausedForCombat: false,
                    ResupplyRequested: true,
                    context.Tick));
            return;
        }

        context.Entities.SetComponent(
            entity,
            state with
            {
                Status = requested
                    ? CombatOrderStatus.Resupplying
                    : state.Status,
                ResupplyRequested = requested,
                UpdatedAtTick = context.Tick
            });
    }
}
