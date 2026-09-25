using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game;

public readonly record struct ArtilleryFireMissionMetrics(
    int ActiveMissions,
    int NoAmmoMissions,
    int ShellsInFlight,
    int ShotsFiredThisTick,
    int ImpactsThisTick,
    int AreaDamageTargetsThisTick,
    double AmmunitionConsumedThisTick,
    double AreaDamageQueuedThisTick,
    ulong TotalShotsFired,
    ulong TotalImpacts,
    ulong TotalAreaDamageTargets,
    double TotalAmmunitionConsumed,
    double TotalAreaDamageQueued);

public readonly record struct ArtilleryMissionDebugEntry(
    EntityId Entity,
    Vector3 SourcePosition,
    Vector3 TargetPosition,
    FireMissionStatus Status,
    float MinimumRangeMeters,
    float MaximumRangeMeters,
    float AreaRadiusMeters,
    int RoundsFired,
    int RequestedRounds);

public readonly record struct ArtilleryProjectileDebugEntry(
    EntityId Entity,
    Vector3 Position,
    Vector3 TargetPosition,
    SimulationTick ImpactTick,
    float AreaRadiusMeters);

public sealed class ArtilleryDebugSnapshot
{
    private readonly ArtilleryMissionDebugEntry[] _missions;
    private readonly ArtilleryProjectileDebugEntry[] _projectiles;

    internal ArtilleryDebugSnapshot(
        ArtilleryFireMissionMetrics metrics,
        ArtilleryMissionDebugEntry[] missions,
        ArtilleryProjectileDebugEntry[] projectiles)
    {
        Metrics = metrics;
        _missions = missions;
        _projectiles = projectiles;
    }

    public static ArtilleryDebugSnapshot Empty { get; } =
        new(default, [], []);

    public ArtilleryFireMissionMetrics Metrics { get; }

    public IReadOnlyList<ArtilleryMissionDebugEntry> Missions =>
        _missions;

    public IReadOnlyList<ArtilleryProjectileDebugEntry> Projectiles =>
        _projectiles;
}

public sealed class ArtilleryFireMissionSystem : ISimulationSystem
{
    private readonly ArtilleryWeaponCatalog _weapons;
    private readonly InventoryStore _inventories;
    private readonly CombatRuntime _combat;
    private readonly FactionIntelligenceStore _intelligence;
    private readonly ITerrainQuery _terrain;
    private readonly SpatialGridIndex? _spatialIndex;
    private readonly SpatialQueryBuffer _impactQueryBuffer = new(256);
    private readonly List<EntityId> _impactCandidates = new();
    private readonly List<ArtilleryMissionDebugEntry> _missionDebug = new();
    private readonly List<ArtilleryProjectileDebugEntry> _projectileDebug =
        new();

    private int _shotsFiredThisTick;
    private int _impactsThisTick;
    private int _areaDamageTargetsThisTick;
    private double _ammunitionConsumedThisTick;
    private double _areaDamageQueuedThisTick;

    private ulong _totalShotsFired;
    private ulong _totalImpacts;
    private ulong _totalAreaDamageTargets;
    private double _totalAmmunitionConsumed;
    private double _totalAreaDamageQueued;

    public ArtilleryFireMissionSystem(
        ArtilleryWeaponCatalog weapons,
        InventoryStore inventories,
        CombatRuntime combat,
        FactionIntelligenceStore intelligence,
        ITerrainQuery terrain,
        SpatialGridIndex? spatialIndex = null)
    {
        _weapons = weapons ??
            throw new ArgumentNullException(nameof(weapons));
        _inventories = inventories ??
            throw new ArgumentNullException(nameof(inventories));
        _combat = combat ??
            throw new ArgumentNullException(nameof(combat));
        _intelligence = intelligence ??
            throw new ArgumentNullException(nameof(intelligence));
        _terrain = terrain ??
            throw new ArgumentNullException(nameof(terrain));
        _spatialIndex = spatialIndex;
    }

