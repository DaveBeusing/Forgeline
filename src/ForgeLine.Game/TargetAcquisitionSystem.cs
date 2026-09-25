using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game;

public readonly record struct TargetAcquisitionMetrics(
    int ScansThisTick,
    int CandidatesThisTick,
    int AcquisitionsThisTick,
    int ReacquisitionsThisTick,
    int RejectionsThisTick,
    ulong TotalScans,
    ulong TotalCandidates,
    ulong TotalAcquisitions,
    ulong TotalReacquisitions,
    ulong TotalRejections,
    ulong FriendlyRejections,
    ulong TargetClassRejections,
    ulong RangeRejections,
    ulong AvailabilityRejections,
    ulong LineOfFireRejections,
    ulong FirePolicyRejections);

public readonly record struct TargetRejectionDebugEntry(
    EntityId Source,
    EntityId Candidate,
    Vector3 CandidatePosition,
    TargetRejectionReason Reason);

public sealed class TargetAcquisitionSystem : ISimulationSystem
{
    private const int MaximumDebugRejections = 256;

    private readonly WeaponCatalog _weapons;
    private readonly SpatialGridIndex? _spatialIndex;
    private readonly ITargetAvailabilityPolicy _targetAvailability;
    private readonly ILineOfFirePolicy _lineOfFire;
    private readonly SpatialQueryBuffer _queryBuffer = new(256);
    private readonly List<TargetRejectionDebugEntry> _debugRejections =
        new(MaximumDebugRejections);

    private int _scansThisTick;
    private int _candidatesThisTick;
    private int _acquisitionsThisTick;
    private int _reacquisitionsThisTick;
    private int _rejectionsThisTick;

    private ulong _totalScans;
    private ulong _totalCandidates;
    private ulong _totalAcquisitions;
    private ulong _totalReacquisitions;
    private ulong _totalRejections;
    private ulong _friendlyRejections;
    private ulong _targetClassRejections;
    private ulong _rangeRejections;
    private ulong _availabilityRejections;
    private ulong _lineOfFireRejections;
    private ulong _firePolicyRejections;

    public TargetAcquisitionSystem(
        WeaponCatalog weapons,
        SpatialGridIndex? spatialIndex = null,
        ITargetAvailabilityPolicy? targetAvailability = null,
        ILineOfFirePolicy? lineOfFire = null)
    {
        _weapons = weapons ??
            throw new ArgumentNullException(nameof(weapons));
        _spatialIndex = spatialIndex;
        _targetAvailability =
            targetAvailability ??
            AlwaysTargetAvailablePolicy.Instance;
        _lineOfFire =
            lineOfFire ??
            UnobstructedLineOfFirePolicy.Instance;
    }

    public SimulationPhase Phase => SimulationPhase.Sensors;

    public bool DebugCaptureEnabled { get; set; }

    public IReadOnlyList<TargetRejectionDebugEntry> DebugRejections =>
        _debugRejections;

    public TargetAcquisitionMetrics Metrics =>
        new(
            _scansThisTick,
            _candidatesThisTick,
            _acquisitionsThisTick,
            _reacquisitionsThisTick,
            _rejectionsThisTick,
            _totalScans,
            _totalCandidates,
            _totalAcquisitions,
            _totalReacquisitions,
            _totalRejections,
            _friendlyRejections,
            _targetClassRejections,
            _rangeRejections,
            _availabilityRejections,
            _lineOfFireRejections,
            _firePolicyRejections);

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ResetTickMetrics();

