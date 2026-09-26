using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public readonly record struct TacticalTestOpponentMetrics(
    int EvaluatedUnits,
    int AttackDecisionsThisTick,
    int AdvanceDecisionsThisTick,
    int ResupplyDecisionsThisTick,
    int RetreatDecisionsThisTick,
    ulong TotalAttackDecisions,
    ulong TotalAdvanceDecisions,
    ulong TotalResupplyDecisions,
    ulong TotalRetreatDecisions);

public sealed class TacticalTestOpponentSystem : ISimulationSystem
{
    private readonly FactionIntelligenceStore _intelligence;
    private readonly List<EntityId> _units = new();

    private ulong _totalAttackDecisions;
    private ulong _totalAdvanceDecisions;
    private ulong _totalResupplyDecisions;
    private ulong _totalRetreatDecisions;

    public TacticalTestOpponentSystem(
        FactionIntelligenceStore intelligence)
    {
        _intelligence = intelligence ??
            throw new ArgumentNullException(nameof(intelligence));
    }

    public SimulationPhase Phase =>
        SimulationPhase.AiDecisions;

    public TacticalTestOpponentMetrics Metrics { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _units.Clear();

        foreach (EntityId entity in
                 context.Entities.Query<
                     TacticalTestOpponent,
                     WorldTransform>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            _units.Add(entity);
        }

        int attacks = 0;
        int advances = 0;
        int resupplies = 0;
        int retreats = 0;

        for (int index = 0;
             index < _units.Count;
             index++)
        {
            EntityId entity = _units[index];

            if (!context.Entities.IsAlive(entity) ||
                !context.Entities.TryGetComponent(
                    entity,
                    out TacticalTestOpponent behavior) ||
                !context.Entities.TryGetComponent(
                    entity,
                    out WorldTransform transform) ||
                !context.Entities.TryGetComponent(
                    entity,
                    out ControllableEntity controllable) ||
                !controllable.IsControllable ||
                !TryGetFaction(
                    context,
                    entity,
                    out FactionId faction))
            {
                continue;
            }

            EnsureAutomaticResupplyPolicy(
                context,
                entity,
                behavior.ResupplyThreshold);

            if (context.Entities.HasComponent<ResupplyOrder>(entity))
            {
                resupplies++;
                _totalResupplyDecisions++;
                continue;
            }

            if (context.Entities.TryGetComponent(
                    entity,
                    out UnitCombatReadiness readiness))
            {
                double minimumSupply =
                    Math.Min(
                        readiness.Fuel,
                        readiness.Ammunition);

                if (minimumSupply <= behavior.ResupplyThreshold)
                {
                    resupplies++;
                    _totalResupplyDecisions++;
                    continue;
                }

                if (readiness.OverallReadiness <=
                    behavior.RetreatThreshold)
                {
                    Vector3 retreatDestination =
                        CalculateRetreatDestination(
                            faction,
                            transform.Position,
                            distanceMeters: 120.0f);

                    IssueRetreatIntent(
                        context,
                        entity,
                        controllable.Owner,
                        retreatDestination);

                    retreats++;
                    _totalRetreatDecisions++;
                    continue;
                }
            }

            if (TryFindBestContact(
                    context,
                    faction,
                    transform.Position,
                    out IntelligenceContact contact,
                    out EntityId identifiedTarget))
            {
                if (identifiedTarget.IsValid)
                {
                    IssueAttackIntent(
                        context,
                        entity,
                        controllable.Owner,
                        identifiedTarget,
                        transform.Position,
                        behavior.EngagementLeashMeters);

                    attacks++;
                    _totalAttackDecisions++;
                }
                else
                {
                    IssueAttackMoveIntent(
                        context,
                        entity,
                        controllable.Owner,
                        contact.LastKnownPosition,
                        transform.Position,
                        behavior.EngagementLeashMeters);

                    advances++;
                    _totalAdvanceDecisions++;
                }

                continue;
            }

            if (context.Entities.TryGetComponent(
                    entity,
                    out CombatOrderState strategicOrder) &&
                strategicOrder.Kind is
                    CombatOrderKind.AttackMove or
                    CombatOrderKind.Retreat)
            {
                continue;
            }

            if (context.Entities.TryGetComponent(
                    entity,
                    out CombatOrderState existingOrder) &&
                existingOrder.Kind is
                    CombatOrderKind.AttackMove or
                    CombatOrderKind.Retreat)
            {
                continue;
            }

            if (context.Entities.TryGetComponent(
                    entity,
                    out CombatOrderState existingOrder) &&
                existingOrder.Kind is
                    CombatOrderKind.AttackMove or
                    CombatOrderKind.Retreat)
            {
                continue;
            }

            IssueHoldIntent(
                context,
                entity,
                controllable.Owner,
                transform.Position);
        }

        Metrics =
            new TacticalTestOpponentMetrics(
                _units.Count,
                attacks,
                advances,
                resupplies,
                retreats,
                _totalAttackDecisions,
                _totalAdvanceDecisions,
                _totalResupplyDecisions,
                _totalRetreatDecisions);
    }

