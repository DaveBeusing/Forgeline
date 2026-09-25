using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public readonly record struct TacticalCombatMetrics(
    int OrderedUnits,
    int EngagingUnits,
    int PursuingUnits,
    int HoldingUnits,
    int RetreatingUnits,
    int WaitingForIntelligenceUnits,
    int GroupAssignmentsThisTick,
    ulong TotalGroupAssignments);

public readonly record struct TacticalCombatDebugEntry(
    EntityId Entity,
    CombatOrderKind Order,
    CombatOrderStatus Status,
    EntityId Target,
    Vector3 TargetPosition,
    bool HasTargetPosition,
    Vector3 Position,
    Vector3 Destination,
    Vector3 Anchor,
    float PursuitLeashMeters,
    bool MovementAllowed,
    bool ResupplyRequested,
    double OverallReadiness);

public sealed class TacticalOrderPreparationSystem : ISimulationSystem
{
    private readonly List<EntityId> _ordered = new();

    public SimulationPhase Phase =>
        SimulationPhase.OrderProcessing;

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _ordered.Clear();

        foreach (EntityId entity in
                 context.Entities.Query<
                     CombatOrderState,
                     WorldTransform>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            _ordered.Add(entity);
        }

        for (int index = 0;
             index < _ordered.Count;
             index++)
        {
            EntityId entity = _ordered[index];

            if (!context.Entities.IsAlive(entity) ||
                !context.Entities.TryGetComponent(
                    entity,
                    out CombatOrderState order))
            {
                continue;
            }

            if (context.Entities.HasComponent<ResupplyOrder>(entity))
            {
                ClearWeaponTarget(
                    context,
                    entity);
                SetMovementAllowed(
                    context,
                    entity,
                    allowed: true);
                SetTacticalState(
                    context,
                    entity,
                    CombatOrderStatus.Resupplying,
                    EntityId.Invalid,
                    default,
                    hasTargetPosition: false,
                    movementPaused: false);
                continue;
            }

            switch (order.Kind)
            {
                case CombatOrderKind.Attack:
                    SetAutoTarget(
                        context,
                        entity,
                        enabled: false);
                    SetMovementConstraint(
                        context,
                        entity,
                        canMove: true);
                    SetStateIfMissing(
                        context,
                        entity,
                        CombatOrderStatus.WaitingForIntelligence);
                    break;

                case CombatOrderKind.AttackMove:
                    SetAutoTarget(
                        context,
                        entity,
                        enabled: true);
                    SetMovementConstraint(
                        context,
                        entity,
                        canMove: true);
                    SetStateIfMissing(
                        context,
                        entity,
                        CombatOrderStatus.Advancing);
                    break;

                case CombatOrderKind.Stop:
                    SetAutoTarget(
                        context,
                        entity,
                        enabled: false);
                    SetMovementConstraint(
                        context,
                        entity,
                        canMove: false);
                    ClearWeaponTarget(
                        context,
                        entity);
                    SetState(
                        context,
                        entity,
                        CombatOrderStatus.Complete,
                        EntityId.Invalid,
                        default,
                        hasTargetPosition: false,
                        movementPaused: true);
                    break;

                case CombatOrderKind.HoldPosition:
                    SetAutoTarget(
                        context,
                        entity,
                        enabled: true);
                    SetMovementConstraint(
                        context,
                        entity,
                        canMove: false);
                    SetStateIfMissing(
                        context,
                        entity,
                        CombatOrderStatus.Holding);
                    break;

                case CombatOrderKind.Retreat:
                    SetAutoTarget(
                        context,
                        entity,
                        enabled: false);
                    SetMovementConstraint(
                        context,
                        entity,
                        canMove: true);
                    ClearWeaponTarget(
                        context,
                        entity);
                    SetState(
                        context,
                        entity,
                        CombatOrderStatus.Retreating,
                        EntityId.Invalid,
                        default,
                        hasTargetPosition: false,
                        movementPaused: false);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported combat order '{order.Kind}'.");
            }
        }
    }

    private static void SetAutoTarget(
        SimulationContext context,
        EntityId entity,
        bool enabled)
    {
        var state =
            new AutoTargetState(enabled);

        if (context.Entities.HasComponent<AutoTargetState>(entity))
        {
            context.Entities.SetComponent(
                entity,
                state);
        }
        else
        {
            context.Entities.AddComponent(
                entity,
                state);
        }
    }

    private static void SetMovementConstraint(
        SimulationContext context,
        EntityId entity,
        bool canMove)
    {
        var constraint =
            new TacticalMovementConstraint(canMove);

        if (context.Entities.HasComponent<
                TacticalMovementConstraint>(entity))
        {
            context.Entities.SetComponent(
                entity,
                constraint);
        }
        else
        {
            context.Entities.AddComponent(
                entity,
                constraint);
        }
    }

    private static void ClearWeaponTarget(
        SimulationContext context,
        EntityId entity)
    {
        if (!context.Entities.TryGetComponent(
                entity,
                out WeaponState weapon) ||
            !weapon.Target.IsValid)
        {
            return;
        }

        context.Entities.SetComponent(
            entity,
            weapon with
            {
                Target = EntityId.Invalid
            });
    }

    private static void SetStateIfMissing(
        SimulationContext context,
        EntityId entity,
        CombatOrderStatus status)
    {
        if (context.Entities.HasComponent<TacticalCombatState>(entity))
        {
            return;
        }

        SetState(
            context,
            entity,
            status,
            EntityId.Invalid,
            default,
            hasTargetPosition: false,
            movementPaused:
                status is
                    CombatOrderStatus.Holding or
                    CombatOrderStatus.Complete);
    }

    private static void SetState(
        SimulationContext context,
        EntityId entity,
        CombatOrderStatus status,
        EntityId target,
        Vector3 targetPosition,
        bool hasTargetPosition,
        bool movementPaused)
    {
        var state =
            new TacticalCombatState(
                status,
                target,
                targetPosition,
                hasTargetPosition,
                movementPaused,
                ResupplyRequested:
                    context.Entities.TryGetComponent(
                        entity,
                        out TacticalCombatState existing) &&
                    existing.ResupplyRequested,
                context.Tick);

        if (context.Entities.HasComponent<TacticalCombatState>(entity))
        {
            context.Entities.SetComponent(
                entity,
                state);
        }
        else
        {
            context.Entities.AddComponent(
                entity,
                state);
        }
    }
}

