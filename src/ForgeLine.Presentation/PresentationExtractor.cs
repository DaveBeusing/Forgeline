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

public sealed partial class PresentationExtractor : ISimulationTickObserver, IDecalVisibility, IWorldAudioVisibility
{
    private static readonly ArtilleryWeaponCatalog DefaultArtilleryWeapons = DirectorateContent.CreateArtilleryWeaponCatalog();
    private readonly MechanicalPoseHistory _mechanicalHistory = new();
    private readonly HashSet<EntityId> _mechanicalShots = [];
    private readonly HashSet<EntityId> _infantryHits = [];
    private readonly HashSet<EntityId> _infantryDeaths = [];
    private readonly Dictionary<string, (RuntimeSkinData Skin, RuntimeAnimationData Clip, int[] Nodes)> _infantrySockets = new(StringComparer.Ordinal);
    private readonly PresentationSnapshotBuffer _buffer;
    private readonly FactionIntelligenceStore? _intelligence;
    private readonly FactionId _viewingFaction;
    private readonly AxisAlignedBounds? _intelligenceWorldBounds;
    private readonly PresentationExtractionContext? _extraction;
    private readonly RuntimeAssetCatalog? _runtimeAssets;
    private readonly Dictionary<EntityId, (WorldVisualId Visual, ResourceDepositVariantSelection Selection, Vector3 Tint)> _resourceVariants = [];
    private readonly ResourceDepositFieldComposer? _resourceFields;
    private readonly RenderInstance[] _staticDressing = [];
    private readonly CrossingVisualLayout? _crossingLayout;
    private readonly EntityId[] _visualCrossingEntities = [];
    private RenderInstance[] _captureScratch = [];
    internal const int MaximumRetainedCaptureInstances = 16_384;
    internal long CaptureScratchRetainedBytes => (long)_captureScratch.Length * System.Runtime.CompilerServices.Unsafe.SizeOf<RenderInstance>() +
        (long)_combatGroupScratch.Capacity * System.Runtime.CompilerServices.Unsafe.SizeOf<CombatGroupMemberReadModel>();
    private readonly VfxEffectPool _vfxPool = new();
    private readonly DecalPool _decals = new();
    private readonly RenderInstance[] _decalScratch = new RenderInstance[128];
    private EntityRegistry? _decalEntities;
    private ulong? _vfxTick;
    private SimulationSessionId _vfxSession;
    private readonly AlertLifecycleTracker _alerts = new();
    private readonly CargoFleetShortageTracker _cargoFleet = new();
    private readonly List<CombatGroupMemberReadModel> _combatGroupScratch = [];
    private CombatGroupOperationalSnapshot? _previousCombatGroups;
    private readonly WorldAudioEventBridge? _worldAudio;
    public const int MaximumSnapshotEffects = 4096;
    private readonly List<QueuedVfxInstance> _queuedVfx = new(256);
    private int _droppedQueuedVfx;
    private readonly HashSet<EntityId> _seenWrecks = [];
    private readonly Dictionary<EntityId, UnitSupplyState> _previousSupplyStates = [];

    public PresentationExtractor(
        PresentationSnapshotBuffer buffer,
        FactionIntelligenceStore? intelligence = null,
        FactionId viewingFaction = default,
        AxisAlignedBounds? intelligenceWorldBounds = null,
        RuntimeAssetCatalog? runtimeAssets = null,
        ITerrainQuery? resourceTerrain = null)
    {
        _buffer =
            buffer ??
            throw new ArgumentNullException(nameof(buffer));
        _intelligence = intelligence;
        _viewingFaction = viewingFaction;
        _intelligenceWorldBounds = intelligenceWorldBounds;
        _runtimeAssets = runtimeAssets;
        _resourceFields = resourceTerrain is null ? null : new(resourceTerrain, runtimeAssets);

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
        _resourceVariants = ResourceDepositVariantSeed.CreateMap(extraction.Scenario);
        _resourceFields = ResourceDepositFieldComposer.Create(extraction.Scenario, runtimeAssets);
        _worldAudio = new();
        _crossingLayout = CrossingVisualLayout.Create(extraction.Scenario.Battlefield, extraction.Scenario.Terrain);
        _visualCrossingEntities = extraction.Scenario.BattlefieldRuntime.CrossingEntities.Values.ToArray();
        _staticDressing = CaptureStaticDressing(extraction.Scenario, _crossingLayout);
    }

    private static RenderInstance[] CaptureStaticDressing(MatchRuntime scenario, CrossingVisualLayout layout)
    {
        var instances = new List<RenderInstance>();
        foreach (var item in scenario.Battlefield.WorldObjects)
        {
            if (!item.PresentationOnly) continue;
            if (!scenario.Terrain.TrySampleHeight(item.Position.X, item.Position.Z, out float height))
                throw new InvalidOperationException($"Dressing '{item.Key}' lies outside terrain.");
            float offset = item.Kind == WorldPresentationKind.Vegetation ? 0
                : item.Kind == WorldPresentationKind.Decal ? .04f : item.Scale.Y * .5f;
            // A separate presentation identity range cannot consume simulation allocator slots.
            uint identity = checked(0xE000_0000U + (uint)instances.Count);
            RenderTransform display = layout.Aprons.TryGetValue(item.Key, out var apron) ? apron :
                new(new(item.Position.X, height + offset, item.Position.Z),
                    Quaternion.CreateFromAxisAngle(Vector3.UnitY, item.RotationDegrees * MathF.PI / 180), item.Scale);
            instances.Add(new(new EntityId(identity, 1), display,
                new(checked(5_000u + (uint)item.Visual)), RenderMaterialHandle.Default,
                RenderVisibilityMask.World, identity,
                WorldFeature: new(item.Visual, item.Kind, default, Inspectable: false)));
        }
        instances.AddRange(layout.Approaches);
        return instances.ToArray();
    }

