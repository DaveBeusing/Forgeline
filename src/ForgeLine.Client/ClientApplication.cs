using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Input;
using ForgeLine.Intelligence;
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
    internal const int RestartRequestedExitCode = 10;

    private static readonly PlayerId LocalPlayer = new(1);
    private static readonly PlayerId OpposingPlayer = new(2);
    private static readonly TimeSpan IdleWait = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan SmokeTestDuration = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan DiagnosticInterval = TimeSpan.FromSeconds(1);

    private readonly IPlatform _platform;

    internal ClientApplication(IPlatform platform)
    {
        _platform = platform;
    }

    internal int Run(bool smokeTest, int renderInstanceCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(renderInstanceCount);

        var configuration = new WindowConfiguration(
            "FORGELINE",
            1600,
            900,
            resizable: true,
            WindowMode.Windowed);

        using IWindow window = _platform.CreateWindow(configuration);

        var snapshotBuffer =
            new PresentationSnapshotBuffer();
        using var jobScheduler =
            new JobScheduler();
        VerticalSliceRuntimeSettings runtimeSettings =
            VerticalSliceRuntimeSettings.CreateClient(
                jobScheduler);
        using VerticalSliceScenario scenario =
            VerticalSliceScenario.Create(
                runtimeSettings);

        SimulationCoordinator simulation =
            scenario.Simulation;
        PrototypeBattlefieldDefinition prototypeBattlefield =
            scenario.Battlefield;
        TerrainWorld terrainWorld =
            scenario.Terrain;
        SkirmishStartingBase westBase =
            scenario.West;
        SkirmishStartingBase eastBase =
            scenario.East;

        if (renderInstanceCount > 0)
        {
            PopulateSimulationEntities(
                simulation,
                terrainWorld,
                renderInstanceCount);
        }

        EntityId constructionInventory =
            westBase.CommandCore;

        var presentationInteraction =
            new PresentationInteractionState();
        var commandGateway =
            new PlayerCommandGateway(
                simulation,
                scenario.Services.BuildingCommands,
                scenario.BattlefieldRuntime.MatchStateEntity,
                intelligence:
                    scenario.Intelligence,
                weapons:
                    scenario.Services.Weapons,
                artilleryWeapons:
                    scenario.Services.ArtilleryWeapons);
        var presentationExtraction =
            new PresentationExtractionContext(
                scenario,
                LocalPlayer,
                presentationInteraction,
                commandGateway);

        simulation.RegisterTickObserver(
            commandGateway);
        simulation.RegisterTickObserver(
            new PresentationExtractor(
                snapshotBuffer,
                presentationExtraction));

        var renderWorld = new RenderWorld();

        BattlefieldStartPosition localStart =
            prototypeBattlefield.Starts.Single(
                start =>
                    start.Player == LocalPlayer);
        float targetHeight = terrainWorld.TrySampleHeight(
            localStart.Position.X,
            localStart.Position.Z,
            out float sampledHeight)
            ? sampledHeight
            : 0.0f;

        var inputState = new InputState();
        var actionMapper = new RtsCameraActionMapper();
        var camera = new RtsCamera(
            new RtsCameraSettings
            {
                InitialTarget = new Vector3(
                    localStart.Position.X,
                    targetHeight,
                    localStart.Position.Z),
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
                ControllableEntityCategory.Building |
                ControllableEntityCategory.Logistics));
        var buildingPlacementController =
            new RtsBuildingPlacementController(LocalPlayer);
        var actionPanel =
            new PlayerActionPanelController();
        var tacticalTargetingController =
            new RtsTacticalTargetingController();
        var debugDraw = new DebugDraw();

        using var simulationHost =
            new ClientSimulationHost(
                scenario,
                commandGateway,
                snapshotBuffer);
        _ = renderWorld.Update(
            snapshotBuffer);

        var graphicsTarget =
            new GraphicsWindowTarget(
                window.NativeHandle.Value,
                window.ClientSize.Width,
                window.ClientSize.Height,
                window.IsMinimized ||
                window.ClientSize.IsEmpty);
        using var renderHost =
            new ClientRenderHost(
                graphicsTarget,
                terrainWorld,
                snapshotBuffer,
                camera.Settings);

        PlayerCommandSubmissionReceipt? lastCommandReceipt = null;
        PlayerCommandResultReadModel? lastCommandResult = null;
        bool overlayEnabled = true;
        bool worldDebugEnabled = false;
        bool overlayToggleHeld = false;
        bool worldDebugToggleHeld = false;
        bool formationToggleHeld = false;
        bool restartHeld = false;
        bool returnHeld = false;
        FormationTemplate activeFormation =
            FormationTemplate.Compact;
        bool simulationPausedForWindow = false;
        bool terminalAcknowledgementRequested = false;
        bool smokeCompletionRequested = false;

        WriteWindowState("started", window);
        WriteWorldState("started", terrainWorld);
        WriteCameraState("started", camera);
        Console.WriteLine(
            $"[execution:owners] platform={Environment.CurrentManagedThreadId} " +
            $"session={simulationHost.SessionId.Value} simulation=dedicated render=dedicated");
        WriteInteractionState(
            "started",
            selectionController,
            lastCommandReceipt,
            lastCommandResult,
            activeFormation,
            buildingPlacementController);

        long startedAt = _platform.Clock.GetTimestamp();
        long previousFrameAt = startedAt;
        long nextDiagnosticAt = startedAt;
        bool smokeMatchCompleted = false;

        while (window.IsOpen)
        {
            inputState.BeginFrame();

            if (!_platform.PumpEvents())
            {
                break;
            }

            DrainWindowEvents(window);
            DrainInputEvents(window, inputState);
            simulationHost.ThrowIfFaulted();
            renderHost.ThrowIfFaulted();
            _ = renderWorld.Update(
                snapshotBuffer);
            DrainSubmissionCompletions(
                simulationHost,
                ref lastCommandReceipt);

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
            presentationInteraction.SetDebugState(
                worldDebugEnabled,
                camera.Target.Y);

            bool restartPressed =
                ConsumeKeyPress(
                    inputState,
                    PlatformKey.R,
                    ref restartHeld);
            bool returnPressed =
                ConsumeKeyPress(
                    inputState,
                    PlatformKey.Escape,
                    ref returnHeld);
            PresentationSnapshot? inputSnapshot =
                renderWorld.CurrentSnapshot;
            PlayerExperienceSnapshot? inputExperience =
                inputSnapshot?.PlayerExperience;
            bool inputMatchTerminal =
                inputExperience?.IsMatchComplete ==
                true;

            if (inputExperience?.MatchStatus ==
                    PlayerMatchStatus.Ended &&
                terminalAcknowledgementRequested)
            {
                return 0;
            }

            if (inputMatchTerminal &&
                restartPressed)
            {
                return RestartRequestedExitCode;
            }

            if (inputMatchTerminal &&
                returnPressed &&
                !terminalAcknowledgementRequested)
            {
                if (!simulationHost.TryAcknowledgeTerminal(
                        simulationHost.SessionId,
                        LocalPlayer,
                        inputSnapshot!.Tick))
                {
                    throw new InvalidOperationException(
                        "Simulation control boundary is full while acknowledging the terminal match.");
                }

                terminalAcknowledgementRequested = true;
            }

            if (!window.IsOpen)
            {
                continue;
            }

            bool shouldPauseForWindow =
                window.IsMinimized ||
                window.ClientSize.IsEmpty;

            if (shouldPauseForWindow !=
                simulationPausedForWindow)
            {
                if (!simulationHost.TrySetPaused(
                        shouldPauseForWindow))
                {
                    throw new InvalidOperationException(
                        "Simulation control boundary is full while changing the window pause state.");
                }

                simulationPausedForWindow =
                    shouldPauseForWindow;
            }

            if (shouldPauseForWindow)
            {
                debugDraw.Clear();
                _ = renderHost.Publish(
                    new ClientRenderFrame(
                        camera.CaptureState(),
                        window.ClientSize.Width,
                        window.ClientSize.Height,
                        overlayEnabled,
                        worldDebugEnabled,
                        actionPanel.CreateView(
                            window.ClientSize.Width,
                            window.ClientSize.Height,
                            inputSnapshot?.PlayerActions),
                        tacticalTargetingController.CreateView(
                            inputSnapshot),
                        activeFormation,
                        [],
                        []));
                _platform.WaitForEvents(IdleWait);
                continue;
            }

            if (inputMatchTerminal)
            {
                actionPanel.Close();
                tacticalTargetingController.Cancel();
            }
            else
            {
                actionPanel.Update(
                    inputState,
                    inputSnapshot,
                    window.ClientSize.Width,
                    window.ClientSize.Height);

                if (actionPanel.HasKeyboardFocus)
                {
                    tacticalTargetingController.Cancel();

                    if (buildingPlacementController.IsActive)
                    {
                        buildingPlacementController.Cancel(
                            presentationInteraction);
                    }
                }

                if (actionPanel.TryTakeRequest(
                        out PlayerActionRequest actionRequest))
                {
                    DispatchPlayerActionRequest(
                        actionRequest,
                        commandGateway,
                        buildingPlacementController,
                        tacticalTargetingController,
                        presentationInteraction,
                        inputSnapshot?.Tick ??
                            SimulationTick.Zero,
                        ref lastCommandReceipt);
                }
            }

            if (!actionPanel.HasKeyboardFocus)
            {
                RtsCameraInputFrame cameraInput =
                    actionMapper.Map(inputState);
                camera.Update(
                    cameraInput,
                    cameraDeltaSeconds,
                    window.ClientSize.Width,
                    window.ClientSize.Height);
            }

            if (smokeTest &&
                smokeMatchCompleted &&
                _platform.Clock.GetElapsedTime(startedAt, now) >= SmokeTestDuration)
            {
                window.RequestClose();
            }

            _ = renderWorld.Update(
                snapshotBuffer);

            PresentationSnapshot? currentSnapshot =
                renderWorld.CurrentSnapshot;
            const float renderAlpha = 1.0f;
            PlayerExperienceSnapshot? currentExperience =
                currentSnapshot?.PlayerExperience;
            bool gameplayActive =
                currentExperience?.MatchStatus ==
                PlayerMatchStatus.Active;

            if (gameplayActive)
            {
                tacticalTargetingController.Update(
                    inputState,
                    camera,
                    terrainWorld,
                    currentSnapshot,
                    window.ClientSize.Width,
                    window.ClientSize.Height,
                    activeFormation,
                    actionPanel.PointerCaptured);

                if (tacticalTargetingController.TryTakeRequest(
                        out PlayerActionRequest tacticalRequest))
                {
                    DispatchPlayerActionRequest(
                        tacticalRequest,
                        commandGateway,
                        buildingPlacementController,
                        tacticalTargetingController,
                        presentationInteraction,
                        currentSnapshot?.Tick ??
                            SimulationTick.Zero,
                        ref lastCommandReceipt);
                }

                if (!tacticalTargetingController.IsActive)
                {
                    buildingPlacementController.Update(
                    inputState,
                    camera,
                    terrainWorld,
                    currentSnapshot,
                    presentationInteraction,
                    window.ClientSize.Width,
                    window.ClientSize.Height,
                    actionPanel.PointerCaptured);
                }

                if (!buildingPlacementController.IsActive)
                {
                    selectionController.Update(
                        inputState,
                        camera,
                        renderWorld,
                        terrainWorld,
                        window.ClientSize.Width,
                        window.ClientSize.Height,
                        renderAlpha,
                        actionPanel.PointerCaptured ||
                        tacticalTargetingController.PointerCaptured);

                    if (selectionController.TryTakeMovementRequest(
                            out MovementOrderRequest movementRequest))
                    {
                        lastCommandReceipt =
                            commandGateway.SubmitMovement(
                                LocalPlayer,
                                movementRequest.Entities,
                                movementRequest.WorldTarget,
                                currentSnapshot?.Tick ??
                                    SimulationTick.Zero,
                                activeFormation);
                    }
                }

                presentationInteraction.SetSelection(
                    selectionController.Selection.Entities);

                if (buildingPlacementController.TryTakePlacementRequest(
                        out BuildingPlacementRequest placementRequest))
                {
                    lastCommandReceipt =
                        commandGateway.SubmitBuild(
                            LocalPlayer,
                            placementRequest.BuildingId,
                            placementRequest.Position,
                            placementRequest.Orientation,
                            constructionInventory,
                            currentSnapshot?.Tick ??
                                SimulationTick.Zero);
                }
            }
            else
            {
                presentationInteraction.SetSelection(
                    selectionController.Selection.Entities);
            }

            DrainCommandResults(
                commandGateway,
                ref lastCommandResult);

            BuildWorldDebugVisualization(
                debugDraw,
                worldDebugEnabled,
                renderWorld,
                renderAlpha,
                camera,
                selectionController,
                buildingPlacementController,
                currentSnapshot?.Construction,
                currentSnapshot?.Debug);

            if (worldDebugEnabled &&
                currentSnapshot?.Debug is
                    PresentationDebugSnapshot debugSnapshot)
            {
                PrototypeBattlefieldDebugVisualization.Draw(
                    debugDraw,
                    prototypeBattlefield,
                    debugSnapshot.CrossingStates);
                SkirmishOpponentDebugVisualization.Draw(
                    debugDraw,
                    debugSnapshot.Opponents,
                    MaximumDebugLabels);
            }

            _ = renderHost.Publish(
                new ClientRenderFrame(
                    camera.CaptureState(),
                    window.ClientSize.Width,
                    window.ClientSize.Height,
                    overlayEnabled,
                    worldDebugEnabled,
                    actionPanel.CreateView(
                        window.ClientSize.Width,
                        window.ClientSize.Height,
                        currentSnapshot?.PlayerActions),
                    tacticalTargetingController.CreateView(
                        currentSnapshot),
                    activeFormation,
                    debugDraw.Lines.ToArray(),
                    debugDraw.Labels.ToArray()));

            if (_platform.Clock.GetElapsedTime(nextDiagnosticAt, now) >= DiagnosticInterval)
            {
                WriteCameraState("frame", camera);
                Console.WriteLine(
                    $"[presentation:frame] session={currentSnapshot?.SessionId.Value ?? 0} " +
                    $"tick={currentSnapshot?.Tick.Value ?? 0} " +
                    $"entities={currentSnapshot?.SimulationEntityCount ?? 0} " +
                    $"instances={renderWorld.InstanceCount}");
                WriteInteractionState(
                    "frame",
                    selectionController,
                    lastCommandReceipt,
                    lastCommandResult,
                    activeFormation,
                    buildingPlacementController);
                nextDiagnosticAt = now;
            }
        }

        DrainWindowEvents(window);
        DrainInputEvents(window, inputState);
        WriteCameraState("stopped", camera);
        WriteInteractionState(
            "stopped",
            selectionController,
            lastCommandReceipt,
            lastCommandResult,
            activeFormation,
            buildingPlacementController);
        renderHost.ThrowIfFaulted();
        simulationHost.ThrowIfFaulted();
        return 0;
    }

    private static void DispatchPlayerActionRequest(
        in PlayerActionRequest request,
        PlayerCommandGateway commandGateway,
        RtsBuildingPlacementController buildingPlacementController,
        RtsTacticalTargetingController tacticalTargetingController,
        PresentationInteractionState presentationInteraction,
        SimulationTick observedTick,
        ref PlayerCommandSubmissionReceipt? lastCommandReceipt)
    {
        if (PlayerActionRequestDispatcher.TryDispatch(
                request,
                LocalPlayer,
                commandGateway,
                observedTick,
                out PlayerCommandSubmissionReceipt receipt))
        {
            lastCommandReceipt = receipt;
            return;
        }

        switch (request.Kind)
        {
            case PlayerActionRequestKind.BeginBuildingPlacement:
                tacticalTargetingController.Cancel();
                buildingPlacementController.SelectBuilding(
                    request.BuildingId);
                break;

            case PlayerActionRequestKind.BeginAttackTargeting:
            case PlayerActionRequestKind.BeginAttackMoveTargeting:
            case PlayerActionRequestKind.BeginRetreatTargeting:
            case PlayerActionRequestKind.BeginFireMissionTargeting:
                if (buildingPlacementController.IsActive)
                {
                    buildingPlacementController.Cancel(
                        presentationInteraction);
                }

                tacticalTargetingController.Begin(
                    request,
                    commandGateway.SessionId);
                break;
        }
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
            float x =
                terrainWorld.WorldBounds.Minimum.X +
                620.0f +
                xIndex * spacing -
                halfSpan;
            float z =
                terrainWorld.WorldBounds.Center.Z +
                zIndex * spacing -
                halfSpan;
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

            FactionId faction =
                new(
                    checked(
                        (uint)owner.Value));

            simulation.Entities.AddComponent(entity, new VisualIdentity(1));
            simulation.Entities.AddComponent(
                entity,
                new IntelligenceSignature(
                    faction,
                    identityKey:
                        checked((uint)index + 1)));
            simulation.Entities.AddComponent(
                entity,
                new ControllableEntity(owner, category));

            if (owner == LocalPlayer &&
                index % 17 == 0)
            {
                simulation.Entities.AddComponent(
                    entity,
                    new VisualSensorState(
                        faction,
                        rangeMeters: 140.0f,
                        updateIntervalTicks: 1));
            }

            if (owner == LocalPlayer &&
                index % 29 == 0)
            {
                simulation.Entities.AddComponent(
                    entity,
                    new RadarSensorState(
                        faction,
                        detectionRangeMeters: 280.0f,
                        identificationRangeMeters: 90.0f,
                        updateIntervalTicks: 4));
            }
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

    private static void BuildWorldDebugVisualization(
        DebugDraw debugDraw,
        bool worldDebugEnabled,
        RenderWorld renderWorld,
        float alpha,
        RtsCamera camera,
        RtsSelectionController selectionController,
        RtsBuildingPlacementController buildingPlacementController,
        BuildingConstructionDebugSnapshot? constructionSnapshot,
        PresentationDebugSnapshot? debugSnapshot)
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

        if (worldDebugEnabled &&
            debugSnapshot is not null)
        {
            SpatialIndexDebugVisualization.DrawRadiusQuery(
                debugDraw,
                camera.Target,
                40.0f,
                rangeColor);
            debugDraw.Point(camera.Target, 8.0f, pointColor);

            SpatialIndexDebugVisualization.DrawOccupiedCells(
                debugDraw,
                debugSnapshot.Spatial,
                new Vector4(0.35f, 0.65f, 1.0f, 0.8f),
                new Vector4(1.0f, 0.35f, 0.15f, 1.0f),
                maximumCells: 256,
                maximumLabels: MaximumDebugLabels);
            GroundMovementDebugVisualization.Draw(
                debugDraw,
                debugSnapshot.Movement,
                maximumAgents: 64);
            FormationMovementDebugVisualization.Draw(
                debugDraw,
                debugSnapshot.Formation,
                maximumSlots: 128);
            NavigationDebugVisualization.Draw(
                debugDraw,
                debugSnapshot.NavigationWorld,
                NavigationCapabilities.For(
                    NavigationMovementClass.Tracked),
                debugSnapshot.NavigationPath,
                camera.Target);
            if (debugSnapshot.Resources is not null)
            {
                ResourceDepositDebugVisualization.DrawDeposits(
                    debugDraw,
                    debugSnapshot.Resources,
                    new Vector4(0.65f, 0.9f, 0.25f, 1.0f),
                    new Vector4(0.35f, 0.35f, 0.35f, 1.0f),
                    maximumDeposits: 64,
                    maximumLabels: 8);
            }

            if (debugSnapshot.Logistics is not null)
            {
                LogisticsDebugVisualization.Draw(
                    debugDraw,
                    debugSnapshot.Logistics,
                    maximumNodes: 128,
                    maximumEdges: 256,
                    maximumLabels: 8);
            }

            if (debugSnapshot.CargoTransport is not null)
            {
                CargoTransportDebugVisualization.Draw(
                    debugDraw,
                    debugSnapshot.CargoTransport,
                    maximumTransports: 128,
                    maximumLabels: 12);
            }

            if (debugSnapshot.Distribution is not null)
            {
                AutomatedDistributionDebugVisualization.Draw(
                    debugDraw,
                    debugSnapshot.Distribution,
                    maximumRequests: 128,
                    maximumLabels: 12);
            }

            if (debugSnapshot.LogisticsCapacity is not null)
            {
                LogisticsCapacityDebugVisualization.Draw(
                    debugDraw,
                    debugSnapshot.LogisticsCapacity,
                    maximumNodes: 128,
                    maximumEdges: 256,
                    maximumLabels: 12);
            }

            if (debugSnapshot.BattlefieldSupply is not null)
            {
                BattlefieldSupplyDebugVisualization.Draw(
                    debugDraw,
                    debugSnapshot.BattlefieldSupply,
                    maximumProviders: 64,
                    maximumUnits: 128,
                    maximumLabels: 20);
            }

            if (renderWorld.CurrentSnapshot?.Intelligence is
                FactionIntelligenceSnapshot intelligenceSnapshot)
            {
                IntelligenceDebugVisualization.Draw(
                    debugDraw,
                    intelligenceSnapshot,
                    maximumCells: 512,
                    maximumContacts: 96);
                IntelligenceDebugVisualization.DrawSensors(
                    debugDraw,
                    debugSnapshot.IntelligenceSensors,
                    maximumSensors: 64);
                IntelligenceDebugVisualization.DrawMetrics(
                    debugDraw,
                    debugSnapshot.IntelligenceMetrics,
                    camera.Target + Vector3.UnitY * 6.0f);
            }

            if (debugSnapshot.Artillery is not null)
            {
                ArtilleryDebugVisualization.Draw(
                    debugDraw,
                    debugSnapshot.Artillery,
                    camera.Target + Vector3.UnitY * 9.0f,
                    maximumMissions: 64,
                    maximumProjectiles: 128);
            }

            if (debugSnapshot.Readiness is not null)
            {
                TacticalCombatDebugVisualization.Draw(
                    debugDraw,
                    debugSnapshot.TacticalEntries,
                    debugSnapshot.TacticalMetrics,
                    debugSnapshot.Readiness,
                    debugSnapshot.ResupplyDecisionMetrics,
                    camera.Target + Vector3.UnitY * 13.0f,
                    maximumUnits: 96,
                    maximumReadinessLabels: 64);
            }

            if (debugSnapshot.Combat is not null)
            {
                CombatDebugVisualization.Draw(
                    debugDraw,
                    debugSnapshot.Combat,
                    maximumWeapons: 64,
                    maximumProjectiles: 256,
                    maximumHealthLabels: 32,
                    maximumImpacts: 128);
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
        PresentationSnapshot? snapshot,
        TerrainRenderer terrainRenderer,
        SimpleInstanceRenderer instanceRenderer,
        DebugDrawRenderer debugDrawRenderer,
        RenderWorld renderWorld)
    {
        TerrainRenderDiagnostics terrain =
            terrainRenderer.LastDiagnostics;
        InstanceRenderDiagnostics instances =
            instanceRenderer.LastDiagnostics;
        DebugDrawRenderDiagnostics debug =
            debugDrawRenderer.LastDiagnostics;
        SimulationDiagnosticsSnapshot? simulationDiagnostics =
            snapshot?.SimulationDiagnostics;

        double jobExecutionMilliseconds =
            simulationDiagnostics?.Jobs?
                .TotalExecutionDuration
                .TotalMilliseconds ??
            0.0;

        return new DevelopmentOverlayMetrics(
            frameTiming.FramesPerSecond,
            frameTiming.FrameMilliseconds,
            frameTiming.CpuRenderMilliseconds,
            snapshot?.Tick.Value ?? 0,
            simulationDiagnostics?
                .LastTickDuration
                .TotalMilliseconds ??
            0.0,
            snapshot?.SimulationEntityCount ?? 0,
            terrain.VisibleChunks,
            terrain.TotalChunks,
            terrain.DrawCalls +
            instances.DrawCalls +
            debug.DrawCalls,
            instances.VisibleInstances,
            renderWorld.InstanceCount,
            jobExecutionMilliseconds,
            simulationDiagnostics?
                .Runtime.TotalAllocatedBytes ??
            0,
            simulationDiagnostics?
                .Runtime.HeapSizeBytes ??
            0,
            simulationDiagnostics?
                .Runtime.Gen0Collections ??
            0,
            simulationDiagnostics?
                .Runtime.Gen1Collections ??
            0,
            simulationDiagnostics?
                .Runtime.Gen2Collections ??
            0);
    }

    private static bool ConsumeKeyPress(
        InputState inputState,
        PlatformKey key,
        ref bool held)
    {
        bool down =
            inputState.IsKeyDown(key);
        bool pressed =
            down &&
            !held;
        held = down;
        return pressed;
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
        RenderWorld renderWorld,
        TerrainRenderer terrainRenderer,
        SimpleInstanceRenderer instanceRenderer)
    {
        TerrainRenderDiagnostics terrain =
            terrainRenderer.LastDiagnostics;
        InstanceRenderDiagnostics instances =
            instanceRenderer.LastDiagnostics;
        PresentationSnapshot? snapshot =
            renderWorld.CurrentSnapshot;

        Console.WriteLine(
            $"[presentation:{state}] session={snapshot?.SessionId.Value ?? 0} " +
            $"tick={snapshot?.Tick.Value ?? 0} " +
            $"entities={snapshot?.SimulationEntityCount ?? 0} " +
            $"instances={renderWorld.InstanceCount} " +
            $"visibleInstances={instances.VisibleInstances} " +
            $"visibleChunks={terrain.VisibleChunks} " +
            $"drawCalls={terrain.DrawCalls + instances.DrawCalls}");
    }

    private static void WriteInteractionState(
        string state,
        RtsSelectionController selectionController,
        PlayerCommandSubmissionReceipt? commandReceipt,
        PlayerCommandResultReadModel? commandResult,
        FormationTemplate activeFormation,
        RtsBuildingPlacementController buildingPlacementController)
    {
        EntityId hovered =
            selectionController.HoveredEntity;
        string hoveredText =
            hovered.IsValid
                ? hovered.ToString()
                : "none";

        string commandSequence =
            commandReceipt?.Sequence.ToString(
                System.Globalization.CultureInfo.InvariantCulture) ??
            "none";

        string correlation =
            commandReceipt?.CorrelationId.Value.ToString(
                System.Globalization.CultureInfo.InvariantCulture) ??
            commandResult?.CorrelationId.Value.ToString(
                System.Globalization.CultureInfo.InvariantCulture) ??
            "none";

        Console.WriteLine(
            $"[interaction:{state}] selected={selectionController.Selection.Count} " +
            $"hovered={hoveredText} commandSequence={commandSequence} " +
            $"correlation={correlation} " +
            $"commandAccepted={commandReceipt?.Accepted ?? false} " +
            $"submissionFailure={commandReceipt?.Failure ?? PlayerCommandSubmissionFailure.None} " +
            $"result={commandResult?.State ?? PlayerCommandFeedbackState.None} " +
            $"acceptedTargets={commandResult?.AcceptedTargets ?? 0} " +
            $"rejectedTargets={commandResult?.RejectedTargets ?? 0} " +
            $"resolvedTick={commandResult?.ResolvedAtTick.Value ?? 0} " +
            $"buildRejection={commandResult?.BuildRejection ?? BuildCommandRejectionReason.None} " +
            $"placementFailure={commandResult?.PlacementFailure ?? BuildingPlacementFailureReason.None} " +
            $"formation={activeFormation} " +
            $"placementActive={buildingPlacementController.IsActive} " +
            $"building={buildingPlacementController.ActiveBuilding} " +
            $"orientation={buildingPlacementController.Orientation} " +
            $"previewFreshness={buildingPlacementController.PreviewFreshness}");
    }

    private static void DrainCommandResults(
        PlayerCommandGateway commandGateway,
        ref PlayerCommandResultReadModel? lastCommandResult)
    {
        while (commandGateway.Results.TryRead(
                   out PlayerCommandResultReadModel result))
        {
            lastCommandResult =
                result;
        }
    }

}
