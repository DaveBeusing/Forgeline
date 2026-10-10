using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public readonly record struct CombatGroupMemberReadModel(
    EntityId Entity,
    ControllableEntityCategory Category,
    bool HasHealth,
    double HealthFraction,
    bool HasSupply,
    BattlefieldSupplyStatus SupplyStatus,
    double FuelFraction,
    double AmmunitionFraction,
    bool HasReadiness,
    double Strength,
    double Readiness,
    bool HasFormation,
    FormationTemplate Formation,
    bool HasOrder,
    CombatOrderKind Order,
    bool HasOrderStatus,
    CombatOrderStatus OrderStatus,
    UnitId Unit = default,
    bool CombatEligible = false);

public sealed class CombatGroupOperationalSnapshot
{
    private readonly CombatGroupMemberReadModel[] _members;
    private readonly EntityId[] _eligibleEntities;
    private readonly Dictionary<EntityId, CombatGroupMemberReadModel> _lookup;

    public CombatGroupOperationalSnapshot(
        SimulationTick tick,
        IReadOnlyList<CombatGroupMemberReadModel> members,
        SimulationSessionId sessionId = default)
    {
        ArgumentNullException.ThrowIfNull(members);

        SessionId = sessionId;
        Tick =
            tick;
        _members =
            members.Where(static member => member.Entity.IsValid).DistinctBy(static member => member.Entity).ToArray();
        _lookup = _members.ToDictionary(static member => member.Entity);
        _eligibleEntities =
            new EntityId[
                _members.Length];

        for (int index = 0;
             index < _members.Length;
             index++)
        {
            _eligibleEntities[index] =
                _members[index].Entity;
        }
    }

    public SimulationSessionId SessionId { get; }

    public SimulationTick Tick { get; }

    public IReadOnlyList<CombatGroupMemberReadModel> Members =>
        _members;

    public IReadOnlyCollection<EntityId> EligibleEntities =>
        _eligibleEntities;

    public bool TryGet(EntityId entity, out CombatGroupMemberReadModel member) =>
        _lookup.TryGetValue(entity, out member);

}

public readonly record struct CombatGroupSummaryReadModel(
    int Slot,
    string Label,
    int MemberCount,
    bool IsActive,
    bool IsSelected,
    bool HasHealth,
    double Health,
    bool HasStrength,
    double Strength,
    bool HasSupply,
    BattlefieldSupplyStatus SupplyStatus,
    double Fuel,
    double Ammunition,
    bool HasReadiness,
    double Readiness,
    bool HasFormation,
    bool MixedFormation,
    FormationTemplate Formation,
    bool HasOrder,
    bool MixedOrder,
    CombatOrderKind Order,
    bool HasOrderStatus,
    bool MixedOrderStatus,
    CombatOrderStatus OrderStatus)
{
    public bool IsAssigned =>
        MemberCount >
        0;
}

public sealed class CombatGroupOverviewView
{
    private readonly CombatGroupSummaryReadModel[] _groups;
    private readonly EntityId[] _activeMembers;

    public CombatGroupOverviewView(
        IReadOnlyList<CombatGroupSummaryReadModel> groups,
        IReadOnlyCollection<EntityId> activeMembers,
        SelectedCombatGroup? selection = null)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(activeMembers);

        Selection = selection;
        _groups =
            groups.ToArray();
        _activeMembers =
            activeMembers.ToArray();
    }

    public SelectedCombatGroup? Selection { get; }

    public IReadOnlyList<CombatGroupSummaryReadModel> Groups =>
        _groups;

    public IReadOnlyCollection<EntityId> ActiveMembers =>
        _activeMembers;

    public static CombatGroupOverviewView Empty { get; } =
        new(
            [],
            []);
}

public static class CombatGroupOverviewModel
{
    public static CombatGroupOverviewView Create(
        CombatGroupRegistry registry,
        CombatGroupOperationalSnapshot? operational,
        SelectionSet selection)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(selection);

        if (operational is null)
        {
            return CombatGroupOverviewView.Empty;
        }

        var summaries =
            new CombatGroupSummaryReadModel[
                CombatGroupRegistry.SlotCount];

        for (int slot = 0;
             slot <
                 CombatGroupRegistry.SlotCount;
             slot++)
        {
            CombatGroupSlotView group =
                registry.GetSlot(
                    slot);
            summaries[slot] =
                Summarize(
                    group,
                    operational,
                    selection);
        }