    private bool TryFindBestContact(
        SimulationContext context,
        FactionId faction,
        Vector3 origin,
        out IntelligenceContact selected,
        out EntityId identifiedTarget)
    {
        selected = default;
        identifiedTarget = EntityId.Invalid;

        FactionIntelligenceSnapshot snapshot =
            _intelligence.Capture(faction);

        bool found = false;
        int bestStateRank = int.MinValue;
        float bestDistanceSquared =
            float.PositiveInfinity;

        for (int index = 0;
             index < snapshot.Contacts.Count;
             index++)
        {
            IntelligenceContact contact =
                snapshot.Contacts[index];

            if (!contact.IsCurrent ||
                contact.State is not
                    IntelligenceState.Detected and not
                    IntelligenceState.Identified)
            {
                continue;
            }

            int stateRank =
                contact.State == IntelligenceState.Identified
                    ? 1
                    : 0;
            float distanceSquared =
                HorizontalDistanceSquared(
                    origin,
                    contact.LastKnownPosition);

            if (!found ||
                stateRank > bestStateRank ||
                (stateRank == bestStateRank &&
                 (distanceSquared < bestDistanceSquared ||
                  (distanceSquared == bestDistanceSquared &&
                   contact.ContactKey < selected.ContactKey))))
            {
                selected = contact;
                bestStateRank = stateRank;
                bestDistanceSquared = distanceSquared;
                found = true;
            }
        }

        if (!found)
        {
            return false;
        }

        if (selected.State == IntelligenceState.Identified &&
            _intelligence.TryResolveCurrentlyIdentifiedEntity(
                faction,
                selected.ContactKey,
                out EntityId resolved) &&
            context.Entities.IsAlive(resolved))
        {
            identifiedTarget = resolved;
        }

        return true;
    }

    private Vector3 CalculateRetreatDestination(
        FactionId faction,
        Vector3 origin,
        float distanceMeters)
    {
        FactionIntelligenceSnapshot snapshot =
            _intelligence.Capture(faction);

        Vector3 threatCentroid = Vector3.Zero;
        int threatCount = 0;

        for (int index = 0;
             index < snapshot.Contacts.Count;
             index++)
        {
            IntelligenceContact contact =
                snapshot.Contacts[index];

            if (!contact.IsCurrent)
            {
                continue;
            }

            threatCentroid +=
                contact.LastKnownPosition;
            threatCount++;
        }

        Vector3 away =
            threatCount > 0
                ? origin -
                  threatCentroid / threatCount
                : -Vector3.UnitZ;

        away.Y = 0.0f;

        if (away.LengthSquared() <= 0.000001f)
        {
            away = -Vector3.UnitZ;
        }
        else
        {
            away = Vector3.Normalize(away);
        }

        return origin +
            away * distanceMeters;
    }