public sealed class TacticalCombatSystem : ISimulationSystem
{
    private readonly WeaponCatalog _weapons;
    private readonly FactionIntelligenceStore _intelligence;
    private readonly List<EntityId> _ordered = new();
    private readonly Dictionary<EntityId, List<EntityId>> _membersByGroup =
        new();
    private readonly List<EntityId> _groups = new();
    private readonly List<GroupCandidate> _candidates = new();
    private readonly Dictionary<EntityId, int> _assignmentCounts = new();
    private readonly List<TacticalCombatDebugEntry> _debug = new();

    private int _groupAssignmentsThisTick;
    private ulong _totalGroupAssignments;

    public TacticalCombatSystem(
        WeaponCatalog weapons,
        FactionIntelligenceStore intelligence)
    {
        _weapons = weapons ??
            throw new ArgumentNullException(nameof(weapons));
        _intelligence = intelligence ??
            throw new ArgumentNullException(nameof(intelligence));
    }

    public SimulationPhase Phase =>
        SimulationPhase.Sensors;

    public bool DebugCaptureEnabled { get; set; }

    public TacticalCombatMetrics Metrics { get; private set; }

    public IReadOnlyList<TacticalCombatDebugEntry> DebugEntries =>
        _debug;

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _groupAssignmentsThisTick = 0;
        _debug.Clear();
        GatherOrderedUnits(context);

        int engaging = 0;
        int pursuing = 0;
        int holding = 0;
        int retreating = 0;
        int waiting = 0;

