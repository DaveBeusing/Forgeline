using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game;

public sealed class CombatExecutionSystem : ISimulationSystem
{
    private const float DirectionEpsilonSquared = 0.000001f;
    private const float SegmentEpsilon = 0.000001f;

    private readonly WeaponCatalog _weapons;
    private readonly InventoryStore _inventories;
    private readonly CombatRuntime _runtime;
    private readonly SpatialGridIndex? _spatialIndex;
    private readonly ITargetAvailabilityPolicy _targetAvailability;
    private readonly ILineOfFirePolicy _lineOfFire;
    private readonly SpatialQueryBuffer _projectileQueryBuffer = new(128);
    private readonly List<ProjectileSpawnRequest> _projectileSpawns = new();

    public CombatExecutionSystem(
        WeaponCatalog weapons,
        InventoryStore inventories,
        CombatRuntime runtime,
        SpatialGridIndex? spatialIndex = null,
        ITargetAvailabilityPolicy? targetAvailability = null,
        ILineOfFirePolicy? lineOfFire = null)
    {
        _weapons = weapons ??
            throw new ArgumentNullException(nameof(weapons));
        _inventories = inventories ??
            throw new ArgumentNullException(nameof(inventories));
        _runtime = runtime ??
            throw new ArgumentNullException(nameof(runtime));
        _spatialIndex = spatialIndex;
        _targetAvailability =
            targetAvailability ??
            AlwaysTargetAvailablePolicy.Instance;
        _lineOfFire =
            lineOfFire ??
            UnobstructedLineOfFirePolicy.Instance;
    }

    public SimulationPhase Phase => SimulationPhase.Combat;

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _runtime.BeginTick(context.Tick);
        _projectileSpawns.Clear();

        ProcessProjectiles(context);
        ProcessWeapons(context);
        CreatePendingProjectiles(context);

