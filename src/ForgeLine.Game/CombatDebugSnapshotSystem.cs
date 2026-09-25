using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public readonly record struct CombatWeaponReadModel(
    EntityId Entity,
    WeaponId Weapon,
    Vector3 Position,
    float RangeMeters,
    EntityId Target,
    Vector3 TargetPosition,
    bool HasTarget);

public readonly record struct CombatProjectileReadModel(
    EntityId Entity,
    WeaponId Weapon,
    Vector3 Position,
    Vector3 Velocity);

public readonly record struct CombatHealthReadModel(
    EntityId Entity,
    Vector3 Position,
    double Current,
    double Maximum);

public readonly record struct CombatImpactReadModel(
    EntityId Target,
    WeaponId Weapon,
    Vector3 Position);

public sealed class CombatDebugSnapshot
{
    private readonly CombatWeaponReadModel[] _weapons;
    private readonly CombatProjectileReadModel[] _projectiles;
    private readonly CombatHealthReadModel[] _health;
    private readonly CombatImpactReadModel[] _impacts;

    internal CombatDebugSnapshot(
        CombatRuntimeMetrics metrics,
        CombatWeaponReadModel[] weapons,
        CombatProjectileReadModel[] projectiles,
        CombatHealthReadModel[] health,
        CombatImpactReadModel[] impacts)
    {
        Metrics = metrics;
        _weapons = weapons;
        _projectiles = projectiles;
        _health = health;
        _impacts = impacts;
    }

    public static CombatDebugSnapshot Empty { get; } =
        new(default, [], [], [], []);

    public CombatRuntimeMetrics Metrics { get; }

    public IReadOnlyList<CombatWeaponReadModel> Weapons =>
        _weapons;

    public IReadOnlyList<CombatProjectileReadModel> Projectiles =>
        _projectiles;

    public IReadOnlyList<CombatHealthReadModel> Health =>
        _health;

    public IReadOnlyList<CombatImpactReadModel> Impacts =>
        _impacts;
}

public sealed class CombatDebugSnapshotSystem : ISimulationSystem
{
    private readonly WeaponCatalog _weapons;
    private readonly CombatRuntime _runtime;
    private readonly List<CombatWeaponReadModel> _weaponReadModels = new();
    private readonly List<CombatProjectileReadModel> _projectileReadModels =
        new();
    private readonly List<CombatHealthReadModel> _healthReadModels = new();
    private readonly List<CombatImpactReadModel> _impactReadModels = new();

    public CombatDebugSnapshotSystem(
        WeaponCatalog weapons,
        CombatRuntime runtime)
    {
        _weapons = weapons ??
            throw new ArgumentNullException(nameof(weapons));
        _runtime = runtime ??
            throw new ArgumentNullException(nameof(runtime));
    }

    public SimulationPhase Phase =>
        SimulationPhase.SnapshotEvents;

    public bool DebugCaptureEnabled { get; set; }

    public CombatDebugSnapshot LastDebugSnapshot
    {
        get;
        private set;
    } = CombatDebugSnapshot.Empty;

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _runtime.BeginTick(context.Tick);

        if (!DebugCaptureEnabled)
        {
            LastDebugSnapshot = CombatDebugSnapshot.Empty;
            return;
        }

        _weaponReadModels.Clear();
        _projectileReadModels.Clear();
        _healthReadModels.Clear();
        _impactReadModels.Clear();

        CaptureWeapons(context);
        CaptureProjectiles(context);
        CaptureHealth(context);
        CaptureImpacts();

        LastDebugSnapshot =
            new CombatDebugSnapshot(
                _runtime.Metrics,
                _weaponReadModels.ToArray(),
                _projectileReadModels.ToArray(),
                _healthReadModels.ToArray(),
                _impactReadModels.ToArray());
    }

    private void CaptureWeapons(
        SimulationContext context)
    {
        foreach (EntityId entity in
                 context.Entities.Query<WeaponState, WorldTransform>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            WeaponState state =
                context.Entities.GetComponent<WeaponState>(
                    entity);
            WorldTransform transform =
                context.Entities.GetComponent<WorldTransform>(
                    entity);
            WeaponDefinition definition =
                _weapons.GetRequired(state.WeaponId);

            bool hasTarget =
                state.Target.IsValid &&
                context.Entities.IsAlive(state.Target) &&
                context.Entities.TryGetComponent(
                    state.Target,
                    out WorldTransform targetTransform);

            _weaponReadModels.Add(
                new CombatWeaponReadModel(
                    entity,
                    state.WeaponId,
                    transform.Position,
                    definition.RangeMeters,
                    state.Target,
                    hasTarget
                        ? targetTransform.Position
                        : default,
                    hasTarget));
        }
    }

    private void CaptureProjectiles(
        SimulationContext context)
    {
        foreach (EntityId entity in
                 context.Entities.Query<ProjectileState, WorldTransform>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            ProjectileState projectile =
                context.Entities.GetComponent<ProjectileState>(
                    entity);
            WorldTransform transform =
                context.Entities.GetComponent<WorldTransform>(
                    entity);

            _projectileReadModels.Add(
                new CombatProjectileReadModel(
                    entity,
                    projectile.Weapon,
                    transform.Position,
                    projectile.Velocity));
        }
    }

    private void CaptureHealth(
        SimulationContext context)
    {
        foreach (EntityId entity in
                 context.Entities.Query<HealthState, WorldTransform>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            HealthState health =
                context.Entities.GetComponent<HealthState>(
                    entity);
            WorldTransform transform =
                context.Entities.GetComponent<WorldTransform>(
                    entity);

            _healthReadModels.Add(
                new CombatHealthReadModel(
                    entity,
                    transform.Position,
                    health.Current,
                    health.Maximum));
        }
    }

    private void CaptureImpacts()
    {
        for (int index = 0;
             index < _runtime.Events.Count;
             index++)
        {
            CombatEvent combatEvent =
                _runtime.Events[index];

            if (combatEvent.Type !=
                CombatEventType.Impact)
            {
                continue;
            }

            _impactReadModels.Add(
                new CombatImpactReadModel(
                    combatEvent.Target,
                    combatEvent.Weapon,
                    combatEvent.Position));
        }
    }
}