    public void OnTickCompleted(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        PresentationInteractionRequestSnapshot interaction =
            _extraction?.Interaction.Capture() ??
            default;

        DebugOverlayCategory requiredDebugData =
            DebugOverlayPolicy.ResolveRequiredData(
                interaction.DebugCategories,
                interaction.StrategicOverlay);
        bool diagnosticsRequested =
            requiredDebugData !=
            DebugOverlayCategory.None;

        if (_extraction is not null)
        {
            ApplyDebugCaptureState(
                _extraction.Scenario,
                requiredDebugData);
        }

        _worldAudio?.Begin(_extraction?.Scenario.Simulation.SessionId ?? SimulationSessionId.None,
            _extraction?.Player ?? default, context.Tick, context.TickDuration);
        _mechanicalShots.Clear(); _infantryHits.Clear(); _infantryDeaths.Clear();
        _mechanicalHistory.Begin(_extraction?.Scenario.Simulation.SessionId ?? SimulationSessionId.None,
            context.Tick.Value, context.TickDuration.TotalSeconds);
        _vfxPool.BeginTick(
            context.Tick);
        SimulationSessionId vfxSession = _extraction?.Scenario.Simulation.SessionId ?? SimulationSessionId.None;
        if (_vfxTick.HasValue && (vfxSession != _vfxSession || context.Tick.Value < _vfxTick.Value))
        {
            _vfxPool.Reset(); _decals.Reset(); _seenWrecks.Clear(); _previousSupplyStates.Clear(); _vfxTick = null;
        }
        _decalEntities = context.Entities; _decals.BeginTick(context.Tick);
        if (_vfxTick != context.Tick.Value) CaptureCombatVfx(context);
        _vfxTick = context.Tick.Value; _vfxSession = vfxSession;
        CaptureNewWreckVfx(
            context);

        FactionIntelligenceSnapshot? intelligenceSnapshot =
            CaptureIntelligence();

        RenderInstance[] instances =
            CaptureRenderInstances(
                context);
        _resourceFields?.Apply(instances, context.Entities);

        PlayerExperienceSnapshot? playerExperience =
            _extraction is null
                ? null
                : CapturePlayerExperience(
                    context,
                    interaction);

        CargoFleetCapacityObservation? cargoFleet = null;
        if (_extraction is not null && playerExperience is { IsMatchComplete: false } activePlayer)
        {
            var scenario = _extraction.Scenario;
            cargoFleet = _cargoFleet.Observe(context, scenario.Simulation.SessionId,
                _extraction.Player, scenario.AutomatedDistribution.LastDebugSnapshot,
                scenario.Inventories);
            if (cargoFleet.HasValue)
                playerExperience = activePlayer with
                {
                    Alerts = activePlayer.Alerts | PlayerAlertState.CargoFleetShortage
                };
        }

        PlayerActionSnapshot? playerActions =
            _extraction is null
                ? null
                : PlayerActionSnapshotFactory.Capture(
                    context,
                    _extraction,
                    interaction);

        StrategicOverlaySnapshot? strategicOverlay =
            _extraction is null ||
            interaction.StrategicOverlay ==
                StrategicOverlayMode.None
                ? null
                : StrategicOverlaySnapshotFactory.Capture(
                    context,
                    _extraction,
                    interaction.StrategicOverlay,
                    intelligenceSnapshot);

        CombatGroupOperationalSnapshot? combatGroups =
            _extraction is null
                ? null
                : CombatGroupOperationalSnapshotFactory.Capture(
                    context,
                    _extraction.Player,
                    _extraction.Scenario.Simulation.SessionId,
                    _combatGroupScratch, _previousCombatGroups);

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
            diagnosticsRequested
                ? CaptureDebug(
                    context,
                    interaction,
                    construction)
                : null;

        _extraction?.Scenario.Services.GroundMovement.SetNavigationDiagnosticObservations(interaction.NavigationDiagnosticsOpen);
        if (_extraction is { } diagnosticSource) diagnosticSource.Scenario.Progress.SetNavigationDiagnosticWindow(diagnosticSource.Player, interaction.NavigationDiagnosticsOpen, context.Tick);
        var operationsDiagnosis = _extraction is null ? null :
            OperationsMeasurementCapture.Attach(context, _extraction,
                OperationsChainCapture.Capture(context, _extraction, playerActions, playerExperience, instances, interaction.SelectedEntities),
                playerActions, instances, interaction.OperationsOpen && playerExperience is { IsMatchComplete: false });
        var operationsCauses = _extraction is not null && playerExperience is { IsMatchComplete: false }
            ? OperationsCauseCapture.Capture(context, _extraction, instances) : null;
        _buffer.Publish(
            new PresentationSnapshot(
                instances,
                context.Tick,
                context.TickDuration,
                context.Entities.EntityCount,
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
                _vfxPool.Metrics with { PublishedEffects = _queuedVfx.Count, DiscardedSnapshotEffects = _droppedQueuedVfx },
                strategicOverlay,
                combatGroups,
                _extraction is null || interaction.HoverSessionId != _extraction.Scenario.Simulation.SessionId
                    ? null : PlayerHoverSummaryFactory.Capture(
                    context, _extraction, interaction.HoveredEntity),
                _extraction is null ? null : PlayerGuidanceSummaryFactory.Capture(context, _extraction, intelligenceSnapshot),
                _extraction is not null && interaction.OperationsOpen ? OperationsSnapshotFactory.Capture(context, _extraction, instances,
                    interaction.SelectedEntities.Count == 1 ? interaction.SelectedEntities[0] : default, interaction.SelectedEntities) : null,
                _extraction is not null && playerExperience is { } experience ? _alerts.Capture(context, _extraction, experience, cargoFleet).WithCauses(operationsCauses) : null) { Diagnosis = operationsDiagnosis, OperationsCauses = operationsCauses,
                    NavigationDiagnosis = interaction.NavigationDiagnosticsOpen ? CaptureNavigationDiagnostics(context, instances) : null,
                    ConstructionTemplate = _extraction is null ? null : ConstructionTemplatePreviewFactory.Capture(context, _extraction),
                    Conveyors = CaptureConveyors(context), DecalMetrics = _decals.Metrics, WorldAudio = _worldAudio?.Complete(playerExperience, this),
                    SelectedOrders = _extraction is null || interaction.SelectedEntities.Count == 0
                        ? null
                        : SelectedUnitOrderSnapshotFactory.Capture(context, _extraction, interaction.SelectedEntities),
                    DockingZones = _extraction is null ? null : LogisticsZoneSnapshot.Capture(
                        context.Entities, _extraction.Player, _extraction.Scenario.Terrain,
                        interaction.SelectedEntities,
                        interaction.HoverSessionId == _extraction.Scenario.Simulation.SessionId
                            ? interaction.HoveredEntity : default) });
        _previousCombatGroups = combatGroups;
    }

