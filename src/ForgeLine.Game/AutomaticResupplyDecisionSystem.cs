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
    private readonly List<EntityId> _candidates = new();
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

        _candidates.Clear();

        foreach (EntityId entity in
                 context.Entities.Query<
                     AutomaticResupplyPolicy,
                     ControllableEntity>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            _candidates.Add(entity);
        }

        int lowSupply = 0;
        int activeOrders = 0;
        int issued = 0;
        int unavailable = 0;

        for (int index = 0;
             index < _candidates.Count;
             index++)
        {
            EntityId entity =
                _candidates[index];

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
                NeedsFuel(
                    context.Entities,
                    entity,
                    policy.FuelThreshold);

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

    private bool CanContinueWithProvider(
        SimulationContext context,
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

        return true;
    }

    private static void ClearResupplyIntentIfPresent(
        SimulationContext context,
        EntityId entity)
    {
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