    public SimulationPhase Phase => SimulationPhase.Combat;

    public bool DebugCaptureEnabled { get; set; }

    public ArtilleryFireMissionMetrics Metrics { get; private set; }

    public ArtilleryDebugSnapshot LastDebugSnapshot
    {
        get;
        private set;
    } = ArtilleryDebugSnapshot.Empty;

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _combat.BeginTick(context.Tick);
        ResetTickMetrics();

        ProcessProjectiles(context);
        ResolvePendingRequests(context);
        ProcessMissions(context);
        UpdateMetrics(context);
        CaptureDebugSnapshot(context);
    }

    private void ResolvePendingRequests(SimulationContext context)
    {
        foreach (EntityId entity in
                 context.Entities.Query<
                     FireMissionRequest,
                     ArtilleryCapability>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            FireMissionRequest request =
                context.Entities.GetComponent<FireMissionRequest>(entity);
            ArtilleryCapability capability =
                context.Entities.GetComponent<ArtilleryCapability>(entity);

            if (!context.Entities.TryGetComponent(
                    entity,
                    out WorldTransform sourceTransform) ||
                !TryResolveFaction(
                    context.Entities,
                    entity,
                    request.Issuer,
                    out FactionId faction) ||
                !TryResolveTarget(
                    faction,
                    request,
                    out Vector3 targetPosition,
                    out IntelligenceContactKey contactKey,
                    out SimulationTick informationTick))
            {
                SetCancelledMission(
                    context,
                    entity,
                    request,
                    capability,
                    sourceTransform.Position);
                context.Entities.RemoveComponent<FireMissionRequest>(entity);
                continue;
            }

            ArtilleryWeaponDefinition definition =
                _weapons.GetRequired(capability.WeaponId);

            if (!_terrain.TrySampleHeight(
                    targetPosition.X,
                    targetPosition.Z,
                    out float targetHeight))
            {
                SetCancelledMission(
                    context,
                    entity,
                    request,
                    capability,
                    sourceTransform.Position);
                context.Entities.RemoveComponent<FireMissionRequest>(entity);
                continue;
            }

            targetPosition.Y = targetHeight;

            if (!definition.IsInRange(
                    sourceTransform.Position,
                    targetPosition))
            {
                SetMission(
                    context,
                    entity,
                    new FireMissionState(
                        targetPosition,
                        contactKey,
                        request.RequestedRounds,
                        0,
                        FireMissionStatus.Cancelled,
                        context.Tick,
                        informationTick,
                        context.Tick,
                        context.Tick));
                context.Entities.RemoveComponent<FireMissionRequest>(entity);
                continue;
            }

            SimulationTick acquisitionComplete =
                new(
                    checked(
                        context.Tick.Value +
                        (ulong)definition.AcquisitionTicks));

            SetMission(
                context,
                entity,
                new FireMissionState(
                    targetPosition,
                    contactKey,
                    request.RequestedRounds,
                    0,
                    FireMissionStatus.Ordered,
                    context.Tick,
                    informationTick,
                    acquisitionComplete,
                    context.Tick));

            context.Entities.RemoveComponent<FireMissionRequest>(entity);
        }
    }

    private void ProcessMissions(SimulationContext context)
    {
        foreach (EntityId entity in
                 context.Entities.Query<
                     FireMissionState,
                     ArtilleryCapability,
                     WorldTransform>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            FireMissionState state =
                context.Entities.GetComponent<FireMissionState>(entity);

            if (state.Status is
                FireMissionStatus.Complete or
                FireMissionStatus.Cancelled)
            {
                continue;
            }

            ArtilleryCapability capability =
                context.Entities.GetComponent<ArtilleryCapability>(entity);
            ArtilleryWeaponDefinition definition =
                _weapons.GetRequired(capability.WeaponId);
            WorldTransform transform =
                context.Entities.GetComponent<WorldTransform>(entity);

            if (!definition.IsInRange(
                    transform.Position,
                    state.TargetPosition))
            {
                context.Entities.SetComponent(
                    entity,
                    state with
                    {
                        Status = FireMissionStatus.Cancelled
                    });
                continue;
            }

            if (context.Tick < state.AcquisitionCompleteTick)
            {
                if (state.Status != FireMissionStatus.Acquiring)
                {
                    context.Entities.SetComponent(
                        entity,
                        state with
                        {
                            Status = FireMissionStatus.Acquiring
                        });
                }

                continue;
            }

            if (context.Tick < state.NextFireTick)
            {
                if (state.Status != FireMissionStatus.WaitingReload)
                {
                    context.Entities.SetComponent(
                        entity,
                        state with
                        {
                            Status = FireMissionStatus.WaitingReload
                        });
                }

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
                if (state.Status != FireMissionStatus.NoAmmo)
                {
                    context.Entities.SetComponent(
                        entity,
                        state with
                        {
                            Status = FireMissionStatus.NoAmmo
                        });
                }

                continue;
            }

            Vector3 dispersedTarget =
                ApplyDispersion(
                    context.Random,
                    state.TargetPosition,
                    definition.DispersionRadiusMeters);

            if (_terrain.TrySampleHeight(
                    dispersedTarget.X,
                    dispersedTarget.Z,
                    out float impactHeight))
            {
                dispersedTarget.Y = impactHeight;
            }

            SpawnProjectile(
                context,
                entity,
                capability,
                definition,
                transform.Position,
                dispersedTarget);

            _combat.RecordShot(
                entity,
                EntityId.Invalid,
                capability.WeaponId,
                transform.Position,
                definition.AmmunitionPerShot);

            _shotsFiredThisTick++;
            _ammunitionConsumedThisTick +=
                definition.AmmunitionPerShot;
            _totalShotsFired++;
            _totalAmmunitionConsumed +=
                definition.AmmunitionPerShot;

            int roundsFired =
                state.RoundsFired + 1;
            bool complete =
                roundsFired >= state.RequestedRounds;
            SimulationTick nextFireTick =
                new(
                    checked(
                        context.Tick.Value +
                        (ulong)definition.FireIntervalTicks));

            context.Entities.SetComponent(
                entity,
                state with
                {
                    RoundsFired = roundsFired,
                    Status = complete
                        ? FireMissionStatus.Complete
                        : FireMissionStatus.Firing,
                    NextFireTick = nextFireTick
                });
        }
    }

    private void ProcessProjectiles(SimulationContext context)
    {
        foreach (EntityId projectile in
                 context.Entities.Query<
                     IndirectFireProjectileState,
                     WorldTransform>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            IndirectFireProjectileState state =
                context.Entities.GetComponent<IndirectFireProjectileState>(
                    projectile);

            if (state.HasImpacted)
            {
                _combat.QueueProjectileRemoval(projectile);
                continue;
            }

            if (context.Tick >= state.ImpactTick)
            {
                WorldTransform transform =
                    context.Entities.GetComponent<WorldTransform>(
                        projectile);

                context.Entities.SetComponent(
                    projectile,
                    transform with
                    {
                        Position = state.TargetPosition
                    });
                context.Entities.SetComponent(
                    projectile,
                    state with
                    {
                        HasImpacted = true
                    });

                _combat.RecordImpact(
                    state.Source,
                    EntityId.Invalid,
                    projectile,
                    state.Weapon,
                    state.TargetPosition);

                QueueAreaDamage(
                    context,
                    projectile,
                    state);

                _combat.QueueProjectileRemoval(projectile);
                _impactsThisTick++;
                _totalImpacts++;
                continue;
            }

            ulong elapsedTicks =
                context.Tick.Value -
                state.LaunchTick.Value;
            ulong totalTicks =
                state.ImpactTick.Value -
                state.LaunchTick.Value;
            float progress =
                Math.Clamp(
                    elapsedTicks / (float)totalTicks,
                    0.0f,
                    1.0f);

            Vector3 position =
                Vector3.Lerp(
                    state.LaunchPosition,
                    state.TargetPosition,
                    progress);
            position.Y +=
                4.0f *
                state.ApexHeightMeters *
                progress *
                (1.0f - progress);

            WorldTransform current =
                context.Entities.GetComponent<WorldTransform>(
                    projectile);
            context.Entities.SetComponent(
                projectile,
                current with
                {
                    Position = position
                });
        }
    }

    private void SpawnProjectile(
        SimulationContext context,
        EntityId source,
        in ArtilleryCapability capability,
        ArtilleryWeaponDefinition definition,
        Vector3 launchPosition,
        Vector3 targetPosition)
    {
        float x =
            targetPosition.X -
            launchPosition.X;
        float z =
            targetPosition.Z -
            launchPosition.Z;
        float horizontalDistance =
            MathF.Sqrt(x * x + z * z);
        double flightSeconds =
            horizontalDistance /
            definition.ProjectileSpeedMetersPerSecond;
        ulong flightTicks =
            Math.Max(
                1UL,
                checked(
                    (ulong)Math.Ceiling(
                        flightSeconds /
                        context.TickDuration.TotalSeconds)));
        SimulationTick impactTick =
            new(
                checked(
                    context.Tick.Value +
                    flightTicks));

        FactionId faction =
            context.Entities.TryGetComponent(
                source,
                out Combatant combatant)
                ? combatant.Faction
                : FactionId.None;

        if (!faction.IsSpecified &&
            context.Entities.TryGetComponent(
                source,
                out IntelligenceSignature signature))
        {
            faction = signature.Faction;
        }

        if (!faction.IsSpecified)
        {
            throw new InvalidOperationException(
                $"Artillery entity {source} has no faction.");
        }

        EntityId projectile =
            context.Entities.CreateEntity();

        context.Entities.AddComponent(
            projectile,
            new WorldTransform(
                launchPosition,
                Quaternion.Identity,
                Vector3.One));
        context.Entities.AddComponent(
            projectile,
            new IndirectFireProjectileState(
                source,
                faction,
                capability.WeaponId,
                launchPosition,
                targetPosition,
                context.Tick,
                impactTick,
                definition.ApexHeightMeters,
                definition.AreaRadiusMeters,
                definition.MinimumDamageFraction,
                definition.Damage));

        _combat.RecordProjectileSpawned(
            source,
            EntityId.Invalid,
            projectile,
            capability.WeaponId,
            launchPosition);
    }

    private void QueueAreaDamage(
        SimulationContext context,
        EntityId projectile,
        in IndirectFireProjectileState state)
    {
        CollectImpactCandidates(
            context,
            state.TargetPosition,
            state.AreaRadiusMeters);

        float radiusSquared =
            state.AreaRadiusMeters *
            state.AreaRadiusMeters;

        for (int index = 0;
             index < _impactCandidates.Count;
             index++)
        {
            EntityId target =
                _impactCandidates[index];

            if (!context.Entities.IsAlive(target) ||
                !context.Entities.TryGetComponent(
                    target,
                    out HealthState health) ||
                health.IsDepleted ||
                !context.Entities.TryGetComponent(
                    target,
                    out WorldTransform transform))
            {
                continue;
            }

            float dx =
                transform.Position.X -
                state.TargetPosition.X;
            float dz =
                transform.Position.Z -
                state.TargetPosition.Z;
            float distanceSquared =
                dx * dx + dz * dz;

            if (distanceSquared > radiusSquared)
            {
                continue;
            }

            double normalizedDistance =
                Math.Sqrt(distanceSquared) /
                state.AreaRadiusMeters;
            double damageFraction =
                Math.Clamp(
                    1.0 -
                    normalizedDistance *
                    (1.0 - state.MinimumDamageFraction),
                    state.MinimumDamageFraction,
                    1.0);
            double damageAmount =
                state.Damage.Amount *
                damageFraction;

            if (damageAmount <= 0.0)
            {
                continue;
            }

            _combat.QueueDamage(
                state.Source,
                target,
                projectile,
                state.Weapon,
                state.TargetPosition,
                -Vector3.UnitY,
                new DamagePayload(damageAmount));

            _areaDamageTargetsThisTick++;
            _areaDamageQueuedThisTick +=
                damageAmount;
            _totalAreaDamageTargets++;
            _totalAreaDamageQueued +=
                damageAmount;
        }
    }

    private void CollectImpactCandidates(
        SimulationContext context,
        Vector3 center,
        float radius)
    {
        _impactCandidates.Clear();

        if (_spatialIndex is not null)
        {
            _spatialIndex.QueryRadius(
                center,
                radius,
                _impactQueryBuffer,
                order: SpatialQueryOrder.StableEntityId);

            ReadOnlySpan<EntityId> candidates =
                _impactQueryBuffer.Results;

            for (int index = 0;
                 index < candidates.Length;
                 index++)
            {
                _impactCandidates.Add(
                    candidates[index]);
            }

            return;
        }

        foreach (EntityId entity in
                 context.Entities.Query<
                     HealthState,
                     WorldTransform>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            _impactCandidates.Add(entity);
        }
    }

    private bool TryResolveTarget(
        FactionId faction,
        in FireMissionRequest request,
        out Vector3 targetPosition,
        out IntelligenceContactKey contactKey,
        out SimulationTick informationTick)
    {
        targetPosition = default;
        contactKey = IntelligenceContactKey.None;
        informationTick = _intelligence.CurrentTick;

        if (request.TargetKind == FireMissionTargetKind.Contact)
        {
            if (!_intelligence.TryGetContact(
                    faction,
                    request.ContactKey,
                    out IntelligenceContact contact))
            {
                return false;
            }

            targetPosition =
                contact.LastKnownPosition;
            contactKey =
                contact.ContactKey;
            informationTick =
                contact.LastSeenTick;
            return true;
        }

        if (request.TargetKind != FireMissionTargetKind.Coordinate)
        {
            return false;
        }

        VisibilityCellCoordinate cell =
            _intelligence.WorldToCell(
                request.RequestedCoordinate);

        if (_intelligence.GetTerrainState(
                faction,
                cell) !=
            IntelligenceState.Visible)
        {
            return false;
        }

        targetPosition =
            request.RequestedCoordinate;
        informationTick =
            _intelligence.CurrentTick;
        return true;
    }

    private static bool TryResolveFaction(
        EntityRegistry entities,
        EntityId entity,
        PlayerId issuer,
        out FactionId faction)
    {
        if (entities.TryGetComponent(
                entity,
                out Combatant combatant))
        {
            faction = combatant.Faction;
            return faction.IsSpecified;
        }

        if (entities.TryGetComponent(
                entity,
                out IntelligenceSignature signature))
        {
            faction = signature.Faction;
            return faction.IsSpecified;
        }

        if (issuer.Value <= uint.MaxValue)
        {
            faction =
                new FactionId(
                    (uint)issuer.Value);
            return faction.IsSpecified;
        }

        faction = FactionId.None;
        return false;
    }

    private static Vector3 ApplyDispersion(
        SimulationRandom random,
        Vector3 target,
        float dispersionRadius)
    {
        if (dispersionRadius <= 0.0f)
        {
            return target;
        }

        double radialUnit =
            (random.NextUInt32() + 0.5) /
            (uint.MaxValue + 1.0);
        double angularUnit =
            (random.NextUInt32() + 0.5) /
            (uint.MaxValue + 1.0);
        float radius =
            dispersionRadius *
            MathF.Sqrt((float)radialUnit);
        float angle =
            MathF.Tau *
            (float)angularUnit;

        return target +
            new Vector3(
                MathF.Cos(angle) * radius,
                0.0f,
                MathF.Sin(angle) * radius);
    }

    private void SetCancelledMission(
        SimulationContext context,
        EntityId entity,
        in FireMissionRequest request,
        in ArtilleryCapability capability,
        Vector3 fallbackPosition)
    {
        _ = capability;

        SetMission(
            context,
            entity,
            new FireMissionState(
                fallbackPosition,
                request.ContactKey,
                request.RequestedRounds,
                0,
                FireMissionStatus.Cancelled,
                context.Tick,
                context.Tick,
                context.Tick,
                context.Tick));
    }

    private static void SetMission(
        SimulationContext context,
        EntityId entity,
        in FireMissionState mission)
    {
        if (context.Entities.HasComponent<FireMissionState>(entity))
        {
            context.Entities.SetComponent(entity, mission);
        }
        else
        {
            context.Entities.AddComponent(entity, mission);
        }
    }

    private void UpdateMetrics(SimulationContext context)
    {
        int activeMissions = 0;
        int noAmmo = 0;

        foreach (EntityId entity in
                 context.Entities.Query<FireMissionState>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            FireMissionState state =
                context.Entities.GetComponent<FireMissionState>(entity);

            if (state.Status is not
                FireMissionStatus.Complete and not
                FireMissionStatus.Cancelled)
            {
                activeMissions++;
            }

            if (state.Status == FireMissionStatus.NoAmmo)
            {
                noAmmo++;
            }
        }

        Metrics =
            new ArtilleryFireMissionMetrics(
                activeMissions,
                noAmmo,
                context.Entities.GetComponentCount<IndirectFireProjectileState>(),
                _shotsFiredThisTick,
                _impactsThisTick,
                _areaDamageTargetsThisTick,
                _ammunitionConsumedThisTick,
                _areaDamageQueuedThisTick,
                _totalShotsFired,
                _totalImpacts,
                _totalAreaDamageTargets,
                _totalAmmunitionConsumed,
                _totalAreaDamageQueued);
    }

    private void CaptureDebugSnapshot(SimulationContext context)
    {
        if (!DebugCaptureEnabled)
        {
            LastDebugSnapshot = ArtilleryDebugSnapshot.Empty;
            return;
        }

        _missionDebug.Clear();
        _projectileDebug.Clear();

        foreach (EntityId entity in
                 context.Entities.Query<
                     FireMissionState,
                     ArtilleryCapability,
                     WorldTransform>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            FireMissionState mission =
                context.Entities.GetComponent<FireMissionState>(entity);
            ArtilleryCapability capability =
                context.Entities.GetComponent<ArtilleryCapability>(entity);
            WorldTransform transform =
                context.Entities.GetComponent<WorldTransform>(entity);
            ArtilleryWeaponDefinition definition =
                _weapons.GetRequired(capability.WeaponId);

            _missionDebug.Add(
                new ArtilleryMissionDebugEntry(
                    entity,
                    transform.Position,
                    mission.TargetPosition,
                    mission.Status,
                    definition.MinimumRangeMeters,
                    definition.MaximumRangeMeters,
                    definition.AreaRadiusMeters,
                    mission.RoundsFired,
                    mission.RequestedRounds));
        }

        foreach (EntityId entity in
                 context.Entities.Query<
                     IndirectFireProjectileState,
                     WorldTransform>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            IndirectFireProjectileState projectile =
                context.Entities.GetComponent<IndirectFireProjectileState>(
                    entity);
            WorldTransform transform =
                context.Entities.GetComponent<WorldTransform>(entity);

            _projectileDebug.Add(
                new ArtilleryProjectileDebugEntry(
                    entity,
                    transform.Position,
                    projectile.TargetPosition,
                    projectile.ImpactTick,
                    projectile.AreaRadiusMeters));
        }

        LastDebugSnapshot =
            new ArtilleryDebugSnapshot(
                Metrics,
                _missionDebug.ToArray(),
                _projectileDebug.ToArray());
    }

    private void ResetTickMetrics()
    {
        _shotsFiredThisTick = 0;
        _impactsThisTick = 0;
        _areaDamageTargetsThisTick = 0;
        _ammunitionConsumedThisTick = 0.0;
        _areaDamageQueuedThisTick = 0.0;
    }
}