    private ConveyorConstructionReadModel? CaptureConveyors(SimulationContext context)
    {
        if (_extraction is null) return null;
        var request=_extraction.Interaction.CaptureConveyorRequest();
        var service=_extraction.Scenario.Services.Conveyors;
        ConveyorPlan? plan=request is {} r && r.Issuer==_extraction.Player?
            service.Preview(context.Entities,_extraction.Player,r.Request,_extraction.Side.CommandCore):null;
        double available=context.Entities.TryGetComponent(_extraction.Side.CommandCore,out InventoryStorage storage)?
            _extraction.Scenario.Inventories.GetAvailableQuantity(storage.InventoryId,ResourceIds.Steel):0;
        var scenario = _extraction.Scenario;
        var ports = ConveyorConstructionService.CapturePorts(context.Entities, _extraction.Player);
        // The marker/description budget is independent of the authoritative port set.
        var information = ports.Take(128).Select(port => new ConveyorPortInformation(port.Building, port.Input,
            ConveyorPortInformationCapture.Materials(scenario, scenario.Services.ConveyorFlow.AllowedResources(context.Entities, port)))).ToArray();
        return new(request?.Id??0,context.Tick,plan,available,ConveyorConstructionService.Capture(context.Entities,_extraction.Player,scenario.Inventories),ports)
        { PortInformation = Array.AsReadOnly(information),
            BeltMaterials = ConveyorPortInformationCapture.Materials(scenario, (1 << ConveyorSimulationSystem.EligibleResources.Count) - 1) };
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

        _queuedVfx.Clear(); _droppedQueuedVfx = 0;

        int required = checked(visualCount + _staticDressing.Length);
        bool privateScratch = _intelligence is not null && _viewingFaction.IsSpecified;
        var baseInstances = privateScratch ? PrepareCaptureScratch(required) :
            required == 0 ? [] : new RenderInstance[required];
        int baseIndex =
            0;

        bool bridgeOperational = false, fordOperational = false;
        if (_extraction is not null && _crossingLayout is not null)
        {
            foreach (var crossing in _visualCrossingEntities)
            {
                if (!entities.TryGetComponent(crossing, out StrategicInfrastructureState state) ||
                    state.State != StrategicInfrastructureOperationalState.Operational ||
                    !entities.TryGetComponent(crossing, out InfrastructurePresentationIdentity identity)) continue;
                bridgeOperational |= identity.Kind == InfrastructurePresentationKind.RoadBridge;
                fordOperational |= identity.Kind == InfrastructurePresentationKind.Ford;
            }
        }
        var conveyorStates=_extraction is null?new Dictionary<EntityId,ConveyorLinkStatus>():
            ConveyorConstructionService.Capture(entities,_extraction.Player).ToDictionary(p=>p.Entity,p=>p.Status);
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
                        controllable.Category,
                        _extraction is not null && controllable.Owner == _extraction.Player &&
                        entities.HasComponent<GroundMovement>(entity))
                    : SelectablePresentationMetadata.None;

            if (entities.TryGetComponent(entity, out BridgeCombat bridgeCombat))
                selectable = new SelectablePresentationMetadata(new PlayerId(bridgeCombat.Faction.Value),
                    ControllableEntityCategory.Building, false);

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

            if (worldFeature.Kind == WorldPresentationKind.ResourceDeposit &&
                _resourceVariants.TryGetValue(entity, out var resourceVariant) && resourceVariant.Visual == worldFeature.Visual)
                worldFeature = worldFeature with { ResourceVariant = resourceVariant.Selection, ResourceTint = resourceVariant.Tint };

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

            // Keep legacy ECS transforms replay-stable; root the mesh only in presentation.
            if (_extraction is not null && worldFeature.Kind == WorldPresentationKind.Vegetation &&
                _extraction.Scenario.Terrain.TrySampleHeight(transform.Position.X, transform.Position.Z, out float ground))
                transform = transform with { Position = transform.Position with { Y = ground } };

            bool ownedProductionMotion = _extraction is not null &&
                MechanicalDoorPolicy.CanReadProduction(selectable.Owner, _extraction.Player);