        _runtime.SetActiveProjectiles(
            checked(
                context.Entities.GetComponentCount<ProjectileState>() +
                context.Entities.GetComponentCount<IndirectFireProjectileState>()));
    }

    private void ProcessProjectiles(SimulationContext context)
    {
        float deltaSeconds =
            (float)context.TickDuration.TotalSeconds;

        if (deltaSeconds <= 0.0f)
        {
            return;
        }

        foreach (EntityId projectile in
                 context.Entities.Query<ProjectileState, WorldTransform>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            ProjectileState state =
                context.Entities.GetComponent<ProjectileState>(
                    projectile);
            WorldTransform transform =
                context.Entities.GetComponent<WorldTransform>(
                    projectile);

            if (state.HasImpacted)
            {
                _runtime.QueueProjectileRemoval(projectile);
                continue;
            }

            Vector3 start = transform.Position;
            Vector3 end =
                start + state.Velocity * deltaSeconds;

            if (TryFindProjectileHit(
                    context.Entities,
                    projectile,
                    state,
                    start,
                    end,
                    out EntityId target,
                    out Vector3 impactPosition))
            {
                context.Entities.SetComponent(
                    projectile,
                    transform with
                    {
                        Position = impactPosition
                    });
                context.Entities.SetComponent(
                    projectile,
                    state with
                    {
                        HasImpacted = true
                    });

                _runtime.RecordImpact(
                    state.Source,
                    target,
                    projectile,
                    state.Weapon,
                    impactPosition);
                _runtime.QueueDamage(
                    state.Source,
                    target,
                    projectile,
                    state.Weapon,
                    impactPosition,
                    Vector3.Normalize(state.Velocity),
                    state.Damage);
                _runtime.QueueProjectileRemoval(projectile);
                continue;
            }

            int remainingTicks =
                state.RemainingTicks - 1;

            context.Entities.SetComponent(
                projectile,
                transform with
                {
                    Position = end
                });

            if (remainingTicks <= 0)
            {
                _runtime.QueueProjectileRemoval(projectile);
                continue;
            }

            context.Entities.SetComponent(
                projectile,
                state with
                {
                    RemainingTicks = remainingTicks
                });
        }
    }

    private void ProcessWeapons(SimulationContext context)
    {
        foreach (EntityId entity in
                 context.Entities.Query<WeaponState, WorldTransform>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            WeaponState state =
                context.Entities.GetComponent<WeaponState>(
                    entity);
            WeaponDefinition definition =
                _weapons.GetRequired(state.WeaponId);

            if (!state.MagazineInitialized)
            {
                state = state with
                {
                    MagazineShotsRemaining = definition.MagazineSize,
                    MagazineInitialized = true
                };
                context.Entities.SetComponent(entity, state);
            }

            if (!state.CanAttemptFire(context.Tick))
            {
                continue;
            }

            EntityId target = state.Target;
            if (!context.Entities.IsAlive(target))
            {
                context.Entities.SetComponent(
                    entity,
                    state with
                    {
                        Target = EntityId.Invalid
                    });
                continue;
            }

            if (!context.Entities.TryGetComponent(
                    entity,
                    out Combatant sourceCombatant) ||
                !context.Entities.TryGetComponent(
                    target,
                    out Combatant targetCombatant) ||
                sourceCombatant.Faction == targetCombatant.Faction ||
                !context.Entities.TryGetComponent(
                    target,
                    out HealthState health) ||
                health.IsDepleted ||
                !context.Entities.TryGetComponent(
                    target,
                    out WorldTransform targetTransform))
            {
                continue;
            }

            if (context.Entities.TryGetComponent(
                    entity,
                    out FirePolicyState firePolicy) &&
                !firePolicy.Permits(target))
            {
                continue;
            }

            if (context.Entities.TryGetComponent(
                    target,
                    out Targetable targetable) &&
                !definition.Effectiveness.CanEngage(
                    targetable.Class))
            {
                continue;
            }

            if (!_targetAvailability.IsTargetAvailable(
                    entity,
                    target))
            {
                continue;
            }

            WorldTransform sourceTransform =
                context.Entities.GetComponent<WorldTransform>(
                    entity);

            Vector3 delta =
                targetTransform.Position -
                sourceTransform.Position;
            float distanceSquared =
                delta.LengthSquared();
            float maximumRangeSquared =
                definition.RangeMeters *
                definition.RangeMeters;

            if (distanceSquared > maximumRangeSquared)
            {
                continue;
            }

            if (!_lineOfFire.HasLineOfFire(
                    entity,
                    target,
                    sourceTransform.Position,
                    targetTransform.Position))
            {
                continue;
            }

            if (!context.Entities.TryGetComponent(
                    entity,
                    out AmmunitionState ammunition) ||
                !AmmunitionConsumption.TryConsume(
                    _inventories,
                    ammunition,
                    definition.AmmunitionPerShot))
            {
                continue;
            }

            _runtime.RecordShot(
                entity,
                target,
                definition.Id,
                sourceTransform.Position,
                definition.AmmunitionPerShot);

            if (definition.DeliveryModel ==
                WeaponDeliveryModel.Hitscan)
            {
                _runtime.RecordImpact(
                    entity,
                    target,
                    EntityId.Invalid,
                    definition.Id,
                    targetTransform.Position);
                _runtime.QueueDamage(
                    entity,
                    target,
                    EntityId.Invalid,
                    definition.Id,
                    targetTransform.Position,
                    CreateProjectileDirection(
                        delta,
                        sourceTransform.Rotation),
                    definition.Damage);
            }
            else
            {
                Vector3 direction =
                    CreateProjectileDirection(
                        delta,
                        sourceTransform.Rotation);

                _projectileSpawns.Add(
                    new ProjectileSpawnRequest(
                        entity,
                        target,
                        sourceCombatant.Faction,
                        definition,
                        sourceTransform.Position,
                        direction));
            }

            int remainingMagazineShots =
                state.MagazineShotsRemaining - 1;
            SimulationTick nextFireTick =
                new(
                    checked(
                        context.Tick.Value +
                        (ulong)definition.FireIntervalTicks));

            SimulationTick reloadUntilTick =
                state.ReloadUntilTick;

            if (remainingMagazineShots <= 0)
            {
                remainingMagazineShots =
                    definition.MagazineSize;

                reloadUntilTick =
                    definition.ReloadTicks > 0
                        ? new SimulationTick(
                            checked(
                                context.Tick.Value +
                                (ulong)definition.ReloadTicks))
                        : context.Tick;
            }

            context.Entities.SetComponent(
                entity,
                state with
                {
                    MagazineShotsRemaining =
                        remainingMagazineShots,
                    NextFireTick = nextFireTick,
                    ReloadUntilTick = reloadUntilTick
                });
        }
    }

    private void CreatePendingProjectiles(
        SimulationContext context)
    {
        for (int index = 0;
             index < _projectileSpawns.Count;
             index++)
        {
            ProjectileSpawnRequest spawn =
                _projectileSpawns[index];

            if (!context.Entities.IsAlive(spawn.Source))
            {
                continue;
            }

            EntityId projectile =
                context.Entities.CreateEntity();

            context.Entities.AddComponent(
                projectile,
                new WorldTransform(
                    spawn.Position,
                    Quaternion.Identity,
                    Vector3.One));
            context.Entities.AddComponent(
                projectile,
                new ProjectileState(
                    spawn.Source,
                    spawn.Faction,
                    spawn.Definition.Id,
                    spawn.Direction *
                        spawn.Definition.ProjectileSpeedMetersPerSecond,
                    spawn.Definition.ProjectileLifetimeTicks,
                    spawn.Definition.ProjectileRadiusMeters,
                    spawn.Definition.Damage));

            _runtime.RecordProjectileSpawned(
                spawn.Source,
                spawn.Target,
                projectile,
                spawn.Definition.Id,
                spawn.Position);
        }
    }

    private bool TryFindProjectileHit(
        EntityRegistry entities,
        EntityId projectile,
        in ProjectileState state,
        Vector3 start,
        Vector3 end,
        out EntityId target,
        out Vector3 impactPosition)
    {
        return _spatialIndex is null
            ? TryFindProjectileHitFromEntities(
                entities,
                projectile,
                state,
                start,
                end,
                out target,
                out impactPosition)
            : TryFindProjectileHitFromSpatialIndex(
                entities,
                projectile,
                state,
                start,
                end,
                out target,
                out impactPosition);
    }

    private bool TryFindProjectileHitFromSpatialIndex(
        EntityRegistry entities,
        EntityId projectile,
        in ProjectileState state,
        Vector3 start,
        Vector3 end,
        out EntityId target,
        out Vector3 impactPosition)
    {
        AxisAlignedBounds sweptBounds =
            CreateSweptBounds(
                start,
                end,
                state.RadiusMeters);

        _spatialIndex!.QueryAabb(
            sweptBounds,
            _projectileQueryBuffer,
            order: SpatialQueryOrder.StableEntityId);

        EntityId bestTarget = EntityId.Invalid;
        float bestT = float.PositiveInfinity;

        ReadOnlySpan<EntityId> candidates =
            _projectileQueryBuffer.Results;

        for (int index = 0;
             index < candidates.Length;
             index++)
        {
            EntityId candidate = candidates[index];

            if (!IsEligibleProjectileTarget(
                    entities,
                    projectile,
                    state,
                    candidate) ||
                !_spatialIndex.TryGetEntry(
                    candidate,
                    out SpatialEntry entry))
            {
                continue;
            }

            AxisAlignedBounds expanded =
                ExpandBounds(
                    entry.Bounds,
                    state.RadiusMeters);

            if (!TryIntersectSegment(
                    start,
                    end,
                    expanded,
                    out float t))
            {
                continue;
            }

            if (t < bestT ||
                (t == bestT &&
                 (!bestTarget.IsValid ||
                  candidate < bestTarget)))
            {
                bestT = t;
                bestTarget = candidate;
            }
        }

        return CompleteHit(
            start,
            end,
            bestTarget,
            bestT,
            out target,
            out impactPosition);
    }

    private bool TryFindProjectileHitFromEntities(
        EntityRegistry entities,
        EntityId projectile,
        in ProjectileState state,
        Vector3 start,
        Vector3 end,
        out EntityId target,
        out Vector3 impactPosition)
    {
        EntityId bestTarget = EntityId.Invalid;
        float bestT = float.PositiveInfinity;

        foreach (EntityId candidate in
                 entities.Query<HealthState, WorldTransform>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (!IsEligibleProjectileTarget(
                    entities,
                    projectile,
                    state,
                    candidate))
            {
                continue;
            }

            WorldTransform transform =
                entities.GetComponent<WorldTransform>(
                    candidate);
            AxisAlignedBounds bounds =
                CreateTargetBounds(
                    entities,
                    candidate,
                    transform);
            AxisAlignedBounds expanded =
                ExpandBounds(
                    bounds,
                    state.RadiusMeters);

            if (!TryIntersectSegment(
                    start,
                    end,
                    expanded,
                    out float t))
            {
                continue;
            }

            if (t < bestT ||
                (t == bestT &&
                 (!bestTarget.IsValid ||
                  candidate < bestTarget)))
            {
                bestT = t;
                bestTarget = candidate;
            }
        }

        return CompleteHit(
            start,
            end,
            bestTarget,
            bestT,
            out target,
            out impactPosition);
    }

    private bool IsEligibleProjectileTarget(
        EntityRegistry entities,
        EntityId projectile,
        in ProjectileState state,
        EntityId candidate)
    {
        if (candidate == projectile ||
            candidate == state.Source ||
            !entities.IsAlive(candidate) ||
            !entities.TryGetComponent(
                candidate,
                out HealthState health) ||
            health.IsDepleted ||
            !entities.TryGetComponent(
                candidate,
                out Combatant combatant))
        {
            return false;
        }

        if (combatant.Faction == state.Faction)
        {
            return false;
        }

        if (entities.TryGetComponent(
                candidate,
                out Targetable targetable))
        {
            WeaponDefinition weapon =
                _weapons.GetRequired(
                    state.Weapon);

            if (!weapon.Effectiveness.CanEngage(
                    targetable.Class))
            {
                return false;
            }
        }

        return true;
    }

    private static AxisAlignedBounds CreateTargetBounds(
        EntityRegistry entities,
        EntityId entity,
        in WorldTransform transform)
    {
        if (entities.TryGetComponent(
                entity,
                out SpatialPresence presence))
        {
            return presence.CreateEntry(
                entity,
                transform).Bounds;
        }

        CombatHitbox hitbox =
            entities.TryGetComponent(
                entity,
                out CombatHitbox configured)
                ? configured
                : CombatHitbox.Default;

        return new AxisAlignedBounds(
            transform.Position - hitbox.HalfExtents,
            transform.Position + hitbox.HalfExtents);
    }

    private static AxisAlignedBounds CreateSweptBounds(
        Vector3 start,
        Vector3 end,
        float radius)
    {
        Vector3 expansion =
            new(radius, radius, radius);

        return new AxisAlignedBounds(
            Vector3.Min(start, end) - expansion,
            Vector3.Max(start, end) + expansion);
    }

    private static AxisAlignedBounds ExpandBounds(
        in AxisAlignedBounds bounds,
        float radius)
    {
        Vector3 expansion =
            new(radius, radius, radius);

        return new AxisAlignedBounds(
            bounds.Minimum - expansion,
            bounds.Maximum + expansion);
    }

    private static bool TryIntersectSegment(
        Vector3 start,
        Vector3 end,
        in AxisAlignedBounds bounds,
        out float t)
    {
        Vector3 direction = end - start;
        float minimumT = 0.0f;
        float maximumT = 1.0f;

        if (!ClipAxis(
                start.X,
                direction.X,
                bounds.Minimum.X,
                bounds.Maximum.X,
                ref minimumT,
                ref maximumT) ||
            !ClipAxis(
                start.Y,
                direction.Y,
                bounds.Minimum.Y,
                bounds.Maximum.Y,
                ref minimumT,
                ref maximumT) ||
            !ClipAxis(
                start.Z,
                direction.Z,
                bounds.Minimum.Z,
                bounds.Maximum.Z,
                ref minimumT,
                ref maximumT))
        {
            t = 0.0f;
            return false;
        }

        t = minimumT;
        return true;
    }

    private static bool ClipAxis(
        float start,
        float direction,
        float minimum,
        float maximum,
        ref float minimumT,
        ref float maximumT)
    {
        if (MathF.Abs(direction) <= SegmentEpsilon)
        {
            return start >= minimum &&
                   start <= maximum;
        }

        float inverse = 1.0f / direction;
        float first = (minimum - start) * inverse;
        float second = (maximum - start) * inverse;

        if (first > second)
        {
            (first, second) = (second, first);
        }

        minimumT = MathF.Max(minimumT, first);
        maximumT = MathF.Min(maximumT, second);

        return minimumT <= maximumT &&
               maximumT >= 0.0f &&
               minimumT <= 1.0f;
    }

    private static bool CompleteHit(
        Vector3 start,
        Vector3 end,
        EntityId bestTarget,
        float bestT,
        out EntityId target,
        out Vector3 impactPosition)
    {
        if (!bestTarget.IsValid ||
            !float.IsFinite(bestT))
        {
            target = EntityId.Invalid;
            impactPosition = default;
            return false;
        }

        target = bestTarget;
        impactPosition =
            Vector3.Lerp(
                start,
                end,
                Math.Clamp(bestT, 0.0f, 1.0f));
        return true;
    }

    private static Vector3 CreateProjectileDirection(
        Vector3 targetDelta,
        Quaternion sourceRotation)
    {
        if (targetDelta.LengthSquared() >
            DirectionEpsilonSquared)
        {
            return Vector3.Normalize(targetDelta);
        }

        Vector3 forward =
            Vector3.Transform(
                Vector3.UnitZ,
                sourceRotation);

        return forward.LengthSquared() >
            DirectionEpsilonSquared
                ? Vector3.Normalize(forward)
                : Vector3.UnitZ;
    }

    private readonly record struct ProjectileSpawnRequest(
        EntityId Source,
        EntityId Target,
        FactionId Faction,
        WeaponDefinition Definition,
        Vector3 Position,
        Vector3 Direction);
}