        return new CombatGroupOverviewView(
            summaries,
            registry.GetActiveMembers(),
            SelectedCombatGroup.Create(operational, selection));
    }

    private static CombatGroupSummaryReadModel Summarize(
        in CombatGroupSlotView group,
        CombatGroupOperationalSnapshot operational,
        SelectionSet selection)
    {
        int validMembers = 0;
        int healthCount = 0;
        int strengthCount = 0;
        int supplyCount = 0;
        int readinessCount = 0;
        double health = 0.0;
        double strength = 0.0;
        double fuel = 0.0;
        double ammunition = 0.0;
        double readiness = 0.0;
        BattlefieldSupplyStatus supplyStatus =
            BattlefieldSupplyStatus.Supplied;

        bool anyFormation = false;
        bool missingFormation = false;
        bool mixedFormation = false;
        FormationTemplate formation =
            FormationTemplate.Compact;

        bool anyOrder = false;
        bool missingOrder = false;
        bool mixedOrder = false;
        CombatOrderKind order =
            default;

        bool anyOrderStatus = false;
        bool missingOrderStatus = false;
        bool mixedOrderStatus = false;
        CombatOrderStatus orderStatus =
            default;

        for (int index = 0;
             index < group.Members.Length;
             index++)
        {
            if (!operational.TryGet(
                    group.Members[index],
                    out CombatGroupMemberReadModel member))
            {
                continue;
            }

            validMembers++;

            if (member.HasHealth)
            {
                health +=
                    member.HealthFraction;
                healthCount++;
            }

            if (member.HasReadiness)
            {
                strength +=
                    member.Strength;
                strengthCount++;
                readiness +=
                    member.Readiness;
                readinessCount++;
            }

            if (member.HasSupply)
            {
                fuel +=
                    member.FuelFraction;
                ammunition +=
                    member.AmmunitionFraction;
                supplyCount++;
                supplyStatus =
                    WorseSupply(
                        supplyStatus,
                        member.SupplyStatus);
            }

            if (member.HasFormation)
            {
                if (!anyFormation)
                {
                    formation =
                        member.Formation;
                    anyFormation =
                        true;
                }
                else if (formation !=
                         member.Formation)
                {
                    mixedFormation =
                        true;
                }
            }
            else
            {
                missingFormation =
                    true;
            }

            if (member.HasOrder)
            {
                if (!anyOrder)
                {
                    order =
                        member.Order;
                    anyOrder =
                        true;
                }
                else if (order !=
                         member.Order)
                {
                    mixedOrder =
                        true;
                }
            }
            else
            {
                missingOrder =
                    true;
            }

            if (member.HasOrderStatus)
            {
                if (!anyOrderStatus)
                {
                    orderStatus =
                        member.OrderStatus;
                    anyOrderStatus =
                        true;
                }
                else if (orderStatus !=
                         member.OrderStatus)
                {
                    mixedOrderStatus =
                        true;
                }
            }
            else
            {
                missingOrderStatus =
                    true;
            }
        }

        if (anyFormation &&
            missingFormation)
        {
            mixedFormation =
                true;
        }

        if (anyOrder &&
            missingOrder)
        {
            mixedOrder =
                true;
        }

        if (anyOrderStatus &&
            missingOrderStatus)
        {
            mixedOrderStatus =
                true;
        }

        bool selected =
            validMembers >
                0 &&
            selection.Count ==
                validMembers;

        if (selected)
        {
            for (int index = 0;
                 index < group.Members.Length;
                 index++)
            {
                EntityId entity =
                    group.Members[index];

                if (operational.TryGet(
                        entity,
                        out _) &&
                    !selection.Contains(
                        entity))
                {
                    selected =
                        false;
                    break;
                }
            }
        }

        return new CombatGroupSummaryReadModel(
            group.Slot,
            group.Label,
            validMembers,
            group.IsActive,
            selected,
            healthCount >
                0,
            healthCount >
                0
                ? health /
                  healthCount
                : 0.0,
            strengthCount >
                0,
            strengthCount >
                0
                ? strength /
                  strengthCount
                : 0.0,
            supplyCount >
                0,
            supplyStatus,
            supplyCount >
                0
                ? fuel /
                  supplyCount
                : 0.0,
            supplyCount >
                0
                ? ammunition /
                  supplyCount
                : 0.0,
            readinessCount >
                0,
            readinessCount >
                0
                ? readiness /
                  readinessCount
                : 0.0,
            anyFormation,
            mixedFormation,
            formation,
            anyOrder,
            mixedOrder,
            order,
            anyOrderStatus,
            mixedOrderStatus,
            orderStatus);
    }

    private static BattlefieldSupplyStatus WorseSupply(
        BattlefieldSupplyStatus current,
        BattlefieldSupplyStatus candidate) =>
        SupplySeverity(
            candidate) >
        SupplySeverity(
            current)
            ? candidate
            : current;

    private static int SupplySeverity(
        BattlefieldSupplyStatus status) =>
        status switch
        {
            BattlefieldSupplyStatus.Supplied =>
                0,
            BattlefieldSupplyStatus.LowSupply =>
                1,
            BattlefieldSupplyStatus.Critical =>
                2,
            BattlefieldSupplyStatus.Unsupplied =>
                3,
            _ =>
                3
        };
}

