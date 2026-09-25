using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public readonly record struct CombatRuntimeMetrics(
    int ActiveProjectiles,
    int ShotsFiredThisTick,
    double AmmunitionConsumedThisTick,
    int ProjectilesSpawnedThisTick,
    int ImpactsThisTick,
    int HitsThisTick,
    double DamageAppliedThisTick,
    int DestructionsThisTick,
    ulong TotalShotsFired,
    double TotalAmmunitionConsumed,
    ulong TotalProjectilesSpawned,
    ulong TotalImpacts,
    ulong TotalHits,
    double TotalDamageApplied,
    ulong TotalDestructions);

internal readonly record struct PendingCombatDamage(
    EntityId Source,
    EntityId Target,
    EntityId Projectile,
    WeaponId Weapon,
    Vector3 Position,
    Vector3 IncomingDirection,
    DamagePayload Damage);

internal readonly record struct PendingCombatDestruction(
    EntityId Entity,
    EntityId Source,
    WeaponId Weapon,
    Vector3 Position,
    bool EmitCombatDestructionEvent);

public sealed class CombatRuntime
{
    private readonly List<CombatEvent> _events = new();
    private readonly List<PendingCombatDamage> _pendingDamage = new();
    private readonly List<PendingCombatDestruction> _pendingDestructions = new();
    private readonly HashSet<EntityId> _pendingDestructionEntities = new();

    private SimulationTick _tick;
    private int _activeProjectiles;
    private int _shotsFiredThisTick;
    private double _ammunitionConsumedThisTick;
    private int _projectilesSpawnedThisTick;
    private int _impactsThisTick;
    private int _hitsThisTick;
    private double _damageAppliedThisTick;
    private int _destructionsThisTick;

    private ulong _totalShotsFired;
    private double _totalAmmunitionConsumed;
    private ulong _totalProjectilesSpawned;
    private ulong _totalImpacts;
    private ulong _totalHits;
    private double _totalDamageApplied;
    private ulong _totalDestructions;

    public SimulationTick CurrentTick => _tick;

    public IReadOnlyList<CombatEvent> Events => _events;

    public CombatRuntimeMetrics Metrics =>
        new(
            _activeProjectiles,
            _shotsFiredThisTick,
            _ammunitionConsumedThisTick,
            _projectilesSpawnedThisTick,
            _impactsThisTick,
            _hitsThisTick,
            _damageAppliedThisTick,
            _destructionsThisTick,
            _totalShotsFired,
            _totalAmmunitionConsumed,
            _totalProjectilesSpawned,
            _totalImpacts,
            _totalHits,
            _totalDamageApplied,
            _totalDestructions);

    internal ReadOnlySpan<PendingCombatDamage> PendingDamage =>
        CollectionsMarshal.AsSpan(_pendingDamage);

    internal ReadOnlySpan<PendingCombatDestruction> PendingDestructions =>
        CollectionsMarshal.AsSpan(_pendingDestructions);

    internal void BeginTick(SimulationTick tick)
    {
        if (_tick == tick)
        {
            return;
        }

        _tick = tick;
        _events.Clear();
        _shotsFiredThisTick = 0;
        _ammunitionConsumedThisTick = 0.0;
        _projectilesSpawnedThisTick = 0;
        _impactsThisTick = 0;
        _hitsThisTick = 0;
        _damageAppliedThisTick = 0.0;
        _destructionsThisTick = 0;
    }

    internal void RecordShot(
        EntityId source,
        EntityId target,
        WeaponId weapon,
        Vector3 position,
        double ammunitionConsumed)
    {
        _shotsFiredThisTick++;
        _ammunitionConsumedThisTick += ammunitionConsumed;
        _totalShotsFired++;
        _totalAmmunitionConsumed += ammunitionConsumed;

        _events.Add(
            new CombatEvent(
                _tick,
                CombatEventType.ShotFired,
                source,
                target,
                EntityId.Invalid,
                weapon,
                position,
                ammunitionConsumed));
    }

    internal void RecordProjectileSpawned(
        EntityId source,
        EntityId target,
        EntityId projectile,
        WeaponId weapon,
        Vector3 position)
    {
        _projectilesSpawnedThisTick++;
        _totalProjectilesSpawned++;

        _events.Add(
            new CombatEvent(
                _tick,
                CombatEventType.ProjectileSpawned,
                source,
                target,
                projectile,
                weapon,
                position,
                0.0));
    }

    internal void RecordImpact(
        EntityId source,
        EntityId target,
        EntityId projectile,
        WeaponId weapon,
        Vector3 position)
    {
        _impactsThisTick++;
        _totalImpacts++;

        _events.Add(
            new CombatEvent(
                _tick,
                CombatEventType.Impact,
                source,
                target,
                projectile,
                weapon,
                position,
                0.0));
    }

    internal void QueueDamage(
        EntityId source,
        EntityId target,
        EntityId projectile,
        WeaponId weapon,
        Vector3 position,
        Vector3 incomingDirection,
        in DamagePayload damage)
    {
        _pendingDamage.Add(
            new PendingCombatDamage(
                source,
                target,
                projectile,
                weapon,
                position,
                incomingDirection,
                damage));
    }

    internal void RecordDamage(
        in PendingCombatDamage request,
        double appliedDamage)
    {
        _hitsThisTick++;
        _damageAppliedThisTick += appliedDamage;
        _totalHits++;
        _totalDamageApplied += appliedDamage;

        _events.Add(
            new CombatEvent(
                _tick,
                CombatEventType.DamageApplied,
                request.Source,
                request.Target,
                request.Projectile,
                request.Weapon,
                request.Position,
                appliedDamage));
    }

    internal void QueueHealthDestruction(
        in PendingCombatDamage request)
    {
        QueueDestruction(
            new PendingCombatDestruction(
                request.Target,
                request.Source,
                request.Weapon,
                request.Position,
                EmitCombatDestructionEvent: true));
    }

    internal void QueueProjectileRemoval(EntityId projectile)
    {
        QueueDestruction(
            new PendingCombatDestruction(
                projectile,
                EntityId.Invalid,
                WeaponId.None,
                Vector3.Zero,
                EmitCombatDestructionEvent: false));
    }

    internal void RecordDestruction(
        in PendingCombatDestruction destruction)
    {
        _destructionsThisTick++;
        _totalDestructions++;

        _events.Add(
            new CombatEvent(
                _tick,
                CombatEventType.EntityDestroyed,
                destruction.Source,
                destruction.Entity,
                EntityId.Invalid,
                destruction.Weapon,
                destruction.Position,
                0.0));
    }

    internal void ClearPendingDamage()
    {
        _pendingDamage.Clear();
    }

    internal void ClearPendingDestructions()
    {
        _pendingDestructions.Clear();
        _pendingDestructionEntities.Clear();
    }

    internal void SetActiveProjectiles(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _activeProjectiles = count;
    }

    private void QueueDestruction(
        in PendingCombatDestruction destruction)
    {
        if (_pendingDestructionEntities.Add(destruction.Entity))
        {
            _pendingDestructions.Add(destruction);
        }
    }
}