            // Keep simulation transforms and mechanical travel untouched; adjust only the drawn copy.
            var renderTransform = ResourceDepositVisualTransform.Apply(
                new RenderTransform(transform.Position, transform.Rotation, transform.Scale), worldFeature);
            if (_crossingLayout is not null)
            {
                // Terrain wear lies under the opaque intact deck; do not project soil through concrete.
                if (_crossingLayout.IsCoveredGroundWear(renderTransform,worldFeature.Visual,bridgeOperational)) continue;
                if (worldFeature.Visual is WorldVisualId.DecalConcreteCrack or WorldVisualId.DecalRoadWear)
                    renderTransform = _crossingLayout.AlignBridgeOverlay(renderTransform,worldFeature.Visual,bridgeOperational);
                if (infrastructureFeature.Kind is InfrastructurePresentationKind.RoadBridge or InfrastructurePresentationKind.Ford &&
                    entities.TryGetComponent(entity, out InfrastructurePresentationIdentity crossingIdentity) &&
                    _crossingLayout.Crossings.TryGetValue(crossingIdentity.Key, out var crossingTransform))
                    renderTransform = crossingTransform;
                if (!unitFeature.IsWreck && (unitFeature.Unit == UnitIds.CargoTruck || unitFeature.Unit == UnitIds.SupplyTruck))
                    renderTransform = _crossingLayout.GroundTruck(renderTransform, unitFeature.Unit == UnitIds.CargoTruck,
                        bridgeOperational, fordOperational);
            }
            ConveyorFeaturePresentationMetadata conveyor=default;
            if (entities.TryGetComponent(entity,out ConveyorPiece piece))
            {
                string state=piece.Placement.IsSupport || piece.Placement.Module==ConveyorModule.Endcap?"structural_intact":"idle_no_input";
                if(conveyorStates.TryGetValue(entity,out ConveyorLinkStatus link))state=ConveyorFeaturePresentationMetadata.Surface(link);
                if (entities.TryGetComponent(entity,out HealthState health) && health.Fraction<.7)
                    state=health.IsDepleted?"destroyed":health.Fraction<.25?"critical":"damaged";
                conveyor=new(piece.Placement,state);
                renderTransform=new(piece.Placement.RenderPivot,piece.Placement.Rotation,new Vector3(18)*piece.Placement.LocalScale);
            }
            baseInstances[baseIndex++] =
                new RenderInstance(
                    entity,
                    renderTransform,
                    new RenderMeshHandle(
                        visual.VisualId),
                    RenderMaterialHandle.Default,
                    visibility,
                    entity.Index,
                    selectable,
                    worldFeature,
                    unitFeature,
                    buildingFeature,
                    infrastructureFeature,
                    ConveyorFeature: conveyor,
                    MechanicalPose: conveyor.IsSpecified && conveyorStates.ContainsKey(entity) && entities.TryGetComponent(entity,out ConveyorFlowState flowPose)
                        ? new MechanicalPose(true,0,flowPose.ActiveSeconds,context.Tick.Value*context.TickDuration.TotalSeconds,-1,0,0)
                        : unitFeature.IsSpecified || buildingFeature.IsSpecified
                        ? _mechanicalHistory.Capture(entity, transform.Position, transform.Rotation,
                            unitFeature.IsSpecified && !unitFeature.IsWreck || buildingFeature.State == BuildingPresentationState.Operational,
                            _mechanicalShots.Contains(entity), ResolveAimElevation(entities, entity),
                            buildingFeature.State == BuildingPresentationState.Operational,
                            MechanicalDoorPolicy.CanMove(buildingFeature.State,
                                ownedProductionMotion && entities.TryGetComponent(entity, out UnitProductionFacility motionFactory) ? motionFactory.Status : null,
                                ownedProductionMotion && entities.TryGetComponent(entity, out ProductionFacility motionProduction) ? motionProduction.Status : null),
                            ResolveInfantryAction(entities, entity, unitFeature, context.Tick, context.TickDuration.TotalSeconds)) : default);

            CaptureWorldAudioEntity(entities, entity, transform.Position, selectable, unitFeature, buildingFeature);