internal static class CombatGroupOperationalSnapshotFactory
{
    public static CombatGroupOperationalSnapshot Capture(
        SimulationContext context,
        PlayerId player,
        SimulationSessionId sessionId = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        EntityRegistry entities =
            context.Entities;
        var members =
            new List<CombatGroupMemberReadModel>(
                entities.GetComponentCount<ControllableEntity>());

        foreach (EntityId entity in
                 entities.Query<ControllableEntity>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            ControllableEntity controllable =
                entities.GetComponent<ControllableEntity>(
                    entity);

            if (!controllable.IsControllable || controllable.Owner !=
                    player ||
                (controllable.Category &
                 (ControllableEntityCategory.Unit |
                  ControllableEntityCategory.Logistics)) ==
                    0)
            {
                continue;
            }

            bool hasHealth =
                entities.TryGetComponent(
                    entity,
                    out HealthState health);

            if (hasHealth &&
                health.IsDepleted)
            {
                continue;
            }

            bool hasSupply =
                entities.TryGetComponent(
                    entity,
                    out UnitSupplyState supply);
            bool hasReadiness =
                entities.TryGetComponent(
                    entity,
                    out UnitCombatReadiness readiness);
            bool hasOrder =
                entities.TryGetComponent(
                    entity,
                    out CombatOrderState order);
            bool hasOrderStatus =
                entities.TryGetComponent(
                    entity,
                    out TacticalCombatState tactical);

            bool hasFormation =
                false;
            FormationTemplate formation =
                FormationTemplate.Compact;

            if (hasOrder)
            {
                hasFormation =
                    true;
                formation =
                    order.Formation;
            }
            else if (entities.TryGetComponent(
                         entity,
                         out MovementGroupMember movementMember) &&
                     entities.TryGetComponent(
                         movementMember.Group,
                         out MovementGroupOrder movementOrder))
            {
                hasFormation =
                    true;
                formation =
                    movementOrder.Formation;
            }

            members.Add(
                new CombatGroupMemberReadModel(
                    entity,
                    controllable.Category,
                    hasHealth,
                    hasHealth
                        ? health.Fraction
                        : 0.0,
                    hasSupply,
                    hasSupply
                        ? supply.Status
                        : BattlefieldSupplyStatus.Supplied,
                    hasSupply
                        ? supply.FuelFraction
                        : 0.0,
                    hasSupply
                        ? supply.AmmunitionFraction
                        : 0.0,
                    hasReadiness,
                    hasReadiness
                        ? readiness.Strength
                        : 0.0,
                    hasReadiness
                        ? readiness.OverallReadiness
                        : 0.0,
                    hasFormation,
                    formation,
                    hasOrder,
                    hasOrder
                        ? order.Kind
                        : default,
                    hasOrderStatus,
                    hasOrderStatus
                        ? tactical.Status
                        : default,
                    entities.TryGetComponent(entity, out UnitIdentity identity) ? identity.UnitId : default,
                    entities.HasComponent<WorldTransform>(entity) && entities.HasComponent<Combatant>(entity)));
        }

        return new CombatGroupOperationalSnapshot(
            context.Tick,
            members,
            sessionId);
    }
}