        for (int index = 0;
             index < _ordered.Count;
             index++)
        {
            EntityId entity =
                _ordered[index];

            if (!context.Entities.IsAlive(entity) ||
                !context.Entities.TryGetComponent(
                    entity,
                    out CombatOrderState order) ||
                !context.Entities.TryGetComponent(
                    entity,
                    out WorldTransform transform))
            {
                continue;
            }

            switch (order.Kind)
            {
                case CombatOrderKind.Attack:
                    ProcessAttack(
                        context,
                        entity,
                        order,
                        transform);
                    break;

                case CombatOrderKind.AttackMove:
                    ProcessAttackMove(
                        context,
                        entity,
                        order,
                        transform);
                    break;

                case CombatOrderKind.HoldPosition:
                    ProcessHold(
                        context,
                        entity);
                    break;

                case CombatOrderKind.Retreat:
                    ProcessRetreat(
                        context,
                        entity,
                        order,
                        transform);
                    break;

                case CombatOrderKind.Stop:
                    SetMovementAllowed(
                        context,
                        entity,
                        allowed: false);
                    ClearWeaponTarget(
                        context,
                        entity);
                    SetTacticalState(
                        context,
                        entity,
                        CombatOrderStatus.Complete,
                        EntityId.Invalid,
                        default,
                        hasTargetPosition: false,
                        movementPaused: true);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported combat order '{order.Kind}'.");
            }
        }

        CoordinateGroupTargets(context);

        for (int index = 0;
             index < _ordered.Count;
             index++)
        {
            EntityId entity = _ordered[index];

            if (!context.Entities.TryGetComponent(
                    entity,
                    out TacticalCombatState state))
            {
                continue;
            }

            switch (state.Status)
            {
                case CombatOrderStatus.Engaging:
                    engaging++;
                    break;
                case CombatOrderStatus.Pursuing:
                    pursuing++;
                    break;
                case CombatOrderStatus.Holding:
                    holding++;
                    break;
                case CombatOrderStatus.Retreating:
                    retreating++;
                    break;
                case CombatOrderStatus.WaitingForIntelligence:
                    waiting++;
                    break;
            }

            if (DebugCaptureEnabled &&
                context.Entities.TryGetComponent(
                    entity,
                    out CombatOrderState order) &&
                context.Entities.TryGetComponent(
                    entity,
                    out WorldTransform transform))
            {
                bool movementAllowed =
                    !context.Entities.TryGetComponent(
                        entity,
                        out TacticalMovementConstraint constraint) ||
                    constraint.CanMove;
                double readiness =
                    context.Entities.TryGetComponent(
                        entity,
                        out UnitCombatReadiness unitReadiness)
                        ? unitReadiness.OverallReadiness
                        : 0.0;

                _debug.Add(
                    new TacticalCombatDebugEntry(
                        entity,
                        order.Kind,
                        state.Status,
                        state.AssignedTarget,
                        state.LastLegitimateTargetPosition,
                        state.HasLastLegitimateTargetPosition,
                        transform.Position,
                        order.Destination,
                        order.AnchorPosition,
                        order.PursuitLeashMeters,
                        movementAllowed,
                        state.ResupplyRequested,
                        readiness));
            }
        }

