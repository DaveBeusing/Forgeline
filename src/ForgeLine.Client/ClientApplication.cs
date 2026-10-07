using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Input;
using ForgeLine.UI;
using ForgeLine.Intelligence;
using ForgeLine.Jobs;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Platform;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Client;

internal readonly record struct ClientWindowQualificationSnapshot(
    WindowMode Mode,
    int ClientWidth,
    int ClientHeight,
    bool IsMinimized,
    uint Dpi);

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
    private static readonly TimeSpan PauseTransitionTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FrontendFramePresentationTimeout = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions VisualQualificationJsonOptions =
        new()
        {
            PropertyNamingPolicy =
                JsonNamingPolicy.CamelCase,
            WriteIndented =
                true
        };

    private readonly IPlatform _platform;
    private readonly ClientUserSettings _settings;
    private readonly string _settingsPath;

    internal ClientApplication(
        IPlatform platform,
        ClientUserSettings? settings = null,
        string? settingsPath = null)
    {
        _platform =
            platform ??
            throw new ArgumentNullException(nameof(platform));
        _settings =
            settings ??
            new ClientUserSettings();
        _settings.Validate();
        _settingsPath =
            string.IsNullOrWhiteSpace(settingsPath)
                ? "default settings"
                : settingsPath;
    }

    internal int Run(
        bool smokeTest,
        int renderInstanceCount,
        string? visualQualificationOutput = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(renderInstanceCount);

        WindowConfiguration configuration =
            _settings.CreateWindowConfiguration();

        using IWindow window = _platform.CreateWindow(configuration);
        ClientWindowQualificationSnapshot qualificationWindow =
            CaptureWindowQualification(
                window);

        var frontendShell = new GameFrontendShell();
        var frontendLoading = new FrontendLoadingController();
        var bootstrapTarget =
            new GraphicsWindowTarget(
                window.NativeHandle.Value,
                window.ClientSize.Width,
                window.ClientSize.Height,
                window.IsMinimized ||
                window.ClientSize.IsEmpty);

        var newGame =
            new NewGameModel();
        using var jobScheduler =
            new JobScheduler();
        RuntimeAssetCatalog? runtimeAssets;

        using (var bootRenderer =
            new ClientFrontendRenderHost(bootstrapTarget))
        {
            frontendLoading.BeginPhase(
                FrontendLoadingPhase.LoadingSettings,
                "Settings validated");
            bootRenderer.Publish(
                FrontendPresentationAdapter.Loading(
                    frontendLoading.State));
            PumpBootFrame(window, bootRenderer);

            frontendLoading.BeginPhase(
                FrontendLoadingPhase.LoadingAssets,
                "Loading runtime assets");
            bootRenderer.Publish(
                FrontendPresentationAdapter.Loading(
                    frontendLoading.State));
            PumpBootFrame(window, bootRenderer);
            runtimeAssets =
                LoadRuntimeAssets();

            frontendLoading.Complete(
                "Command interface ready");
            bootRenderer.Publish(
                FrontendPresentationAdapter.Loading(
                    frontendLoading.State));
            PumpBootFrame(window, bootRenderer);
        }

        ClientSessionRequest sessionRequest;
        if (smokeTest)
        {
            sessionRequest =
                ClientSessionRequest.NewGame(
                    newGame.Configuration.Seed);
        }
        else
        {
            FrontendSessionSelectionResult selection =
                RunFrontendSessionSelection(
                    window,
                    frontendShell,
                    newGame);

            if (selection.RestartRequested)
            {
                return RestartRequestedExitCode;
            }

            if (selection.Session is not ClientSessionRequest selected)
            {
                return 0;
            }

            sessionRequest = selected;
        }

        var sessionTransitionTarget =
            new GraphicsWindowTarget(
                window.NativeHandle.Value,
                window.ClientSize.Width,
                window.ClientSize.Height,
                window.IsMinimized ||
                window.ClientSize.IsEmpty);
        using var sessionTransitionRenderer =
            new ClientFrontendRenderHost(
                sessionTransitionTarget);

        frontendLoading.BeginPhase(
            FrontendLoadingPhase.PreparingFrontend,
            sessionRequest.Kind ==
                ClientSessionRequestKind.LoadGame
                ? "RESTORING SAVED BATTLEFIELD"
                : "CREATING BATTLEFIELD SIMULATION",
            totalSteps: 4);
        sessionTransitionRenderer.Publish(
            FrontendPresentationAdapter.Loading(
                frontendLoading.State));
        PumpBootFrame(
            window,
            sessionTransitionRenderer);

        using VerticalSliceScenario scenario =
            ClientSessionFactory.Create(
                sessionRequest,
                jobScheduler);

        frontendLoading.ReportProgress(
            1,
            "PREPARING PRESENTATION STATE");
        sessionTransitionRenderer.Publish(
            FrontendPresentationAdapter.Loading(
                frontendLoading.State));
        PumpBootFrame(
            window,
            sessionTransitionRenderer);

        var snapshotBuffer =
            new PresentationSnapshotBuffer();

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
                    scenario.Services.ArtilleryWeapons,
                technologies:
                    scenario.Services.TechnologyDefinitions);
        var presentationExtraction =
            new PresentationExtractionContext(
                scenario,
                LocalPlayer,
                presentationInteraction,
                commandGateway);

        simulation.AttachTickObserver(
            commandGateway);
        simulation.AttachTickObserver(
            new PresentationExtractor(
                snapshotBuffer,
                presentationExtraction,
                runtimeAssets));

        frontendLoading.ReportProgress(
            2,
            "INITIALIZING PLAYER CONTROLS");
        sessionTransitionRenderer.Publish(
            FrontendPresentationAdapter.Loading(
                frontendLoading.State));
        PumpBootFrame(
            window,
            sessionTransitionRenderer);

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
        var actionMapper = new RtsCameraActionMapper(
            _settings.CameraBindings);
        var camera = new RtsCamera(
            _settings.CreateCameraSettings(
                new Vector3(
                    localStart.Position.X,
                    targetHeight,
                    localStart.Position.Z)));
        var selectionController = new RtsSelectionController(
            new SelectionFilter(
                LocalPlayer,
                ControllableEntityCategory.Unit |
                ControllableEntityCategory.Building |
                ControllableEntityCategory.Logistics));
        var combatGroupRegistry =
            new CombatGroupRegistry();
        var combatGroupInput =
            new CombatGroupInputController();
        var buildingPlacementController =
            new RtsBuildingPlacementController(LocalPlayer);
        var actionPanel =
            new PlayerActionPanelController();
        var hudInteraction =
            new HudInteractionContext();
        var tacticalTargetingController =
            new RtsTacticalTargetingController();
        var informationLayer =
            new RtsInformationLayerController();
        var minimapInteraction =
            new RtsMinimapInteractionController();
        var debugOverlay =
            new DebugOverlayController();
        var gameplayDraw =
            new DebugDraw();
        var debugDraw =
            new DebugDraw();

        frontendLoading.ReportProgress(
            3,
            "STARTING SIMULATION");
        sessionTransitionRenderer.Publish(
            FrontendPresentationAdapter.Loading(
                frontendLoading.State));
        PumpBootFrame(
            window,
            sessionTransitionRenderer);

        using var simulationHost =
            new ClientSimulationHost(
                scenario,
                commandGateway,
                snapshotBuffer);
        _ = renderWorld.Update(
            snapshotBuffer);

        frontendLoading.ReportProgress(
            4,
            "ENTERING BATTLEFIELD");
        sessionTransitionRenderer.Publish(
            FrontendPresentationAdapter.Loading(
                frontendLoading.State));
        PumpBootFrame(
            window,
            sessionTransitionRenderer);
        sessionTransitionRenderer.Dispose();

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
                camera.Settings,
                runtimeAssets,
                _settings.CreateSceneLightingSettings());

        PlayerCommandSubmissionReceipt? lastCommandReceipt = null;
        PlayerCommandResultReadModel? lastCommandResult = null;
        bool overlayEnabled = false;
        bool overlayToggleHeld = false;
        bool debugMasterToggleHeld = false;
        bool debugNavigationToggleHeld = false;
        bool debugWorldToggleHeld = false;
        bool debugLogisticsToggleHeld = false;
        bool debugSensorsToggleHeld = false;
        bool debugCombatToggleHeld = false;
        bool debugEntitiesToggleHeld = false;
        bool formationToggleHeld = false;
        bool strategicOverlayToggleHeld = false;
        bool minimapToggleHeld = false;
        var pauseMenu =
            new PauseMenuModel();
        string saveDirectory =
            ResolveSaveDirectory();
        bool restartHeld = false;
        bool returnHeld = false;
        bool pauseHeld = false;
        bool helpHeld = false;
        bool pauseMenuUpHeld = false;
        bool pauseMenuDownHeld = false;
        bool pauseMenuEnterHeld = false;
        bool pauseMenuPrimaryPointerHeld = false;
        bool pauseMenuActive = false;
        bool pauseControlsVisible = false;
        bool helpVisible = false;
        string pauseMenuFeedback = string.Empty;
        FormationTemplate activeFormation =
            FormationTemplate.Compact;
        bool simulationPaused = false;
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

            if (window.IsOpen &&
                !window.ClientSize.IsEmpty)
            {
                qualificationWindow =
                    CaptureWindowQualification(
                        window);
            }

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
            UpdateDebugOverlayToggles(
                inputState,
                debugOverlay,
                ref debugMasterToggleHeld,
                ref debugNavigationToggleHeld,
                ref debugWorldToggleHeld,
                ref debugLogisticsToggleHeld,
                ref debugSensorsToggleHeld,
                ref debugCombatToggleHeld,
                ref debugEntitiesToggleHeld);
            UpdateFormationSelection(
                inputState,
                ref formationToggleHeld,
                ref activeFormation);

            if (ConsumeKeyPress(
                    inputState,
                    PlatformKey.F10,
                    ref strategicOverlayToggleHeld))
            {
                informationLayer.CycleOverlay();
            }

            if (ConsumeKeyPress(
                    inputState,
                    PlatformKey.F11,
                    ref minimapToggleHeld))
            {
                informationLayer.ToggleMinimap();
            }

            bool helpPressed =
                ConsumeKeyPress(
                    inputState,
                    PlatformKey.F12,
                    ref helpHeld);

            if (!pauseMenuActive &&
                helpPressed)
            {
                helpVisible =
                    !helpVisible;
            }

            bool pausePressed =
                ConsumeKeyPress(
                    inputState,
                    PlatformKey.Space,
                    ref pauseHeld);

            presentationInteraction.SetDebugState(
                debugOverlay.View,
                camera.Target.Y);
            presentationInteraction.SetStrategicOverlay(
                informationLayer.OverlayMode);

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
            hudInteraction.BeginFrame(
                inputSnapshot?.SessionId ??
                SimulationSessionId.None);
            PlayerExperienceSnapshot? inputExperience =
                inputSnapshot?.PlayerExperience;
            bool inputMatchTerminal =
                inputExperience?.IsMatchComplete ==
                true;

            PauseMenuCommand? pauseMenuCommand =
                null;

            if (!inputMatchTerminal &&
                pauseMenuActive &&
                pauseControlsVisible &&
                returnPressed)
            {
                pauseControlsVisible =
                    false;
                pauseMenuFeedback =
                    string.Empty;
            }
            else if (!inputMatchTerminal &&
                     (pausePressed ||
                      returnPressed))
            {
                pauseMenuActive =
                    !pauseMenuActive;
                pauseControlsVisible =
                    false;
                pauseMenuFeedback =
                    string.Empty;
                helpVisible =
                    false;
            }

            if (pauseMenuActive &&
                !inputMatchTerminal)
            {
                FrontendLayout pauseLayout =
                    FrontendDesign.ResolveLayout(
                        window.ClientSize.Width,
                        window.ClientSize.Height,
                        _settings.UiScale);
                bool pointerDown =
                    inputState.IsMouseButtonDown(
                        PlatformMouseButton.Left);
                bool pointerPressed =
                    pointerDown &&
                    !pauseMenuPrimaryPointerHeld;
                pauseMenuPrimaryPointerHeld =
                    pointerDown;

                if (pauseControlsVisible)
                {
                    pauseMenuUpHeld =
                        inputState.IsKeyDown(
                            PlatformKey.Up);
                    pauseMenuDownHeld =
                        inputState.IsKeyDown(
                            PlatformKey.Down);
                    pauseMenuEnterHeld =
                        inputState.IsKeyDown(
                            PlatformKey.Enter);

                    if (pointerPressed &&
                        inputState.HasPointerPosition &&
                        (FrontendHitTesting.SecondaryAction(
                             inputState.PointerPosition.X,
                             inputState.PointerPosition.Y,
                             pauseLayout) ||
                         FrontendHitTesting.Footer(
                             inputState.PointerPosition.X,
                             inputState.PointerPosition.Y,
                             pauseLayout)))
                    {
                        pauseControlsVisible =
                            false;
                        pauseMenuFeedback =
                            string.Empty;
                    }
                }
                else
                {
                    if (inputState.HasPointerPosition)
                    {
                        string? hoveredId =
                            FrontendHitTesting.PauseMenu(
                                inputState.PointerPosition.X,
                                inputState.PointerPosition.Y,
                                pauseLayout,
                                pauseMenu.Items);

                        if (hoveredId is not null)
                        {
                            pauseMenu.TryFocus(
                                hoveredId);
                        }
                    }

                    if (ConsumeKeyPress(
                            inputState,
                            PlatformKey.Up,
                            ref pauseMenuUpHeld))
                    {
                        pauseMenu.MovePrevious();
                    }

                    if (ConsumeKeyPress(
                            inputState,
                            PlatformKey.Down,
                            ref pauseMenuDownHeld))
                    {
                        pauseMenu.MoveNext();
                    }

                    bool activate =
                        ConsumeKeyPress(
                            inputState,
                            PlatformKey.Enter,
                            ref pauseMenuEnterHeld);

                    if (pointerPressed &&
                        inputState.HasPointerPosition &&
                        FrontendHitTesting.PauseMenu(
                            inputState.PointerPosition.X,
                            inputState.PointerPosition.Y,
                            pauseLayout,
                            pauseMenu.Items) is string clickedId)
                    {
                        pauseMenu.TryFocus(
                            clickedId);
                        activate =
                            true;
                    }

                    if (activate)
                    {
                        pauseMenuCommand =
                            pauseMenu.ActivateFocused();

                        if (pauseMenuCommand ==
                            PauseMenuCommand.Resume)
                        {
                            pauseMenuActive =
                                false;
                            pauseControlsVisible =
                                false;
                            pauseMenuFeedback =
                                string.Empty;
                        }
                        else if (pauseMenuCommand ==
                                 PauseMenuCommand.Controls)
                        {
                            pauseControlsVisible =
                                true;
                            pauseMenuFeedback =
                                string.Empty;
                            pauseMenuCommand =
                                null;
                        }
                    }
                }
            }
            else
            {
                pauseControlsVisible =
                    false;
                pauseMenuUpHeld =
                    inputState.IsKeyDown(
                        PlatformKey.Up);
                pauseMenuDownHeld =
                    inputState.IsKeyDown(
                        PlatformKey.Down);
                pauseMenuEnterHeld =
                    inputState.IsKeyDown(
                        PlatformKey.Enter);
                pauseMenuPrimaryPointerHeld =
                    inputState.IsMouseButtonDown(
                        PlatformMouseButton.Left);
            }

            PreAlphaUxView preAlphaUx =
                CreatePreAlphaUxView(
                    false,
                    false,
                    helpVisible,
                    _settings.ShowOnboarding &&
                    !smokeTest);

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
            bool shellBlocksGameplay =
                pauseMenuActive ||
                helpVisible;
            bool shouldPauseSimulation =
                !inputMatchTerminal &&
                (shouldPauseForWindow ||
                 shellBlocksGameplay);

            if (shouldPauseSimulation !=
                simulationPaused)
            {
                if (!simulationHost.TrySetPaused(
                        shouldPauseSimulation))
                {
                    throw new InvalidOperationException(
                        "Simulation control boundary is full while changing the pause state.");
                }

                simulationPaused =
                    shouldPauseSimulation;
            }

            if (pauseMenuCommand ==
                PauseMenuCommand.ReturnToMenu)
            {
                return RestartRequestedExitCode;
            }

            if (pauseMenuCommand is
                    PauseMenuCommand.SaveGame or
                    PauseMenuCommand.SaveAndReturnToMenu)
            {
                if (!simulationHost.WaitForPauseState(
                        paused: true,
                        timeout:
                            PauseTransitionTimeout))
                {
                    pauseMenuFeedback =
                        "SAVE FAILED - PAUSE TIMEOUT";
                }
                else if (TrySaveCurrentMatch(
                             scenario,
                             saveDirectory,
                             out pauseMenuFeedback) &&
                         pauseMenuCommand ==
                             PauseMenuCommand.SaveAndReturnToMenu)
                {
                    return RestartRequestedExitCode;
                }
            }

            if (shouldPauseForWindow ||
                shellBlocksGameplay)
            {
                actionPanel.Close();
                tacticalTargetingController.Cancel();
                debugDraw.Clear();

                FrontendSurfaceView? frontendSurface =
                    null;

                if (pauseMenuActive)
                {
                    if (pauseControlsVisible)
                    {
                        FrontendSurfaceView controlsSurface =
                            FrontendPresentationAdapter.Controls(
                                _settings.CameraBindings,
                                "ESC  BACK TO PAUSE");
                        FrontendLayout pauseLayout =
                            FrontendDesign.ResolveLayout(
                                window.ClientSize.Width,
                                window.ClientSize.Height,
                                _settings.UiScale);
                        bool secondaryHovered =
                            inputState.HasPointerPosition &&
                            FrontendHitTesting.SecondaryAction(
                                inputState.PointerPosition.X,
                                inputState.PointerPosition.Y,
                                pauseLayout);

                        frontendSurface =
                            controlsSurface.WithInteraction(
                                string.Empty,
                                1f,
                                false,
                                false,
                                secondaryHovered,
                                secondaryHovered &&
                                inputState.IsMouseButtonDown(
                                    PlatformMouseButton.Left));
                    }
                    else
                    {
                        FrontendSurfaceView pauseSurface =
                            FrontendPresentationAdapter.PauseMenu(
                                pauseMenu);
                        frontendSurface =
                            pauseSurface.WithInteraction(
                                pauseMenuFeedback,
                                1f,
                                false,
                                false,
                                false,
                                false);
                    }
                }

                _ = renderHost.Publish(
                    new ClientRenderFrame(
                        camera.CaptureState(),
                        window.ClientSize.Width,
                        window.ClientSize.Height,
                        overlayEnabled,
                        debugOverlay.View,
                        actionPanel.CreateView(
                            window.ClientSize.Width,
                            window.ClientSize.Height,
                            inputSnapshot?.PlayerActions,
                            window.Dpi,
                            _settings.UiScale),
                        tacticalTargetingController.CreateView(
                            inputSnapshot),
                        activeFormation,
                        [],
                        [],
                        [],
                        [],
                        window.Dpi,
                        default,
                        _settings.UiScale,
                        preAlphaUx,
                        frontendSurface));

                if (shouldPauseForWindow)
                {
                    _platform.WaitForEvents(
                        IdleWait);
                }

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
                    window.ClientSize.Height,
                    window.Dpi,
                    _settings.UiScale);

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
                        simulationHost,
                        buildingPlacementController,
                        tacticalTargetingController,
                        presentationInteraction,
                        inputSnapshot?.Tick ??
                            SimulationTick.Zero);
                }
            }

            hudInteraction.CapturePointer(
                actionPanel.PointerCaptured);
            hudInteraction.CaptureKeyboard(
                actionPanel.HasKeyboardFocus);

            GameplayHudLayout interactionLayout =
                GameplayHudLayout.Create(
                    window.ClientSize.Width,
                    window.ClientSize.Height,
                    window.Dpi,
                    _settings.UiScale);
            minimapInteraction.Update(
                inputState,
                camera,
                terrainWorld,
                inputSnapshot,
                interactionLayout,
                selectionController.Selection.Entities,
                tacticalTargetingController.Mode,
                activeFormation,
                minimapEnabled:
                    informationLayer.MinimapEnabled,
                inputBlocked:
                    hudInteraction.PointerCaptured ||
                    hudInteraction.KeyboardCaptured);
            hudInteraction.CapturePointer(
                minimapInteraction.PointerCaptured);

            if (minimapInteraction.TryTakeMovementRequest(
                    out MovementOrderRequest minimapMovement))
            {
                EntityId[] movementEntities =
                    minimapMovement.Entities.ToArray();
                SimulationTick observedTick =
                    inputSnapshot?.Tick ??
                    SimulationTick.Zero;

                RequireSubmission(
                    simulationHost,
                    gateway =>
                        gateway.SubmitMovement(
                            LocalPlayer,
                            movementEntities,
                            minimapMovement.WorldTarget,
                            observedTick,
                            activeFormation));
            }

            if (minimapInteraction.TryTakeActionRequest(
                    out PlayerActionRequest minimapAction))
            {
                DispatchPlayerActionRequest(
                    minimapAction,
                    simulationHost,
                    buildingPlacementController,
                    tacticalTargetingController,
                    presentationInteraction,
                    inputSnapshot?.Tick ??
                        SimulationTick.Zero);
                tacticalTargetingController.Cancel();
            }

            if (minimapInteraction.TryTakeOverlaySelection(
                    out StrategicOverlayMode selectedOverlay))
            {
                informationLayer.SetOverlay(
                    selectedOverlay);
                presentationInteraction.SetStrategicOverlay(
                    selectedOverlay);
            }

            if (!hudInteraction.KeyboardCaptured)
            {
                RtsCameraInputFrame cameraInput =
                    actionMapper.Map(inputState);

                if (hudInteraction.PointerCaptured)
                {
                    cameraInput =
                        cameraInput with
                        {
                            DragPan = false,
                            HasPointerPosition = false,
                            PointerDelta =
                                Vector2.Zero
                        };
                }

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

            if (smokeTest &&
                !smokeCompletionRequested)
            {
                if (!simulationHost.TryRunSmokeCompletion(
                        eastBase.CommandCore))
                {
                    throw new InvalidOperationException(
                        "Simulation control boundary is full while scheduling smoke completion.");
                }

                smokeCompletionRequested = true;
            }

            if (smokeTest &&
                smokeCompletionRequested &&
                !smokeMatchCompleted &&
                currentExperience?.MatchStatus ==
                    PlayerMatchStatus.Victory)
            {
                smokeMatchCompleted = true;
            }

            bool gameplayActive =
                currentExperience?.MatchStatus ==
                PlayerMatchStatus.Active;

            CombatGroupInputResult combatGroupInputResult =
                combatGroupInput.Update(
                    inputState,
                    currentSnapshot,
                    combatGroupRegistry,
                    selectionController.Selection,
                    inputBlocked:
                        !gameplayActive ||
                        hudInteraction.KeyboardCaptured ||
                        buildingPlacementController.IsActive ||
                        tacticalTargetingController.IsActive);
            hudInteraction.CaptureKeyboard(
                combatGroupInputResult.Handled);

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
                    hudInteraction.PointerCaptured);

                hudInteraction.CapturePointer(
                    tacticalTargetingController.PointerCaptured);

                if (tacticalTargetingController.TryTakeRequest(
                        out PlayerActionRequest tacticalRequest))
                {
                    DispatchPlayerActionRequest(
                        tacticalRequest,
                        simulationHost,
                        buildingPlacementController,
                        tacticalTargetingController,
                        presentationInteraction,
                        currentSnapshot?.Tick ??
                            SimulationTick.Zero);
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
                    hudInteraction.PointerCaptured);
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
                        hudInteraction.PointerCaptured);

                    if (selectionController.TryTakeMovementRequest(
                            out MovementOrderRequest movementRequest))
                    {
                        EntityId[] movementEntities =
                            movementRequest.Entities.ToArray();
                        SimulationTick observedTick =
                            currentSnapshot?.Tick ??
                            SimulationTick.Zero;

                        RequireSubmission(
                            simulationHost,
                            gateway =>
                                gateway.SubmitMovement(
                                    LocalPlayer,
                                    movementEntities,
                                    movementRequest.WorldTarget,
                                    observedTick,
                                    activeFormation));
                    }
                }

                presentationInteraction.SetSelection(
                    selectionController.Selection.Entities);

                if (buildingPlacementController.TryTakePlacementRequest(
                        out BuildingPlacementRequest placementRequest))
                {
                    SimulationTick observedTick =
                        currentSnapshot?.Tick ??
                        SimulationTick.Zero;

                    RequireSubmission(
                        simulationHost,
                        gateway =>
                            gateway.SubmitBuild(
                                LocalPlayer,
                                placementRequest.BuildingId,
                                placementRequest.Position,
                                placementRequest.Orientation,
                                constructionInventory,
                                observedTick));
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

            TacticalTargetingView tacticalTargetingView =
                tacticalTargetingController.CreateView(
                    currentSnapshot);

            BuildGameplayWorldOverlay(
                gameplayDraw,
                renderWorld,
                renderAlpha,
                camera,
                selectionController,
                buildingPlacementController,
                tacticalTargetingView,
                informationLayer.OverlayMode,
                currentSnapshot?.StrategicOverlay);

            BuildDevelopmentDebugOverlay(
                debugDraw,
                debugOverlay.View,
                prototypeBattlefield,
                renderWorld,
                renderAlpha,
                camera,
                currentSnapshot?.Construction,
                currentSnapshot?.Debug);

            CombatGroupOverviewView combatGroupOverview =
                CombatGroupOverviewModel.Create(
                    combatGroupRegistry,
                    currentSnapshot?.CombatGroups,
                    selectionController.Selection);

            PlayerActionPanelView actionPanelView =
                actionPanel.CreateView(
                    window.ClientSize.Width,
                    window.ClientSize.Height,
                    currentSnapshot?.PlayerActions,
                    window.Dpi,
                    _settings.UiScale);
            bool placementValid =
                buildingPlacementController.PreviewFreshness ==
                    PlacementPreviewFreshness.Current &&
                buildingPlacementController.Preview?.IsValid ==
                    true;
            RtsCursorKind cursor =
                minimapInteraction.PointerCaptured
                    ? minimapInteraction.View.Cursor
                    : RtsCursorResolver.Resolve(
                        new RtsCursorContext(
                        inputState.HasPointerPosition,
                        selectionController.IsDragSelecting,
                        inputState.IsMouseButtonDown(
                            PlatformMouseButton.Middle),
                        buildingPlacementController.IsActive,
                        placementValid,
                        tacticalTargetingView.Mode,
                        tacticalTargetingView.PointerTargetValid,
                        selectionController.HoveredEntity.IsValid,
                        selectionController.Selection.Count > 0,
                        actionPanel.Mode ==
                            PlayerActionPanelMode.Supply));
            var informationView =
                new RtsInformationLayerView(
                    informationLayer.MinimapEnabled,
                    informationLayer.OverlayMode,
                    cursor,
                    inputState.HasPointerPosition,
                    inputState.PointerPosition,
                    selectionController.IsDragSelecting,
                    selectionController.DragStart,
                    selectionController.DragCurrent,
                    selectionController.Selection.Entities.ToArray(),
                    MinimapPointerCaptured:
                        minimapInteraction.View.PointerCaptured,
                    MinimapPointerWorldValid:
                        minimapInteraction.View.PointerWorldValid,
                    MinimapPointerWorldTarget:
                        minimapInteraction.View.PointerWorldTarget,
                    MinimapCameraDragging:
                        minimapInteraction.View.IsCameraDragging);

            _ = renderHost.Publish(
                new ClientRenderFrame(
                    camera.CaptureState(),
                    window.ClientSize.Width,
                    window.ClientSize.Height,
                    overlayEnabled,
                    debugOverlay.View,
                    actionPanelView,
                    tacticalTargetingView,
                    activeFormation,
                    gameplayDraw.Lines.ToArray(),
                    gameplayDraw.Labels.ToArray(),
                    debugDraw.Lines.ToArray(),
                    debugDraw.Labels.ToArray(),
                    window.Dpi,
                    informationView,
                    _settings.UiScale,
                    preAlphaUx,
                    SurfaceSuspended:
                        window.IsMinimized,
                    CombatGroups:
                        combatGroupOverview));

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

        if (!string.IsNullOrWhiteSpace(
                visualQualificationOutput))
        {
            WriteVisualQualificationReport(
                visualQualificationOutput,
                renderInstanceCount,
                qualificationWindow,
                renderHost.LatestQualification);
        }

        return 0;
    }

    private void WriteVisualQualificationReport(
        string outputPath,
        int renderStressInstances,
        in ClientWindowQualificationSnapshot window,
        ClientVisualQualificationSnapshot? qualification)
    {
        ClientVisualQualificationSnapshot metrics =
            qualification ??
            throw new InvalidOperationException(
                "No completed render frame was available for visual qualification.");

        string fullPath =
            Path.GetFullPath(
                outputPath);
        string? directory =
            Path.GetDirectoryName(
                fullPath);

        if (!string.IsNullOrWhiteSpace(
                directory))
        {
            Directory.CreateDirectory(
                directory);
        }

        var report =
            new
            {
                scene =
                    "vertical-slice-client",
                renderStressInstances,
                settings =
                    new
                    {
                        path =
                            _settingsPath,
                        requestedMode =
                            _settings.BorderlessFullscreen
                                ? WindowMode.BorderlessFullscreen.ToString()
                                : WindowMode.Windowed.ToString(),
                        _settings.WindowWidth,
                        _settings.WindowHeight
                    },
                window =
                    new
                    {
                        mode =
                            window.Mode.ToString(),
                        window.ClientWidth,
                        window.ClientHeight,
                        window.IsMinimized,
                        window.Dpi
                    },
                metrics
            };

        File.WriteAllText(
            fullPath,
            JsonSerializer.Serialize(
                report,
                VisualQualificationJsonOptions));

        Console.WriteLine(
            $"[render:qualification] output=\"{fullPath}\" " +
            $"fps={metrics.FramesPerSecond:F1} " +
            $"frameMs={metrics.FrameMilliseconds:F3} " +
            $"cpuRenderMs={metrics.CpuRenderMilliseconds:F3} " +
            $"gpuMs={(metrics.GpuMilliseconds?.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) ?? "unavailable")} " +
            $"gpuTiming={metrics.GpuTimingAvailable} " +
            $"debugLayer={metrics.DebugLayerEnabled} " +
            $"draws={metrics.TotalMeasuredDrawCalls} " +
            $"instances={metrics.VisibleInstances}/{metrics.TotalInstances} " +
            $"lod={metrics.HighLodInstances}/{metrics.ReducedLodInstances} " +
            $"vfx={metrics.ActiveVfxEffects}/{metrics.VfxPoolCapacity} " +
            $"textures={metrics.LoadedTextureCount} " +
            $"textureBytes={metrics.ResidentTextureBytes} " +
            $"descriptors={metrics.ShaderResourceDescriptorsUsed}/{metrics.ShaderResourceDescriptorCapacity} " +
            $"descriptorPeak={metrics.PeakShaderResourceDescriptorsUsed} " +
            $"texturePeak={metrics.PeakLoadedTextureCount}/{metrics.PeakResidentTextureBytes} " +
            $"textureTransfers={metrics.TextureUploadCount}/{metrics.TextureReleaseCount} " +
            $"materials={metrics.LoadedMaterialCount} " +
            $"materialTextures={metrics.LoadedMaterialAssetTextureCount} " +
            $"bindingFailures={metrics.TextureBindingFailureCount + metrics.MaterialBindingFailureCount} " +
            $"lighting={metrics.LightingDirectionalIntensity:F2}/{metrics.LightingAmbientIntensity:F2} " +
            $"exposure={metrics.LightingExposure:F2} " +
            $"tone={metrics.LightingToneMapping} " +
            $"windowMode={window.Mode} " +
            $"surface={metrics.Surface.Width}x{metrics.Surface.Height} " +
            $"submitted={metrics.Surface.SubmittedFrameCount} " +
            $"presented={metrics.Surface.PresentedFrameCount} " +
            $"pendingResize={metrics.Surface.ResizePending} " +
            $"occluded={metrics.Surface.IsOccluded}");
    }

    private static ClientWindowQualificationSnapshot CaptureWindowQualification(
        IWindow window)
    {
        ArgumentNullException.ThrowIfNull(
            window);

        return new ClientWindowQualificationSnapshot(
            window.Mode,
            window.ClientSize.Width,
            window.ClientSize.Height,
            window.IsMinimized,
            window.Dpi);
    }

    private static void DispatchPlayerActionRequest(
        in PlayerActionRequest request,
        ClientSimulationHost simulationHost,
        RtsBuildingPlacementController buildingPlacementController,
        RtsTacticalTargetingController tacticalTargetingController,
        PresentationInteractionState presentationInteraction,
        SimulationTick observedTick)
    {
        switch (request.Kind)
        {
            case PlayerActionRequestKind.BeginBuildingPlacement:
                tacticalTargetingController.Cancel();
                buildingPlacementController.SelectBuilding(
                    request.BuildingId);
                return;

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
                    simulationHost.SessionId);
                return;
        }

        PlayerActionRequest transported =
            request with
            {
                TacticalEntities =
                    request.TacticalEntities?.ToArray()
            };

        RequireSubmission(
            simulationHost,
            gateway =>
            {
                if (!PlayerActionRequestDispatcher.TryDispatch(
                        transported,
                        LocalPlayer,
                        gateway,
                        observedTick,
                        out PlayerCommandSubmissionReceipt receipt))
                {
                    throw new InvalidOperationException(
                        $"Unsupported simulation player action '{transported.Kind}'.");
                }

                return receipt;
            });
    }

    private static void RequireSubmission(
        ClientSimulationHost simulationHost,
        Func<PlayerCommandGateway, PlayerCommandSubmissionReceipt> submission)
    {
        if (!simulationHost.TrySubmit(
                simulationHost.SessionId,
                submission))
        {
            throw new InvalidOperationException(
                "Simulation command boundary is full or stopping.");
        }
    }

    private static void DrainSubmissionCompletions(
        ClientSimulationHost simulationHost,
        ref PlayerCommandSubmissionReceipt? lastCommandReceipt)
    {
        while (simulationHost.TryReadSubmissionCompletion(
                   out ClientSubmissionCompletion completion))
        {
            if (completion.Receipt is
                PlayerCommandSubmissionReceipt receipt)
            {
                lastCommandReceipt =
                    receipt;
                continue;
            }

            if (completion.Failure !=
                ClientSubmissionFailure.None)
            {
                Console.Error.WriteLine(
                    $"[simulation:submission-rejected] sequence={completion.HostSequence} " +
                    $"reason={completion.Failure}");
            }
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

    private static void BuildGameplayWorldOverlay(
        DebugDraw gameplayDraw,
        RenderWorld renderWorld,
        float alpha,
        RtsCamera camera,
        RtsSelectionController selectionController,
        RtsBuildingPlacementController buildingPlacementController,
        TacticalTargetingView tacticalTargeting,
        StrategicOverlayMode strategicOverlayMode,
        StrategicOverlaySnapshot? strategicOverlaySnapshot)
    {
        gameplayDraw.Clear();

        bool interactionFeedback =
            selectionController.Selection.Count > 0 ||
            selectionController.InspectedEntity.IsValid ||
            selectionController.HoveredEntity.IsValid ||
            buildingPlacementController.IsActive ||
            tacticalTargeting.HasPointerTarget ||
            strategicOverlayMode !=
                StrategicOverlayMode.None;
        gameplayDraw.Enabled =
            interactionFeedback;

        if (!gameplayDraw.Enabled)
        {
            return;
        }

        Vector4 selectedColor =
            new(
                0.25f,
                1.0f,
                0.35f,
                1.0f);
        Vector4 hoveredColor =
            new(
                0.15f,
                0.85f,
                1.0f,
                1.0f);
        Vector4 placementValidColor =
            new(
                0.15f,
                1.0f,
                0.35f,
                1.0f);
        Vector4 placementInvalidColor =
            new(
                1.0f,
                0.2f,
                0.15f,
                1.0f);

        if (buildingPlacementController.Preview is
            BuildingPlacementPreview placementPreview)
        {
            BuildingConstructionDebugVisualization.DrawPreview(
                gameplayDraw,
                placementPreview,
                placementValidColor,
                placementInvalidColor);
        }

        if (strategicOverlayMode !=
                StrategicOverlayMode.None &&
            strategicOverlaySnapshot is not null)
        {
            RtsStrategicOverlayVisualization.Draw(
                gameplayDraw,
                strategicOverlayMode,
                strategicOverlaySnapshot);
        }

        if (tacticalTargeting.HasPointerTarget)
        {
            RtsWorldMarkerVisualization.DrawTarget(
                gameplayDraw,
                tacticalTargeting.PointerWorldTarget,
                tacticalTargeting.PointerTargetValid,
                new Vector4(
                    1.0f,
                    0.72f,
                    0.18f,
                    1.0f),
                new Vector4(
                    1.0f,
                    0.18f,
                    0.12f,
                    1.0f));
        }

        foreach (EntityId entity in
                 selectionController.Selection.Entities)
        {
            if (!renderWorld.TryGetInterpolatedInstance(
                    entity,
                    alpha,
                    out RenderInstance instance))
            {
                continue;
            }

            RtsWorldMarkerVisualization.DrawSelected(
                gameplayDraw,
                instance,
                selectedColor);
        }

        EntityId inspected =
            selectionController.InspectedEntity;
        if (inspected.IsValid &&
            !selectionController.Selection.Contains(
                inspected) &&
            renderWorld.TryGetInterpolatedInstance(
                inspected,
                alpha,
                out RenderInstance inspectedInstance))
        {
            RtsWorldMarkerVisualization.DrawHover(
                gameplayDraw,
                inspectedInstance,
                selectedColor);
        }

        EntityId hovered =
            selectionController.HoveredEntity;
        if (hovered.IsValid &&
            !selectionController.Selection.Contains(
                hovered) &&
            renderWorld.TryGetInterpolatedInstance(
                hovered,
                alpha,
                out RenderInstance hoveredInstance))
        {
            RtsWorldMarkerVisualization.DrawHover(
                gameplayDraw,
                hoveredInstance,
                hoveredColor);
        }
    }

    private static void BuildDevelopmentDebugOverlay(
        DebugDraw debugDraw,
        in DebugOverlayView overlay,
        PrototypeBattlefieldDefinition prototypeBattlefield,
        RenderWorld renderWorld,
        float alpha,
        RtsCamera camera,
        BuildingConstructionDebugSnapshot? constructionSnapshot,
        PresentationDebugSnapshot? debugSnapshot)
    {
        debugDraw.Clear();
        debugDraw.Enabled =
            overlay.Enabled;

        if (!debugDraw.Enabled)
        {
            return;
        }

        Vector4 rangeColor =
            new(
                0.2f,
                0.75f,
                1.0f,
                1.0f);
        Vector4 boundsColor =
            new(
                1.0f,
                0.72f,
                0.18f,
                1.0f);
        Vector4 pointColor =
            new(
                1.0f,
                0.25f,
                0.18f,
                1.0f);

        if (overlay.IsEnabled(
                DebugOverlayCategory.Rendering))
        {
            debugDraw.Point(
                camera.Target,
                8.0f,
                pointColor);
        }

        if (debugSnapshot is null)
        {
            if (overlay.IsEnabled(
                    DebugOverlayCategory.Entities))
            {
                DrawEntityBounds(
                    debugDraw,
                    renderWorld,
                    alpha,
                    boundsColor);
            }

            return;
        }

        if (overlay.IsEnabled(
                DebugOverlayCategory.Navigation))
        {
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
        }

        if (overlay.IsEnabled(
                DebugOverlayCategory.World))
        {
            if (constructionSnapshot is not null)
            {
                BuildingConstructionDebugVisualization.DrawConstructionSites(
                    debugDraw,
                    constructionSnapshot,
                    new Vector4(
                        1.0f,
                        0.75f,
                        0.2f,
                        1.0f),
                    new Vector4(
                        0.2f,
                        0.9f,
                        0.35f,
                        1.0f),
                    maximumCompleted: 64,
                    maximumLabels: 32);
            }

            if (debugSnapshot.Resources is not null)
            {
                ResourceDepositDebugVisualization.DrawDeposits(
                    debugDraw,
                    debugSnapshot.Resources,
                    new Vector4(
                        0.65f,
                        0.9f,
                        0.25f,
                        1.0f),
                    new Vector4(
                        0.35f,
                        0.35f,
                        0.35f,
                        1.0f),
                    maximumDeposits: 64,
                    maximumLabels: 8);
            }

            PrototypeBattlefieldDebugVisualization.Draw(
                debugDraw,
                prototypeBattlefield,
                debugSnapshot.CrossingStates);
        }

        if (overlay.IsEnabled(
                DebugOverlayCategory.Logistics))
        {
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
        }

        if (overlay.IsEnabled(
                DebugOverlayCategory.Sensors) &&
            renderWorld.CurrentSnapshot?.Intelligence is
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
                camera.Target +
                Vector3.UnitY *
                    6.0f);
        }

        if (overlay.IsEnabled(
                DebugOverlayCategory.Combat))
        {
            if (debugSnapshot.Artillery is not null)
            {
                ArtilleryDebugVisualization.Draw(
                    debugDraw,
                    debugSnapshot.Artillery,
                    camera.Target +
                    Vector3.UnitY *
                        9.0f,
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
                    camera.Target +
                    Vector3.UnitY *
                        13.0f,
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
        }

        if (overlay.IsEnabled(
                DebugOverlayCategory.Entities))
        {
            SpatialIndexDebugVisualization.DrawRadiusQuery(
                debugDraw,
                camera.Target,
                40.0f,
                rangeColor);
            SpatialIndexDebugVisualization.DrawOccupiedCells(
                debugDraw,
                debugSnapshot.Spatial,
                new Vector4(
                    0.35f,
                    0.65f,
                    1.0f,
                    0.8f),
                new Vector4(
                    1.0f,
                    0.35f,
                    0.15f,
                    1.0f),
                maximumCells: 256,
                maximumLabels: MaximumDebugLabels);
            SkirmishOpponentDebugVisualization.Draw(
                debugDraw,
                debugSnapshot.Opponents,
                MaximumDebugLabels);
            DrawEntityBounds(
                debugDraw,
                renderWorld,
                alpha,
                boundsColor);
        }
    }

    private static void DrawEntityBounds(
        DebugDraw debugDraw,
        RenderWorld renderWorld,
        float alpha,
        Vector4 color)
    {
        int debugCount =
            Math.Min(
                renderWorld.InstanceCount,
                MaximumDebugInstanceBoxes);

        for (int index = 0;
             index < debugCount;
             index++)
        {
            RenderInstance instance =
                renderWorld.GetInterpolatedInstance(
                    index,
                    alpha);
            DrawInstanceBounds(
                debugDraw,
                instance,
                color,
                index <
                    MaximumDebugLabels
                    ? $"E{instance.Entity.Index}"
                    : null);
        }
    }

    private static void DrawInstanceBounds(
        DebugDraw debugDraw,
        in RenderInstance instance,
        Vector4 color,
        string? label)
    {
        AxisAlignedBounds bounds =
            PresentationBounds.ResolveAxisAlignedBounds(
                instance);
        Vector3 extents =
            (bounds.Maximum -
             bounds.Minimum) *
            0.5f;

        debugDraw.Box(
            bounds,
            color);

        if (label is not null)
        {
            debugDraw.Label(
                instance.Transform.Position +
                new Vector3(
                    0.0f,
                    extents.Y +
                    2.0f,
                    0.0f),
                label,
                color);
        }
    }

    private PreAlphaUxView CreatePreAlphaUxView(
        bool matchSetupActive,
        bool userPaused,
        bool helpVisible,
        bool showOnboarding)
    {
        PreAlphaUxMode mode =
            helpVisible
                ? PreAlphaUxMode.Help
                : matchSetupActive
                    ? PreAlphaUxMode.MatchSetup
                    : userPaused
                        ? PreAlphaUxMode.Paused
                        : PreAlphaUxMode.None;
        RtsCameraBindings bindings =
            _settings.CameraBindings;

        return new PreAlphaUxView(
            mode,
            showOnboarding,
            "Central Divide",
            "Directorate",
            "Directorate AI",
            bindings.PanForward.ToString(),
            bindings.PanBackward.ToString(),
            bindings.PanLeft.ToString(),
            bindings.PanRight.ToString(),
            bindings.RotateLeft.ToString(),
            bindings.RotateRight.ToString(),
            bindings.PitchUp.ToString(),
            bindings.PitchDown.ToString(),
            bindings.DragPanButton.ToString(),
            _settingsPath);
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

    private static void UpdateDebugOverlayToggles(
        InputState inputState,
        DebugOverlayController overlay,
        ref bool masterHeld,
        ref bool navigationHeld,
        ref bool worldHeld,
        ref bool logisticsHeld,
        ref bool sensorsHeld,
        ref bool combatHeld,
        ref bool entitiesHeld)
    {
        bool shiftDown =
            inputState.IsKeyDown(
                PlatformKey.LeftShift) ||
            inputState.IsKeyDown(
                PlatformKey.RightShift);

        if (ConsumeKeyPress(
                inputState,
                PlatformKey.F2,
                ref masterHeld))
        {
            if (shiftDown)
            {
                overlay.ToggleCategory(
                    DebugOverlayCategory.Rendering);
            }
            else
            {
                overlay.ToggleMaster();
            }
        }

        ToggleDebugCategory(
            inputState,
            PlatformKey.F4,
            DebugOverlayCategory.Navigation,
            overlay,
            modifierActive:
                shiftDown,
            ref navigationHeld);
        ToggleDebugCategory(
            inputState,
            PlatformKey.F5,
            DebugOverlayCategory.World,
            overlay,
            modifierActive:
                shiftDown,
            ref worldHeld);
        ToggleDebugCategory(
            inputState,
            PlatformKey.F6,
            DebugOverlayCategory.Logistics,
            overlay,
            modifierActive:
                shiftDown,
            ref logisticsHeld);
        ToggleDebugCategory(
            inputState,
            PlatformKey.F7,
            DebugOverlayCategory.Sensors,
            overlay,
            modifierActive:
                shiftDown,
            ref sensorsHeld);
        ToggleDebugCategory(
            inputState,
            PlatformKey.F8,
            DebugOverlayCategory.Combat,
            overlay,
            modifierActive:
                shiftDown,
            ref combatHeld);
        ToggleDebugCategory(
            inputState,
            PlatformKey.F9,
            DebugOverlayCategory.Entities,
            overlay,
            modifierActive:
                shiftDown,
            ref entitiesHeld);
    }

    private static void ToggleDebugCategory(
        InputState inputState,
        PlatformKey key,
        DebugOverlayCategory category,
        DebugOverlayController overlay,
        bool modifierActive,
        ref bool held)
    {
        bool pressed =
            ConsumeKeyPress(
                inputState,
                key,
                ref held);

        if (pressed &&
            modifierActive)
        {
            overlay.ToggleCategory(
                category);
        }
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
        IWindow window)
    {
        while (window.TryDequeueEvent(
                   out WindowEvent windowEvent))
        {
            Console.WriteLine(
                $"[platform:event] kind={windowEvent.Kind} " +
                $"size={windowEvent.ClientSize.Width}x{windowEvent.ClientSize.Height} " +
                $"dpi={windowEvent.Dpi} focused={windowEvent.IsFocused} " +
                $"minimized={windowEvent.IsMinimized} mode={windowEvent.Mode}");
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

    private static string ResolveSaveDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "FORGELINE",
            "Saves");

    private static bool TrySaveCurrentMatch(
        VerticalSliceScenario scenario,
        string saveDirectory,
        out string feedback)
    {
        ArgumentNullException.ThrowIfNull(
            scenario);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            saveDirectory);

        try
        {
            Directory.CreateDirectory(
                saveDirectory);

            MatchSaveData save =
                MatchPersistenceService.CaptureSave(
                    scenario);
            string timestamp =
                DateTimeOffset.UtcNow.ToString(
                    "yyyyMMdd-HHmmss-fff",
                    System.Globalization.CultureInfo.InvariantCulture);
            string tick =
                save.SavedTick.ToString(
                    "D8",
                    System.Globalization.CultureInfo.InvariantCulture);
            string path =
                Path.Combine(
                    saveDirectory,
                    $"manual-{timestamp}-tick-{tick}.save.json");

            MatchPersistenceSerializer.WriteSave(
                path,
                save);

            feedback =
                "GAME SAVED";
            Console.WriteLine(
                $"[save:written] tick={save.SavedTick} path=\"{path}\"");
            return true;
        }
        catch (Exception exception)
            when (exception is
                      MatchPersistenceException or
                      IOException or
                      UnauthorizedAccessException)
        {
            feedback =
                "SAVE FAILED";
            Console.Error.WriteLine(
                $"[save:error] type={exception.GetType().Name} message={exception.Message}");
            return false;
        }
    }

    private static RuntimeAssetCatalog LoadRuntimeAssets()
    {
        RuntimeAssetDevelopmentBootstrap.EnsureAvailable();

        RuntimeAssetPathResolution resolution =
            RuntimeAssetPathResolver.Resolve();

        if (!resolution.Found)
        {
            throw new InvalidOperationException(
                "Production runtime assets are unavailable. " +
                $"Expected '{RuntimeAssetCatalog.ManifestFileName}' in one of: " +
                string.Join(
                    "; ",
                    resolution.CandidateRoots));
        }

        string runtimeRoot =
            resolution.RuntimeRoot!;
        RuntimeAssetCatalog catalog =
            RuntimeAssetCatalog.Load(
                runtimeRoot);

        ValidateProductionRuntimeAsset(
            catalog,
            "building.directorate.command_core",
            RuntimeAssetType.Mesh);
        ValidateProductionRuntimeAsset(
            catalog,
            "unit.directorate.main_battle_tank",
            RuntimeAssetType.Mesh);
        ValidateProductionRuntimeAsset(
            catalog,
            "material.world.terrain.grass_ground",
            RuntimeAssetType.Material);
        ValidateProductionRuntimeAsset(
            catalog,
            "texture.world.terrain.rocky_scrub",
            RuntimeAssetType.Texture);
        ValidateProductionRuntimeAsset(
            catalog,
            "texture.directorate.material.painted_metal_base",
            RuntimeAssetType.Texture);

        Console.WriteLine(
            $"[assets:runtime] manifest=loaded assets={catalog.AssetIds.Count} root=\"{runtimeRoot}\" production=validated");
        return catalog;
    }

    private static void ValidateProductionRuntimeAsset(
        RuntimeAssetCatalog catalog,
        string rawAssetId,
        RuntimeAssetType expectedType)
    {
        AssetId assetId =
            AssetId.Parse(
                rawAssetId);
        RuntimeAssetRecord record =
            catalog.Get(
                assetId);

        if (record.Type != expectedType)
        {
            throw new InvalidDataException(
                $"Production runtime asset '{assetId}' has type {record.Type}; expected {expectedType}.");
        }

        RuntimeAssetContent content =
            catalog.Read(
                assetId);

        switch (expectedType)
        {
            case RuntimeAssetType.Mesh:
                _ = RuntimeMeshData.FromPayload(
                    content.Payload);
                break;
            case RuntimeAssetType.Material:
                _ = RuntimeMaterialData.FromPayload(
                    content.Payload);
                break;
            case RuntimeAssetType.Texture:
                _ = RuntimeTextureData.FromPayload(
                    content.Payload);
                break;
            default:
                throw new InvalidDataException(
                    $"Production runtime asset '{assetId}' uses unsupported startup validation type {expectedType}.");
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

    private FrontendSessionSelectionResult RunFrontendSessionSelection(
        IWindow window,
        GameFrontendShell shell,
        NewGameModel newGame)
    {
        shell.Dispatch(
            GameFrontendAction.LoadingCompleted);

        string saveDirectory =
            ResolveSaveDirectory();
        var loadGame =
            new LoadGameModel(
                ClientSaveCatalog.Discover(
                    saveDirectory));
        var settingsInteraction =
            new SettingsInteractionModel();
        var settings =
            new SettingsModel(
                new FrontendSettingsSnapshot(
                    _settings.WindowWidth,
                    _settings.WindowHeight,
                    _settings.BorderlessFullscreen,
                    _settings.UiScale,
                    _settings.ShowOnboarding,
                    _settings.EdgeScrollEnabled,
                    _settings.CameraPanSpeedMultiplier,
                    _settings.CameraBindings));
        var mainMenu =
            new MainMenuModel(
                loadGame.CanContinue);
        var input = new InputState();
        bool enterHeld = false;
        bool escapeHeld = false;
        bool upHeld = false;
        bool downHeld = false;
        bool leftHeld = false;
        bool rightHeld = false;
        bool primaryPointerHeld = false;
        string frontendFeedback = string.Empty;
        GameFrontendScreen transitionScreen = shell.Screen;
        long transitionStarted = Stopwatch.GetTimestamp();

        var target =
            new GraphicsWindowTarget(
                window.NativeHandle.Value,
                window.ClientSize.Width,
                window.ClientSize.Height,
                window.IsMinimized ||
                window.ClientSize.IsEmpty);
        using var renderer =
            new ClientFrontendRenderHost(target);

        while (window.IsOpen)
        {
            input.BeginFrame();
            if (!_platform.PumpEvents())
            {
                return FrontendSessionSelectionResult.Exit();
            }

            DrainWindowEvents(window);
            DrainInputEvents(window, input);

            bool enter =
                ConsumeKeyPress(
                    input,
                    PlatformKey.Enter,
                    ref enterHeld);
            bool escape =
                ConsumeKeyPress(
                    input,
                    PlatformKey.Escape,
                    ref escapeHeld);
            bool primaryPointerDown =
                input.IsMouseButtonDown(
                    PlatformMouseButton.Left);
            bool primaryPointerPressed =
                primaryPointerDown &&
                !primaryPointerHeld;
            primaryPointerHeld =
                primaryPointerDown;
            if (shell.Screen != transitionScreen)
            {
                transitionScreen = shell.Screen;
                transitionStarted = Stopwatch.GetTimestamp();
                frontendFeedback = string.Empty;
            }

            float transition =
                Math.Clamp(
                    (float)Stopwatch.GetElapsedTime(
                        transitionStarted).TotalMilliseconds / 140f,
                    0f,
                    1f);
            FrontendLayout frontendLayout =
                FrontendDesign.ResolveLayout(
                    window.ClientSize.Width,
                    window.ClientSize.Height,
                    _settings.UiScale);

            if (shell.Screen == GameFrontendScreen.MainMenu)
            {
                if (input.HasPointerPosition)
                {
                    string? hoveredId =
                        FrontendHitTesting.MainMenu(
                            input.PointerPosition.X,
                            input.PointerPosition.Y,
                            frontendLayout,
                            mainMenu.Items);
                    if (hoveredId is not null)
                    {
                        mainMenu.TryFocus(hoveredId);
                    }
                }
                if (ConsumeKeyPress(
                        input,
                        PlatformKey.Up,
                        ref upHeld))
                {
                    mainMenu.MovePrevious();
                }

                if (ConsumeKeyPress(
                        input,
                        PlatformKey.Down,
                        ref downHeld))
                {
                    mainMenu.MoveNext();
                }

                if (enter ||
                    (primaryPointerPressed &&
                     input.HasPointerPosition &&
                     FrontendHitTesting.MainMenu(
                         input.PointerPosition.X,
                         input.PointerPosition.Y,
                            frontendLayout,
                         mainMenu.Items) is not null))
                {
                    GameFrontendAction action =
                        mainMenu.ActivateFocused();

                    if (action == GameFrontendAction.Exit)
                    {
                        window.RequestClose();
                        return FrontendSessionSelectionResult.Exit();
                    }

                    if (action == GameFrontendAction.LoadGame &&
                        loadGame.CanContinue)
                    {
                        if (loadGame.ContinueTarget is LoadGameEntry continueTarget &&
                            loadGame.TryGetLoadTarget(
                                continueTarget.Id,
                                out LoadGameEntry targetSave))
                        {
                            return FrontendSessionSelectionResult.Start(
                                ClientSessionRequest.Load(
                                    targetSave));
                        }
                    }

                    shell.Dispatch(action);
                }
            }
            else if (shell.Screen == GameFrontendScreen.NewGame)
            {
                if (ConsumeKeyPress(
                        input,
                        PlatformKey.Left,
                        ref leftHeld))
                {
                    newGame.SetSeed(
                        newGame.Configuration.Seed == 0
                            ? 0
                            : newGame.Configuration.Seed - 1);
                }

                if (ConsumeKeyPress(
                        input,
                        PlatformKey.Right,
                        ref rightHeld))
                {
                    newGame.SetSeed(
                        newGame.Configuration.Seed == ulong.MaxValue
                            ? ulong.MaxValue
                            : newGame.Configuration.Seed + 1);
                }

                if (primaryPointerPressed &&
                    input.HasPointerPosition &&
                    FrontendHitTesting.DetailAdjust(
                        input.PointerPosition.X,
                        input.PointerPosition.Y,
                            frontendLayout,
                        3) is int seedDirection)
                {
                    newGame.SetSeed(
                        seedDirection < 0
                            ? newGame.Configuration.Seed == 0
                                ? 0
                                : newGame.Configuration.Seed - 1
                            : newGame.Configuration.Seed == ulong.MaxValue
                                ? ulong.MaxValue
                                : newGame.Configuration.Seed + 1);
                }

                if (enter ||
                    (primaryPointerPressed &&
                     input.HasPointerPosition &&
                     FrontendHitTesting.PrimaryAction(
                         input.PointerPosition.X,
                         input.PointerPosition.Y,
                        frontendLayout)))
                {
                    return FrontendSessionSelectionResult.Start(
                        ClientSessionRequest.NewGame(
                            newGame.Configuration.Seed));
                }
            }
            else if (shell.Screen == GameFrontendScreen.LoadGame)
            {
                if (input.HasPointerPosition &&
                    FrontendHitTesting.DetailRow(
                        input.PointerPosition.X,
                        input.PointerPosition.Y,
                            frontendLayout,
                        loadGame.Entries.Count) is int saveRow)
                {
                    loadGame.FocusVisible(
                        saveRow,
                        FrontendDesign.MaximumVisibleDetailRows);
                }
                if (ConsumeKeyPress(
                        input,
                        PlatformKey.Up,
                        ref upHeld))
                {
                    loadGame.MovePrevious();
                }

                if (ConsumeKeyPress(
                        input,
                        PlatformKey.Down,
                        ref downHeld))
                {
                    loadGame.MoveNext();
                }

                bool loadActionClicked =
                    primaryPointerPressed &&
                    input.HasPointerPosition &&
                    FrontendHitTesting.PrimaryAction(
                        input.PointerPosition.X,
                        input.PointerPosition.Y,
                        frontendLayout);

                if (enter || loadActionClicked)
                {
                    if (loadGame.TryGetFocusedLoadTarget(
                            out LoadGameEntry selectedSave))
                    {
                        return FrontendSessionSelectionResult.Start(
                            ClientSessionRequest.Load(
                                selectedSave));
                    }

                    frontendFeedback =
                        "SELECT A VALID SAVE TO LOAD";
                }
            }
            else if (shell.Screen == GameFrontendScreen.Settings)
            {
                if (input.HasPointerPosition &&
                    FrontendHitTesting.DetailRow(
                        input.PointerPosition.X,
                        input.PointerPosition.Y,
                            frontendLayout,
                        6) is int settingsRow &&
                    settingsRow > 0)
                {
                    settingsInteraction.Focus(
                        (FrontendSettingsField)settingsRow);

                    if (primaryPointerPressed &&
                        FrontendHitTesting.DetailAdjust(
                            input.PointerPosition.X,
                            input.PointerPosition.Y,
                            frontendLayout,
                            6) is int settingsDirection)
                    {
                        settingsInteraction.Adjust(
                            settings,
                            settingsDirection);
                    }
                }
                if (ConsumeKeyPress(
                        input,
                        PlatformKey.Up,
                        ref upHeld))
                {
                    settingsInteraction.MovePrevious();
                }

                if (ConsumeKeyPress(
                        input,
                        PlatformKey.Down,
                        ref downHeld))
                {
                    settingsInteraction.MoveNext();
                }

                if (ConsumeKeyPress(
                        input,
                        PlatformKey.Left,
                        ref leftHeld))
                {
                    settingsInteraction.Adjust(
                        settings,
                        -1);
                }

                if (ConsumeKeyPress(
                        input,
                        PlatformKey.Right,
                        ref rightHeld))
                {
                    settingsInteraction.Adjust(
                        settings,
                        1);
                }

                bool applyClicked =
                    primaryPointerPressed &&
                    input.HasPointerPosition &&
                    FrontendHitTesting.PrimaryAction(
                        input.PointerPosition.X,
                        input.PointerPosition.Y,
                        frontendLayout);

                if (enter || applyClicked)
                {
                    string settingsDirectory =
                        Path.GetDirectoryName(_settingsPath) ??
                        throw new InvalidOperationException(
                            "Settings path must have a directory.");
                    string settingsRoot =
                        Directory.GetParent(settingsDirectory)?.FullName ??
                        settingsDirectory;
                    var adapter =
                        new ClientSettingsFrontendAdapter(
                            new ClientSettingsStore(
                                settingsRoot));
                    ClientUserSettings applied =
                        adapter.Apply(settings);
                    shell.Dispatch(
                        SettingsModel.Back());

                    if (applied.WindowWidth != _settings.WindowWidth ||
                        applied.WindowHeight != _settings.WindowHeight ||
                        applied.BorderlessFullscreen !=
                            _settings.BorderlessFullscreen)
                    {
                        return FrontendSessionSelectionResult.Restart();
                    }
                }
            }

            if ((escape ||
                 (primaryPointerPressed &&
                  input.HasPointerPosition &&
                  (FrontendHitTesting.SecondaryAction(
                       input.PointerPosition.X,
                       input.PointerPosition.Y,
                        frontendLayout) ||
                   FrontendHitTesting.Footer(
                       input.PointerPosition.X,
                       input.PointerPosition.Y,
                        frontendLayout)))) &&
                shell.Screen != GameFrontendScreen.MainMenu)
            {
                shell.Dispatch(
                    GameFrontendAction.Back);
            }

            FrontendSurfaceView surface =
                CreateFrontendSurface(
                    shell.Screen,
                    mainMenu,
                    newGame,
                    loadGame,
                    settings,
                    settingsInteraction,
                    _settings.CameraBindings);
            if (input.HasPointerPosition &&
                shell.Screen != GameFrontendScreen.MainMenu)
            {
                bool primaryHovered =
                    FrontendHitTesting.PrimaryAction(
                        input.PointerPosition.X,
                        input.PointerPosition.Y,
                        frontendLayout) &&
                    !string.IsNullOrEmpty(surface.PrimaryAction);
                bool secondaryHovered =
                    FrontendHitTesting.SecondaryAction(
                        input.PointerPosition.X,
                        input.PointerPosition.Y,
                        frontendLayout) &&
                    !string.IsNullOrEmpty(surface.SecondaryAction);
                surface =
                    surface.WithInteraction(
                        frontendFeedback,
                        transition,
                        primaryHovered,
                        primaryHovered && primaryPointerDown,
                        secondaryHovered,
                        secondaryHovered && primaryPointerDown);
            }
            else
            {
                surface =
                    surface.WithInteraction(
                        frontendFeedback,
                        transition,
                        false,
                        false,
                        false,
                        false);
            }

            renderer.Publish(surface);
            renderer.ThrowIfFaulted();
            _platform.WaitForEvents(
                IdleWait);
        }

        return FrontendSessionSelectionResult.Exit();
    }

    private readonly record struct FrontendSessionSelectionResult(
        ClientSessionRequest? Session,
        bool RestartRequested)
    {
        internal static FrontendSessionSelectionResult Start(
            ClientSessionRequest session) =>
            new(session, false);

        internal static FrontendSessionSelectionResult Restart() =>
            new(null, true);

        internal static FrontendSessionSelectionResult Exit() =>
            new(null, false);
    }

    private static FrontendSurfaceView CreateFrontendSurface(
        GameFrontendScreen screen,
        MainMenuModel mainMenu,
        NewGameModel newGame,
        LoadGameModel loadGame,
        SettingsModel settings,
        SettingsInteractionModel settingsInteraction,
        RtsCameraBindings cameraBindings) =>
        screen switch
        {
            GameFrontendScreen.MainMenu =>
                FrontendPresentationAdapter.MainMenu(
                    mainMenu),
            GameFrontendScreen.NewGame =>
                FrontendPresentationAdapter.NewGame(
                    newGame),
            GameFrontendScreen.LoadGame =>
                FrontendPresentationAdapter.LoadGame(
                    loadGame),
            GameFrontendScreen.Settings =>
                FrontendPresentationAdapter.Settings(
                    settings,
                    settingsInteraction),
            GameFrontendScreen.Controls =>
                FrontendPresentationAdapter.Controls(
                    cameraBindings),
            GameFrontendScreen.Credits =>
                FrontendPresentationAdapter.Credits(),
            _ =>
                FrontendPresentationAdapter.MainMenu(
                    mainMenu)
        };

    private void PumpBootFrame(
        IWindow window,
        ClientFrontendRenderHost renderer)
    {
        if (!_platform.PumpEvents())
        {
            return;
        }

        DrainWindowEvents(window);
        renderer.ThrowIfFaulted();

        if (!window.IsMinimized &&
            !window.ClientSize.IsEmpty &&
            !renderer.WaitForLatestFrame(
                FrontendFramePresentationTimeout))
        {
            throw new TimeoutException(
                "Frontend loading frame was not presented within the expected transition window.");
        }

        _platform.WaitForEvents(
            IdleWait);
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