    private static void IssueAttackIntent(
        SimulationContext context,
        EntityId entity,
        PlayerId owner,
        EntityId target,
        Vector3 anchor,
        float leash)
    {
        if (context.Entities.TryGetComponent(
                entity,
                out CombatOrderState existing) &&
            existing.Kind == CombatOrderKind.Attack &&
            existing.ExplicitTarget == target)
        {
            return;
        }

        TacticalCommandUtilities.ClearMovementIntent(
            context,
            entity);
        TacticalCommandUtilities.SetOrder(
            context,
            entity,
            new CombatOrderState(
                CombatOrderKind.Attack,
                owner,
                target,
                Vector3.Zero,
                hasDestination: false,
                anchor,
                leash,
                FormationTemplate.Compact,
                context.Tick,
                context.Tick));
    }

    private static void IssueAttackMoveIntent(
        SimulationContext context,
        EntityId entity,
        PlayerId owner,
        Vector3 destination,
        Vector3 anchor,
        float leash)
    {
        if (context.Entities.TryGetComponent(
                entity,
                out CombatOrderState existing) &&
            existing.Kind == CombatOrderKind.AttackMove &&
            HorizontalDistanceSquared(
                existing.Destination,
                destination) <= 4.0f)
        {
            return;
        }

        TacticalCommandUtilities.SetOrder(
            context,
            entity,
            new CombatOrderState(
                CombatOrderKind.AttackMove,
                owner,
                EntityId.Invalid,
                destination,
                hasDestination: true,
                anchor,
                leash,
                FormationTemplate.Compact,
                context.Tick,
                context.Tick));

        SetMovementOrder(
            context,
            entity,
            owner,
            destination);
    }

    private static void IssueRetreatIntent(
        SimulationContext context,
        EntityId entity,
        PlayerId owner,
        Vector3 destination)
    {
        if (context.Entities.TryGetComponent(
                entity,
                out CombatOrderState existing) &&
            existing.Kind == CombatOrderKind.Retreat)
        {
            return;
        }

        TacticalCommandUtilities.SetOrder(
            context,
            entity,
            new CombatOrderState(
                CombatOrderKind.Retreat,
                owner,
                EntityId.Invalid,
                destination,
                hasDestination: true,
                context.Entities.GetComponent<WorldTransform>(
                    entity).Position,
                pursuitLeashMeters: 0.0f,
                FormationTemplate.Column,
                context.Tick,
                context.Tick));

        SetMovementOrder(
            context,
            entity,
            owner,
            destination);
    }

    private static void IssueHoldIntent(
        SimulationContext context,
        EntityId entity,
        PlayerId owner,
        Vector3 position)
    {
        if (context.Entities.TryGetComponent(
                entity,
                out CombatOrderState existing) &&
            existing.Kind == CombatOrderKind.HoldPosition)
        {
            return;
        }

        TacticalCommandUtilities.ClearMovementIntent(
            context,
            entity);
        TacticalCommandUtilities.SetOrder(
            context,
            entity,
            new CombatOrderState(
                CombatOrderKind.HoldPosition,
                owner,
                EntityId.Invalid,
                position,
                hasDestination: false,
                position,
                pursuitLeashMeters: 0.0f,
                FormationTemplate.Compact,
                context.Tick,
                context.Tick));
    }

    private static void SetMovementOrder(
        SimulationContext context,
        EntityId entity,
        PlayerId owner,
        Vector3 destination)
    {
        var movement =
            new MovementOrder(
                owner,
                destination,
                context.Tick,
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

    private static void EnsureAutomaticResupplyPolicy(
        SimulationContext context,
        EntityId entity,
        double threshold)
    {
        var policy =
            new AutomaticResupplyPolicy(
                threshold,
                threshold,
                enabled: true);

        if (context.Entities.HasComponent<
                AutomaticResupplyPolicy>(entity))
        {
            context.Entities.SetComponent(
                entity,
                policy);
        }
        else
        {
            context.Entities.AddComponent(
                entity,
                policy);
        }
    }

    private static bool TryGetFaction(
        SimulationContext context,
        EntityId entity,
        out FactionId faction)
    {
        if (context.Entities.TryGetComponent(
                entity,
                out Combatant combatant))
        {
            faction = combatant.Faction;
            return faction.IsSpecified;
        }

        if (context.Entities.TryGetComponent(
                entity,
                out IntelligenceSignature signature))
        {
            faction = signature.Faction;
            return faction.IsSpecified;
        }

        faction = FactionId.None;
        return false;
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