            QueuePersistentStateVfx(
                entity,
                transform,
                unitFeature,
                buildingFeature);
            if (unitFeature.IsSpecified && !unitFeature.IsWreck &&
                entities.TryGetComponent(entity, out UnitIdentity movingUnit) &&
                movingUnit.UnitId != UnitIds.RifleSquad && movingUnit.UnitId != UnitIds.CombatEngineer &&
                _mechanicalHistory.HasMoved(entity))
            {
                Vector3 dust = transform.Position - Vector3.Transform(Vector3.UnitZ * MathF.Abs(transform.Scale.Z) * .35f, transform.Rotation);
                if (_extraction is not null && _extraction.Scenario.Terrain.TrySampleHeight(dust.X, dust.Z, out float dustGround))
                    dust.Y = dustGround + .15f;
                QueueAt(VfxEffectKind.PersistentDust, dust, entity);
                if (context.Tick.Value % 8 == entity.Index % 8 && (!_viewingFaction.IsSpecified ||
                    entities.TryGetComponent(entity, out IntelligenceSignature signature) && signature.Faction == _viewingFaction))
                {
                    Vector3 trackPosition = dust with { Y = dust.Y - .09f };
                    bool tracked = movingUnit.UnitId == UnitIds.MainBattleTank || movingUnit.UnitId == UnitIds.MobileArtillery;
                    _decals.TrySpawn(new(tracked ? DecalKind.TrackedVehicle : DecalKind.TireTrack, DecalSurface.Ground,
                        new(trackPosition, transform.Rotation, new(Math.Clamp(MathF.Abs(transform.Scale.X) * .8f, .5f, 12), 1, 4)),
                        LifetimeTicks: 200, Strength: .28f), context.Tick, out _);
                }
            }
            QueueIndustrialVfx(entities, entity, transform, buildingFeature);
            QueueLogisticsVfx(
                entities,
                entity,
                transform);
        }

        _staticDressing.CopyTo(baseInstances, baseIndex);
        baseIndex += _staticDressing.Length;

        QueueProjectileVfx(
            context);
        QueuePooledVfx();
        int decalCount = _decals.CopyVisibleTo(_decalScratch, context.Tick, this);

        // This fresh array is transferred to the snapshot and never reused.
        if (!privateScratch && _queuedVfx.Count == 0 && baseIndex == baseInstances.Length && decalCount == 0)
            return baseInstances;

        if (baseIndex == 0 &&
            _queuedVfx.Count == 0 && decalCount == 0)
        {
            return [];
        }

        var instances =
            new RenderInstance[
                checked(
                    baseIndex +
                    _queuedVfx.Count + decalCount)];

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
                            queued.Kind, queued.EmitterId, queued.Generation, queued.Continuous, queued.SpawnedAtTick));
        }

        _decalScratch.AsSpan(0, decalCount).CopyTo(instances.AsSpan(baseIndex + _queuedVfx.Count));
        return instances;
    }

    private RenderInstance[] PrepareCaptureScratch(int count)
    {
        if (_captureScratch.Length > MaximumRetainedCaptureInstances && count <= MaximumRetainedCaptureInstances)
            _captureScratch = [];
        if (_captureScratch.Length < count)
        {
            int capacity = count > MaximumRetainedCaptureInstances ? count :
                (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Max(16, count));
            _captureScratch = new RenderInstance[capacity];
        }
        else Array.Clear(_captureScratch);
        return _captureScratch;
    }

    private UnitFeaturePresentationMetadata ResolveUnitPresentationState(
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

    private float ResolveAimYaw(
        EntityRegistry entities,
        EntityId entity)
    {
        if (!entities.TryGetComponent(
                entity,
                out WeaponState weapon) ||
            !weapon.Target.IsValid ||
            !IsVisibleToViewer(entities, weapon.Target) ||
            !entities.IsAlive(
                weapon.Target) ||
            !entities.TryGetComponent(
                entity,
                out WorldTransform source) ||
            !entities.TryGetComponent(
                weapon.Target,
                out WorldTransform target))
        {
            return TryReadArtilleryAim(entities, entity, out Vector3 missionDirection, out _)
                ? MathF.Atan2(missionDirection.X, missionDirection.Z) : 0.0f;
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

    private float ResolveAimElevation(EntityRegistry entities, EntityId entity)
    {
        if (!entities.TryGetComponent(entity, out WeaponState weapon) || !weapon.Target.IsValid ||
            !entities.IsAlive(weapon.Target) || !IsVisibleToViewer(entities, weapon.Target) ||
            !entities.TryGetComponent(entity, out WorldTransform source) ||
            !entities.TryGetComponent(weapon.Target, out WorldTransform target))
        {
            if (!TryReadArtilleryAim(entities, entity, out Vector3 missionDirection, out float launchRise)) return 0;
            return Math.Clamp(MathF.Atan2(missionDirection.Y + launchRise,
                MathF.Sqrt(missionDirection.X * missionDirection.X + missionDirection.Z * missionDirection.Z)), -0.15f, 0.55f);
        }
        Vector3 direction = Vector3.Transform(target.Position - source.Position, Quaternion.Inverse(source.Rotation));
        return Math.Clamp(MathF.Atan2(direction.Y, MathF.Sqrt(direction.X * direction.X + direction.Z * direction.Z)), -0.15f, 0.55f);
    }
    private bool TryReadArtilleryAim(EntityRegistry entities, EntityId entity, out Vector3 direction, out float launchRise)
    {
        direction = Vector3.Zero;
        launchRise = 0;
        if (!entities.TryGetComponent(entity, out UnitIdentity unit) || unit.UnitId != UnitIds.MobileArtillery ||
            (_viewingFaction.IsSpecified && (!entities.TryGetComponent(entity, out IntelligenceSignature signature) ||
                signature.Faction != _viewingFaction)) ||
            !entities.TryGetComponent(entity, out FireMissionState mission) ||
            mission.Status is not (FireMissionStatus.Acquiring or FireMissionStatus.Firing or FireMissionStatus.WaitingReload) ||
            !entities.TryGetComponent(entity, out ArtilleryCapability capability) ||
            !entities.TryGetComponent(entity, out WorldTransform source)) return false;
        var weapons = _extraction?.Scenario.Services.ArtilleryWeapons ?? DefaultArtilleryWeapons;
        if (!weapons.TryGet(capability.WeaponId, out ArtilleryWeaponDefinition? definition) || definition is null) return false;
        direction = Vector3.Transform(mission.TargetPosition - source.Position, Quaternion.Inverse(source.Rotation));
        if (!float.IsFinite(direction.X) || !float.IsFinite(direction.Y) || !float.IsFinite(direction.Z) ||
            direction.X * direction.X + direction.Z * direction.Z < .0001f) return false;
        // Tangent of the existing parabolic trajectory; retain bounded cosmetic elevation.
        launchRise = 4 * definition.ApexHeightMeters;
        return true;
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

        if ((entities.HasComponent<RadarSensorState>(entity) &&
             !RadarOperation.IsOperational(entities, entity)) ||
            (entities.TryGetComponent(
                entity,
                out PowerConsumer consumer) &&
            (!consumer.Enabled ||
             consumer.State !=
             PowerOperationalState.Powered)))
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
        MatchRuntime scenario =
            extraction.Scenario;

        var experience = PlayerExperienceSnapshotFactory.Capture(
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
        return experience with { Selection = PlayerTacticalProtection.Attach(context.Entities,
            extraction.Player, experience.Selection, scenario.Battlefield.TacticalAreas) };
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

        if (_extraction.Scenario.Services.BuildingDefinitions.TryGet(preview.BuildingId, out var building))
        {
            var scenario = _extraction.Scenario;
            var deposit = context.Entities.TryGetComponent(preview.ResourceDeposit, out ResourceDeposit resource)
                ? resource.ResourceId : default;
            preview = preview with
            {
                ConveyorInputMaterials = preview.ConveyorInput.HasValue
                    ? ConveyorPortInformationCapture.Materials(scenario, scenario.Services.ConveyorFlow.PreviewAllowedResources(building, true, deposit)) : string.Empty,
                ConveyorOutputMaterials = preview.ConveyorOutput.HasValue
                    ? ConveyorPortInformationCapture.Materials(scenario, scenario.Services.ConveyorFlow.PreviewAllowedResources(building, false, deposit)) : string.Empty
            };
        }
        return new BuildingPlacementPreviewReadModel(
            request.RequestId,
            context.Tick,
            preview);
    }

    private BuildingConstructionDebugSnapshot? CaptureConstruction(
        SimulationContext context,
        bool debugEnabled)
    {
        MatchRuntime scenario =
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
        MatchRuntime scenario =
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

            if (combatEvent.Type == CombatEventType.DamageApplied) _infantryHits.Add(combatEvent.Target);
            if (combatEvent.Type == CombatEventType.EntityDestroyed) _infantryDeaths.Add(combatEvent.Target);
            CaptureWorldAudioCombat(context.Entities, combatEvent);

            switch (combatEvent.Type)
            {
                case CombatEventType.ShotFired:
                    _mechanicalShots.Add(combatEvent.Source);
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

    private InfantryActionInput ResolveInfantryAction(EntityRegistry entities, EntityId entity, UnitFeaturePresentationMetadata feature,
        SimulationTick tick, double tickSeconds)
    {
        if (feature.Unit != UnitIds.RifleSquad && feature.Unit != UnitIds.CombatEngineer) return default;
        bool hasWeapon = entities.TryGetComponent(entity, out WeaponState weapon);
        bool reload = hasWeapon && weapon.ReloadUntilTick > tick;
        return new(true, feature.IsWreck, feature.IsWreck && entities.TryGetComponent(entity, out UnitWreckPresentationIdentity wreck) && _infantryDeaths.Contains(wreck.SourceEntity),
            hasWeapon && weapon.Target.IsValid, reload, reload ? (weapon.ReloadUntilTick.Value - tick.Value) * tickSeconds : 0,
            _infantryHits.Contains(entity), entities.TryGetComponent(entity, out SuppressionState suppression) && suppression.Level != SuppressionLevel.Normal);
    }

    private void CaptureWorldAudioEntity(EntityRegistry entities, EntityId entity, Vector3 position,
        in SelectablePresentationMetadata selectable, in UnitFeaturePresentationMetadata unit,
        in BuildingFeaturePresentationMetadata building)
    {
        if (_extraction is null || _worldAudio is null || selectable.Owner != _extraction.Player || !unit.IsSpecified && !building.IsSpecified) return;
        bool working = entities.TryGetComponent(entity, out ResourceExtractor extractor) && extractor.State == ResourceExtractorState.Extracting ||
            entities.TryGetComponent(entity, out ProductionFacility processing) && processing.Status == ProductionStatus.Running ||
            entities.TryGetComponent(entity, out UnitProductionFacility factory) && factory.Status == UnitProductionStatus.Running;
        _worldAudio.ObserveEntity(new(entity, position, unit, building, true, working,
            entities.TryGetComponent(entity, out CargoTransportRuntimeState cargo) ? cargo.Lifecycle : default,
            entities.TryGetComponent(entity, out UnitSupplyState supply) ? supply : null,
            entities.TryGetComponent(entity, out HealthState health) ? health.Fraction : null,
            entities.TryGetComponent(entity, out GroundMovementState movement) ?
                float.IsFinite(movement.Velocity.LengthSquared()) && movement.Velocity.LengthSquared() > .0025f : null));
    }

    bool IWorldAudioVisibility.Allows(EntityId entity, Vector3 position) =>
        _extraction is not null && _decalEntities is not null &&
        (IsOwnedAudioEntity(_decalEntities, entity) || IsPositionVisible(position));

    private bool IsOwnedAudioEntity(EntityRegistry entities, EntityId entity) => _extraction is not null &&
        entity.IsValid && entities.TryGetComponent(entity, out ControllableEntity control) && control.Owner == _extraction.Player;

    private void CaptureWorldAudioCombat(EntityRegistry entities, in CombatEvent item)
    {
        if (_extraction is null) return;
        if (item.Type == CombatEventType.ShotFired)
        {
            bool knownSource = entities.TryGetComponent(item.Source, out WorldTransform source) &&
                (IsOwnedAudioEntity(entities, item.Source) || IsVisibleToViewer(entities, item.Source) && IsPositionVisible(source.Position));
            if (!knownSource && !IsPositionVisible(item.Position)) return;
            _worldAudio?.ObserveCombat(VfxPresentationCatalog.ResolveCombatEvent(item), knownSource ? item.Source : default,
                knownSource ? source.Position : item.Position, item.Tick);
        }
        else if (item.Type == CombatEventType.Impact)
        {
            if (!IsPositionVisible(item.Position)) return;
            bool knownTarget = IsVisibleToViewer(entities, item.Target) &&
                entities.TryGetComponent(item.Target, out WorldTransform target) &&
                (IsOwnedAudioEntity(entities, item.Target) || IsPositionVisible(target.Position));
            TargetClass? targetClass = knownTarget && entities.TryGetComponent(item.Target, out Targetable targetable) ? targetable.Class : null;
            var kind = VfxPresentationCatalog.ResolveCombatEvent(item, targetClass);
            _worldAudio?.ObserveCombat(kind, knownTarget ? item.Target : default, item.Position, item.Tick);
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
            IsVisibleToViewer(entities, combatEvent.Source) && entities.TryGetComponent(
                combatEvent.Source,
                out WorldTransform source)
                ? source
                : new WorldTransform(
                    combatEvent.Position,
                    Quaternion.Identity,
                    Vector3.One);
        Vector3 muzzlePosition =
            IsVisibleToViewer(entities, combatEvent.Source) ? ResolveSocketWorldPosition(
                entities,
                combatEvent.Source,
                "weapon_muzzle",
                sourceTransform.Position) : combatEvent.Position;

        VfxPresentationDefinition muzzleDefinition =
            VfxPresentationCatalog.Get(
                muzzle);

        _vfxPool.TrySpawn(
            muzzle,
            new RenderTransform(
                muzzlePosition,
                IsVisibleToViewer(entities, combatEvent.Source)
                    ? sourceTransform.Rotation * Quaternion.CreateFromYawPitchRoll(ResolveAimYaw(entities, combatEvent.Source),
                        -ResolveAimElevation(entities, combatEvent.Source), 0)
                    : Quaternion.Identity,
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
                    combatEvent.Weapon || candidate.Tick != combatEvent.Tick ||
                !IsCombatEffectVisible(entities, candidate) || !IsPositionVisible(candidate.Position))
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
            IsVisibleToViewer(entities, combatEvent.Target) && entities.TryGetComponent(
                combatEvent.Target,
                out Targetable targetable)
                ? targetable.Class
                : null;

        VfxEffectKind impact =
            VfxPresentationCatalog.ResolveCombatEvent(
                combatEvent,
                targetClass);

        QueueImpactDecal(entities, combatEvent, targetClass);
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

            if (IsPositionVisible(transform.Position))
                _worldAudio?.ObserveCombat(VfxEffectKind.ExplosionVehicle, entity, transform.Position, context.Tick);

            SpawnAt(
                VfxEffectKind.ExplosionVehicle,
                transform.Position,
                context.Tick);
            SpawnAt(
                VfxEffectKind.DestructionVehicleBurst,
                transform.Position,
                context.Tick);
            SpawnAt(
                VfxEffectKind.DestructionDebris,
                transform.Position,
                context.Tick);
            SpawnAt(
                VfxEffectKind.DestructionSparkEmission,
                transform.Position,
                context.Tick);
            SpawnAt(
                VfxEffectKind.DestructionDustCloud,
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

            if (IsPositionVisible(transform.Position))
                _worldAudio?.ObserveCombat(VfxEffectKind.ExplosionBuilding, entity, transform.Position, context.Tick);

            SpawnAt(
                VfxEffectKind.ExplosionBuilding,
                transform.Position,
                context.Tick);
            SpawnAt(
                VfxEffectKind.DestructionBuildingBurst,
                transform.Position,
                context.Tick);
            SpawnAt(
                VfxEffectKind.DestructionDebris,
                transform.Position,
                context.Tick);
            SpawnAt(
                VfxEffectKind.DestructionDustCloud,
                transform.Position,
                context.Tick);
            SpawnAt(
                VfxEffectKind.DestructionSparkEmission,
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

            QueueVfx(
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

            QueueVfx(
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

            if (!effect.Active || !IsPositionVisible(effect.Transform.Position) ||
                effect.VisibilityStart is { } start && !IsPositionVisible(start) ||
                effect.VisibilityEnd is { } end && !IsPositionVisible(end))
            {
                continue;
            }

            QueueVfx(
                new QueuedVfxInstance(
                    effect.Kind,
                    effect.Transform, 0x8000_0000_0000_0000UL | (uint)(index + 1), effect.Generation, false, effect.SpawnedAtTick.Value));
        }
    }

    private void QueuePersistentStateVfx(
        EntityId entity,
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
                        anchor, entity);
                    break;

                case UnitPresentationDamageState.Critical:
                    QueueAt(
                        VfxEffectKind.PersistentHeavySmoke,
                        anchor, entity);
                    QueueAt(
                        VfxEffectKind.PersistentFire,
                        anchor, entity);
                    QueueAt(
                        VfxEffectKind.PersistentSparks,
                        anchor, entity);
                    break;

                case UnitPresentationDamageState.Wreck:
                    QueueAt(
                        VfxEffectKind.PersistentHeavySmoke,
                        anchor, entity);
                    QueueAt(
                        VfxEffectKind.PersistentFire,
                        anchor, entity);
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
                        anchor, entity);
                    break;

                case BuildingPresentationState.Critical:
                    QueueAt(
                        VfxEffectKind.PersistentHeavySmoke,
                        anchor, entity);
                    QueueAt(
                        VfxEffectKind.PersistentFire,
                        anchor, entity);
                    QueueAt(
                        VfxEffectKind.PersistentSparks,
                        anchor, entity);
                    break;

                case BuildingPresentationState.Destroyed:
                    QueueAt(
                        VfxEffectKind.DestructionSmokePlume,
                        anchor, entity);
                    QueueAt(
                        VfxEffectKind.DestructionPersistentFire,
                        anchor, entity);
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

    bool IDecalVisibility.IsGroundVisible(Vector3 position) => IsPositionVisible(position);

    bool IDecalVisibility.TryGetReceiver(EntityId receiver, out RenderTransform transform)
    {
        transform = default;
        if (_decalEntities is null || !_decalEntities.IsAlive(receiver) || !IsVisibleToViewer(_decalEntities, receiver) ||
            !_decalEntities.TryGetComponent(receiver, out VisualIdentity visual) || ((uint)visual.Visibility & (uint)RenderVisibilityMask.World) == 0 ||
            !_decalEntities.TryGetComponent(receiver, out WorldTransform pose) ||
            _decalEntities.TryGetComponent(receiver, out HealthState health) && health.Fraction <= 0) return false;
        transform = new(pose.Position, pose.Rotation, pose.Scale);
        return true;
    }

    internal void QueueImpactDecal(EntityRegistry entities, in CombatEvent impact, TargetClass? targetClass)
    {
        if (!IsPositionVisible(impact.Position)) return;
        if (impact.Weapon == DirectorateContent.WeaponIds.MobileArtillery || impact.Weapon == DirectorateContent.WeaponIds.MainBattleCannon)
        {
            Vector3 position = impact.Position;
            if (_extraction is not null && _extraction.Scenario.Terrain.TrySampleHeight(position.X, position.Z, out float height)) position.Y = height;
            Quaternion groundRotation = Quaternion.Identity;
            if (_extraction is not null && _extraction.Scenario.Terrain.TrySampleNormal(position.X, position.Z, out Vector3 groundNormal))
            {
                Vector3 axis = Vector3.Cross(Vector3.UnitY, groundNormal);
                if (axis.LengthSquared() > .000001f) groundRotation = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(Math.Clamp(groundNormal.Y, -1, 1)));
            }
            position.Y += .06f;
            bool artillery = impact.Weapon == DirectorateContent.WeaponIds.MobileArtillery;
            _decals.TrySpawn(new(artillery ? DecalKind.Crater : DecalKind.Scorch, DecalSurface.Ground,
                new(position, groundRotation, new(artillery ? 8 : 4, 1, artillery ? 8 : 4))), impact.Tick, out _);
            return;
        }
        if (targetClass is not (TargetClass.ArmoredVehicle or TargetClass.Structure) || !IsVisibleToViewer(entities, impact.Target) ||
            !entities.TryGetComponent(impact.Target, out WorldTransform receiver)) return;
        Vector3 incoming = Vector3.UnitZ;
        if (IsVisibleToViewer(entities, impact.Source) && entities.TryGetComponent(impact.Source, out WorldTransform source))
            incoming = Vector3.Transform(source.Position - receiver.Position, Quaternion.Inverse(receiver.Rotation));
        Vector3 normal = MathF.Abs(incoming.X) > MathF.Abs(incoming.Z) ? Vector3.UnitX * (incoming.X >= 0 ? 1 : -1)
            : Vector3.UnitZ * (incoming.Z >= 0 ? 1 : -1);
        Vector3 anchor = normal * .51f;
        Quaternion rotation = normal.X != 0 ? Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -normal.X * MathF.PI / 2)
            : Quaternion.CreateFromAxisAngle(Vector3.UnitX, normal.Z * MathF.PI / 2);
        Vector3 world = receiver.Position + Vector3.Transform(anchor * receiver.Scale, receiver.Rotation);
        _decals.TrySpawn(new(DecalKind.Hit, DecalSurface.Receiver, new(world, rotation, new(1.2f, 1, 1.2f)),
            LifetimeTicks: 400, Strength: .35f, Receiver: impact.Target, ReceiverLocalAnchor: anchor), impact.Tick, out _);
    }

    private void QueueVfx(in QueuedVfxInstance effect)
    {
        if (_queuedVfx.Count < MaximumSnapshotEffects) _queuedVfx.Add(effect);
        else _droppedQueuedVfx++;
    }

    private void QueueIndustrialVfx(EntityRegistry entities, EntityId entity, in WorldTransform transform,
        in BuildingFeaturePresentationMetadata building)
    {
        bool working = entities.TryGetComponent(entity, out ProductionFacility production) && production.Status == ProductionStatus.Running ||
            entities.TryGetComponent(entity, out UnitProductionFacility unitProduction) && unitProduction.Status == UnitProductionStatus.Running;
        if (!IndustrialEffectPreset.TryResolve(building, working, out var preset)) return;
        Vector3 position = preset.ResolveAnchor(building, new RenderTransform(transform.Position, transform.Rotation, transform.Scale), _runtimeAssets);
        QueueAt(preset.Effect, position, entity);
    }

    private void QueueAt(
        VfxEffectKind kind,
        Vector3 position,
        EntityId owner = default)
    {
        VfxPresentationDefinition definition =
            VfxPresentationCatalog.Get(
                kind);

        QueueVfx(
            new QueuedVfxInstance(
                kind,
                new RenderTransform(
                    position,
                    Quaternion.Identity,
                    definition.BaseScale), ((ulong)owner.Generation << 32) | owner.Index, owner.Generation, owner.IsValid));
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
            tick, start, end);
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
                    socket.Z);
            if (socketName == "weapon_muzzle" && definition.HasArticulatedTurret)
            {
                Vector3 gunPivot = definition.TurretPivot;
                foreach (var anchor in record.Sockets)
                    if (anchor.Name == "gun_pivot") gunPivot = new(anchor.X, anchor.Y, anchor.Z);
                local = MechanicalMotionSpace.RotateOffset(local - gunPivot, transform.Scale,
                    Quaternion.CreateFromAxisAngle(Vector3.UnitX, -ResolveAimElevation(entities, entity))) + gunPivot;
                local = MechanicalMotionSpace.RotateOffset(local - definition.TurretPivot, transform.Scale,
                    Quaternion.CreateFromAxisAngle(Vector3.UnitY, ResolveAimYaw(entities, entity))) + definition.TurretPivot;
            }
            if (socketName == "weapon_muzzle" && record.SkinReference is { } skinId)
            {
                if (!_infantrySockets.TryGetValue(skinId, out var binding))
                {
                    if (_infantrySockets.Count >= 8) return fallback;
                    var skinRecord = _runtimeAssets.Get(AssetId.Parse(skinId));
                    var skin = RuntimeSkinData.FromPayload(_runtimeAssets.Read(AssetId.Parse(skinId)).Payload);
                    RuntimeAnimationData? fire = null;
                    foreach (string clipId in skinRecord.AnimationReferences)
                    {
                        var clip = RuntimeAnimationData.FromPayload(_runtimeAssets.Read(AssetId.Parse(clipId)).Payload);
                        if (clip.Name == "fire") { fire = clip; break; }
                    }
                    if (fire is null) throw new InvalidDataException("Infantry skin has no fire clip.");
                    binding = (skin, fire, skin.Bind(fire)); _infantrySockets.Add(skinId, binding);
                }
                // The shot event starts fire at phase zero in the same completed tick.
                Span<Matrix4x4> palette = stackalloc Matrix4x4[RuntimeSkinData.MaximumJoints];
                binding.Skin.Evaluate(binding.Clip, binding.Nodes, 0, palette);
                local = binding.Skin.SocketPosition(socketName, palette);
            }
            local *= transform.Scale;

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
        bool positionVisible =
            IsPositionVisible(
                combatEvent.Position);

        if (combatEvent.Type is
            CombatEventType.ShotFired or
            CombatEventType.ProjectileSpawned)
        {
            return
                positionVisible ||
                (combatEvent.Source.IsValid &&
                 entities.IsAlive(
                     combatEvent.Source) &&
                 IsVisibleToViewer(
                     entities,
                     combatEvent.Source));
        }

        if (combatEvent.Type is
            CombatEventType.Impact or
            CombatEventType.DamageApplied or
            CombatEventType.EntityDestroyed)
        {
            return
                positionVisible ||
                (combatEvent.Target.IsValid &&
                 entities.IsAlive(
                     combatEvent.Target) &&
                 IsVisibleToViewer(
                     entities,
                     combatEvent.Target));
        }

        return positionVisible;
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
        RenderTransform Transform,
        ulong EmitterId = 0,
        uint Generation = 0,
        bool Continuous = false,
        ulong SpawnedAtTick = 0);

    private static void ApplyDebugCaptureState(
        MatchRuntime scenario,
        DebugOverlayCategory categories)
    {
        bool navigation =
            (categories &
             DebugOverlayCategory.Navigation) !=
            0;
        bool logistics =
            (categories &
             DebugOverlayCategory.Logistics) !=
            0;
        bool sensors =
            (categories &
             DebugOverlayCategory.Sensors) !=
            0;
        bool combat =
            (categories &
             DebugOverlayCategory.Combat) !=
            0;
        bool entities =
            (categories &
             DebugOverlayCategory.Entities) !=
            0;

        scenario.Services.GroundMovement.DebugCaptureEnabled =
            navigation;
        scenario.Services.FormationMovement.DebugCaptureEnabled =
            navigation;
        scenario.BattlefieldSupply.DebugCaptureEnabled =
            logistics;
        scenario.Services.BattlefieldIntelligence.TimingEnabled =
            sensors;
        scenario.Services.BattlefieldIntelligence.DebugCaptureEnabled =
            sensors;
        scenario.Artillery.DebugCaptureEnabled =
            combat;
        scenario.Services.TargetAcquisition.DebugCaptureEnabled =
            combat;
        scenario.Services.TacticalCombat.DebugCaptureEnabled =
            combat;
        scenario.Readiness.DebugCaptureEnabled =
            combat;
        scenario.Services.CombatDebugSnapshots.DebugCaptureEnabled =
            combat;
        scenario.Opponents.DebugCaptureEnabled =
            entities;
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
        BattlefieldRuntime runtime)
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
