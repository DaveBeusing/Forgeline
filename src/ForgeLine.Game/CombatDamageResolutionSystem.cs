using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public readonly record struct CombatDamageResolutionMetrics(
    ulong ArmoredHits,
    ulong FrontHits,
    ulong SideHits,
    ulong RearHits,
    ulong TopHits,
    double TotalMitigatedDamage);

public sealed class CombatDamageResolutionSystem : ISimulationSystem
{
    private readonly CombatRuntime _runtime;
    private readonly WeaponCatalog? _weapons;
    private readonly ArmorCatalog? _armor;

    private ulong _armoredHits;
    private ulong _frontHits;
    private ulong _sideHits;
    private ulong _rearHits;
    private ulong _topHits;
    private double _totalMitigatedDamage;

    public CombatDamageResolutionSystem(
        CombatRuntime runtime,
        WeaponCatalog? weapons = null,
        ArmorCatalog? armor = null)
    {
        _runtime = runtime ??
            throw new ArgumentNullException(nameof(runtime));
        _weapons = weapons;
        _armor = armor;
    }

    public SimulationPhase Phase =>
        SimulationPhase.DamageResolution;

    public CombatDamageResolutionMetrics Metrics =>
        new(
            _armoredHits,
            _frontHits,
            _sideHits,
            _rearHits,
            _topHits,
            _totalMitigatedDamage);

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _runtime.BeginTick(context.Tick);

        ReadOnlySpan<PendingCombatDamage> pending =
            _runtime.PendingDamage;

        for (int index = 0;
             index < pending.Length;
             index++)
        {
            PendingCombatDamage request =
                pending[index];

            if (!context.Entities.IsAlive(
                    request.Target) ||
                !context.Entities.TryGetComponent(
                    request.Target,
                    out HealthState health) ||
                health.IsDepleted)
            {
                continue;
            }

            DamagePayload resolvedDamage =
                ResolveDamage(
                    context,
                    request);

            HealthState updated =
                health.ApplyDamage(
                    resolvedDamage,
                    out double appliedDamage);

            if (appliedDamage <= 0.0)
            {
                continue;
            }

            context.Entities.SetComponent(
                request.Target,
                updated);
            RecordRetaliation(
                context,
                request);
            _runtime.RecordDamage(
                request,
                appliedDamage);

            if (updated.IsDepleted)
            {
                _runtime.QueueHealthDestruction(
                    request);
            }
        }

        _runtime.ClearPendingDamage();
    }

    private DamagePayload ResolveDamage(
        SimulationContext context,
        in PendingCombatDamage request)
    {
        if (!context.Entities.TryGetComponent(
                request.Target,
                out ArmorState armorState))
        {
            return request.Damage;
        }

        if (_weapons is null || _armor is null)
        {
            throw new InvalidOperationException(
                "Armored combatants require weapon and armor catalogs during damage resolution.");
        }

        if (!context.Entities.TryGetComponent(
                request.Target,
                out WorldTransform targetTransform))
        {
            throw new InvalidOperationException(
                $"Armored entity {request.Target} has no authoritative world transform.");
        }

        WeaponDefinition weapon =
            _weapons.GetRequired(
                request.Weapon);
        ArmorProfileDefinition armor =
            _armor.GetRequired(
                armorState.ProfileId);
        ArmorZone zone =
            ArmorFacing.Classify(
                targetTransform.Rotation,
                request.IncomingDirection);
        ArmorDamageResult result =
            ArmorDamageResolver.Resolve(
                request.Damage,
                weapon.Effectiveness,
                armor,
                zone);

        _armoredHits++;
        _totalMitigatedDamage += result.MitigatedDamage;

        switch (zone)
        {
            case ArmorZone.Front:
                _frontHits++;
                break;
            case ArmorZone.Side:
                _sideHits++;
                break;
            case ArmorZone.Rear:
                _rearHits++;
                break;
            case ArmorZone.Top:
                _topHits++;
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(zone));
        }

        return new DamagePayload(
            result.AppliedDamage);
    }

    private static void RecordRetaliation(
        SimulationContext context,
        in PendingCombatDamage request)
    {
        if (!request.Source.IsValid ||
            !context.Entities.IsAlive(request.Source) ||
            !context.Entities.TryGetComponent(
                request.Target,
                out FirePolicyState policy) ||
            policy.Policy != FirePolicy.ReturnFire)
        {
            return;
        }

        context.Entities.SetComponent(
            request.Target,
            policy with
            {
                RetaliationTarget = request.Source
            });
    }
}
