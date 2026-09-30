using System.Numerics;
using ForgeLine.Assets;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Logistics;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public sealed class PresentationExtractor : ISimulationTickObserver
{
    private readonly PresentationSnapshotBuffer _buffer;
    private readonly FactionIntelligenceStore? _intelligence;
    private readonly FactionId _viewingFaction;
    private readonly AxisAlignedBounds? _intelligenceWorldBounds;
    private readonly PresentationExtractionContext? _extraction;
    private readonly RuntimeAssetCatalog? _runtimeAssets;
    private readonly VfxEffectPool _vfxPool = new();
    private readonly List<QueuedVfxInstance> _queuedVfx = new(256);
    private readonly HashSet<EntityId> _seenWrecks = [];
    private readonly Dictionary<EntityId, UnitSupplyState> _previousSupplyStates = [];

    public PresentationExtractor(
        PresentationSnapshotBuffer buffer,
        FactionIntelligenceStore? intelligence = null,
        FactionId viewingFaction = default,
        AxisAlignedBounds? intelligenceWorldBounds = null,
        RuntimeAssetCatalog? runtimeAssets = null)
    {
        _buffer =
            buffer ??
            throw new ArgumentNullException(nameof(buffer));
        _intelligence = intelligence;
        _viewingFaction = viewingFaction;
        _intelligenceWorldBounds = intelligenceWorldBounds;
        _runtimeAssets = runtimeAssets;

        if (_intelligence is not null &&
            !_viewingFaction.IsSpecified)
        {
            throw new ArgumentException(
                "Faction-specific intelligence extraction requires a viewing faction.",
                nameof(viewingFaction));
        }
    }

    public PresentationExtractor(
        PresentationSnapshotBuffer buffer,
        PresentationExtractionContext extraction,
        RuntimeAssetCatalog? runtimeAssets = null)
        : this(
            buffer,
            extraction?.Scenario.Intelligence ??
                throw new ArgumentNullException(nameof(extraction)),
            new FactionId(
                checked((uint)extraction.Player.Value)),
            extraction.Scenario.Terrain.WorldBounds,
            runtimeAssets)
    {
        _extraction = extraction;
    }

    public void OnTickCompleted(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        PresentationInteractionRequestSnapshot interaction =
            _extraction?.Interaction.Capture() ??
            default;

        if (_extraction is not null)
        {
            ApplyDebugCaptureState(
                _extraction.Scenario,
                interaction.DebugEnabled);
        }

        _vfxPool.BeginTick(
            context.Tick);
        CaptureCombatVfx(
            context);
        CaptureNewWreckVfx(
            context);

        FactionIntelligenceSnapshot? intelligenceSnapshot =
            CaptureIntelligence();

        RenderInstance[] instances =
            CaptureRenderInstances(
                context);

        PlayerExperienceSnapshot? playerExperience =
            _extraction is null
                ? null
                : CapturePlayerExperience(
                    context,
                    interaction);

        PlayerActionSnapshot? playerActions =
            _extraction is null
                ? null
                : PlayerActionSnapshotFactory.Capture(
                    context,
                    _extraction,
                    interaction);

        BuildingPlacementPreviewReadModel? placementPreview =
            _extraction is null
                ? null
                : CapturePlacementPreview(
                    context,
                    interaction);

        BuildingConstructionDebugSnapshot? construction =
            _extraction is null
                ? null
                : CaptureConstruction(
                    context,
                    interaction.DebugEnabled);

        PresentationDebugSnapshot? debug =
            _extraction is not null &&
            interaction.DebugEnabled
                ? CaptureDebug(
                    context,
                    interaction,
                    construction)
                : null;

        _buffer.Publish(
            new PresentationSnapshot(
                context.Tick,
                context.TickDuration,
                context.Entities.EntityCount,
                instances,
                intelligenceSnapshot,
                _extraction?.Scenario.Simulation.SessionId ??
                    SimulationSessionId.None,
                playerExperience,
                placementPreview,
                debug,
                construction,
                _extraction?.Scenario.Simulation.Diagnostics.Capture(
                    _extraction.Scenario.Simulation),
                playerActions,
                _vfxPool.Metrics));
    }

    private RenderInstance[] CaptureRenderInstances(
        SimulationContext context)
    {
        EntityRegistry entities =
            context.Entities;
        int transformCount =
            entities.GetComponentCount<WorldTransform>();
        int visualCount =
            Math.Min(
                transformCount,
                entities.GetComponentCount<VisualIdentity>());

        _queuedVfx.Clear();

        var baseInstances =
            visualCount == 0
                ? []
                : new RenderInstance[
                    visualCount];
        int baseIndex =
            0;

        foreach (EntityId entity in entities.Query<
                     WorldTransform,
                     VisualIdentity>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            if (!IsVisibleToViewer(
                    entities,
                    entity))
            {
                continue;
            }

            WorldTransform transform =
                entities.GetComponent<WorldTransform>(
                    entity);
            VisualIdentity visual =
                entities.GetComponent<VisualIdentity>(
                    entity);

            RenderVisibilityMask visibility =
                (RenderVisibilityMask)(uint)visual.Visibility;

            SelectablePresentationMetadata selectable =
                entities.TryGetComponent(
                    entity,
                    out ControllableEntity controllable) &&
                controllable.IsControllable
                    ? new SelectablePresentationMetadata(
                        controllable.Owner,
                        controllable.Category)
                    : SelectablePresentationMetadata.None;

            WorldFeaturePresentationMetadata worldFeature =
                entities.TryGetComponent(
                    entity,
                    out WorldPresentationIdentity worldPresentation)
                    ? new WorldFeaturePresentationMetadata(
                        worldPresentation.Visual,
                        worldPresentation.Kind,
                        ResolveResourcePresentationState(
                            entities,
                            entity,
                            worldPresentation.Kind),
                        worldPresentation.Inspectable)
                    : WorldFeaturePresentationMetadata.None;

            UnitFeaturePresentationMetadata unitFeature =
                ResolveUnitPresentationState(
                    entities,
                    entity);
            BuildingFeaturePresentationMetadata buildingFeature =
                ResolveBuildingPresentationState(
                    entities,
                    entity);
            InfrastructureFeaturePresentationMetadata infrastructureFeature =
                ResolveInfrastructurePresentationState(
                    entities,
                    entity);

            baseInstances[baseIndex++] =
                new RenderInstance(
                    entity,
                    new RenderTransform(
                        transform.Position,
                        transform.Rotation,
                        transform.Scale),
                    new RenderMeshHandle(
                        visual.VisualId),
                    RenderMaterialHandle.Default,
                    visibility,
                    entity.Index,
                    selectable,
                    worldFeature,
                    unitFeature,
                    buildingFeature,
                    infrastructureFeature);

            QueuePersistentStateVfx(
                transform,
                unitFeature,
                buildingFeature);
            QueueLogisticsVfx(
                entities,
                entity,
                transform);
        }

        QueueProjectileVfx(
            context);
        QueuePooledVfx();

        if (baseIndex == 0 &&
            _queuedVfx.Count == 0)
        {
            return [];
        }

        var instances =
            new RenderInstance[
                checked(
                    baseIndex +
                    _queuedVfx.Count)];

        if (baseIndex > 0)
        {
            baseInstances
                .AsSpan(
                    0,
                    baseIndex)
                .CopyTo(
                    instances);
        }

        const uint SyntheticBase =
            0xF000_0000U;

        for (int index = 0;
             index < _queuedVfx.Count;
             index++)
        {
            QueuedVfxInstance queued =
                _queuedVfx[index];
            uint syntheticIndex =
                checked(
                    SyntheticBase +
                    (uint)index);

            instances[
                baseIndex +
                index] =
                new RenderInstance(
                    new EntityId(
                        syntheticIndex,
                        1),
                    queued.Transform,
                    new RenderMeshHandle(
                        1),
                    RenderMaterialHandle.Default,
                    RenderVisibilityMask.World,
                    (uint)queued.Kind,
                    VfxFeature:
                        new VfxFeaturePresentationMetadata(
                            queued.Kind));
        }

        return instances;
    }

    private static UnitFeaturePresentationMetadata ResolveUnitPresentationState(
        EntityRegistry entities,
        EntityId entity)
    {
        if (entities.TryGetComponent(
                entity,
                out UnitWreckPresentationIdentity wreck))
        {
            return new UnitFeaturePresentationMetadata(
                wreck.UnitId,
                UnitPresentationDamageState.Wreck);
        }

        if (!entities.TryGetComponent(
                entity,
                out UnitIdentity unit) ||
            !UnitPresentationCatalog.TryGet(
                unit.UnitId,
                out _))
        {
            return UnitFeaturePresentationMetadata.None;
        }

        UnitPresentationDamageState state =
            UnitPresentationDamageState.Intact;

        if (entities.TryGetComponent(
                entity,
                out HealthState health))
        {
            state =
                health.Fraction switch
                {
                    <= 0.0 =>
                        UnitPresentationDamageState.Wreck,
                    <= 0.33 =>
                        UnitPresentationDamageState.Critical,
                    <= 0.67 =>
                        UnitPresentationDamageState.Damaged,
                    _ =>
                        UnitPresentationDamageState.Intact
                };
        }

        return new UnitFeaturePresentationMetadata(
            unit.UnitId,
            state,
            ResolveAimYaw(
                entities,
                entity));
    }

    private static float ResolveAimYaw(
        EntityRegistry entities,
        EntityId entity)
    {
        if (!entities.TryGetComponent(
                entity,
                out WeaponState weapon) ||
            !weapon.Target.IsValid ||
            !entities.IsAlive(
                weapon.Target) ||
            !entities.TryGetComponent(
                entity,
                out WorldTransform source) ||
            !entities.TryGetComponent(
                weapon.Target,
                out WorldTransform target))
        {
            return 0.0f;
        }

        Vector3 direction =
            target.Position -
            source.Position;
        direction.Y =
            0.0f;

        if (direction.LengthSquared() <=
            0.0001f)
        {
            return 0.0f;
        }

        Vector3 forward =
            Vector3.Transform(
                Vector3.UnitZ,
                source.Rotation);
        float bodyYaw =
            MathF.Atan2(
                forward.X,
                forward.Z);
        float targetYaw =
            MathF.Atan2(
                direction.X,
                direction.Z);
        float delta =
            targetYaw -
            bodyYaw;

        return MathF.Atan2(
            MathF.Sin(
                delta),
            MathF.Cos(
                delta));
    }

    private static BuildingFeaturePresentationMetadata ResolveBuildingPresentationState(
        EntityRegistry entities,
        EntityId entity)
    {
        if (entities.TryGetComponent(
                entity,
                out BuildingWreckPresentationIdentity wreck) &&
            BuildingPresentationCatalog.TryGet(
                wreck.BuildingId,
                out _))
        {
            return new BuildingFeaturePresentationMetadata(
                wreck.BuildingId,
                BuildingPresentationState.Destroyed);
        }

        if (entities.TryGetComponent(
                entity,
                out ConstructionSite site) &&
            BuildingPresentationCatalog.TryGet(
                site.BuildingId,
                out _))
        {
            BuildingPresentationState constructionState =
                site.Progress switch
                {
                    < 0.34f =>
                        BuildingPresentationState.ConstructionFoundation,
                    < 0.67f =>
                        BuildingPresentationState.ConstructionFrame,
                    _ =>
                        BuildingPresentationState.ConstructionShell
                };

            return new BuildingFeaturePresentationMetadata(
                site.BuildingId,
                constructionState,
                site.Progress);
        }

        if (!entities.TryGetComponent(
                entity,
                out CompletedBuilding building) ||
            !BuildingPresentationCatalog.TryGet(
                building.BuildingId,
                out _))
        {
            return BuildingFeaturePresentationMetadata.None;
        }

        BuildingPresentationState state =
            BuildingPresentationState.Operational;

        if (entities.TryGetComponent(
                entity,
                out HealthState health))
        {
            state =
                health.Fraction switch
                {
                    <= 0.0 =>
                        BuildingPresentationState.Destroyed,
                    <= 0.33 =>
                        BuildingPresentationState.Critical,
                    <= 0.67 =>
                        BuildingPresentationState.Damaged,
                    _ =>
                        BuildingPresentationState.Operational
                };

            if (state is
                BuildingPresentationState.Destroyed or
                BuildingPresentationState.Critical or
                BuildingPresentationState.Damaged)
            {
                return new BuildingFeaturePresentationMetadata(
                    building.BuildingId,
                    state);
            }
        }

        if (entities.TryGetComponent(
                entity,
                out PowerConsumer consumer) &&
            (!consumer.Enabled ||
             consumer.State !=
             PowerOperationalState.Powered))
        {
            state =
                BuildingPresentationState.Unpowered;
        }
        else if (entities.TryGetComponent(
                     entity,
                     out PowerGenerator generator) &&
                 (!generator.Enabled ||
                  generator.State ==
                  PowerGeneratorState.Offline))
        {
            state =
                BuildingPresentationState.Unpowered;
        }
        else if (entities.TryGetComponent(
                     entity,
                     out ResourceExtractor extractor) &&
                 extractor.State !=
                 ResourceExtractorState.Extracting)
        {
            state =
                BuildingPresentationState.Idle;
        }
        else if (entities.TryGetComponent(
                     entity,
                     out ProductionFacility production) &&
                 production.Status !=
                 ProductionStatus.Running)
        {
            state =
                BuildingPresentationState.Idle;
        }
        else if (entities.TryGetComponent(
                     entity,
                     out UnitProductionFacility unitProduction) &&
                 unitProduction.Status !=
                 UnitProductionStatus.Running)
        {
            state =
                BuildingPresentationState.Idle;
        }

        return new BuildingFeaturePresentationMetadata(
            building.BuildingId,
            state);
    }

    private static InfrastructureFeaturePresentationMetadata ResolveInfrastructurePresentationState(
        EntityRegistry entities,
        EntityId entity)
    {
        if (!entities.TryGetComponent(
                entity,
                out InfrastructurePresentationIdentity presentation))
        {
            return InfrastructureFeaturePresentationMetadata.None;
        }

        InfrastructurePresentationState state =
            InfrastructurePresentationState.Operational;

        if (entities.TryGetComponent(
                entity,
                out StrategicInfrastructureState infrastructure))
        {
            state =
                infrastructure.State switch
                {
                    StrategicInfrastructureOperationalState.Operational =>
                        InfrastructurePresentationState.Operational,
                    StrategicInfrastructureOperationalState.Restoring =>
                        InfrastructurePresentationState.Restoring,
                    StrategicInfrastructureOperationalState.Disabled =>
                        InfrastructurePresentationState.Disabled,
                    _ =>
                        InfrastructurePresentationState.Operational
                };
        }

        return new InfrastructureFeaturePresentationMetadata(
            presentation.Kind,
            state);
    }

    private static ResourceDepositPresentationState ResolveResourcePresentationState(
        EntityRegistry entities,
        EntityId entity,
        WorldPresentationKind kind)
    {
        if (kind != WorldPresentationKind.ResourceDeposit ||
            !entities.TryGetComponent(
                entity,
                out ResourceDeposit deposit))
        {
            return ResourceDepositPresentationState.None;
        }

        if (deposit.IsDepleted)
        {
            return ResourceDepositPresentationState.Depleted;
        }

        foreach (EntityId extractorEntity in entities.Query<ResourceExtractor>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            ResourceExtractor extractor =
                entities.GetComponent<ResourceExtractor>(
                    extractorEntity);

            if (extractor.Deposit == entity &&
                extractor.State == ResourceExtractorState.Extracting)
            {
                return ResourceDepositPresentationState.Active;
            }
        }

        return deposit.RemainingFraction >= 0.999_999
            ? ResourceDepositPresentationState.Untouched
            : ResourceDepositPresentationState.Active;
    }

    private PlayerExperienceSnapshot CapturePlayerExperience(
        SimulationContext context,
        in PresentationInteractionRequestSnapshot interaction)
    {
        PresentationExtractionContext extraction =
            _extraction!;
        VerticalSliceScenario scenario =
            extraction.Scenario;

        return PlayerExperienceSnapshotFactory.Capture(
            context.Entities,
            extraction.Player,
            extraction.Side.CommandCore,
            extraction.Side.StartingInventory,
            scenario.BattlefieldRuntime.MatchStateEntity,
            interaction.SelectedEntities,
            scenario.Inventories,
            scenario.Power,
            scenario.Production,
            scenario.UnitProduction,
            extraction.Commands.LatestFeedback,
            scenario.Intelligence,
            scenario.Services.UnitDefinitions,
            scenario.Services.BuildingDefinitions,
            context.Tick,
            scenario.Simulation.Clock.TicksPerSecond);
    }

    private BuildingPlacementPreviewReadModel? CapturePlacementPreview(
        SimulationContext context,
        in PresentationInteractionRequestSnapshot interaction)
    {
        if (interaction.PlacementRequest is not
            BuildingPlacementPreviewRequest request ||
            request.Issuer !=
            _extraction!.Player)
        {
            return null;
        }

        BuildingPlacementPreview preview =
            _extraction.Scenario.Services.BuildingPlacement.CreatePreview(
                context.Entities,
                request.Issuer,
                request.BuildingId,
                request.RequestedPosition,
                request.Orientation);

        return new BuildingPlacementPreviewReadModel(
            request.RequestId,
            context.Tick,
            preview);
    }

    private BuildingConstructionDebugSnapshot? CaptureConstruction(
        SimulationContext context,
        bool debugEnabled)
    {
        VerticalSliceScenario scenario =
            _extraction!.Scenario;

        if (!debugEnabled &&
            scenario.Services.BuildingConstruction.Metrics.ActiveSites <= 0)
        {
            return null;
        }

        return BuildingConstructionDebugSnapshot.Capture(
            context.Entities,
            scenario.Services.BuildingDefinitions);
    }

    private PresentationDebugSnapshot CaptureDebug(
        SimulationContext context,
        in PresentationInteractionRequestSnapshot interaction,
        BuildingConstructionDebugSnapshot? construction)
    {
        VerticalSliceScenario scenario =
            _extraction!.Scenario;

        ResourceExtractionDebugSnapshot resources =
            ResourceExtractionDebugSnapshot.Capture(
                context.Entities,
                scenario.Extraction.Metrics,
                scenario.Services.Resources);
        LogisticsNetworkDebugSnapshot logistics =
            LogisticsNetworkDebugSnapshot.Capture(
                scenario.Logistics);

        return new PresentationDebugSnapshot(
            scenario.Services.GroundMovement.CaptureDebugSnapshot(),
            scenario.Services.FormationMovement.CaptureDebugSnapshot(),
            scenario.Services.Navigation.World,
            scenario.Services.Navigation.LastCompletedPath,
            scenario.Services.SpatialIndex.CaptureDebugSnapshot(
                interaction.DebugPlaneHeight + 0.1f),
            construction,
            resources,
            logistics,
            scenario.CargoTransport.LastDebugSnapshot,
            scenario.AutomatedDistribution.LastDebugSnapshot,
            scenario.AutomatedDistribution.LastCapacityDebugSnapshot,
            scenario.BattlefieldSupply.LastDebugSnapshot,
            scenario.Services.CombatDebugSnapshots.LastDebugSnapshot,
            scenario.Artillery.LastDebugSnapshot,
            scenario.Readiness.LastDebugSnapshot,
            scenario.Services.TacticalCombat.DebugEntries,
            scenario.Services.TacticalCombat.Metrics,
            scenario.Services.AutomaticResupply.Metrics,
            scenario.Services.BattlefieldIntelligence.DebugSensors,
            scenario.Services.BattlefieldIntelligence.Metrics,
            scenario.Opponents.DebugSnapshot,
            CaptureCrossingStates(
                context.Entities,
                scenario.BattlefieldRuntime));
    }

    private void CaptureCombatVfx(
        SimulationContext context)
    {
        if (_extraction is null)
        {
            return;
        }

        IReadOnlyList<CombatEvent> events =
            _extraction.Scenario.Services.CombatRuntime.Events;

        for (int index = 0;
             index < events.Count;
             index++)
        {
            CombatEvent combatEvent =
                events[index];

            if (combatEvent.Tick !=
                    context.Tick ||
                !IsCombatEffectVisible(
                    context.Entities,
                    combatEvent))
            {
                continue;
            }

            switch (combatEvent.Type)
            {
                case CombatEventType.ShotFired:
                    CaptureShotVfx(
                        context.Entities,
                        events,
                        index,
                        combatEvent);
                    break;

                case CombatEventType.Impact:
                    CaptureImpactVfx(
                        context.Entities,
                        combatEvent);
                    break;
            }
        }
    }

    private void CaptureShotVfx(
        EntityRegistry entities,
        IReadOnlyList<CombatEvent> events,
        int eventIndex,
        in CombatEvent combatEvent)
    {
        VfxEffectKind muzzle =
            VfxPresentationCatalog.ResolveCombatEvent(
                combatEvent);
        WorldTransform sourceTransform =
            entities.TryGetComponent(
                combatEvent.Source,
                out WorldTransform source)
                ? source
                : new WorldTransform(
                    combatEvent.Position,
                    Quaternion.Identity,
                    Vector3.One);
        Vector3 muzzlePosition =
            ResolveSocketWorldPosition(
                entities,
                combatEvent.Source,
                "weapon_muzzle",
                sourceTransform.Position);

        VfxPresentationDefinition muzzleDefinition =
            VfxPresentationCatalog.Get(
                muzzle);

        _vfxPool.TrySpawn(
            muzzle,
            new RenderTransform(
                muzzlePosition,
                sourceTransform.Rotation,
                muzzleDefinition.BaseScale),
            combatEvent.Tick);

        if (combatEvent.Weapon ==
                DirectorateContent.WeaponIds.MainBattleCannon ||
            combatEvent.Weapon ==
                DirectorateContent.WeaponIds.MobileArtillery)
        {
            VfxPresentationDefinition dustDefinition =
                VfxPresentationCatalog.Get(
                    VfxEffectKind.PersistentDust);
            _vfxPool.TrySpawn(
                VfxEffectKind.PersistentDust,
                new RenderTransform(
                    sourceTransform.Position,
                    Quaternion.Identity,
                    dustDefinition.BaseScale),
                combatEvent.Tick);
        }

        if (_extraction is null ||
            !_extraction.Scenario.Services.Weapons.TryGet(
                combatEvent.Weapon,
                out WeaponDefinition? weapon) ||
            weapon is null ||
            weapon.DeliveryModel !=
                WeaponDeliveryModel.Hitscan)
        {
            return;
        }

        for (int index = eventIndex + 1;
             index < events.Count;
             index++)
        {
            CombatEvent candidate =
                events[index];

            if (candidate.Type !=
                    CombatEventType.Impact ||
                candidate.Source !=
                    combatEvent.Source ||
                candidate.Target !=
                    combatEvent.Target ||
                candidate.Weapon !=
                    combatEvent.Weapon)
            {
                continue;
            }

            QueueBeamVfx(
                VfxPresentationCatalog.ResolveProjectile(
                    combatEvent.Weapon),
                muzzlePosition,
                candidate.Position,
                combatEvent.Tick);
            break;
        }
    }

    private void CaptureImpactVfx(
        EntityRegistry entities,
        in CombatEvent combatEvent)
    {
        TargetClass? targetClass =
            entities.TryGetComponent(
                combatEvent.Target,
                out Targetable targetable)
                ? targetable.Class
                : null;

        VfxEffectKind impact =
            VfxPresentationCatalog.ResolveCombatEvent(
                combatEvent,
                targetClass);

        SpawnAt(
            impact,
            combatEvent.Position,
            combatEvent.Tick);

        VfxEffectKind? explosion =
            VfxPresentationCatalog.ResolveImpactExplosion(
                combatEvent.Weapon);

        if (explosion.HasValue)
        {
            SpawnAt(
                explosion.Value,
                combatEvent.Position,
                combatEvent.Tick);
        }
    }

    private void CaptureNewWreckVfx(
        SimulationContext context)
    {
        EntityRegistry entities =
            context.Entities;

        foreach (EntityId entity in
                 entities.Query<
                     UnitWreckPresentationIdentity,
                     WorldTransform>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            if (!_seenWrecks.Add(
                    entity) ||
                !IsVisibleToViewer(
                    entities,
                    entity))
            {
                continue;
            }

            UnitWreckPresentationIdentity wreck =
                entities.GetComponent<UnitWreckPresentationIdentity>(
                    entity);
            WorldTransform transform =
                entities.GetComponent<WorldTransform>(
                    entity);

            bool infantry =
                wreck.UnitId ==
                    UnitIds.RifleSquad ||
                wreck.UnitId ==
                    UnitIds.CombatEngineer;

            if (infantry)
            {
                SpawnAt(
                    VfxEffectKind.DestructionDebris,
                    transform.Position,
                    context.Tick);
                SpawnAt(
                    VfxEffectKind.PersistentDust,
                    transform.Position,
                    context.Tick);
                continue;
            }

            SpawnAt(
                VfxEffectKind.ExplosionVehicle,
                transform.Position,
                context.Tick);
            SpawnAt(
                VfxEffectKind.DestructionVehicleBurst,
                transform.Position,
                context.Tick);
            SpawnAt(
                VfxEffectKind.DestructionSmokePlume,
                transform.Position,
                context.Tick);
            SpawnAt(
                VfxEffectKind.DestructionPersistentFire,
                transform.Position,
                context.Tick);
        }

        foreach (EntityId entity in
                 entities.Query<
                     BuildingWreckPresentationIdentity,
                     WorldTransform>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            if (!_seenWrecks.Add(
                    entity) ||
                !IsVisibleToViewer(
                    entities,
                    entity))
            {
                continue;
            }

            WorldTransform transform =
                entities.GetComponent<WorldTransform>(
                    entity);

            SpawnAt(
                VfxEffectKind.ExplosionBuilding,
                transform.Position,
                context.Tick);
            SpawnAt(
                VfxEffectKind.DestructionBuildingBurst,
                transform.Position,
                context.Tick);
            SpawnAt(
                VfxEffectKind.DestructionDustCloud,
                transform.Position,
                context.Tick);
            SpawnAt(
                VfxEffectKind.DestructionSmokePlume,
                transform.Position,
                context.Tick);
            SpawnAt(
                VfxEffectKind.DestructionPersistentFire,
                transform.Position,
                context.Tick);
        }
    }

    private void QueueProjectileVfx(
        SimulationContext context)
    {
        EntityRegistry entities =
            context.Entities;

        foreach (EntityId entity in
                 entities.Query<
                     ProjectileState,
                     WorldTransform>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            ProjectileState projectile =
                entities.GetComponent<ProjectileState>(
                    entity);
            WorldTransform transform =
                entities.GetComponent<WorldTransform>(
                    entity);

            if (!IsPositionVisible(
                    transform.Position))
            {
                continue;
            }

            VfxEffectKind kind =
                VfxPresentationCatalog.ResolveProjectile(
                    projectile.Weapon);
            VfxPresentationDefinition definition =
                VfxPresentationCatalog.Get(
                    kind);
            Quaternion rotation =
                CreateDirectionRotation(
                    projectile.Velocity);

            _queuedVfx.Add(
                new QueuedVfxInstance(
                    kind,
                    new RenderTransform(
                        transform.Position,
                        rotation,
                        definition.BaseScale)));
        }

        foreach (EntityId entity in
                 entities.Query<
                     IndirectFireProjectileState,
                     WorldTransform>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            IndirectFireProjectileState projectile =
                entities.GetComponent<IndirectFireProjectileState>(
                    entity);
            WorldTransform transform =
                entities.GetComponent<WorldTransform>(
                    entity);

            if (!IsPositionVisible(
                    transform.Position))
            {
                continue;
            }

            VfxEffectKind kind =
                VfxPresentationCatalog.ResolveProjectile(
                    projectile.Weapon);
            VfxPresentationDefinition definition =
                VfxPresentationCatalog.Get(
                    kind);
            Vector3 direction =
                projectile.TargetPosition -
                transform.Position;

            _queuedVfx.Add(
                new QueuedVfxInstance(
                    kind,
                    new RenderTransform(
                        transform.Position,
                        CreateDirectionRotation(
                            direction),
                        definition.BaseScale)));
        }
    }

    private void QueuePooledVfx()
    {
        ReadOnlySpan<VfxPooledEffect> slots =
            _vfxPool.Slots;

        for (int index = 0;
             index < slots.Length;
             index++)
        {
            VfxPooledEffect effect =
                slots[index];

            if (!effect.Active)
            {
                continue;
            }

            _queuedVfx.Add(
                new QueuedVfxInstance(
                    effect.Kind,
                    effect.Transform));
        }
    }

    private void QueuePersistentStateVfx(
        in WorldTransform transform,
        in UnitFeaturePresentationMetadata unit,
        in BuildingFeaturePresentationMetadata building)
    {
        Vector3 anchor =
            transform.Position +
            Vector3.Transform(
                new Vector3(
                    0.0f,
                    MathF.Max(
                        0.6f,
                        MathF.Abs(
                            transform.Scale.Y) *
                        0.35f),
                    0.0f),
                transform.Rotation);

        if (unit.IsSpecified)
        {
            switch (unit.DamageState)
            {
                case UnitPresentationDamageState.Damaged:
                    QueueAt(
                        VfxEffectKind.PersistentLightSmoke,
                        anchor);
                    break;

                case UnitPresentationDamageState.Critical:
                    QueueAt(
                        VfxEffectKind.PersistentHeavySmoke,
                        anchor);
                    QueueAt(
                        VfxEffectKind.PersistentFire,
                        anchor);
                    QueueAt(
                        VfxEffectKind.PersistentSparks,
                        anchor);
                    break;

                case UnitPresentationDamageState.Wreck:
                    QueueAt(
                        VfxEffectKind.PersistentHeavySmoke,
                        anchor);
                    QueueAt(
                        VfxEffectKind.PersistentFire,
                        anchor);
                    break;
            }
        }

        if (building.IsSpecified)
        {
            switch (building.State)
            {
                case BuildingPresentationState.Damaged:
                    QueueAt(
                        VfxEffectKind.PersistentLightSmoke,
                        anchor);
                    break;

                case BuildingPresentationState.Critical:
                    QueueAt(
                        VfxEffectKind.PersistentHeavySmoke,
                        anchor);
                    QueueAt(
                        VfxEffectKind.PersistentFire,
                        anchor);
                    QueueAt(
                        VfxEffectKind.PersistentSparks,
                        anchor);
                    break;

                case BuildingPresentationState.Destroyed:
                    QueueAt(
                        VfxEffectKind.DestructionSmokePlume,
                        anchor);
                    QueueAt(
                        VfxEffectKind.DestructionPersistentFire,
                        anchor);
                    break;
            }
        }
    }

    private void QueueLogisticsVfx(
        EntityRegistry entities,
        EntityId entity,
        in WorldTransform transform)
    {
        if (entities.TryGetComponent(
                entity,
                out CargoTransportRuntimeState cargo))
        {
            if (cargo.Lifecycle ==
                CargoTransportLifecycleState.Loading)
            {
                Vector3 position =
                    ResolveSocketWorldPosition(
                        entities,
                        entity,
                        "cargo_load",
                        transform.Position);
                QueueAt(
                    VfxEffectKind.LogisticsLoading,
                    position);
                QueueAt(
                    VfxEffectKind.LogisticsResourceTransfer,
                    position);
            }
            else if (cargo.Lifecycle ==
                     CargoTransportLifecycleState.Unloading)
            {
                Vector3 position =
                    ResolveSocketWorldPosition(
                        entities,
                        entity,
                        "cargo_load",
                        transform.Position);
                QueueAt(
                    VfxEffectKind.LogisticsUnloading,
                    position);
                QueueAt(
                    VfxEffectKind.LogisticsResourceTransfer,
                    position);
            }
        }

        if (!entities.TryGetComponent(
                entity,
                out UnitSupplyState supply))
        {
            return;
        }

        if (_previousSupplyStates.TryGetValue(
                entity,
                out UnitSupplyState previous))
        {
            const double Epsilon =
                0.000_001;

            bool fuelIncreased =
                supply.FuelFraction >
                previous.FuelFraction +
                Epsilon;
            bool ammunitionIncreased =
                supply.AmmunitionFraction >
                previous.AmmunitionFraction +
                Epsilon;

            if (fuelIncreased ||
                ammunitionIncreased)
            {
                Vector3 position =
                    ResolveSocketWorldPosition(
                        entities,
                        entity,
                        "supply_transfer",
                        transform.Position);

                QueueAt(
                    VfxEffectKind.LogisticsSupplyTransfer,
                    position);

                if (fuelIncreased)
                {
                    QueueAt(
                        VfxEffectKind.LogisticsRefuel,
                        position);
                }

                if (ammunitionIncreased)
                {
                    QueueAt(
                        VfxEffectKind.LogisticsRearm,
                        position);
                }
            }
        }

        _previousSupplyStates[entity] =
            supply;
    }

    private void QueueAt(
        VfxEffectKind kind,
        Vector3 position)
    {
        VfxPresentationDefinition definition =
            VfxPresentationCatalog.Get(
                kind);

        _queuedVfx.Add(
            new QueuedVfxInstance(
                kind,
                new RenderTransform(
                    position,
                    Quaternion.Identity,
                    definition.BaseScale)));
    }

    private void SpawnAt(
        VfxEffectKind kind,
        Vector3 position,
        SimulationTick tick)
    {
        VfxPresentationDefinition definition =
            VfxPresentationCatalog.Get(
                kind);

        _vfxPool.TrySpawn(
            kind,
            new RenderTransform(
                position,
                Quaternion.Identity,
                definition.BaseScale),
            tick);
    }

    private void QueueBeamVfx(
        VfxEffectKind kind,
        Vector3 start,
        Vector3 end,
        SimulationTick tick)
    {
        Vector3 delta =
            end -
            start;
        float length =
            delta.Length();

        if (!float.IsFinite(
                length) ||
            length <= 0.001f)
        {
            return;
        }

        VfxPresentationDefinition definition =
            VfxPresentationCatalog.Get(
                kind);
        Vector3 scale =
            definition.BaseScale with
            {
                Z =
                    length
            };

        _vfxPool.TrySpawn(
            kind,
            new RenderTransform(
                (start + end) *
                0.5f,
                CreateDirectionRotation(
                    delta),
                scale),
            tick);
    }

    private Vector3 ResolveSocketWorldPosition(
        EntityRegistry entities,
        EntityId entity,
        string socketName,
        Vector3 fallback)
    {
        if (_runtimeAssets is null ||
            !entities.TryGetComponent(
                entity,
                out UnitIdentity unit) ||
            !entities.TryGetComponent(
                entity,
                out WorldTransform transform) ||
            !UnitPresentationCatalog.TryGet(
                unit.UnitId,
                out UnitPresentationDefinition definition) ||
            !_runtimeAssets.TryGet(
                AssetId.Parse(
                    definition.MeshAssetId),
                out RuntimeAssetRecord? record) ||
            record is null)
        {
            return fallback;
        }

        for (int index = 0;
             index < record.Sockets.Count;
             index++)
        {
            AssetSocket socket =
                record.Sockets[index];

            if (!string.Equals(
                    socket.Name,
                    socketName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            Vector3 local =
                new Vector3(
                    socket.X,
                    socket.Y,
                    socket.Z) *
                transform.Scale;

            return
                transform.Position +
                Vector3.Transform(
                    local,
                    transform.Rotation);
        }

        return fallback;
    }

    private bool IsCombatEffectVisible(
        EntityRegistry entities,
        in CombatEvent combatEvent)
    {
        if (combatEvent.Source.IsValid &&
            entities.IsAlive(
                combatEvent.Source) &&
            IsVisibleToViewer(
                entities,
                combatEvent.Source))
        {
            return true;
        }

        if (combatEvent.Target.IsValid &&
            entities.IsAlive(
                combatEvent.Target) &&
            IsVisibleToViewer(
                entities,
                combatEvent.Target))
        {
            return true;
        }

        return IsPositionVisible(
            combatEvent.Position);
    }

    private bool IsPositionVisible(
        Vector3 position)
    {
        if (_intelligence is null ||
            !_viewingFaction.IsSpecified)
        {
            return true;
        }

        return
            _intelligence.GetTerrainState(
                _viewingFaction,
                _intelligence.WorldToCell(
                    position)) ==
            IntelligenceState.Visible;
    }

    private static Quaternion CreateDirectionRotation(
        Vector3 direction)
    {
        float lengthSquared =
            direction.LengthSquared();

        if (!float.IsFinite(
                lengthSquared) ||
            lengthSquared <=
            0.000_001f)
        {
            return Quaternion.Identity;
        }

        Vector3 normalized =
            Vector3.Normalize(
                direction);
        float yaw =
            MathF.Atan2(
                normalized.X,
                normalized.Z);
        float pitch =
            -MathF.Asin(
                Math.Clamp(
                    normalized.Y,
                    -1.0f,
                    1.0f));

        return Quaternion.CreateFromYawPitchRoll(
            yaw,
            pitch,
            0.0f);
    }

    private readonly record struct QueuedVfxInstance(
        VfxEffectKind Kind,
        RenderTransform Transform);

    private static void ApplyDebugCaptureState(
        VerticalSliceScenario scenario,
        bool enabled)
    {
        scenario.Services.GroundMovement.DebugCaptureEnabled =
            enabled;
        scenario.Services.FormationMovement.DebugCaptureEnabled =
            enabled;
        scenario.BattlefieldSupply.DebugCaptureEnabled =
            enabled;
        scenario.Services.BattlefieldIntelligence.TimingEnabled =
            enabled;
        scenario.Services.BattlefieldIntelligence.DebugCaptureEnabled =
            enabled;
        scenario.Artillery.DebugCaptureEnabled =
            enabled;
        scenario.Services.TargetAcquisition.DebugCaptureEnabled =
            enabled;
        scenario.Services.TacticalCombat.DebugCaptureEnabled =
            enabled;
        scenario.Readiness.DebugCaptureEnabled =
            enabled;
        scenario.Services.CombatDebugSnapshots.DebugCaptureEnabled =
            enabled;
        scenario.Opponents.DebugCaptureEnabled =
            enabled;
    }

    private bool IsVisibleToViewer(
        EntityRegistry entities,
        EntityId entity)
    {
        if (_intelligence is null ||
            !_viewingFaction.IsSpecified ||
            !entities.TryGetComponent(
                entity,
                out IntelligenceSignature signature) ||
            signature.Faction == _viewingFaction)
        {
            return true;
        }

        return _intelligence.IsEntityCurrentlyIdentified(
            _viewingFaction,
            entity);
    }

    private FactionIntelligenceSnapshot? CaptureIntelligence()
    {
        if (_intelligence is null ||
            !_viewingFaction.IsSpecified)
        {
            return null;
        }

        return _intelligenceWorldBounds is AxisAlignedBounds bounds
            ? _intelligence.Capture(
                _viewingFaction,
                bounds)
            : _intelligence.Capture(
                _viewingFaction);
    }

    private static Dictionary<
        string,
        StrategicInfrastructureOperationalState> CaptureCrossingStates(
        EntityRegistry entities,
        PrototypeBattlefieldRuntime runtime)
    {
        var states =
            new Dictionary<
                string,
                StrategicInfrastructureOperationalState>(
                    StringComparer.Ordinal);

        foreach (KeyValuePair<string, EntityId> pair in
                 runtime.CrossingEntities)
        {
            if (entities.TryGetComponent(
                    pair.Value,
                    out StrategicInfrastructureState state))
            {
                states[pair.Key] =
                    state.State;
            }
        }

        return states;
    }
}