        foreach (EntityId source in
                 context.Entities.Query<WeaponState, WorldTransform>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (!context.Entities.TryGetComponent(
                    source,
                    out Combatant sourceCombatant))
            {
                continue;
            }

            WeaponState weaponState =
                context.Entities.GetComponent<WeaponState>(
                    source);
            WorldTransform sourceTransform =
                context.Entities.GetComponent<WorldTransform>(
                    source);
            WeaponDefinition weapon =
                _weapons.GetRequired(
                    weaponState.WeaponId);
            FirePolicyState firePolicy =
                context.Entities.TryGetComponent(
                    source,
                    out FirePolicyState configuredPolicy)
                    ? configuredPolicy
                    : FirePolicyState.FireAtWill;

            if (firePolicy.Policy == FirePolicy.HoldFire)
            {
                continue;
            }

            bool hadTarget = weaponState.Target.IsValid;
            if (hadTarget &&
                IsValidTarget(
                    context,
                    source,
                    sourceCombatant,
                    sourceTransform,
                    weapon,
                    firePolicy,
                    weaponState.Target,
                    requireTargetable: false,
                    recordRejection: false,
                    out _,
                    out _))
            {
                continue;
            }

            if (hadTarget)
            {
                weaponState =
                    weaponState with
                    {
                        Target = EntityId.Invalid
                    };
                context.Entities.SetComponent(
                    source,
                    weaponState);
            }

            bool autoTargetEnabled =
                !context.Entities.TryGetComponent(
                    source,
                    out AutoTargetState autoTarget) ||
                autoTarget.Enabled;

            if (!autoTargetEnabled)
            {
                continue;
            }

            _scansThisTick++;
            _totalScans++;

            EntityId selected =
                firePolicy.Policy == FirePolicy.ReturnFire
                    ? AcquireRetaliationTarget(
                        context,
                        source,
                        sourceCombatant,
                        sourceTransform,
                        weapon,
                        firePolicy)
                    : AcquireBestTarget(
                        context,
                        source,
                        sourceCombatant,
                        sourceTransform,
                        weapon,
                        firePolicy);

            if (!selected.IsValid)
            {
                continue;
            }

            context.Entities.SetComponent(
                source,
                weaponState with
                {
                    Target = selected
                });

            _acquisitionsThisTick++;
            _totalAcquisitions++;

            if (hadTarget)
            {
                _reacquisitionsThisTick++;
                _totalReacquisitions++;
            }
        }
    }

    private EntityId AcquireRetaliationTarget(
        SimulationContext context,
        EntityId source,
        in Combatant sourceCombatant,
        in WorldTransform sourceTransform,
        WeaponDefinition weapon,
        in FirePolicyState firePolicy)
    {
        EntityId retaliationTarget =
            firePolicy.RetaliationTarget;

        if (!retaliationTarget.IsValid)
        {
            return EntityId.Invalid;
        }

        _candidatesThisTick++;
        _totalCandidates++;

        return IsValidTarget(
            context,
            source,
            sourceCombatant,
            sourceTransform,
            weapon,
            firePolicy,
            retaliationTarget,
            requireTargetable: false,
            recordRejection: true,
            out _,
            out _)
                ? retaliationTarget
                : EntityId.Invalid;
    }

    private EntityId AcquireBestTarget(
        SimulationContext context,
        EntityId source,
        in Combatant sourceCombatant,
        in WorldTransform sourceTransform,
        WeaponDefinition weapon,
        in FirePolicyState firePolicy)
    {
        EntityId best = EntityId.Invalid;
        int bestPriority = int.MinValue;
        float bestDistanceSquared = float.PositiveInfinity;

        if (_spatialIndex is not null)
        {
            _spatialIndex.QueryRadius(
                sourceTransform.Position,
                weapon.RangeMeters,
                _queryBuffer,
                order: SpatialQueryOrder.StableEntityId);

            ReadOnlySpan<EntityId> candidates =
                _queryBuffer.Results;

            for (int index = 0;
                 index < candidates.Length;
                 index++)
            {
                ConsiderCandidate(
                    context,
                    source,
                    sourceCombatant,
                    sourceTransform,
                    weapon,
                    firePolicy,
                    candidates[index],
                    ref best,
                    ref bestPriority,
                    ref bestDistanceSquared);
            }
        }
        else
        {
            foreach (EntityId candidate in
                     context.Entities.Query<Combatant, WorldTransform>(
                         QueryIterationOrder.StableByEntityIndex))
            {
                ConsiderCandidate(
                    context,
                    source,
                    sourceCombatant,
                    sourceTransform,
                    weapon,
                    firePolicy,
                    candidate,
                    ref best,
                    ref bestPriority,
                    ref bestDistanceSquared);
            }
        }

        return best;
    }

    private void ConsiderCandidate(
        SimulationContext context,
        EntityId source,
        in Combatant sourceCombatant,
        in WorldTransform sourceTransform,
        WeaponDefinition weapon,
        in FirePolicyState firePolicy,
        EntityId candidate,
        ref EntityId best,
        ref int bestPriority,
        ref float bestDistanceSquared)
    {
        if (candidate == source)
        {
            return;
        }

        _candidatesThisTick++;
        _totalCandidates++;

        if (!IsValidTarget(
                context,
                source,
                sourceCombatant,
                sourceTransform,
                weapon,
                firePolicy,
                candidate,
                requireTargetable: true,
                recordRejection: true,
                out int priority,
                out float distanceSquared))
        {
            return;
        }

        if (priority > bestPriority ||
            (priority == bestPriority &&
             (distanceSquared < bestDistanceSquared ||
              (distanceSquared == bestDistanceSquared &&
               (!best.IsValid ||
                candidate < best)))))
        {
            best = candidate;
            bestPriority = priority;
            bestDistanceSquared = distanceSquared;
        }
    }

    private bool IsValidTarget(
        SimulationContext context,
        EntityId source,
        in Combatant sourceCombatant,
        in WorldTransform sourceTransform,
        WeaponDefinition weapon,
        in FirePolicyState firePolicy,
        EntityId candidate,
        bool requireTargetable,
        bool recordRejection,
        out int priority,
        out float distanceSquared)
    {
        priority = 0;
        distanceSquared = float.PositiveInfinity;
        Vector3 candidatePosition = default;

        if (!candidate.IsValid ||
            !context.Entities.IsAlive(candidate))
        {
            Reject(
                source,
                candidate,
                candidatePosition,
                TargetRejectionReason.Dead,
                recordRejection);
            return false;
        }

        if (!context.Entities.TryGetComponent(
                candidate,
                out Combatant targetCombatant) ||
            targetCombatant.Faction == sourceCombatant.Faction)
        {
            Reject(
                source,
                candidate,
                candidatePosition,
                TargetRejectionReason.Friendly,
                recordRejection);
            return false;
        }

        bool hasTargetClass =
            context.Entities.TryGetComponent(
                candidate,
                out Targetable targetable);

        if (requireTargetable &&
            !hasTargetClass)
        {
            Reject(
                source,
                candidate,
                candidatePosition,
                TargetRejectionReason.NotTargetable,
                recordRejection);
            return false;
        }

        if (hasTargetClass &&
            !weapon.Effectiveness.CanEngage(
                targetable.Class))
        {
            Reject(
                source,
                candidate,
                candidatePosition,
                TargetRejectionReason.UnsupportedTargetClass,
                recordRejection);
            return false;
        }

        if (!context.Entities.TryGetComponent(
                candidate,
                out HealthState health) ||
            health.IsDepleted)
        {
            Reject(
                source,
                candidate,
                candidatePosition,
                TargetRejectionReason.MissingHealth,
                recordRejection);
            return false;
        }

        if (!context.Entities.TryGetComponent(
                candidate,
                out WorldTransform targetTransform))
        {
            Reject(
                source,
                candidate,
                candidatePosition,
                TargetRejectionReason.MissingTransform,
                recordRejection);
            return false;
        }

        candidatePosition =
            targetTransform.Position;
        Vector3 delta =
            targetTransform.Position -
            sourceTransform.Position;
        distanceSquared =
            delta.LengthSquared();
        float rangeSquared =
            weapon.RangeMeters *
            weapon.RangeMeters;

        if (distanceSquared > rangeSquared)
        {
            Reject(
                source,
                candidate,
                candidatePosition,
                TargetRejectionReason.OutOfRange,
                recordRejection);
            return false;
        }

        if (!_targetAvailability.IsTargetAvailable(
                source,
                candidate))
        {
            Reject(
                source,
                candidate,
                candidatePosition,
                TargetRejectionReason.UnavailableIntelligence,
                recordRejection);
            return false;
        }

        if (!_lineOfFire.HasLineOfFire(
                source,
                candidate,
                sourceTransform.Position,
                targetTransform.Position))
        {
            Reject(
                source,
                candidate,
                candidatePosition,
                TargetRejectionReason.BlockedLineOfFire,
                recordRejection);
            return false;
        }

        if (!firePolicy.Permits(candidate))
        {
            Reject(
                source,
                candidate,
                candidatePosition,
                TargetRejectionReason.FirePolicy,
                recordRejection);
            return false;
        }

        if (context.Entities.TryGetComponent(
                candidate,
                out TargetPriority targetPriority))
        {
            priority =
                targetPriority.Value;
        }

        return true;
    }

    private void Reject(
        EntityId source,
        EntityId candidate,
        Vector3 candidatePosition,
        TargetRejectionReason reason,
        bool recordDebug)
    {
        _rejectionsThisTick++;
        _totalRejections++;

        switch (reason)
        {
            case TargetRejectionReason.Friendly:
                _friendlyRejections++;
                break;
            case TargetRejectionReason.UnsupportedTargetClass:
                _targetClassRejections++;
                break;
            case TargetRejectionReason.OutOfRange:
                _rangeRejections++;
                break;
            case TargetRejectionReason.UnavailableIntelligence:
                _availabilityRejections++;
                break;
            case TargetRejectionReason.BlockedLineOfFire:
                _lineOfFireRejections++;
                break;
            case TargetRejectionReason.FirePolicy:
                _firePolicyRejections++;
                break;
        }

        if (!DebugCaptureEnabled ||
            !recordDebug ||
            _debugRejections.Count >= MaximumDebugRejections)
        {
            return;
        }

        _debugRejections.Add(
            new TargetRejectionDebugEntry(
                source,
                candidate,
                candidatePosition,
                reason));
    }

    private void ResetTickMetrics()
    {
        _scansThisTick = 0;
        _candidatesThisTick = 0;
        _acquisitionsThisTick = 0;
        _reacquisitionsThisTick = 0;
        _rejectionsThisTick = 0;
        _debugRejections.Clear();
    }
}
