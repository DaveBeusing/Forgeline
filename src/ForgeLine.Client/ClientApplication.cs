using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Input;
using ForgeLine.Jobs;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Platform;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Client;

internal sealed class ClientApplication
{
    private const float MaximumCameraDeltaSeconds = 0.1f;
    private const int MaximumDebugInstanceBoxes = 24;
    private const int MaximumDebugLabels = 4;

    private static readonly PlayerId LocalPlayer = new(1);
    private static readonly PlayerId OpposingPlayer = new(2);
    private static readonly TimeSpan IdleWait = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan SmokeTestDuration = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan DiagnosticInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumSimulationCatchUp = TimeSpan.FromMilliseconds(250);

    private readonly IPlatform _platform;

    internal ClientApplication(IPlatform platform)
    {
        _platform = platform;
    }

    internal int Run(bool smokeTest, int renderInstanceCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(renderInstanceCount);

        var configuration = new WindowConfiguration(
            "FORGELINE",
            1600,
            900,
            resizable: true,
            WindowMode.Windowed);

        using IWindow window = _platform.CreateWindow(configuration);
        using IGraphicsDevice graphics = GraphicsDeviceFactory.CreateForWindow(window);

        TerrainWorld terrainWorld = DevelopmentTerrainFactory.CreateRepresentativeWorld();
        using var terrainRenderer = new TerrainRenderer(graphics, terrainWorld);
        using var instanceRenderer = new SimpleInstanceRenderer(graphics);
        using var debugDrawRenderer = new DebugDrawRenderer(graphics);
        using var overlayRenderer = new DevelopmentOverlayRenderer(graphics);

        var snapshotBuffer = new PresentationSnapshotBuffer();
        using var jobScheduler = new JobScheduler();
        var simulation = new SimulationCoordinator(
            jobScheduler: jobScheduler,
            diagnosticsOptions: new SimulationDiagnosticsOptions { Enabled = true });
        var spatialIndex = new SpatialGridIndex(
            new SpatialGridSettings
            {
                World = terrainWorld.Settings,
                CellSizeMeters = SpatialGridSettings.DefaultCellSizeMeters,
                EnableQueryTiming = true
            });
        var spatialSynchronizer = new SpatialIndexSynchronizer(spatialIndex);
        var groundMovementSystem = new GroundMovementSystem(
            terrainWorld,
            spatialIndex);
        BuildingDefinitionCatalog buildingDefinitions =
            InitialBuildingDefinitions.CreateCatalog();
        ResourceCatalog resourceCatalog =
            InitialResourceDefinitions.CreateCatalog();
        ProductionRecipeCatalog productionRecipes =
            InitialProductionRecipes.CreateCatalog();
        var inventories = new InventoryStore();
        var buildingPlacement = new BuildingPlacementService(
            buildingDefinitions,
            terrainWorld,
            spatialIndex);
        var buildingCommands = new BuildingCommandProcessingSystem(
            buildingDefinitions,
            buildingPlacement,
            inventories,
            spatialIndex);
        var buildingConstruction = new BuildingConstructionSystem(
            buildingDefinitions,
            inventories,
            spatialIndex);
        var powerNetworks = new PowerNetworkSystem();
        var production = new ProductionSystem(
            productionRecipes,
            inventories);
        var resourceExtraction = new ResourceExtractionSystem(
            inventories: inventories);
        var logisticsNetwork = new LogisticsNetwork();
        var cargoTransportSystem =
            new CargoTransportSystem(
                logisticsNetwork,
                inventories);
        var automatedDistribution =
            new AutomatedDistributionSystem(
                logisticsNetwork,
                inventories,
                cargoTransportSystem);
        var battlefieldSupply =
            new BattlefieldSupplySystem(
                inventories);
        var logisticsRegistration =
            new BuildingLogisticsRegistrationSystem(
                logisticsNetwork);

        PopulateSimulationEntities(
            simulation,
            terrainWorld,
            renderInstanceCount);
        EntityId constructionInventory =
            CreateDevelopmentConstructionInventory(
                simulation,
                inventories);
        CreateDevelopmentResourceDeposit(
            simulation,
            terrainWorld);

        NavigationWorld navigationWorld = NavigationWorld.Build(
            terrainWorld,
            CollectStaticNavigationObstacles(simulation),
            new NavigationGridSettings
            {
                CellSizeMeters = NavigationGridSettings.DefaultCellSizeMeters,
                StaticObstacleClearanceMeters = 0.5f
            },
            new NavigationSectorSettings
            {
                SectorSizeCells = 8
            });
        var pathfinder =
            new HierarchicalPathfinder(navigationWorld);
        var formationMovementSystem =
            new FormationMovementSystem(pathfinder);
        var navigationSystem =
            new HierarchicalNavigationSystem(pathfinder);

        simulation.RegisterSystem(buildingCommands);
        simulation.RegisterSystem(formationMovementSystem);
        simulation.RegisterSystem(navigationSystem);
        simulation.RegisterSystem(groundMovementSystem);
        simulation.RegisterSystem(new SpatialIndexSystem(spatialSynchronizer));
        simulation.RegisterSystem(powerNetworks);
        simulation.RegisterSystem(production);
        simulation.RegisterSystem(buildingConstruction);
        simulation.RegisterSystem(resourceExtraction);
        simulation.RegisterSystem(battlefieldSupply);
        simulation.RegisterSystem(automatedDistribution);
        simulation.RegisterSystem(cargoTransportSystem);
        simulation.RegisterSystem(logisticsRegistration);
        simulation.RegisterSystem(new SpatialIndexCleanupSystem(spatialSynchronizer));
        simulation.RegisterTickObserver(new PresentationExtractor(snapshotBuffer));
        simulation.AdvanceOneTick();

        var renderWorld = new RenderWorld();
        _ = renderWorld.Update(snapshotBuffer);

        float targetHeight = terrainWorld.TrySampleHeight(
            0.0f,
            0.0f,
            out float sampledHeight)
            ? sampledHeight
            : 0.0f;

        var inputState = new InputState();
        var actionMapper = new RtsCameraActionMapper();
        var camera = new RtsCamera(
            new RtsCameraSettings
            {
                InitialTarget = new Vector3(0.0f, targetHeight, 0.0f),
                InitialDistance = 420.0f,
                MinimumDistance = 20.0f,
                MaximumDistance = 1_200.0f,
                PanReferenceDistance = 180.0f,
                MaximumPanSpeedScale = 5.0f
            });
        var selectionController = new RtsSelectionController(
            new SelectionFilter(
                LocalPlayer,
                ControllableEntityCategory.Unit |
                ControllableEntityCategory.Logistics));
        var buildingPlacementController =
            new RtsBuildingPlacementController(LocalPlayer);
        var debugDraw = new DebugDraw();
        var frameTimingTracker = new FrameTimingTracker();

        MoveEntitiesCommand? lastMovementCommand = null;
        SimulationCommandEnvelope? lastMovementEnvelope = null;
        BuildCommand? lastBuildCommand = null;
        SimulationCommandEnvelope? lastBuildEnvelope = null;
        bool overlayEnabled = true;
        bool worldDebugEnabled = false;
        bool overlayToggleHeld = false;
        bool worldDebugToggleHeld = false;
        bool formationToggleHeld = false;
        FormationTemplate activeFormation =
            FormationTemplate.Compact;
        TimeSpan simulationAccumulator = TimeSpan.Zero;
        FrameTimingMetrics frameTiming = default;
        SimulationDiagnosticsSnapshot simulationDiagnostics =
            simulation.Diagnostics.Capture(simulation);

        WriteWindowState("started", window);
        WriteGraphicsState("started", graphics);
        WriteWorldState("started", terrainWorld);
        WriteCameraState("started", camera);
        WritePresentationState(
            "started",
            simulation,
            renderWorld,
            terrainRenderer,
            instanceRenderer);
        WriteInteractionState(
            "started",
            selectionController,
            lastMovementEnvelope,
            lastMovementCommand,
            activeFormation,
            buildingPlacementController,
            lastBuildEnvelope,
            lastBuildCommand,
            buildingCommands);

        long startedAt = _platform.Clock.GetTimestamp();
        long previousFrameAt = startedAt;
        long nextDiagnosticAt = startedAt;

        while (window.IsOpen)
        {
            inputState.BeginFrame();

            if (!_platform.PumpEvents())
            {
                break;
            }

            DrainWindowEvents(window, graphics);
            DrainInputEvents(window, inputState);

            long now = _platform.Clock.GetTimestamp();
            TimeSpan frameElapsed = _platform.Clock.GetElapsedTime(previousFrameAt, now);
            previousFrameAt = now;

            float cameraDeltaSeconds = (float)Math.Min(
                frameElapsed.TotalSeconds,
                MaximumCameraDeltaSeconds);

            UpdateToggle(
                inputState,
                PlatformKey.F1,
                ref overlayToggleHeld,
                ref overlayEnabled);
            UpdateToggle(
                inputState,
                PlatformKey.F2,
                ref worldDebugToggleHeld,
                ref worldDebugEnabled);
            UpdateFormationSelection(
                inputState,
                ref formationToggleHeld,
                ref activeFormation);
            groundMovementSystem.DebugCaptureEnabled =
                worldDebugEnabled;
            formationMovementSystem.DebugCaptureEnabled =
                worldDebugEnabled;

            if (smokeTest &&
                _platform.Clock.GetElapsedTime(startedAt, now) >= SmokeTestDuration)
            {
                window.RequestClose();
            }

            if (!window.IsOpen)
            {
                continue;
            }

            if (window.IsMinimized || window.ClientSize.IsEmpty)
            {
                _platform.WaitForEvents(IdleWait);
                continue;
            }

            RtsCameraInputFrame cameraInput = actionMapper.Map(inputState);
            camera.Update(
                cameraInput,
                cameraDeltaSeconds,
                window.ClientSize.Width,
                window.ClientSize.Height);

            TimeSpan catchUp = frameElapsed <= MaximumSimulationCatchUp
                ? frameElapsed
                : MaximumSimulationCatchUp;
            simulationAccumulator += catchUp;

            while (simulationAccumulator >= simulation.Clock.TickDuration)
            {
                simulation.AdvanceOneTick();
                simulationAccumulator -= simulation.Clock.TickDuration;
            }

            _ = renderWorld.Update(snapshotBuffer);

            float renderAlpha = RenderInterpolation.CalculateAlpha(
                simulationAccumulator,
                simulation.Clock.TickDuration);

            buildingPlacementController.Update(
                inputState,
                camera,
                terrainWorld,
                simulation.Entities,
                buildingPlacement,
                window.ClientSize.Width,
                window.ClientSize.Height);

            if (!buildingPlacementController.IsActive)
            {
                selectionController.Update(
                    inputState,
                    camera,
                    renderWorld,
                    terrainWorld,
                    window.ClientSize.Width,
                    window.ClientSize.Height,
                    renderAlpha);

                if (selectionController.TryTakeMovementRequest(
                        out MovementOrderRequest movementRequest))
                {
                    var targetTick = new SimulationTick(
                        checked(simulation.CurrentTick.Value + 1));
                    var command = new MoveEntitiesCommand(
                        LocalPlayer,
                        movementRequest.Entities,
                        movementRequest.WorldTarget,
                        simulation.CurrentTick,
                        activeFormation);

                    lastMovementEnvelope = simulation.SubmitCommand(
                        command,
                        targetTick,
                        new SimulationCommandSource(LocalPlayer.Value));
                    lastMovementCommand = command;
                }
            }

            if (buildingPlacementController.TryTakePlacementRequest(
                    out BuildingPlacementRequest placementRequest))
            {
                var targetTick = new SimulationTick(
                    checked(simulation.CurrentTick.Value + 1));
                var command = new BuildCommand(
                    LocalPlayer,
                    placementRequest.BuildingId,
                    placementRequest.Position,
                    placementRequest.Orientation,
                    constructionInventory,
                    simulation.CurrentTick);

                lastBuildEnvelope = simulation.SubmitCommand(
                    command,
                    targetTick,
                    new SimulationCommandSource(LocalPlayer.Value));
                lastBuildCommand = command;
            }

            BuildingConstructionDebugSnapshot? constructionDebugSnapshot =
                worldDebugEnabled ||
                buildingConstruction.Metrics.ActiveSites > 0
                    ? BuildingConstructionDebugSnapshot.Capture(
                        simulation.Entities,
                        buildingDefinitions)
                    : null;
            ResourceExtractionDebugSnapshot? resourceDebugSnapshot =
                worldDebugEnabled
                    ? ResourceExtractionDebugSnapshot.Capture(
                        simulation.Entities,
                        resourceExtraction.Metrics,
                        resourceCatalog)
                    : null;
            LogisticsNetworkDebugSnapshot? logisticsDebugSnapshot =
                worldDebugEnabled
                    ? LogisticsNetworkDebugSnapshot.Capture(
                        logisticsNetwork)
                    : null;
            CargoTransportDebugSnapshot? cargoTransportDebugSnapshot =
                worldDebugEnabled
                    ? cargoTransportSystem.LastDebugSnapshot
                    : null;
            AutomatedDistributionDebugSnapshot? distributionDebugSnapshot =
                worldDebugEnabled
                    ? automatedDistribution.LastDebugSnapshot
                    : null;
            BattlefieldSupplyDebugSnapshot? battlefieldSupplyDebugSnapshot =
                worldDebugEnabled
                    ? battlefieldSupply.LastDebugSnapshot
                    : null;

            BuildWorldDebugVisualization(
                debugDraw,
                worldDebugEnabled,
                renderWorld,
                renderAlpha,
                camera,
                selectionController,
                spatialIndex,
                groundMovementSystem.CaptureDebugSnapshot(),
                formationMovementSystem.CaptureDebugSnapshot(),
                navigationSystem.World,
                navigationSystem.LastCompletedPath,
                buildingPlacementController,
                constructionDebugSnapshot,
                resourceDebugSnapshot,
                logisticsDebugSnapshot,
                cargoTransportDebugSnapshot,
                distributionDebugSnapshot,
                battlefieldSupplyDebugSnapshot);

            terrainRenderer.DebugChunksEnabled = worldDebugEnabled;

            DevelopmentOverlayMetrics overlayMetrics = CreateOverlayMetrics(
                frameTiming,
                simulation,
                simulationDiagnostics,
                terrainRenderer,
                instanceRenderer,
                debugDrawRenderer,
                renderWorld);

            long renderStartedAt = _platform.Clock.GetTimestamp();

            graphics.RenderFrame(
                GraphicsColor.ForgeLineClear,
                context =>
                {
                    terrainRenderer.Render(context, camera);
                    instanceRenderer.Render(context, camera, renderWorld, renderAlpha);
                    debugDrawRenderer.Render(context, camera, debugDraw);

                    if (overlayEnabled)
                    {
                        overlayRenderer.Render(
                            context,
                            overlayMetrics,
                            camera,
                            debugDraw);
                    }
                });

            long renderFinishedAt = _platform.Clock.GetTimestamp();

            frameTiming = frameTimingTracker.Record(
                frameElapsed,
                _platform.Clock.GetElapsedTime(renderStartedAt, renderFinishedAt));

            if (_platform.Clock.GetElapsedTime(nextDiagnosticAt, now) >= DiagnosticInterval)
            {
                simulationDiagnostics = simulation.Diagnostics.Capture(simulation);
                WriteCameraState("frame", camera);
                WriteTerrainState("frame", terrainRenderer);
                WritePresentationState(
                    "frame",
                    simulation,
                    renderWorld,
                    terrainRenderer,
                    instanceRenderer);
                WriteInteractionState(
                    "frame",
                    selectionController,
                    lastMovementEnvelope,
                    lastMovementCommand,
                    activeFormation,
                    buildingPlacementController,
                    lastBuildEnvelope,
                    lastBuildCommand,
                    buildingCommands);
                nextDiagnosticAt = now;
            }
        }

        DrainWindowEvents(window, graphics);
        DrainInputEvents(window, inputState);
        WriteCameraState("stopped", camera);
        WriteTerrainState("stopped", terrainRenderer);
        WritePresentationState(
            "stopped",
            simulation,
            renderWorld,
            terrainRenderer,
            instanceRenderer);
        WriteInteractionState(
            "stopped",
            selectionController,
            lastMovementEnvelope,
            lastMovementCommand,
            activeFormation,
            buildingPlacementController,
            lastBuildEnvelope,
            lastBuildCommand,
            buildingCommands);
        WriteGraphicsState("stopped", graphics);
        return 0;
    }