        Metrics =
            new TacticalCombatMetrics(
                _ordered.Count,
                engaging,
                pursuing,
                holding,
                retreating,
                waiting,
                _groupAssignmentsThisTick,
                _totalGroupAssignments);
    }

    private void GatherOrderedUnits(
        SimulationContext context)
    {
        _ordered.Clear();

        foreach (EntityId entity in
                 context.Entities.Query<
                     CombatOrderState,
                     WorldTransform>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            _ordered.Add(entity);
        }
    }

    private void ProcessAttack(
        SimulationContext context,
        EntityId entity,
        in CombatOrderState order,
        in WorldTransform transform)
    {
        EntityId target =
            order.ExplicitTarget;

        if (!TryGetLegitimateTarget(
                context,
                entity,
                target,
                out WorldTransform targetTransform,
                out _))
        {
            ClearWeaponTarget(
                context,
                entity);
            ClearChaseMovement(
                context,
                entity);
            SetMovementAllowed(
                context,
                entity,
                allowed: false);
            SetTacticalState(
                context,
                entity,
                CombatOrderStatus.WaitingForIntelligence,
                EntityId.Invalid,
                default,
                hasTargetPosition: false,
                movementPaused: true);
            return;
        }

        float leashDistanceSquared =
            HorizontalDistanceSquared(
                order.AnchorPosition,
                targetTransform.Position);
        float leashSquared =
            order.PursuitLeashMeters *
            order.PursuitLeashMeters;

        if (leashDistanceSquared > leashSquared)
        {
            ClearWeaponTarget(
                context,
                entity);
            ClearChaseMovement(
                context,
                entity);
            SetMovementAllowed(
                context,
                entity,
                allowed: false);
            SetTacticalState(
                context,
                entity,
                CombatOrderStatus.Holding,
                EntityId.Invalid,
                targetTransform.Position,
                hasTargetPosition: true,
                movementPaused: true);
            return;
        }

        if (!context.Entities.TryGetComponent(
                entity,
                out WeaponState weaponState))
        {
            SetMovementAllowed(
                context,
                entity,
                allowed: false);
            SetTacticalState(
                context,
                entity,
                CombatOrderStatus.Holding,
                EntityId.Invalid,
                targetTransform.Position,
                hasTargetPosition: true,
                movementPaused: true);
            return;
        }

        WeaponDefinition weapon =
            _weapons.GetRequired(
                weaponState.WeaponId);
        float distanceSquared =
            Vector3.DistanceSquared(
                transform.Position,
                targetTransform.Position);
        float rangeSquared =
            weapon.RangeMeters *
            weapon.RangeMeters;

        FirePolicyState policy =
            context.Entities.TryGetComponent(
                entity,
                out FirePolicyState configuredPolicy)
                ? configuredPolicy
                : FirePolicyState.FireAtWill;

        if (distanceSquared <= rangeSquared &&
            policy.Permits(target))
        {
            ClearChaseMovement(
                context,
                entity);
            AssignWeaponTarget(
                context,
                entity,
                target);
            SetMovementAllowed(
                context,
                entity,
                allowed: false);
            SetTacticalState(
                context,
                entity,
                CombatOrderStatus.Engaging,
                target,
                targetTransform.Position,
                hasTargetPosition: true,
                movementPaused: true);
            return;
        }

        ClearWeaponTarget(
            context,
            entity);

        if (distanceSquared > rangeSquared &&
            CanNavigate(context, entity))
        {
            SetMovementAllowed(
                context,
                entity,
                allowed: true);
            IssueChaseMovement(
                context,
                entity,
                order,
                targetTransform.Position);
            SetTacticalState(
                context,
                entity,
                CombatOrderStatus.Pursuing,
                EntityId.Invalid,
                targetTransform.Position,
                hasTargetPosition: true,
                movementPaused: false);
            return;
        }

        SetMovementAllowed(
            context,
            entity,
            allowed: false);
        SetTacticalState(
            context,
            entity,
            CombatOrderStatus.Holding,
            EntityId.Invalid,
            targetTransform.Position,
            hasTargetPosition: true,
            movementPaused: true);
    }

    private void ProcessAttackMove(
        SimulationContext context,
        EntityId entity,
        in CombatOrderState order,
        in WorldTransform transform)
    {
        if (HasLegitimateCurrentWeaponTarget(
                context,
                entity,
                out EntityId target,
                out Vector3 targetPosition))
        {
            float leashSquared =
                order.PursuitLeashMeters *
                order.PursuitLeashMeters;
            float distanceFromAdvance =
                HorizontalDistanceSquared(
                    transform.Position,
                    targetPosition);

            if (distanceFromAdvance <= leashSquared ||
                order.PursuitLeashMeters <= 0.0f)
            {
                SetMovementAllowed(
                    context,
                    entity,
                    allowed: false);
                SetTacticalState(
                    context,
                    entity,
                    CombatOrderStatus.Engaging,
                    target,
                    targetPosition,
                    hasTargetPosition: true,
                    movementPaused: true);
                return;
            }

            ClearWeaponTarget(
                context,
                entity);
        }

        SetMovementAllowed(
            context,
            entity,
            allowed: true);
        EnsureMovementTowardDestination(
            context,
            entity,
            order);

        bool arrived =
            HorizontalDistanceSquared(
                transform.Position,
                order.Destination) <=
            4.0f;

        SetTacticalState(
            context,
            entity,
            arrived
                ? CombatOrderStatus.Complete
                : CombatOrderStatus.Advancing,
            EntityId.Invalid,
            default,
            hasTargetPosition: false,
            movementPaused: false);
    }

    private void ProcessHold(
        SimulationContext context,
        EntityId entity)
    {
        SetMovementAllowed(
            context,
            entity,
            allowed: false);

        if (HasLegitimateCurrentWeaponTarget(
                context,
                entity,
                out EntityId target,
                out Vector3 targetPosition))
        {
            SetTacticalState(
                context,
                entity,
                CombatOrderStatus.Engaging,
                target,
                targetPosition,
                hasTargetPosition: true,
                movementPaused: true);
            return;
        }

        SetTacticalState(
            context,
            entity,
            CombatOrderStatus.Holding,
            EntityId.Invalid,
            default,
            hasTargetPosition: false,
            movementPaused: true);
    }

    private void ProcessRetreat(
        SimulationContext context,
        EntityId entity,
        in CombatOrderState order,
        in WorldTransform transform)
    {
        ClearWeaponTarget(
            context,
            entity);
        SetMovementAllowed(
            context,
            entity,
            allowed: true);
        EnsureMovementTowardDestination(
            context,
            entity,
            order);

        bool arrived =
            HorizontalDistanceSquared(
                transform.Position,
                order.Destination) <=
            4.0f;

        SetTacticalState(
            context,
            entity,
            arrived
                ? CombatOrderStatus.Complete
                : CombatOrderStatus.Retreating,
            EntityId.Invalid,
            default,
            hasTargetPosition: false,
            movementPaused: false);
    }

    private void CoordinateGroupTargets(
        SimulationContext context)
    {
        GatherCombatGroups(context);

        for (int groupIndex = 0;
             groupIndex < _groups.Count;
             groupIndex++)
        {
            EntityId group =
                _groups[groupIndex];

            if (!context.Entities.IsAlive(group) ||
                !context.Entities.TryGetComponent(
                    group,
                    out CombatGroupIntent intent) ||
                intent.Kind is not
                    CombatOrderKind.AttackMove and not
                    CombatOrderKind.HoldPosition ||
                !_membersByGroup.TryGetValue(
                    group,
                    out List<EntityId>? members) ||
                members.Count < 2)
            {
                continue;
            }

            members.Sort();
            BuildGroupCandidates(
                context,
                members);

            if (_candidates.Count == 0)
            {
                continue;
            }

            _assignmentCounts.Clear();

            for (int memberIndex = 0;
                 memberIndex < members.Count;
                 memberIndex++)
            {
                EntityId member =
                    members[memberIndex];

                if (!context.Entities.IsAlive(member) ||
                    !context.Entities.TryGetComponent(
                        member,
                        out WeaponState weaponState) ||
                    !context.Entities.TryGetComponent(
                        member,
                        out WorldTransform memberTransform) ||
                    !TryGetFaction(
                        context.Entities,
                        member,
                        out FactionId faction))
                {
                    continue;
                }

                WeaponDefinition weapon =
                    _weapons.GetRequired(
                        weaponState.WeaponId);
                FirePolicyState policy =
                    context.Entities.TryGetComponent(
                        member,
                        out FirePolicyState configured)
                            ? configured
                            : FirePolicyState.FireAtWill;

                EntityId selected =
                    SelectGroupTarget(
                        context,
                        faction,
                        member,
                        memberTransform.Position,
                        weapon,
                        policy);

                if (!selected.IsValid)
                {
                    continue;
                }

                AssignWeaponTarget(
                    context,
                    member,
                    selected);
                SetMovementAllowed(
                    context,
                    member,
                    allowed: false);

                GroupCandidate selectedCandidate =
                    _candidates.Find(
                        candidate =>
                            candidate.Entity == selected);

                SetTacticalState(
                    context,
                    member,
                    CombatOrderStatus.Engaging,
                    selected,
                    selectedCandidate.Position,
                    hasTargetPosition: true,
                    movementPaused: true);

                _assignmentCounts[selected] =
                    _assignmentCounts.TryGetValue(
                        selected,
                        out int count)
                        ? count + 1
                        : 1;
                _groupAssignmentsThisTick++;
                _totalGroupAssignments++;
            }
        }
    }

    private void GatherCombatGroups(
        SimulationContext context)
    {
        _groups.Clear();

        foreach (List<EntityId> members in
                 _membersByGroup.Values)
        {
            members.Clear();
        }

        foreach (EntityId group in
                 context.Entities.Query<CombatGroupIntent>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            _groups.Add(group);
        }

        foreach (EntityId member in
                 context.Entities.Query<CombatGroupMember>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            CombatGroupMember membership =
                context.Entities.GetComponent<CombatGroupMember>(
                    member);

            if (!context.Entities.IsAlive(membership.Group))
            {
                continue;
            }

            if (!_membersByGroup.TryGetValue(
                    membership.Group,
                    out List<EntityId>? members))
            {
                members = new List<EntityId>();
                _membersByGroup.Add(
                    membership.Group,
                    members);
            }

            members.Add(member);
        }
    }

    private void BuildGroupCandidates(
        SimulationContext context,
        List<EntityId> members)
    {
        _candidates.Clear();

        if (members.Count == 0 ||
            !TryGetFaction(
                context.Entities,
                members[0],
                out FactionId faction))
        {
            return;
        }

        foreach (EntityId candidate in
                 context.Entities.Query<Combatant>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (!context.Entities.IsAlive(candidate) ||
                !context.Entities.TryGetComponent(
                    candidate,
                    out Combatant combatant) ||
                combatant.Faction == faction ||
                !_intelligence.IsEntityCurrentlyIdentified(
                    faction,
                    candidate))
            {
                continue;
            }

            if (!context.Entities.TryGetComponent(
                    candidate,
                    out WorldTransform transform) ||
                !context.Entities.TryGetComponent(
                    candidate,
                    out HealthState health) ||
                health.IsDepleted ||
                !context.Entities.TryGetComponent(
                    candidate,
                    out Targetable targetable))
            {
                continue;
            }

            int priority =
                context.Entities.TryGetComponent(
                    candidate,
                    out TargetPriority configuredPriority)
                    ? configuredPriority.Value
                    : 0;

            _candidates.Add(
                new GroupCandidate(
                    candidate,
                    transform.Position,
                    targetable.Class,
                    priority));
        }

        _candidates.Sort(
            static (left, right) =>
            {
                int priority =
                    right.Priority.CompareTo(
                        left.Priority);

                return priority != 0
                    ? priority
                    : left.Entity.CompareTo(
                        right.Entity);
            });
    }

    private EntityId SelectGroupTarget(
        SimulationContext context,
        FactionId faction,
        EntityId member,
        Vector3 memberPosition,
        WeaponDefinition weapon,
        in FirePolicyState firePolicy)
    {
        EntityId best =
            EntityId.Invalid;
        int bestAssignments =
            int.MaxValue;
        int bestPriority =
            int.MinValue;
        float bestDistanceSquared =
            float.PositiveInfinity;

        for (int index = 0;
             index < _candidates.Count;
             index++)
        {
            GroupCandidate candidate =
                _candidates[index];

            if (!_intelligence.IsEntityCurrentlyIdentified(
                    faction,
                    candidate.Entity) ||
                !weapon.Effectiveness.CanEngage(
                    candidate.TargetClass) ||
                !firePolicy.Permits(
                    candidate.Entity))
            {
                continue;
            }

            float distanceSquared =
                Vector3.DistanceSquared(
                    memberPosition,
                    candidate.Position);
            float rangeSquared =
                weapon.RangeMeters *
                weapon.RangeMeters;

            if (distanceSquared > rangeSquared)
            {
                continue;
            }

            int assignments =
                _assignmentCounts.TryGetValue(
                    candidate.Entity,
                    out int count)
                    ? count
                    : 0;

            if (assignments < bestAssignments ||
                (assignments == bestAssignments &&
                 (candidate.Priority > bestPriority ||
                  (candidate.Priority == bestPriority &&
                   (distanceSquared < bestDistanceSquared ||
                    (distanceSquared == bestDistanceSquared &&
                     (!best.IsValid ||
                      candidate.Entity < best)))))))
            {
                best =
                    candidate.Entity;
                bestAssignments =
                    assignments;
                bestPriority =
                    candidate.Priority;
                bestDistanceSquared =
                    distanceSquared;
            }
        }

        _ = context;
        _ = member;
        return best;
    }

    private bool HasLegitimateCurrentWeaponTarget(
        SimulationContext context,
        EntityId entity,
        out EntityId target,
        out Vector3 targetPosition)
    {
        target =
            EntityId.Invalid;
        targetPosition =
            default;

        if (!context.Entities.TryGetComponent(
                entity,
                out WeaponState weapon) ||
            !weapon.Target.IsValid ||
            !TryGetLegitimateTarget(
                context,
                entity,
                weapon.Target,
                out WorldTransform transform,
                out _))
        {
            return false;
        }

        target =
            weapon.Target;
        targetPosition =
            transform.Position;
        return true;
    }

    private bool TryGetLegitimateTarget(
        SimulationContext context,
        EntityId source,
        EntityId target,
        out WorldTransform targetTransform,
        out Targetable targetable)
    {
        targetTransform =
            default;
        targetable =
            default;

        if (!target.IsValid ||
            !context.Entities.IsAlive(target) ||
            !TryGetFaction(
                context.Entities,
                source,
                out FactionId sourceFaction) ||
            !context.Entities.TryGetComponent(
                target,
                out Combatant targetCombatant) ||
            targetCombatant.Faction == sourceFaction ||
            !_intelligence.IsEntityCurrentlyIdentified(
                sourceFaction,
                target))
        {
            return false;
        }

        if (!context.Entities.TryGetComponent(
                target,
                out HealthState health) ||
            health.IsDepleted ||
            !context.Entities.TryGetComponent(
                target,
                out Targetable resolvedTargetable))
        {
            return false;
        }

        if (context.Entities.TryGetComponent(
                source,
                out WeaponState weaponState))
        {
            WeaponDefinition weapon =
                _weapons.GetRequired(
                    weaponState.WeaponId);

            if (!weapon.Effectiveness.CanEngage(
                    resolvedTargetable.Class))
            {
                return false;
            }
        }

        if (!context.Entities.TryGetComponent(
                target,
                out targetTransform))
        {
            return false;
        }

        targetable =
            resolvedTargetable;
        return true;
    }

    private static bool TryGetFaction(
        EntityRegistry entities,
        EntityId entity,
        out FactionId faction)
    {
        if (entities.TryGetComponent(
                entity,
                out Combatant combatant))
        {
            faction =
                combatant.Faction;
            return faction.IsSpecified;
        }

        if (entities.TryGetComponent(
                entity,
                out IntelligenceSignature signature))
        {
            faction =
                signature.Faction;
            return faction.IsSpecified;
        }

        faction =
            FactionId.None;
        return false;
    }

    private static void AssignWeaponTarget(
        SimulationContext context,
        EntityId entity,
        EntityId target)
    {
        if (!context.Entities.TryGetComponent(
                entity,
                out WeaponState weapon))
        {
            return;
        }

        if (weapon.Target == target)
        {
            return;
        }

        context.Entities.SetComponent(
            entity,
            weapon with
            {
                Target = target
            });
    }

    private static void ClearWeaponTarget(
        SimulationContext context,
        EntityId entity)
    {
        if (!context.Entities.TryGetComponent(
                entity,
                out WeaponState weapon) ||
            !weapon.Target.IsValid)
        {
            return;
        }

        context.Entities.SetComponent(
            entity,
            weapon with
            {
                Target = EntityId.Invalid
            });
    }

    private static bool CanNavigate(
        SimulationContext context,
        EntityId entity) =>
        context.Entities.HasComponent<GroundMovement>(entity) &&
        context.Entities.HasComponent<GroundMovementState>(entity) &&
        context.Entities.HasComponent<NavigationAgent>(entity);

    private static void EnsureMovementTowardDestination(
        SimulationContext context,
        EntityId entity,
        in CombatOrderState order)
    {
        if (!order.HasDestination ||
            context.Entities.HasComponent<MovementOrder>(entity) ||
            context.Entities.HasComponent<NavigationPendingPath>(entity) ||
            context.Entities.HasComponent<NavigationRouteState>(entity) ||
            context.Entities.HasComponent<MovementGroupMember>(entity))
        {
            return;
        }

        var movement =
            new MovementOrder(
                order.Issuer,
                order.Destination,
                order.SubmittedAtTick,
                context.Tick);

        context.Entities.AddComponent(
            entity,
            movement);
    }

    private static void IssueChaseMovement(
        SimulationContext context,
        EntityId entity,
        in CombatOrderState order,
        Vector3 targetPosition)
    {
        var movement =
            new MovementOrder(
                order.Issuer,
                targetPosition,
                order.SubmittedAtTick,
                context.Tick);

        if (context.Entities.HasComponent<MovementOrder>(entity))
        {
            context.Entities.SetComponent(
                entity,
                movement);
        }
        else
        {
            context.Entities.AddComponent(
                entity,
                movement);
        }
    }

    private static void ClearChaseMovement(
        SimulationContext context,
        EntityId entity)
    {
        TacticalCommandUtilities.RemoveIfPresent<MovementOrder>(
            context,
            entity);
        TacticalCommandUtilities.RemoveIfPresent<NavigationPendingPath>(
            context,
            entity);
        TacticalCommandUtilities.RemoveIfPresent<NavigationRouteState>(
            context,
            entity);
        TacticalCommandUtilities.RemoveIfPresent<NavigationFailureState>(
            context,
            entity);
    }

    private static void SetMovementAllowed(
        SimulationContext context,
        EntityId entity,
        bool allowed)
    {
        var constraint =
            new TacticalMovementConstraint(allowed);

        if (context.Entities.HasComponent<
                TacticalMovementConstraint>(entity))
        {
            context.Entities.SetComponent(
                entity,
                constraint);
        }
        else
        {
            context.Entities.AddComponent(
                entity,
                constraint);
        }
    }

    private static void SetTacticalState(
        SimulationContext context,
        EntityId entity,
        CombatOrderStatus status,
        EntityId target,
        Vector3 targetPosition,
        bool hasTargetPosition,
        bool movementPaused)
    {
        bool resupplyRequested =
            context.Entities.TryGetComponent(
                entity,
                out TacticalCombatState existing) &&
            existing.ResupplyRequested;

        var state =
            new TacticalCombatState(
                status,
                target,
                targetPosition,
                hasTargetPosition,
                movementPaused,
                resupplyRequested,
                context.Tick);

        if (context.Entities.HasComponent<TacticalCombatState>(entity))
        {
            context.Entities.SetComponent(
                entity,
                state);
        }
        else
        {
            context.Entities.AddComponent(
                entity,
                state);
        }
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

    private readonly record struct GroupCandidate(
        EntityId Entity,
        Vector3 Position,
        TargetClass TargetClass,
        int Priority);
}