    private static EntityId CreateDevelopmentConstructionInventory(
        SimulationCoordinator simulation,
        InventoryStore inventories)
    {
        EntityId entity = simulation.Entities.CreateEntity();
        InventoryId inventoryId =
            inventories.CreateInventory(
                new InventorySpecification(100_000.0));

        simulation.Entities.AddComponent(
            entity,
            new InventoryStorage(inventoryId));
        simulation.Entities.AddComponent(
            entity,
            new StorageDepot(
                inventoryId,
                new FactionId((uint)LocalPlayer.Value)));

        foreach (ResourceId resourceId in new[]
                 {
                     ResourceIds.FerrousOre,
                     ResourceIds.Silicates,
                     ResourceIds.Volatiles
                 })
        {
            InventoryOperationResult result =
                inventories.Add(
                    inventoryId,
                    resourceId,
                    20_000.0);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Unable to seed development construction inventory with resource {resourceId}: {result.Failure}.");
            }
        }

        return entity;
    }

    private static EntityId CreateDevelopmentResourceDeposit(
        SimulationCoordinator simulation,
        TerrainWorld terrainWorld)
    {
        const float centerX = 900.0f;
        const float centerZ = 900.0f;
        const float halfSize = 14.0f;

        float height = terrainWorld.TrySampleHeight(
            centerX,
            centerZ,
            out float sampledHeight)
            ? sampledHeight
            : 0.0f;

        EntityId entity = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            entity,
            new ResourceDeposit(
                ResourceIds.FerrousOre,
                new AxisAlignedBounds(
                    new Vector3(
                        centerX - halfSize,
                        height - 1.0f,
                        centerZ - halfSize),
                    new Vector3(
                        centerX + halfSize,
                        height + 1.0f,
                        centerZ + halfSize)),
                totalQuantity: 50_000.0,
                baseExtractionRatePerSecond: 10.0));

        return entity;
    }

    private static void PopulateSimulationEntities(
        SimulationCoordinator simulation,
        TerrainWorld terrainWorld,
        int entityCount)
    {
        int side = checked((int)Math.Ceiling(Math.Sqrt(entityCount)));
        const float spacing = 12.0f;
        float halfSpan = (side - 1) * spacing * 0.5f;

        for (int index = 0; index < entityCount; index++)
        {
            int xIndex = index % side;
            int zIndex = index / side;
            float x = xIndex * spacing - halfSpan;
            float z = zIndex * spacing - halfSpan;
            float terrainHeight = terrainWorld.TrySampleHeight(
                x,
                z,
                out float sampledHeight)
                ? sampledHeight
                : 0.0f;

            PlayerId owner =
                index > 0 && index % 7 == 0
                    ? OpposingPlayer
                    : LocalPlayer;
            ControllableEntityCategory category =
                index > 0 && index % 11 == 0
                    ? ControllableEntityCategory.Building
                    : index % 5 == 0
                        ? ControllableEntityCategory.Logistics
                        : ControllableEntityCategory.Unit;

            var entity = simulation.Entities.CreateEntity();
            Vector3 scale = new(8.0f, 6.0f, 8.0f);
            simulation.Entities.AddComponent(
                entity,
                new WorldTransform(
                    new Vector3(x, terrainHeight + 3.0f, z),
                    Quaternion.Identity,
                    scale));

            if (category != ControllableEntityCategory.Building)
            {
                bool logistics =
                    category == ControllableEntityCategory.Logistics;

                simulation.Entities.AddComponent(
                    entity,
                    new GroundMovement(
                        maximumSpeed: logistics ? 10.0f : 14.0f,
                        acceleration: logistics ? 6.0f : 9.0f,
                        deceleration: logistics ? 9.0f : 12.0f,
                        turnRateRadiansPerSecond:
                            logistics ? MathF.PI * 0.6f : MathF.PI,
                        radius: scale.X * 0.5f,
                        stopRadius: 1.0f,
                        separationRadius: scale.X + 2.0f,
                        obstacleLookAhead: logistics ? 14.0f : 10.0f,
                        maximumSlopeDegrees: logistics ? 28.0f : 35.0f,
                        heightOffset: scale.Y * 0.5f));
                simulation.Entities.AddComponent(
                    entity,
                    GroundMovementState.Stationary());
                simulation.Entities.AddComponent(
                    entity,
                    new NavigationAgent(
                        logistics
                            ? NavigationMovementClass.Wheeled
                            : NavigationMovementClass.Tracked));
            }

            simulation.Entities.AddComponent(entity, new VisualIdentity(1));
            simulation.Entities.AddComponent(
                entity,
                new ControllableEntity(owner, category));
            simulation.Entities.AddComponent(
                entity,
                new SpatialPresence(
                    scale * 0.5f,
                    new SpatialEntryMetadata(
                        owner.Value,
                        (ulong)category,
                        category == ControllableEntityCategory.Building
                            ? SpatialMobility.Static
                            : SpatialMobility.Mobile)));
        }
    }

    private static AxisAlignedBounds[] CollectStaticNavigationObstacles(
        SimulationCoordinator simulation)
    {
        var obstacles = new List<AxisAlignedBounds>();

        foreach (EntityId entity in simulation.Entities.Query<
                     WorldTransform,
                     SpatialPresence>())
        {
            SpatialPresence presence =
                simulation.Entities.GetComponent<SpatialPresence>(entity);

            if (presence.Metadata.Mobility != SpatialMobility.Static)
            {
                continue;
            }

            WorldTransform transform =
                simulation.Entities.GetComponent<WorldTransform>(entity);
            obstacles.Add(
                presence.CreateEntry(entity, transform).Bounds);
        }

        return obstacles.ToArray();
    }

    private static void BuildWorldDebugVisualization(
        DebugDraw debugDraw,
        bool worldDebugEnabled,
        RenderWorld renderWorld,
        float alpha,
        RtsCamera camera,
        RtsSelectionController selectionController,
        SpatialGridIndex spatialIndex,
        GroundMovementDebugSnapshot movementSnapshot,
        FormationMovementDebugSnapshot formationSnapshot,
        NavigationWorld navigationWorld,
        NavigationPath? navigationPath,
        RtsBuildingPlacementController buildingPlacementController,
        BuildingConstructionDebugSnapshot? constructionSnapshot,
        ResourceExtractionDebugSnapshot? resourceSnapshot,
        LogisticsNetworkDebugSnapshot? logisticsSnapshot,
        CargoTransportDebugSnapshot? cargoTransportSnapshot,
        AutomatedDistributionDebugSnapshot? distributionSnapshot,
        BattlefieldSupplyDebugSnapshot? battlefieldSupplySnapshot)
    {
        debugDraw.Clear();

        bool interactionFeedback =
            selectionController.Selection.Count > 0 ||
            selectionController.HoveredEntity.IsValid ||
            buildingPlacementController.IsActive ||
            (constructionSnapshot?.Sites.Count ?? 0) > 0;
        debugDraw.Enabled =
            worldDebugEnabled ||
            interactionFeedback;

        if (!debugDraw.Enabled)
        {
            return;
        }

        Vector4 rangeColor = new(0.2f, 0.75f, 1.0f, 1.0f);
        Vector4 boundsColor = new(1.0f, 0.72f, 0.18f, 1.0f);
        Vector4 pointColor = new(1.0f, 0.25f, 0.18f, 1.0f);
        Vector4 selectedColor = new(0.25f, 1.0f, 0.35f, 1.0f);
        Vector4 hoveredColor = new(0.15f, 0.85f, 1.0f, 1.0f);
        Vector4 placementValidColor = new(0.15f, 1.0f, 0.35f, 1.0f);
        Vector4 placementInvalidColor = new(1.0f, 0.2f, 0.15f, 1.0f);
        Vector4 constructionColor = new(1.0f, 0.75f, 0.2f, 1.0f);
        Vector4 completedColor = new(0.2f, 0.9f, 0.35f, 1.0f);

        if (buildingPlacementController.Preview is BuildingPlacementPreview placementPreview)
        {
            BuildingConstructionDebugVisualization.DrawPreview(
                debugDraw,
                placementPreview,
                placementValidColor,
                placementInvalidColor);
        }

        if (constructionSnapshot is not null)
        {
            BuildingConstructionDebugVisualization.DrawConstructionSites(
                debugDraw,
                constructionSnapshot,
                constructionColor,
                completedColor,
                maximumCompleted: worldDebugEnabled ? 64 : 0,
                maximumLabels: 32);
        }

        if (worldDebugEnabled)
        {
            SpatialIndexDebugVisualization.DrawRadiusQuery(
                debugDraw,
                camera.Target,
                40.0f,
                rangeColor);
            debugDraw.Point(camera.Target, 8.0f, pointColor);

            SpatialIndexDebugSnapshot spatialSnapshot =
                spatialIndex.CaptureDebugSnapshot(camera.Target.Y + 0.1f);
            SpatialIndexDebugVisualization.DrawOccupiedCells(
                debugDraw,
                spatialSnapshot,
                new Vector4(0.35f, 0.65f, 1.0f, 0.8f),
                new Vector4(1.0f, 0.35f, 0.15f, 1.0f),
                maximumCells: 256,
                maximumLabels: MaximumDebugLabels);
            GroundMovementDebugVisualization.Draw(
                debugDraw,
                movementSnapshot,
                maximumAgents: 64);
            FormationMovementDebugVisualization.Draw(
                debugDraw,
                formationSnapshot,
                maximumSlots: 128);
            NavigationDebugVisualization.Draw(
                debugDraw,
                navigationWorld,
                NavigationCapabilities.For(
                    NavigationMovementClass.Tracked),
                navigationPath,
                camera.Target);
            if (resourceSnapshot is not null)
            {
                ResourceDepositDebugVisualization.DrawDeposits(
                    debugDraw,
                    resourceSnapshot,
                    new Vector4(0.65f, 0.9f, 0.25f, 1.0f),
                    new Vector4(0.35f, 0.35f, 0.35f, 1.0f),
                    maximumDeposits: 64,
                    maximumLabels: 8);
            }

            if (logisticsSnapshot is not null)
            {
                LogisticsDebugVisualization.Draw(
                    debugDraw,
                    logisticsSnapshot,
                    maximumNodes: 128,
                    maximumEdges: 256,
                    maximumLabels: 8);
            }

            if (cargoTransportSnapshot is not null)
            {
                CargoTransportDebugVisualization.Draw(
                    debugDraw,
                    cargoTransportSnapshot,
                    maximumTransports: 128,
                    maximumLabels: 12);
            }

            if (distributionSnapshot is not null)
            {
                AutomatedDistributionDebugVisualization.Draw(
                    debugDraw,
                    distributionSnapshot,
                    maximumRequests: 128,
                    maximumLabels: 12);
            }

            if (battlefieldSupplySnapshot is not null)
            {
                BattlefieldSupplyDebugVisualization.Draw(
                    debugDraw,
                    battlefieldSupplySnapshot,
                    maximumProviders: 64,
                    maximumUnits: 128,
                    maximumLabels: 20);
            }

            int debugCount = Math.Min(
                renderWorld.InstanceCount,
                MaximumDebugInstanceBoxes);

            for (int index = 0; index < debugCount; index++)
            {
                RenderInstance instance =
                    renderWorld.GetInterpolatedInstance(index, alpha);
                DrawInstanceBounds(
                    debugDraw,
                    instance,
                    boundsColor,
                    index < MaximumDebugLabels
                        ? $"E{instance.Entity.Index}"
                        : null);
            }
        }

        int selectionLabelCount = 0;
        foreach (var entity in selectionController.Selection.Entities)
        {
            if (!renderWorld.TryGetInterpolatedInstance(
                    entity,
                    alpha,
                    out RenderInstance instance))
            {
                continue;
            }

            string? label = selectionLabelCount < MaximumDebugLabels
                ? $"SELECTED E{entity.Index}"
                : null;
            DrawInstanceBounds(
                debugDraw,
                instance,
                selectedColor,
                label);
            selectionLabelCount++;
        }

        EntityId hovered = selectionController.HoveredEntity;
        if (hovered.IsValid &&
            !selectionController.Selection.Contains(hovered) &&
            renderWorld.TryGetInterpolatedInstance(
                hovered,
                alpha,
                out RenderInstance hoveredInstance))
        {
            DrawInstanceBounds(
                debugDraw,
                hoveredInstance,
                hoveredColor,
                $"HOVER E{hovered.Index}");
        }
    }

    private static void DrawInstanceBounds(
        DebugDraw debugDraw,
        in RenderInstance instance,
        Vector4 color,
        string? label)
    {
        Vector3 extents = Vector3.Max(
            Vector3.Abs(instance.Transform.Scale) * 0.5f,
            new Vector3(0.05f));

        debugDraw.Box(
            new AxisAlignedBounds(
                instance.Transform.Position - extents,
                instance.Transform.Position + extents),
            color);

        if (label is not null)
        {
            debugDraw.Label(
                instance.Transform.Position +
                new Vector3(0.0f, extents.Y + 2.0f, 0.0f),
                label,
                color);
        }
    }

    private static DevelopmentOverlayMetrics CreateOverlayMetrics(
        in FrameTimingMetrics frameTiming,
        SimulationCoordinator simulation,
        SimulationDiagnosticsSnapshot simulationDiagnostics,
        TerrainRenderer terrainRenderer,
        SimpleInstanceRenderer instanceRenderer,
        DebugDrawRenderer debugDrawRenderer,
        RenderWorld renderWorld)
    {
        TerrainRenderDiagnostics terrain = terrainRenderer.LastDiagnostics;
        InstanceRenderDiagnostics instances = instanceRenderer.LastDiagnostics;
        DebugDrawRenderDiagnostics debug = debugDrawRenderer.LastDiagnostics;
        double jobExecutionMilliseconds =
            simulationDiagnostics.Jobs?.TotalExecutionDuration.TotalMilliseconds ?? 0.0;

        return new DevelopmentOverlayMetrics(
            frameTiming.FramesPerSecond,
            frameTiming.FrameMilliseconds,
            frameTiming.CpuRenderMilliseconds,
            simulation.CurrentTick.Value,
            simulationDiagnostics.LastTickDuration.TotalMilliseconds,
            simulation.Entities.EntityCount,
            terrain.VisibleChunks,
            terrain.TotalChunks,
            terrain.DrawCalls + instances.DrawCalls + debug.DrawCalls,
            instances.VisibleInstances,
            renderWorld.InstanceCount,
            jobExecutionMilliseconds,
            simulationDiagnostics.Runtime.TotalAllocatedBytes,
            simulationDiagnostics.Runtime.HeapSizeBytes,
            simulationDiagnostics.Runtime.Gen0Collections,
            simulationDiagnostics.Runtime.Gen1Collections,
            simulationDiagnostics.Runtime.Gen2Collections);
    }

    private static void UpdateToggle(
        InputState inputState,
        PlatformKey key,
        ref bool held,
        ref bool enabled)
    {
        bool down = inputState.IsKeyDown(key);

        if (down && !held)
        {
            enabled = !enabled;
        }

        held = down;
    }

    private static void UpdateFormationSelection(
        InputState inputState,
        ref bool held,
        ref FormationTemplate activeFormation)
    {
        bool down = inputState.IsKeyDown(PlatformKey.F3);

        if (down && !held)
        {
            activeFormation = activeFormation switch
            {
                FormationTemplate.Compact => FormationTemplate.Line,
                FormationTemplate.Line => FormationTemplate.Column,
                FormationTemplate.Column => FormationTemplate.Wedge,
                FormationTemplate.Wedge => FormationTemplate.Compact,
                _ => FormationTemplate.Compact
            };
        }

        held = down;
    }

    private static void DrainWindowEvents(
        IWindow window,
        IGraphicsDevice graphics)
    {
        WindowSize? resizeTarget = null;

        while (window.TryDequeueEvent(out WindowEvent windowEvent))
        {
            Console.WriteLine(
                $"[platform:event] kind={windowEvent.Kind} " +
                $"size={windowEvent.ClientSize.Width}x{windowEvent.ClientSize.Height} " +
                $"dpi={windowEvent.Dpi} focused={windowEvent.IsFocused} " +
                $"minimized={windowEvent.IsMinimized} mode={windowEvent.Mode}");

            if (windowEvent.Kind is
                WindowEventKind.Resized or
                WindowEventKind.Minimized or
                WindowEventKind.Restored or
                WindowEventKind.DpiChanged or
                WindowEventKind.ModeChanged)
            {
                resizeTarget = windowEvent.ClientSize;
            }
        }

        if (resizeTarget is WindowSize size)
        {
            graphics.Resize(size.Width, size.Height);
        }
    }

    private static void DrainInputEvents(
        IWindow window,
        InputState inputState)
    {
        while (window.TryDequeueInputEvent(out PlatformInputEvent inputEvent))
        {
            inputState.Apply(inputEvent);
        }
    }

    private static void WriteWindowState(string state, IWindow window)
    {
        Console.WriteLine(
            $"[platform:{state}] handle=0x{window.NativeHandle.Value:X} " +
            $"size={window.ClientSize.Width}x{window.ClientSize.Height} " +
            $"dpi={window.Dpi} focused={window.IsFocused} " +
            $"minimized={window.IsMinimized} mode={window.Mode}");
    }

    private static void WriteGraphicsState(
        string state,
        IGraphicsDevice graphics)
    {
        GraphicsDiagnostics diagnostics = graphics.Diagnostics;

        Console.WriteLine(
            $"[graphics:{state}] adapter=\"{diagnostics.Device.AdapterName}\" " +
            $"featureLevel={diagnostics.Device.FeatureLevel} " +
            $"software={diagnostics.Device.IsSoftwareAdapter} " +
            $"size={diagnostics.Surface.Width}x{diagnostics.Surface.Height} " +
            $"buffers={diagnostics.Surface.BufferCount} " +
            $"frameIndex={diagnostics.Surface.FrameIndex} " +
            $"present={diagnostics.Surface.PresentMode} " +
            $"suspended={diagnostics.Surface.IsSuspended}");
    }

    private static void WriteWorldState(string state, TerrainWorld world)
    {
        AxisAlignedBounds bounds = world.WorldBounds;

        Console.WriteLine(
            $"[world:{state}] chunks={world.Chunks.Count} " +
            $"chunkMeters={world.Settings.ChunkSizeMeters:F0} " +
            $"heightSamples={world.Settings.HeightSamplesPerSide} " +
            $"boundsMin=({bounds.Minimum.X:F0},{bounds.Minimum.Y:F1},{bounds.Minimum.Z:F0}) " +
            $"boundsMax=({bounds.Maximum.X:F0},{bounds.Maximum.Y:F1},{bounds.Maximum.Z:F0})");
    }

    private static void WriteCameraState(string state, RtsCamera camera)
    {
        RtsCameraDiagnostics diagnostics = camera.GetDiagnostics();

        Console.WriteLine(
            $"[camera:{state}] " +
            $"position=({diagnostics.Position.X:F2},{diagnostics.Position.Y:F2},{diagnostics.Position.Z:F2}) " +
            $"target=({diagnostics.Target.X:F2},{diagnostics.Target.Y:F2},{diagnostics.Target.Z:F2}) " +
            $"yaw={diagnostics.YawDegrees:F1} pitch={diagnostics.PitchDegrees:F1} " +
            $"distance={diagnostics.Distance:F1} " +
            $"cursor=({diagnostics.PointerPosition.X:F0},{diagnostics.PointerPosition.Y:F0}) " +
            $"cursorValid={diagnostics.HasPointerPosition}");
    }

    private static void WriteTerrainState(
        string state,
        TerrainRenderer terrainRenderer)
    {
        TerrainRenderDiagnostics diagnostics = terrainRenderer.LastDiagnostics;

        Console.WriteLine(
            $"[terrain:{state}] totalChunks={diagnostics.TotalChunks} " +
            $"visibleChunks={diagnostics.VisibleChunks} " +
            $"culledChunks={diagnostics.CulledChunks} " +
            $"triangles={diagnostics.SubmittedTriangles} " +
            $"drawCalls={diagnostics.DrawCalls} " +
            $"staticBuffers={diagnostics.UploadedBufferCount}");
    }

    private static void WritePresentationState(
        string state,
        SimulationCoordinator simulation,
        RenderWorld renderWorld,
        TerrainRenderer terrainRenderer,
        SimpleInstanceRenderer instanceRenderer)
    {
        TerrainRenderDiagnostics terrain = terrainRenderer.LastDiagnostics;
        InstanceRenderDiagnostics instances = instanceRenderer.LastDiagnostics;

        Console.WriteLine(
            $"[presentation:{state}] tick={simulation.CurrentTick.Value} " +
            $"entities={simulation.Entities.EntityCount} " +
            $"snapshotTick={renderWorld.CurrentSnapshot?.Tick.Value ?? 0} " +
            $"instances={renderWorld.InstanceCount} " +
            $"visibleInstances={instances.VisibleInstances} " +
            $"visibleChunks={terrain.VisibleChunks} " +
            $"drawCalls={terrain.DrawCalls + instances.DrawCalls}");
    }

    private static void WriteInteractionState(
        string state,
        RtsSelectionController selectionController,
        SimulationCommandEnvelope? movementEnvelope,
        MoveEntitiesCommand? movementCommand,
        FormationTemplate activeFormation,
        RtsBuildingPlacementController buildingPlacementController,
        SimulationCommandEnvelope? buildEnvelope,
        BuildCommand? buildCommand,
        BuildingCommandProcessingSystem buildingCommands)
    {
        EntityId hovered = selectionController.HoveredEntity;
        string hoveredText = hovered.IsValid
            ? hovered.ToString()
            : "none";
        string commandText = movementEnvelope.HasValue
            ? movementEnvelope.Value.Sequence.ToString(
                System.Globalization.CultureInfo.InvariantCulture)
            : "none";
        string buildCommandText = buildEnvelope.HasValue
            ? buildEnvelope.Value.Sequence.ToString(
                System.Globalization.CultureInfo.InvariantCulture)
            : "none";
        BuildCommandMetrics buildMetrics = buildingCommands.Metrics;

        Console.WriteLine(
            $"[interaction:{state}] selected={selectionController.Selection.Count} " +
            $"hovered={hoveredText} lastCommand={commandText} " +
            $"acceptedTargets={movementCommand?.AcceptedTargetCount ?? 0} " +
            $"rejectedTargets={movementCommand?.RejectedTargetCount ?? 0} " +
            $"executedTick={movementCommand?.ExecutedAtTick.Value ?? 0} " +
            $"formation={activeFormation} placementActive={buildingPlacementController.IsActive} " +
            $"building={buildingPlacementController.ActiveBuilding} " +
            $"orientation={buildingPlacementController.Orientation} " +
            $"lastBuildCommand={buildCommandText} buildRequestEntity={buildCommand?.RequestEntity.ToString() ?? "none"} " +
            $"acceptedBuilds={buildMetrics.AcceptedCommands} rejectedBuilds={buildMetrics.RejectedCommands} " +
            $"lastBuildRejection={buildMetrics.LastRejection} placementFailure={buildMetrics.LastPlacementFailure}");
    }
}
