using ForgeLine.Client.RenderQualification;
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
using ForgeLine.Platform.Windows;
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
    internal static void UpdateMetricsOverlay(InputState input, bool modalFrame, ref bool enabled, ref bool held)
    {
        bool down = input.WasKeyPressedWithShift(PlatformKey.F1) ||
            (input.IsKeyDown(PlatformKey.F1) &&
             (input.IsKeyDown(PlatformKey.LeftShift) || input.IsKeyDown(PlatformKey.RightShift)));
        if (!modalFrame && down && !held) enabled = !enabled;
        held = down;
    }

    private const float MaximumCameraDeltaSeconds = 0.1f;
    private const int MaximumDebugInstanceBoxes = 24;
    private const int MaximumDebugLabels = 4;
    internal const int RestartRequestedExitCode = 10;
    private const int SessionRestartExitCode = 12;
    private ClientSessionRequest? _pendingRestart;

    private static readonly PlayerId LocalPlayer = new(1);
    private static readonly PlayerId OpposingPlayer = new(2);
    private static readonly TimeSpan IdleWait = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan SmokeTestDuration = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan SmokeCompletionDelay = TimeSpan.FromMilliseconds(1000);
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
    private ClientUserSettings _settings;
    private readonly GameplayBindingRegistry _gameplayBindings;
    private readonly string _settingsPath;
    private readonly StartupDiagnostics _startup;

    internal DeveloperToolsProviders DeveloperTools { get; private set; } = DeveloperToolsComposition.Create();
    internal bool DeveloperSessionToolsEnabled { get; set; } = true;
#if FORGELINE_DEVELOPER_TOOLS
    internal Func<MatchRuntime, DeveloperToolsProviders>? DeveloperProviderFactory { get; init; }
    internal Func<MatchRuntime, DeveloperToolsProviders, Func<MatchRuntimeSettings, bool>, IDeveloperMenuBackend?>? DeveloperMenuBackendFactory { get; init; }
#endif

    internal bool DeveloperNavigationDiagnostics { get; }

    internal ClientApplication(
        IPlatform platform,
        ClientUserSettings? settings = null,
        string? settingsPath = null,
        StartupDiagnostics? startup = null,
        bool developerNavigationDiagnostics = false)
    {
        DeveloperNavigationDiagnostics = developerNavigationDiagnostics;
        _startup = startup ?? StartupDiagnostics.Disabled;
        _platform =
            platform ??
            throw new ArgumentNullException(nameof(platform));
        _settings =
            settings ??
            new ClientUserSettings();
        _settings.Validate();
        _gameplayBindings = new(_settings.GameplayBindings, _settings.CameraBindings);
        _settingsPath =
            string.IsNullOrWhiteSpace(settingsPath)
                ? "default settings"
                : settingsPath;
    }

    internal int Run(
        bool smokeTest,
        int renderInstanceCount,
        string? visualQualificationOutput = null,
        bool skipSplash = false,
        bool applicationRestart = false,
        IWindow? bootstrapWindow = null,
        bool menuSmoke = false,
        string? scenario = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(renderInstanceCount);

        WindowConfiguration configuration =
            _settings.CreateWindowConfiguration();

        using IWindow? ownedWindow = bootstrapWindow is null ? _startup.Measure(StartupPhase.Window,
            () => _platform.CreateWindow(configuration)) : null;
        IWindow window = bootstrapWindow ?? ownedWindow!;
        _startup.Mark(StartupPhase.WindowVisible);
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
            new NewGameModel(InspectScenarios());
        if (scenario is "split-basin" or SplitBasinBattlefield.CompositionKey) newGame.MoveScenario(1);
        RuntimeAssetCatalog runtimeAssets;
        IReadOnlyList<LoadGameEntry> startupSaves;
        string? splashBypassReason = ClientStartupLoop.SplashBypassReason(
            smokeTest, skipSplash, _settings.ShowStudioSplash, applicationRestart);
        bool showSplash = splashBypassReason is null;
        _startup.Begin(StartupPhase.BootRenderer);
        using var bootRenderer = new ClientFrontendRenderHost(bootstrapTarget, _startup,
            ClientStartupLoop.InitialSurface(showSplash), asynchronousStartup: true);
        {
            string startupSaveDirectory = ResolveSaveDirectory();
            using var coordinator = new ClientStartupCoordinator<RuntimeAssetCatalog, IReadOnlyList<LoadGameEntry>>(
                token =>
                {
                    RuntimeAssetCatalog assets = _startup.Measure(StartupPhase.RuntimeAssets,
                        () => LoadRuntimeAssets(token));
                    token.ThrowIfCancellationRequested();
                    _startup.Mark(StartupPhase.RuntimeAssetsReady, detail:
                        $"Root={assets.RuntimeRoot}; Assets={assets.AssetIds.Count}; Signature={assets.Manifest.ContentSignature}");
                    return assets;
                },
                token => _startup.Measure(StartupPhase.SaveCatalog,
                    () => smokeTest ? Array.Empty<LoadGameEntry>() :
                        ClientSaveCatalog.Discover(startupSaveDirectory, token)));
            var startupInput = new InputState();
            bool completed = ClientStartupLoop.Run(coordinator, showSplash,
                splashBypassReason ?? string.Empty, _startup,
                pumpEvents: () =>
                {
                    startupInput.BeginFrame();
                    if (!window.IsOpen || !_platform.PumpEvents()) return false;
                    DrainWindowEvents(window);
                    DrainInputEvents(window, startupInput);
                    bootRenderer.ThrowIfFaulted();
                    if (bootRenderer.HasPresentedFrame && _platform is WindowsPlatform windows)
                        windows.CompleteBootstrap(window);
                    return window.IsOpen;
                },
                readInput: () => new SplashInputState(
                    startupInput.IsKeyDown(PlatformKey.Escape) ||
                    startupInput.IsKeyDown(PlatformKey.Enter) ||
                    startupInput.IsKeyDown(PlatformKey.Space) ||
                    startupInput.IsMouseButtonDown(PlatformMouseButton.Left)),
                publish: view =>
                {
                    bootRenderer.UpdateSurface(window.ClientSize.Width, window.ClientSize.Height,
                        window.IsMinimized || window.ClientSize.IsEmpty);
                    bootRenderer.Publish(view);
                },
                waitForEvents: () => _platform.WaitForEvents(IdleWait),
                firstFramePresented: () => bootRenderer.HasPresentedFrame,
                videoUnavailable: () => bootRenderer.SplashUnavailable,
                splashFramePresented: () => bootRenderer.HasPresentedSplashFrame);
            if (!completed) return 0;
            ClientStartupResult<RuntimeAssetCatalog, IReadOnlyList<LoadGameEntry>> products = coordinator.GetResults();
            runtimeAssets = products.Assets;
            startupSaves = products.Frontend;
            frontendLoading.Complete("Command interface ready");
        }

        bool bootRendererAvailable = true;
        string sessionFeedback = string.Empty;
        while (window.IsOpen)
        {
            ClientSessionRequest sessionRequest;
            if (_pendingRestart is ClientSessionRequest restart)
            {
                sessionRequest = restart;
                _pendingRestart = null;
            }
            else if (smokeTest)
                sessionRequest = ClientSessionRequest.NewGame(newGame.Configuration.Seed, newGame.SelectedScenario.Key);
            else
            {
                FrontendSessionSelectionResult selection = RunFrontendSessionSelection(window,
                    new GameFrontendShell(), newGame, startupSaves, sessionFeedback, menuSmoke, bootRendererAvailable ? bootRenderer : null, runtimeAssets);
                if (selection.RestartRequested) return RestartRequestedExitCode;
                if (selection.Session is not ClientSessionRequest selected) return 0;
                sessionRequest = selected;
            }
            bootRenderer.Dispose();
            bootRendererAvailable = false;
            try
            {
                int result = RunMatchSession(window, runtimeAssets, sessionRequest, smokeTest,
                    renderInstanceCount, visualQualificationOutput, qualificationWindow);
                if (result == SessionRestartExitCode) continue;
                if (result != SessionReturnToMenuExitCode || smokeTest) return result;
                sessionFeedback = "SESSION LOAD CANCELLED";
            }
            catch (ClientSessionLoadingException error) when (!smokeTest && window.IsOpen)
            {
                _startup.SessionLoadEnded(false, error.Category.ToString());
                Console.Error.WriteLine($"[session:load-failed] category={error.Category} {error}");
                sessionFeedback = $"LOAD FAILED - {error.UserMessage}";
            }
        }
        return 0;
    }

    private const int SessionReturnToMenuExitCode = 11;

    private int RunMatchSession(IWindow window, RuntimeAssetCatalog runtimeAssets,
        ClientSessionRequest sessionRequest, bool smokeTest, int renderInstanceCount,
        string? visualQualificationOutput, ClientWindowQualificationSnapshot qualificationWindow)
    {
        bool activated = false;
        ClientSessionLoadPhase loadingPhase = ClientSessionLoadPhase.Configuration;
        _startup.BeginSession();
        int sessionId = _startup.CurrentSessionId;
        try
        {
        var sessionTransitionTarget = new GraphicsWindowTarget(window.NativeHandle.Value,
            window.ClientSize.Width, window.ClientSize.Height, window.IsMinimized || window.ClientSize.IsEmpty);
        using var sessionTransitionRenderer = new ClientFrontendRenderHost(sessionTransitionTarget,
            initialSurface: ClientSessionLoadingLoop.Surface(new(ClientSessionLoadPhase.Configuration)),
            asynchronousStartup: true);
        var loadingInput = new InputState();
        ClientLoadedSession loaded;
        using (var coordinator = new ClientSessionLoadingCoordinator<ClientLoadedSession>((token, report) =>
        {
            StartupPhase? activePhase = null;
            ClientSessionLoadProgress last = default;
            void Track(ClientSessionLoadProgress progress)
            {
                StartupPhase phase = progress.Phase switch
                {
                    ClientSessionLoadPhase.Configuration => StartupPhase.SessionConfiguration,
                    ClientSessionLoadPhase.ScenarioAssembly => StartupPhase.SessionAssembly,
                    ClientSessionLoadPhase.Replay => StartupPhase.SessionReplay,
                    ClientSessionLoadPhase.HashVerification => StartupPhase.SessionVerification,
                    _ => throw new InvalidOperationException("Unexpected construction phase.")
                };
                if (activePhase != phase)
                {
                    if (activePhase.HasValue) _startup.End(activePhase.Value, sessionId: sessionId,
                        detail: last.HasProgress ? $"Ticks={last.CompletedTicks}/{last.TotalTicks}" : null);
                    _startup.Begin(phase, sessionId);
                    activePhase = phase;
                }
                last = progress;
                report(progress);
            }
            ValidateGameplayAssets(runtimeAssets, token);
            _startup.Mark(StartupPhase.GameplayAssetsReady, sessionId);
            ClientLoadedSession result = _startup.Measure(StartupPhase.SessionReconstruction,
                () => ClientSessionFactory.CreateOwned(sessionRequest, Track, token), sessionId);
            try
            {
                if (renderInstanceCount > 0)
                    PopulateSimulationEntities(result.Runtime.Simulation, result.Runtime.Terrain,
                        renderInstanceCount, token);
                token.ThrowIfCancellationRequested();
                if (activePhase.HasValue) _startup.End(activePhase.Value, sessionId: sessionId);
                return result;
            }
            catch { result.Dispose(); throw; }
        }))
        {
            bool completed = ClientSessionLoadingLoop.Wait(coordinator,
                pumpEvents: () =>
                {
                    if (!PumpSessionLoadingEvents(window, loadingInput)) return false;
                    sessionTransitionRenderer.ThrowIfFaulted();
                    sessionTransitionRenderer.UpdateSurface(window.ClientSize.Width, window.ClientSize.Height,
                        window.IsMinimized || window.ClientSize.IsEmpty);
                    return true;
                },
                cancelRequested: () => loadingInput.IsKeyDown(PlatformKey.Escape),
                publish: view => sessionTransitionRenderer.Publish(view),
                waitForEvents: () => _platform.WaitForEvents(IdleWait));
            if (!completed)
            {
                _startup.SessionLoadEnded(true);
                return window.IsOpen ? SessionReturnToMenuExitCode : 0;
            }
            if (!coordinator.TryTake(out ClientLoadedSession? result) || result is null)
                throw new InvalidOperationException("A completed session could not be transferred.");
            loaded = result;
        }
        using var loadedSession = loaded;
        MatchRuntime scenario = loadedSession.Runtime;
        _startup.Mark(StartupPhase.SessionRuntimeReady, sessionId);
        loadingPhase = ClientSessionLoadPhase.PresentationBinding;
        _startup.Begin(StartupPhase.SessionPresentationBinding, sessionId);
        sessionTransitionRenderer.Publish(ClientSessionLoadingLoop.Surface(new(loadingPhase)));
        if (!PumpSessionLoadingEvents(window, loadingInput) || loadingInput.IsKeyDown(PlatformKey.Escape))
        {
            _startup.SessionLoadEnded(true);
            return window.IsOpen ? SessionReturnToMenuExitCode : 0;
        }

        var snapshotBuffer =
            new PresentationSnapshotBuffer();

        SimulationCoordinator simulation =
            scenario.Simulation;
        BattlefieldDefinition prototypeBattlefield =
            scenario.Battlefield;
        TerrainWorld terrainWorld =
            scenario.Terrain;
        SkirmishStartingBase westBase =
            scenario.GetBase(new ForgeLine.Game.PlayerId(1));
        SkirmishStartingBase eastBase =
            scenario.GetBase(new ForgeLine.Game.PlayerId(2));

        EntityId constructionInventory =
            westBase.CommandCore;

        var presentationInteraction =
            new PresentationInteractionState();
        presentationInteraction.SetNotificationPreferences(_settings.Notifications);
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
                    scenario.Services.TechnologyDefinitions,
                demolition:
                    scenario.Services.Demolition,
                operationsRules: scenario.OperationsRules,
                infrastructure: scenario.Services.StrategicInfrastructure);
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

        sessionTransitionRenderer.Publish(ClientSessionLoadingLoop.Surface(new(loadingPhase)));
        if (!PumpSessionLoadingEvents(window, loadingInput) || loadingInput.IsKeyDown(PlatformKey.Escape))
        {
            _startup.SessionLoadEnded(true);
            return window.IsOpen ? SessionReturnToMenuExitCode : 0;
        }

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

        using var tacticalCursors = new TacticalCursorSet(window, runtimeAssets);
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
        var operationsController = new OperationsController(_gameplayBindings);
        var transientMenus = new TransientHudMenuController();
        var alertController = new ActionableAlertController();
        var memberFocusSelection = new SelectionSet();
        var buildingPlacementController =
            new RtsBuildingPlacementController(LocalPlayer, _gameplayBindings);
        var actionPanel =
            new PlayerActionPanelController(_gameplayBindings);
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
        var navigationDiagnostics = new NavigationDiagnosticsController(DeveloperTools.Capability.IsCompiled && DeveloperSessionToolsEnabled);
        var gameplayDraw =
            new DebugDraw();
        var debugDraw =
            new DebugDraw();

        sessionTransitionRenderer.Publish(ClientSessionLoadingLoop.Surface(new(loadingPhase)));
        if (!PumpSessionLoadingEvents(window, loadingInput) || loadingInput.IsKeyDown(PlatformKey.Escape))
        {
            _startup.SessionLoadEnded(true);
            return window.IsOpen ? SessionReturnToMenuExitCode : 0;
        }

        var developerAccess = DeveloperToolsCapability.Current.BindSession(scenario.Simulation.SessionId, DeveloperSessionToolsEnabled);
        using var simulationHost =
            new ClientSimulationHost(
                scenario,
                commandGateway,
                snapshotBuffer, asynchronousStartup: true, developerAccess: developerAccess);
        bool snapshotReady = ClientSessionLoadingLoop.WaitForPresentation(
            pumpEvents: () =>
            {
                simulationHost.ThrowIfFaulted();
                sessionTransitionRenderer.ThrowIfFaulted();
                return PumpSessionLoadingEvents(window, loadingInput);
            },
            cancelRequested: () => loadingInput.IsKeyDown(PlatformKey.Escape),
            firstFramePresented: () => snapshotBuffer.TryReadLatest(out _),
            publish: () =>
            {
                sessionTransitionRenderer.UpdateSurface(window.ClientSize.Width, window.ClientSize.Height,
                    window.IsMinimized || window.ClientSize.IsEmpty);
                sessionTransitionRenderer.Publish(ClientSessionLoadingLoop.Surface(new(loadingPhase)));
            },
            waitForEvents: () => _platform.WaitForEvents(IdleWait),
            requestStop: simulationHost.RequestStop,
            ownersStopped: () => !simulationHost.IsExecutionThreadAlive);
        if (!snapshotReady)
        {
            _startup.SessionLoadEnded(true);
            return window.IsOpen ? SessionReturnToMenuExitCode : 0;
        }
        _ = renderWorld.Update(snapshotBuffer);
        _startup.End(StartupPhase.SessionPresentationBinding, sessionId: sessionId);
        loadingPhase = ClientSessionLoadPhase.RendererReadiness;

        sessionTransitionRenderer.Publish(ClientSessionLoadingLoop.Surface(new(loadingPhase)));
        if (!PumpSessionLoadingEvents(window, loadingInput) || loadingInput.IsKeyDown(PlatformKey.Escape))
        {
            _startup.SessionLoadEnded(true);
            return window.IsOpen ? SessionReturnToMenuExitCode : 0;
        }

        var graphicsTarget =
            new GraphicsWindowTarget(
                window.NativeHandle.Value,
                window.ClientSize.Width,
                window.ClientSize.Height,
                window.IsMinimized ||
                window.ClientSize.IsEmpty);
        _startup.Begin(StartupPhase.GameplayRenderer, sessionId);
        using var renderHost =
            new ClientRenderHost(
                graphicsTarget,
                terrainWorld,
                snapshotBuffer,
                camera.Settings,
                runtimeAssets,
                _settings.CreateSceneLightingSettings(),
                _startup, asynchronousStartup: true, expectedSession: simulationHost.SessionId,
                transitionHost: sessionTransitionRenderer, simulationTelemetry: simulationHost.CaptureTelemetry,
                sceneAtmosphere: _settings.SceneAtmosphere, particles: _settings.Particles, renderQuality: _settings.CreateRenderQualitySettings());
        bool playable = ClientSessionLoadingLoop.WaitForPresentation(
            pumpEvents: () =>
            {
                simulationHost.ThrowIfFaulted();
                renderHost.ThrowIfFaulted();
                return PumpSessionLoadingEvents(window, loadingInput);
            },
            cancelRequested: () => loadingInput.IsKeyDown(PlatformKey.Escape),
            firstFramePresented: () => renderHost.HasPresentedGameplayFrame,
            publish: () => renderHost.Publish(new ClientRenderFrame(camera.CaptureState(),
                window.ClientSize.Width, window.ClientSize.Height, false, default, default, default,
                FormationTemplate.Compact, [], [], [], [], window.Dpi, UiScale: _settings.UiScale,
                Frontend: renderHost.RendererInitialized ? null : ClientSessionLoadingLoop.Surface(new(loadingPhase)),
                SurfaceSuspended: window.IsMinimized || window.ClientSize.IsEmpty)),
            waitForEvents: () => _platform.WaitForEvents(IdleWait),
            requestStop: () => { renderHost.RequestStop(); simulationHost.RequestStop(); },
            ownersStopped: () => !renderHost.IsExecutionThreadAlive && !simulationHost.IsExecutionThreadAlive);
        if (!playable)
        {
            _startup.SessionLoadEnded(true);
            return window.IsOpen ? SessionReturnToMenuExitCode : 0;
        }
        _startup.Mark(StartupPhase.SessionRendererReady, sessionId);
        activated = true;

        PlayerCommandSubmissionReceipt? lastCommandReceipt = null;
        PlayerCommandResultReadModel? lastCommandResult = null;
        bool overlayEnabled = false;
        bool debugMasterToggleHeld = false;
        bool formationToggleHeld = false;
        bool strategicOverlayToggleHeld = false;
        bool minimapToggleHeld = false;
#if FORGELINE_DEVELOPER_TOOLS
        using var defaultDeveloperBackend = new ClientDeveloperMenuBackend(scenario, simulationHost, developerAccess, settings =>
        {
            if (_pendingRestart.HasValue) return false;
            settings.Validate();
            _pendingRestart = ClientSessionRequest.Restart(settings);
            return true;
        });
        var suppliedDeveloperProviders = DeveloperProviderFactory?.Invoke(scenario) ?? defaultDeveloperBackend.Providers;
        DeveloperTools = new(DeveloperToolsCapability.Current, new DeveloperMenuCatalog(suppliedDeveloperProviders.Menu), suppliedDeveloperProviders.Sessions,
            suppliedDeveloperProviders.Actions, suppliedDeveloperProviders.Parameters, suppliedDeveloperProviders.Presets);
        using var developerMenu = new DeveloperMenuController(new(DeveloperTools, debugOverlay,
            DeveloperMenuBackendFactory?.Invoke(scenario, DeveloperTools, settings =>
            {
                if (_pendingRestart.HasValue) return false;
                settings.Validate();
                _pendingRestart = ClientSessionRequest.Restart(settings);
                return true;
            }) ?? defaultDeveloperBackend,
            visualizationSwitches:
            [
                new("PERFORMANCE METRICS", () => overlayEnabled, () => overlayEnabled = !overlayEnabled),
                new("NAVIGATION DIAGNOSTICS", () => navigationDiagnostics.Open, navigationDiagnostics.ToggleOpen),
                .. Enum.GetValues<NavigationDiagnosticLayers>()
                    .Where(layer => layer is not (NavigationDiagnosticLayers.None or NavigationDiagnosticLayers.All))
                    .Select(layer => new DeveloperVisualizationSwitch("NAV / " + layer,
                        () => (navigationDiagnostics.EnabledLayers & layer) != 0,
                        () => navigationDiagnostics.ToggleLayer(layer), () => navigationDiagnostics.Open))
            ],
            visualizationCommands:
            [
                new("EXPORT NAVIGATION REPORT", () => navigationDiagnostics.ExportReport(snapshotBuffer, simulationHost.SessionId,
                    selectionController.Selection.Entities, LocalPlayer, scenario.Battlefield.Metadata.Key, scenario.RuntimeSettings.Seed),
                    () => navigationDiagnostics.Open),
                new("NAVIGATION DETAILS UP", () => navigationDiagnostics.ScrollDetails(-1), () => navigationDiagnostics.Open),
                new("NAVIGATION DETAILS DOWN", () => navigationDiagnostics.ScrollDetails(1), () => navigationDiagnostics.Open)
            ]));
#endif
        bool developerSessionActive = DeveloperTools.Capability.IsCompiled && DeveloperSessionToolsEnabled;
        var pauseMenu = new PauseMenuModel(developerSessionActive);
        string saveDirectory =
            ResolveSaveDirectory();
        bool restartHeld = false;
        bool returnHeld = false;
        bool pauseHeld = false;
        var guidanceInteraction = new GameplayGuidanceInteraction();
        var guidance = new EarlyGameGuidanceController();
        var cameraFocus = new RtsCameraFocusController(LocalPlayer);
        ulong pendingPlacementHostSequence = 0;
        var hoverTooltip = new HoverTooltipController();
        var modalLifetime = new FrontendInputLifetime();
        int controlsScroll = 0;
        bool pauseMenuUpHeld = false;
        bool pauseMenuDownHeld = false;
        bool pauseMenuEnterHeld = false;
        bool pauseMenuPrimaryPointerHeld = false;
        bool pauseMenuActive = false;
        bool pauseControlsVisible = false;
        using var pauseSettings = new PausedSettingsController(window, _settings, CreateSettingsAdapter());
        string pauseMenuFeedback = string.Empty;
        FormationTemplate activeFormation =
            FormationTemplate.Compact;
        bool simulationPaused = false;
        bool terminalAcknowledgementRequested = false;
        bool smokeCompletionRequested = false;
        var smokeRates = smokeTest ? new SmokeRuntimeRateObserver() : null;

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
        var interactionDisplay = (window.ClientSize.Width, window.ClientSize.Height, window.Dpi, _settings.UiScale);
        RtsCameraState? lastPublishedCamera = null;
        PresentationSnapshot? lastPublishedSnapshot = null;
        var lastPublishedDisplay = interactionDisplay;
        ulong lastPublishedSelection = ulong.MaxValue;

        while (window.IsOpen)
        {
            inputState.BeginFrame();

            if (!_platform.PumpEvents())
            {
                break;
            }

            DrainWindowEvents(window);
            bool receivedInput = DrainInputEvents(window, inputState);
            var currentDisplay = (window.ClientSize.Width, window.ClientSize.Height, window.Dpi, _settings.UiScale);
            if (currentDisplay != interactionDisplay)
            {
                inputState.SuppressHeldInput();
                selectionController.CancelPointerInteraction();
                minimapInteraction.Reset();
                combatGroupInput.Reset();
                cameraFocus.ResetFeedback();
                buildingPlacementController.Cancel(presentationInteraction);
                tacticalTargetingController.Cancel();
            }
            interactionDisplay = currentDisplay;

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
                ref lastCommandReceipt,
                buildingPlacementController,
                presentationInteraction,
                ref pendingPlacementHostSequence,
                operationsController);

            long now = _platform.Clock.GetTimestamp();
            TimeSpan frameElapsed = _platform.Clock.GetElapsedTime(previousFrameAt, now);
            previousFrameAt = now;
            selectionController.CommandFeedback.Advance(frameElapsed);
            actionPanel.AdvanceFeedback(frameElapsed);
            tacticalTargetingController.CommandFeedback.Advance(frameElapsed);

            float cameraDeltaSeconds = (float)Math.Min(
                frameElapsed.TotalSeconds,
                MaximumCameraDeltaSeconds);

            bool nextDeveloperSessionActive = DeveloperTools.Capability.IsCompiled && DeveloperSessionToolsEnabled;
            if (nextDeveloperSessionActive != developerSessionActive)
            {
                developerSessionActive = nextDeveloperSessionActive;
                navigationDiagnostics = new(developerSessionActive);
                overlayEnabled = false;
                debugOverlay.SetEnabled(false);
                inputState.SuppressHeldInput();
            }
            navigationDiagnostics.BindSession(simulationHost.SessionId);
            pauseMenu.SetDeveloperToolsAvailable(developerSessionActive);
            bool wasShellBlocking = pauseMenuActive;
            var guidanceLayout = GameplayHudLayout.Create(window.ClientSize.Width, window.ClientSize.Height, window.Dpi, _settings.UiScale);
            var inputGuidance = guidance.Update(renderWorld.CurrentSnapshot, true, bindings: _gameplayBindings, layout: guidanceLayout);
            guidanceInteraction.Update(inputState, renderWorld.CurrentSnapshot?.SessionId ?? SimulationSessionId.None,
                _settings.ShowOnboarding && !smokeTest && inputGuidance.Visible,
                GameplayHudLayout.Create(window.ClientSize.Width, window.ClientSize.Height, window.Dpi, _settings.UiScale),
                _platform.Clock.GetElapsedTime(startedAt, now),
                _settings.CreateUiFeedbackSettings().Animate(renderHost.LatestQualification?.QualityProfile.Active ?? RenderQuality.Low),
                wasShellBlocking || operationsController.View.Open || !window.IsFocused || window.IsMinimized || selectionController.IsDragSelecting ||
                buildingPlacementController.IsActive || tacticalTargetingController.IsActive || inputState.IsMouseButtonDown(PlatformMouseButton.Middle),
                actionPanel.Mode == PlayerActionPanelMode.Closed && !operationsController.View.Open &&
                !GameplayBindingRegistry.UsesCamera(_settings.CameraBindings, _gameplayBindings.Key(GameplayAction.NextItem)),
                detailed: !string.IsNullOrEmpty(inputGuidance.ContentKey), bindings: _gameplayBindings);
            guidance.Navigate(guidanceInteraction.NavigationThisFrame);
            FrontendLayout modalLayout = FrontendDesign.ResolveLayout(window.ClientSize.Width, window.ClientSize.Height,
                _settings.UiScale, window.Dpi, pauseControlsVisible);
            if (modalLifetime.Update(inputState, modalLayout.Geometry, pauseMenuActive ? (pauseSettings.Visible ? 4 : pauseControlsVisible ? 2 : 1) : 0,
                window.IsFocused && !window.IsMinimized && !window.ClientSize.IsEmpty))
            {
                pauseMenuPrimaryPointerHeld = false;
                controlsScroll = 0;
            }
            if (pauseControlsVisible)
            {
                if (inputState.WasKeyPressed(PlatformKey.Down) || inputState.WheelDelta < 0) controlsScroll++;
                if (inputState.WasKeyPressed(PlatformKey.Up) || inputState.WheelDelta > 0) controlsScroll = Math.Max(0, controlsScroll - 1);
            }
            bool developerMenuShortcut = false;
#if FORGELINE_DEVELOPER_TOOLS
            developerMenuShortcut = developerSessionActive && window.IsFocused && !window.IsMinimized &&
                !pauseSettings.Visible && !pauseControlsVisible && !operationsController.View.Open &&
                inputState.WasKeyPressed(PlatformKey.F1) &&
                !inputState.IsKeyDown(PlatformKey.LeftShift) && !inputState.IsKeyDown(PlatformKey.RightShift) &&
                !inputState.IsKeyDown(PlatformKey.LeftControl) && !inputState.IsKeyDown(PlatformKey.RightControl);
#endif
            bool modalFrame = wasShellBlocking || guidanceInteraction.ShortcutConsumedThisFrame || developerMenuShortcut;
            if (!modalFrame)
            {
                if (developerSessionActive &&
                    ConsumeKeyPress(inputState, PlatformKey.F2, ref debugMasterToggleHeld) &&
                    !inputState.IsKeyDown(PlatformKey.LeftShift) && !inputState.IsKeyDown(PlatformKey.RightShift))
                    debugOverlay.ToggleMaster();
                UpdateFormationSelection(
                    inputState,
                    ref formationToggleHeld,
                    ref activeFormation);

                if (ConsumeKeyPress(
                        inputState,
                        _gameplayBindings.Key(GameplayAction.Overlay),
                        ref strategicOverlayToggleHeld))
                {
                    informationLayer.CycleOverlay();
                }

                if (ConsumeKeyPress(
                        inputState,
                        _gameplayBindings.Key(GameplayAction.Minimap),
                        ref minimapToggleHeld))
                {
                    informationLayer.ToggleMinimap();
                }
            }

            bool developerOwnedFrame = false;
#if FORGELINE_DEVELOPER_TOOLS
            developerOwnedFrame = developerMenu.Visible;
            defaultDeveloperBackend.SetActivated(DeveloperSessionToolsEnabled);
            developerMenu.Model.Refresh(DeveloperTools.Capability.BindSession(simulationHost.SessionId, DeveloperSessionToolsEnabled),
                renderWorld.CurrentSnapshot, selectionController.Selection, scenario.RuntimeSettings.Participants,
                simulationHost.IsPaused, window.IsFocused && !window.IsMinimized && !window.ClientSize.IsEmpty);
            if (developerMenuShortcut)
            {
                if (developerMenu.Visible) { developerMenu.Model.Close(); inputState.SuppressHeldInput(); }
                else if (developerMenu.Open(inputState, DeveloperMenuSection.Visualization))
                { pauseMenuActive = true; pauseControlsVisible = false; }
            }
            developerMenu.Update(inputState, modalLayout, window.IsFocused && !window.IsMinimized && !window.ClientSize.IsEmpty);
            developerOwnedFrame |= developerMenu.Visible;
#endif
            bool settingsOwnedFrame = pauseSettings.Visible || developerOwnedFrame;
            if (pauseSettings.Visible)
            {
                ClientUserSettings? applied = pauseSettings.Update(inputState, modalLayout,
                    renderHost.LatestQualification?.Surface ?? default);
                if (applied is not null)
                {
                    _settings = PausedSettingsController.LiveSettings(_settings, applied);
                    presentationInteraction.SetNotificationPreferences(_settings.Notifications);
                    camera.ConfigureControls(_settings.EdgeScrollEnabled, _settings.CameraPanSpeedMultiplier);
                }
            }

#if FORGELINE_DEVELOPER_TOOLS
            if (_pendingRestart.HasValue)
            {
                inputState.SuppressHeldInput();
                return SessionRestartExitCode;
            }
#endif
            bool pausePressed =
                ConsumeKeyPress(
                    inputState,
                    _gameplayBindings.Key(GameplayAction.Pause),
                    ref pauseHeld);

            presentationInteraction.SetDebugState(
                debugOverlay.View,
                camera.Target.Y);
            presentationInteraction.SetStrategicOverlay(
                informationLayer.OverlayMode);

            bool restartPressed =
                !modalFrame && ConsumeKeyPress(
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
            hudInteraction.CapturePointer(guidanceInteraction.BlocksGameplayThisFrame);
            PlayerExperienceSnapshot? inputExperience =
                inputSnapshot?.PlayerExperience;
            bool inputMatchTerminal =
                inputExperience?.IsMatchComplete ==
                true;

            bool menuEscape = transientMenus.ConsumeEscape(inputState, actionPanel, operationsController,
                presentationInteraction, pauseMenuActive || wasShellBlocking || modalFrame || window.ClientSize.IsEmpty || !window.IsFocused);
            if (menuEscape) returnPressed = false;
            else if (returnPressed && !pauseMenuActive && !wasShellBlocking && !modalFrame &&
                (buildingPlacementController.IsActive || tacticalTargetingController.IsActive))
            {
                buildingPlacementController.Cancel(presentationInteraction);
                tacticalTargetingController.Cancel();
                inputState.SuppressKeyUntilRelease(PlatformKey.Escape);
                returnPressed = false;
            }

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
                     !settingsOwnedFrame &&
                     (pausePressed ||
                      returnPressed))
            {
                pauseMenuActive =
                    !pauseMenuActive;
                pauseControlsVisible =
                    false;
                pauseMenuFeedback =
                    string.Empty;
            }

            if (pauseMenuActive &&
                !settingsOwnedFrame &&
                !inputMatchTerminal)
            {
                FrontendLayout pauseLayout = modalLayout;
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
                    if (inputState.HasPointerPosition && (inputState.PointerDelta != Vector2.Zero || pointerPressed))
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
#if FORGELINE_DEVELOPER_TOOLS
                        else if (pauseMenuCommand == PauseMenuCommand.DeveloperTools)
                        {
                            developerMenu.Open(inputState);
                            pauseMenuCommand = null;
                        }
#endif
                        else if (pauseMenuCommand == PauseMenuCommand.Settings)
                        {
                            pauseSettings.Open(_settings);
                            pauseMenuCommand = null;
                            inputState.SuppressHeldInput();
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

            bool shellChanged = wasShellBlocking != (pauseMenuActive);
            if (shellChanged || inputState.FocusLostThisFrame || guidanceInteraction.ShortcutConsumedThisFrame)
            {
                inputState.SuppressHeldInput();
                pauseHeld = false;
                returnHeld = false;
                restartHeld = false;
                formationToggleHeld = false;
                strategicOverlayToggleHeld = false;
                minimapToggleHeld = false;
                debugMasterToggleHeld = false;
                selectionController.CancelPointerInteraction();
                minimapInteraction.Reset();
                combatGroupInput.Reset();
                actionPanel.Close();
                cameraFocus.ResetFeedback();
                tacticalTargetingController.Cancel();
                buildingPlacementController.Cancel(presentationInteraction);
            }

            PreAlphaUxView preAlphaUx =
                CreatePreAlphaUxView(
                    false,
                    false,
                    false,
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
                _pendingRestart = ClientSessionRequest.Restart(scenario.RuntimeSettings);
                return SessionRestartExitCode;
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
                !window.IsFocused ||
                window.IsMinimized ||
                window.ClientSize.IsEmpty;
            bool shellBlocksGameplay =
                pauseMenuActive;
            bool shouldPauseSimulation =
                !inputMatchTerminal &&
                (shouldPauseForWindow ||
                 shellBlocksGameplay);

            bool developerOwnsPause = false;
#if FORGELINE_DEVELOPER_TOOLS
            developerOwnsPause = developerMenu.Model.OwnsMenuPause;
            if (developerOwnsPause) simulationPaused = simulationHost.IsPaused;
#endif
            if (!simulationHost.WindowPause.TrySetPaused(!inputMatchTerminal && shouldPauseForWindow) ||
                !developerOwnsPause && !simulationHost.MenuPause.TrySetPaused(!inputMatchTerminal && shellBlocksGameplay))
                throw new InvalidOperationException("Simulation pause ownership could not be updated.");
            if (!developerOwnsPause) simulationPaused = shouldPauseSimulation;

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

            // Smoke completion must be evaluated before the unfocused-window input exit.
            if (smokeTest)
            {
                TimeSpan smokeElapsed = _platform.Clock.GetElapsedTime(startedAt, now);
                smokeRates!.Sample(smokeElapsed,
                    renderHost.LatestQualification?.Surface.PresentedFrameCount ?? 0,
                    simulationHost.CaptureTelemetry(), !shouldPauseForWindow && !shellBlocksGameplay);
                if (!smokeCompletionRequested && smokeElapsed >= TimeSpan.FromMinutes(1))
                    throw new InvalidOperationException("Smoke qualification did not observe running presentation/tick progress within one minute.");
                if (!smokeCompletionRequested &&
                    _platform.Clock.GetElapsedTime(startedAt, now) >= SmokeCompletionDelay &&
                    smokeRates.LastRunningRates is not null)
                {
                    if (!simulationHost.TryRunSmokeCompletion(eastBase.CommandCore))
                        throw new InvalidOperationException("Simulation control boundary is full while scheduling smoke completion.");
                    smokeCompletionRequested = true;
                }
                if (smokeCompletionRequested &&
                    renderWorld.CurrentSnapshot?.PlayerExperience?.MatchStatus == PlayerMatchStatus.Victory)
                    smokeMatchCompleted = true;
                if (smokeMatchCompleted && _platform.Clock.GetElapsedTime(startedAt, now) >= SmokeTestDuration)
                    window.RequestClose();
            }

            if (shouldPauseForWindow ||
                shellBlocksGameplay || modalFrame || shellChanged || !window.IsFocused)
            {
                transientMenus.Update(inputState, inputSnapshot,
                    GameplayHudLayout.Create(window.ClientSize.Width, window.ClientSize.Height, window.Dpi, _settings.UiScale),
                    selectionController.Selection, actionPanel, operationsController, presentationInteraction,
                    informationLayer.MinimapEnabled, false, blocked: true);
                operationsController.CancelInput(inputState);
                alertController.CancelInput(inputState);
                hoverTooltip.Reset();
                presentationInteraction.SetHover(EntityId.Invalid, SimulationSessionId.None);
                actionPanel.Close();
                tacticalTargetingController.Cancel();
                debugDraw.Clear();

                FrontendSurfaceView? frontendSurface =
                    null;

                if (pauseMenuActive)
                {
#if FORGELINE_DEVELOPER_TOOLS
                    if (developerMenu.Visible)
                    {
                        frontendSurface = developerMenu.Surface(modalLayout);
                    }
                    else
#endif
                    if (pauseSettings.Visible)
                    {
                        frontendSurface = pauseSettings.Surface;
                    }
                    else if (pauseControlsVisible)
                    {
                        FrontendSurfaceView controlsSurface =
                            FrontendPresentationAdapter.Controls(
                                _settings.CameraBindings,
                                "ESC  BACK TO PAUSE", _settings.EdgeScrollEnabled, gameplay: _gameplayBindings, developerSessionActive: developerSessionActive);
                        FrontendLayout pauseLayout = modalLayout;
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

                if (frontendSurface is FrontendSurfaceView modalSurface)
                {
                    var geometry = modalLayout.Geometry;
                    if (geometry.Controls != (modalSurface.Kind == FrontendSurfaceKind.Controls))
                        geometry = FrontendGeometry.Resolve(window.ClientSize.Width, window.ClientSize.Height, _settings.UiScale,
                            window.Dpi, modalSurface.Kind == FrontendSurfaceKind.Controls);
                    controlsScroll = Math.Clamp(controlsScroll, 0, Math.Max(0, modalSurface.DetailLines.Count - geometry.VisibleDetailRows));
                    frontendSurface = modalSurface with { Layout = geometry, DetailScroll = controlsScroll, PauseContext = true };
                    if(inputState.HasPointerPosition)
                    {
                        var pointerLayout = modalLayout with { Geometry = geometry };
                        var positioned = FrontendHitTesting.WithDetailPointer(frontendSurface.Value, pointerLayout,
                            inputState.PointerPosition.X, inputState.PointerPosition.Y, inputState.IsMouseButtonDown(PlatformMouseButton.Left));
                        frontendSurface = FrontendHitTesting.WithMenuPointer(positioned, pointerLayout,
                            inputState.PointerPosition.X, inputState.PointerPosition.Y, inputState.IsMouseButtonDown(PlatformMouseButton.Left));
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
                        frontendSurface,
                        FeedbackSettings: _settings.CreateUiFeedbackSettings(), WorldAudio: _settings.WorldAudio,
                        MetricsActive: (smokeTest || window.IsFocused) && !window.IsMinimized));

                if (shouldPauseForWindow)
                {
                    _platform.WaitForEvents(
                        IdleWait);
                }

                continue;
            }

            GameplayHudLayout interactionLayout = GameplayHudLayout.Create(
                window.ClientSize.Width, window.ClientSize.Height, window.Dpi, _settings.UiScale);
            if (selectionController.ReconcileSelection(renderWorld))
                presentationInteraction.SetSelection(selectionController.Selection.Entities);
            transientMenus.Update(inputState, inputSnapshot, interactionLayout, selectionController.Selection,
                actionPanel, operationsController, presentationInteraction, informationLayer.MinimapEnabled,
                buildingPlacementController.IsActive,
                localFeedback: buildingPlacementController.IsActive ? "" : alertController.Feedback.Length > 0 ? alertController.Feedback : actionPanel.Feedback.Length > 0 ? actionPanel.Feedback : cameraFocus.Feedback, onboarding: buildingPlacementController.IsActive ? null : guidanceInteraction.View,
                expansion: alertController.Expansion);
            hudInteraction.CapturePointer(transientMenus.PointerCaptured);
            actionPanel.SynchronizeObjectRing(inputState, inputSnapshot, renderWorld, selectionController.Selection, false);
            actionPanel.AdoptPresentedRing(renderHost.LatestActionRing);

            if (inputMatchTerminal)
            {
                actionPanel.Close();
                tacticalTargetingController.Cancel();
            }
            else
            {
                if (operationsController.View.Open && !operationsController.View.Notifications) actionPanel.Close();
                else actionPanel.Update(
                    inputState,
                    inputSnapshot,
                    window.ClientSize.Width,
                    window.ClientSize.Height,
                    window.Dpi,
                    _settings.UiScale,
                    currentSelection: selectionController.Selection,
                    globalShortcutsBlocked: modalFrame || simulationPaused ||
                        !window.IsFocused || window.IsMinimized ||
                        buildingPlacementController.IsActive ||
                        tacticalTargetingController.IsActive);

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

            CombatGroupCardResult cardResult = actionPanel.TakeGroupResult();
            var alertResult = alertController.Update(inputState, inputSnapshot, interactionLayout,
                blocked: modalFrame || simulationPaused || !window.IsFocused || inputMatchTerminal ||
                    buildingPlacementController.IsActive || tacticalTargetingController.IsActive, seconds: frameElapsed.TotalSeconds, dockExpanded: actionPanel.DockExpanded,
                localFeedback: buildingPlacementController.IsActive ? "" : alertController.Feedback.Length > 0 ? alertController.Feedback : actionPanel.Feedback.Length > 0 ? actionPanel.Feedback : cameraFocus.Feedback,
                animateDetails: !_settings.ReducedUiMotion && _settings.RenderQuality.Quality != RenderQuality.Low,
                bindings: _gameplayBindings);
            hudInteraction.CapturePointer(alertResult.Captured);
            if (alertResult.Target.IsValid && inputSnapshot is not null)
            {
                memberFocusSelection.SetSingle(alertResult.Target);
                if (RtsCameraFocusController.TryGroup(inputSnapshot, LocalPlayer, memberFocusSelection, out Vector3 alertTarget))
                {
                    camera.CenterOn(alertTarget);
                    selectionController.Selection.SetSingle(alertResult.Target);
                    presentationInteraction.SetSelection(selectionController.Selection.Entities);
                    selectionController.CancelPointerInteraction();
                    minimapInteraction.Reset();
                    actionPanel.Close();
                    inputState.SuppressHeldInput();
                }
                else
                {
                    alertController.Unavailable();
                    operationsController.OpenCategory(alertResult.Category, inputSnapshot.SessionId, presentationInteraction);
                }
            }
            if (alertResult.OpenOperations && inputSnapshot is not null)
            {
                if (alertResult.NotificationDetails) operationsController.OpenNotifications(inputSnapshot.SessionId, presentationInteraction);
                else operationsController.OpenCategory(alertResult.Category, inputSnapshot.SessionId, presentationInteraction);
            }
            var operationsResult = operationsController.Update(inputState, inputSnapshot, interactionLayout, presentationInteraction,
                blocked: modalFrame || simulationPaused || !window.IsFocused || inputMatchTerminal ||
                    buildingPlacementController.IsActive || tacticalTargetingController.IsActive, inputCaptured: alertResult.Captured,
                selection: selectionController.Selection);
            hudInteraction.CapturePointer(operationsResult.Captured);
            hudInteraction.CaptureKeyboard(operationsController.HasKeyboardFocus);
            if (operationsController.TryTakeRuleRequest(out PlayerActionRequest ruleRequest))
            {
                SimulationTick observedTick = inputSnapshot?.Tick ?? SimulationTick.Zero;
                bool submitted = simulationHost.TrySubmit(simulationHost.SessionId, gateway =>
                {
                    if (!PlayerActionRequestDispatcher.TryDispatch(ruleRequest, LocalPlayer, gateway, observedTick, out var receipt))
                        throw new InvalidOperationException("Operations rule request was not dispatched.");
                    return receipt;
                }, out ulong sequence);
                if (submitted) operationsController.ExpectRuleSubmission(sequence);
                else operationsController.RuleSubmissionFailed();
            }
            if (operationsResult.Navigate.IsValid && inputSnapshot is not null)
            {
                memberFocusSelection.SetSingle(operationsResult.Navigate);
                if (RtsCameraFocusController.TryGroup(inputSnapshot, LocalPlayer, memberFocusSelection, out Vector3 operationsTarget))
                {
                    camera.CenterOn(operationsTarget);
                    selectionController.CancelPointerInteraction();
                    minimapInteraction.Reset();
                    if (operationsResult.Controls != PlayerActionPanelMode.Closed)
                    {
                        selectionController.Selection.SetSingle(operationsResult.Navigate);
                        presentationInteraction.SetSelection(selectionController.Selection.Entities);
                        inputState.SuppressHeldInput();
                        operationsController.Close(inputState, presentationInteraction);
                        actionPanel.OpenOperationsControls(operationsResult.Controls);
                        hudInteraction.CaptureKeyboard(actionPanel.HasKeyboardFocus);
                    }
                }
            }
            hudInteraction.CapturePointer(cardResult.Captured);
            if (cardResult.SelectionChanged)
            {
                selectionController.CancelPointerInteraction();
                presentationInteraction.SetSelection(selectionController.Selection.Entities);
            }
            if (cardResult.CycleFormation)
                activeFormation = NextFormation(activeFormation);
            if (cardResult.FocusMember.IsValid || cardResult.FocusSelection)
            {
                memberFocusSelection.SetSingle(cardResult.FocusMember);
                if (inputSnapshot is not null && RtsCameraFocusController.TryGroup(inputSnapshot, LocalPlayer,
                    cardResult.FocusSelection ? selectionController.Selection : memberFocusSelection, out Vector3 focusTarget))
                {
                    camera.CenterOn(focusTarget);
                    selectionController.CancelPointerInteraction();
                    minimapInteraction.Reset();
                }
            }
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
                    hudInteraction.KeyboardCaptured,
                presentationTime: _platform.Clock.GetElapsedTime(startedAt, now),
                expandOnHover: true,
                animateHover: _settings.CreateUiFeedbackSettings().Animate(
                    renderHost.LatestQualification?.QualityProfile.Active ?? RenderQuality.Low),
                presentationActive: window.IsFocused && !window.IsMinimized &&
                    !simulationPaused && !modalFrame,
                ringBounds: actionPanel.PresentedRingBounds);
            hudInteraction.CapturePointer(
                minimapInteraction.PointerCaptured);

            GameplayHudLayout mapInteractionLayout = interactionLayout with
            {
                Minimap = minimapInteraction.DisplayedBounds.IsEmpty
                    ? interactionLayout.Minimap : minimapInteraction.DisplayedBounds
            };
            hudInteraction.CapturePointer(HudInteractionContext.CapturesWorldPointer(
                inputState, mapInteractionLayout, informationLayer.MinimapEnabled, actionPanel.DockExpanded,
                guidanceVisible: false, includeActionDock: false,
                onboarding: buildingPlacementController.IsActive ? null : guidanceInteraction.View, includePrimaryCommands: !actionPanel.ObjectActions));

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
                selectionController.CommandFeedback.Show(minimapMovement.WorldTarget, valid: true);
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
                // The active placement or paginated ring owns only Q/E. Other
                // stored camera rotation bindings must remain functional.
                bool contextOwnsQe = buildingPlacementController.IsActive ||
                    actionPanel.OwnsRadialPaging(inputSnapshot?.PlayerActions);
                RtsCameraInputFrame cameraInput =
                    actionMapper.Map(inputState, hudInteraction.PointerCaptured,
                        contextOwnsQe: contextOwnsQe);

                camera.Update(
                    cameraInput,
                    cameraDeltaSeconds,
                    window.ClientSize.Width,
                    window.ClientSize.Height);
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
                PlayerMatchStatus.Active && !renderHost.IsPreparingTerrain;

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
                        tacticalTargetingController.IsActive,
                    elapsed: frameElapsed);
            if (combatGroupInputResult.Action == CombatGroupInputAction.Recalled) actionPanel.ReopenSelectionRing();
            hudInteraction.CaptureKeyboard(
                combatGroupInputResult.Handled);
            if (cameraFocus.Update(inputState, currentSnapshot, camera, selectionController.Selection,
                combatGroupInputResult.FocusRequested, frameElapsed,
                blocked: !gameplayActive || simulationPaused || !window.IsFocused ||
                    (hudInteraction.KeyboardCaptured && !combatGroupInputResult.Handled) ||
                    buildingPlacementController.IsActive || tacticalTargetingController.IsActive,
                bindings: _settings.CameraBindings, gameplayBindings: _gameplayBindings))
            {
                selectionController.CancelPointerInteraction();
                minimapInteraction.Reset();
                inputState.SuppressHeldInput();
            }
            bool uiPointerCaptured = hudInteraction.PointerCaptured;

            if (gameplayActive)
            {
                bool targetingWasActive = tacticalTargetingController.IsActive;
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

                bool placementWasActive = buildingPlacementController.IsActive;
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
                    hudInteraction.PointerCaptured,
                    displayScale: interactionLayout.Scale);
                }

                bool placementCaptured = placementWasActive || buildingPlacementController.IsActive || targetingWasActive;
                if (placementCaptured)
                {
                    selectionController.Update(inputState, camera, renderWorld, terrainWorld,
                        window.ClientSize.Width, window.ClientSize.Height, renderAlpha,
                        pointerCaptured: true, pointerScale: interactionLayout.Scale, elapsed: frameElapsed);
                }
                else
                {
                    selectionController.Update(
                        inputState,
                        camera,
                        renderWorld,
                        terrainWorld,
                        window.ClientSize.Width,
                        window.ClientSize.Height,
                        renderAlpha,
                        hudInteraction.PointerCaptured,
                        interactionLayout.Scale,
                        elapsed: frameElapsed);

                    if (selectionController.TryTakeMovementRequest(
                            out MovementOrderRequest movementRequest))
                    {
                        EntityId[] movementEntities =
                            movementRequest.Entities.ToArray();
                        FormationTemplate requestedFormation = activeFormation;
                        SimulationTick observedTick =
                            currentSnapshot?.Tick ??
                            SimulationTick.Zero;

                        RequireSubmission(
                            simulationHost,
                            gateway => movementRequest.Append || movementRequest.Patrol
                                ? gateway.SubmitQueuedOrders(LocalPlayer, movementEntities,
                                    movementRequest.WorldTarget, EntityId.Invalid, QueuedOrderKind.Move,
                                    movementRequest.Append ? QueuedOrderEdit.Append : QueuedOrderEdit.Replace,
                                    movementRequest.Patrol, observedTick, requestedFormation)
                                : gateway.SubmitMovement(LocalPlayer, movementEntities,
                                    movementRequest.WorldTarget, observedTick, requestedFormation));
                    }
                }

                presentationInteraction.SetSelection(
                    selectionController.Selection.Entities);

                // Building rings do not own Delete. Dialogs, key rebinding,
                // placement, targeting and inactive game states do.
                if (inputState.WasKeyPressed(_gameplayBindings.Key(GameplayAction.Demolish)) &&
                    !simulationPaused && !modalFrame &&
                    window.IsFocused && !window.IsMinimized &&
                    !buildingPlacementController.IsActive &&
                    !tacticalTargetingController.IsActive)
                {
                    EntityId[] demolitionTargets = selectionController.Selection.Entities.ToArray();
                    if (demolitionTargets.Length > 0)
                    {
                        SimulationTick observedTick =
                            currentSnapshot?.Tick ?? SimulationTick.Zero;
                        RequireSubmission(
                            simulationHost,
                            gateway => gateway.SubmitDemolition(
                                LocalPlayer, demolitionTargets, observedTick));
                    }
                }

                if (buildingPlacementController.TryTakeConveyorRequest(out ConveyorBuildRequest conveyorRequest))
                {
                    SimulationSessionId conveyorSession=currentSnapshot?.SessionId??default;
                    if (!conveyorSession.IsSpecified || !simulationHost.TrySubmit(conveyorSession,
                        gateway=>gateway.SubmitConveyor(scenario.Services.Conveyors, LocalPlayer,
                            conveyorRequest, constructionInventory, currentSnapshot?.Tick??SimulationTick.Zero),
                        out pendingPlacementHostSequence))
                        buildingPlacementController.ObserveSubmission(null,presentationInteraction);
                }

                if (buildingPlacementController.TryTakePlacementRequest(
                        out BuildingPlacementRequest placementRequest))
                {
                    SimulationTick observedTick =
                        currentSnapshot?.Tick ??
                        SimulationTick.Zero;

                    if (!simulationHost.TrySubmit(simulationHost.SessionId,
                        gateway =>
                            gateway.SubmitBuild(
                                LocalPlayer,
                                placementRequest.BuildingId,
                                placementRequest.Position,
                                placementRequest.Orientation,
                                constructionInventory,
                                observedTick),
                        out pendingPlacementHostSequence))
                    {
                        buildingPlacementController.ObserveSubmission(null, presentationInteraction);
                        pendingPlacementHostSequence = 0;
                    }
                }
            }
            else
            {
                presentationInteraction.SetSelection(
                    selectionController.Selection.Entities);
            }

            DrainCommandResults(
                commandGateway,
                ref lastCommandResult,
                buildingPlacementController,
                presentationInteraction,
                actionPanel,
                scenario.Services.Resources,
                operationsController);

            TacticalTargetingView tacticalTargetingView =
                tacticalTargetingController.CreateView(
                    currentSnapshot);

            NavigationDiagnosticsView navigationView = navigationDiagnostics.Update(
                developerSessionActive && window.IsFocused && !window.IsMinimized && !simulationPaused && !modalFrame && !operationsController.View.Open,
                false, false, false,
                snapshotBuffer, simulationHost.SessionId, selectionController.Selection.Entities, LocalPlayer,
                scenario.Battlefield.Metadata.Key, scenario.RuntimeSettings.Seed);
            presentationInteraction.SetNavigationDiagnosticsOpen(navigationView.Visible);

            RtsCameraState publicationCamera = camera.CaptureState();
            bool publishVisualFrame = renderHost.NeedsFramePublication || receivedInput ||
                publicationCamera != lastPublishedCamera || currentDisplay != lastPublishedDisplay ||
                selectionController.Selection.Revision != lastPublishedSelection ||
                !ReferenceEquals(currentSnapshot, lastPublishedSnapshot);
            if (publishVisualFrame)
            {
                BuildGameplayWorldOverlay(
                    gameplayDraw,
                    renderWorld,
                    renderAlpha,
                    camera,
                    selectionController,
                    buildingPlacementController,
                    tacticalTargetingView,
                    terrainWorld,
                    tacticalTargetingController.CommandFeedback,
                    MathF.Max(1.0f, camera.Distance * MathF.Tan(camera.Settings.VerticalFieldOfViewRadians * 0.5f) *
                        12.0f * interactionLayout.Scale / Math.Max(1, window.ClientSize.Height)),
                    informationLayer.OverlayMode,
                    RtsStrategicOverlayHudModel.IsCurrent(currentSnapshot, informationLayer.OverlayMode) ? currentSnapshot?.StrategicOverlay : null,
                    camera.GetMatrices(Math.Max(1,window.ClientSize.Width),Math.Max(1,window.ClientSize.Height)).ViewProjection);

                BuildDevelopmentDebugOverlay(
                    debugDraw,
                    debugOverlay.View,
                    prototypeBattlefield,
                    renderWorld,
                    renderAlpha,
                    camera,
                    currentSnapshot?.Construction,
                    currentSnapshot?.Debug);
                NavigationDiagnosticsViewModel.AppendLayers(debugDraw, currentSnapshot, navigationView);
            }

            CombatGroupOverviewView combatGroupOverview = publishVisualFrame
                ? CombatGroupOverviewModel.Create(
                    combatGroupRegistry,
                    SelectedCombatGroup.Resolve(currentSnapshot),
                    selectionController.Selection) : CombatGroupOverviewView.Empty;

            transientMenus.SelectionCommitted(inputState, selectionController.Selection, actionPanel, operationsController, presentationInteraction);
            actionPanel.SynchronizeObjectRing(inputState, currentSnapshot, renderWorld, selectionController.Selection,
                !hudInteraction.PointerCaptured && !buildingPlacementController.IsActive && !tacticalTargetingController.IsActive &&
                window.IsFocused && !simulationPaused && !modalFrame, selectionResolved: true);
            PlayerActionPanelView actionPanelView =
                actionPanel.CreateView(
                    window.ClientSize.Width,
                    window.ClientSize.Height,
                    currentSnapshot?.PlayerActions,
                    window.Dpi,
                    _settings.UiScale);
            PlacementContextFeedbackView placementFeedback = buildingPlacementController.IsActive && !simulationPaused && window.IsFocused
                ? PlacementContextFeedback.Resolve(buildingPlacementController, currentSnapshot) : default;
            bool placementValid = placementFeedback.State == PlacementContextState.Valid;
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
                        selectionController.CanSelectHoveredEntity,
                        selectionController.Selection.Count > 0,
                        actionPanel.Mode ==
                            PlayerActionPanelMode.Supply,
                        selectionController.PointerMovementTargetValid,
                        PointerCaptured: !gameplayActive || simulationPaused || !window.IsFocused || uiPointerCaptured || buildingPlacementController.AwaitingResult,
                        MovementSelectionSupported: RtsCursorResolver.CanMoveSelection(currentSnapshot, selectionController.Selection, LocalPlayer)));
            tacticalCursors.Update(cursor, window.Dpi, _settings.UiScale,
                gameplayActive && !simulationPaused && window.IsFocused && !window.IsMinimized && !uiPointerCaptured && inputState.HasPointerPosition);
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
                    [],
                    MinimapPointerCaptured:
                        minimapInteraction.View.PointerCaptured,
                    MinimapPointerWorldValid:
                        minimapInteraction.View.PointerWorldValid,
                    MinimapPointerWorldTarget:
                        minimapInteraction.View.PointerWorldTarget,
                    MinimapCameraDragging:
                        minimapInteraction.View.IsCameraDragging)
                {
                    MinimapBounds = minimapInteraction.DisplayedBounds,
                    MovementFeedback = selectionController.CommandFeedback.View,
                    TacticalFeedback = tacticalTargetingController.CommandFeedback.View
                };

            hoverTooltip.Configure(_settings.Tooltips);
            HoverTooltipView tooltipView = hoverTooltip.Update(inputState, currentSnapshot,
                selectionController.HoveredEntity, camera, interactionLayout, actionPanelView,
                selectionController.IsDragSelecting || buildingPlacementController.IsActive ||
                tacticalTargetingView.Mode != TacticalTargetingMode.None || simulationPaused ||
                !window.IsFocused || window.IsMinimized,
                hudInteraction.PointerCaptured, frameElapsed);
            presentationInteraction.SetHover(tooltipView.Entity, tooltipView.SessionId);

            preAlphaUx = preAlphaUx with
            {
                InteractionHint = buildingPlacementController.IsActive ? buildingPlacementController.InteractionHint : alertController.Feedback.Length > 0 ? alertController.Feedback : actionPanel.Feedback.Length > 0 ? actionPanel.Feedback : cameraFocus.Feedback,
                Onboarding = guidanceInteraction.View,
                Guidance = guidance.Update(currentSnapshot, preAlphaUx.ShowOnboarding,
                    simulationPaused || operationsController.View.Open || !window.IsFocused || selectionController.IsDragSelecting || buildingPlacementController.IsActive ||
                    tacticalTargetingView.Mode != TacticalTargetingMode.None || inputState.IsMouseButtonDown(PlatformMouseButton.Middle), _gameplayBindings, guidanceLayout),
                Placement = placementFeedback
            };

            if (publishVisualFrame)
            {
                _ = renderHost.Publish(
                new ClientRenderFrame(
                    publicationCamera,
                    window.ClientSize.Width,
                    window.ClientSize.Height,
                    overlayEnabled,
                    debugOverlay.View,
                    actionPanelView,
                    tacticalTargetingView,
                    activeFormation,
                    [],
                    [],
                    [],
                    [],
                    window.Dpi,
                    informationView,
                    _settings.UiScale,
                    preAlphaUx,
                    SurfaceSuspended:
                        window.IsMinimized,
                    CombatGroups:
                        combatGroupOverview,
                    MetricsActive: (smokeTest || window.IsFocused) && !window.IsMinimized,
                    HoverTooltip: tooltipView,
                    Operations: operationsController.View, FeedbackSettings: _settings.CreateUiFeedbackSettings(), WorldAudio: _settings.WorldAudio,
                    NotificationExpansion: alertController.Expansion, NavigationDiagnostics: navigationView),
                    gameplayDraw, debugDraw, selectionController.Selection.Entities);
                lastPublishedCamera = publicationCamera;
                lastPublishedSnapshot = currentSnapshot;
                lastPublishedDisplay = currentDisplay;
                lastPublishedSelection = selectionController.Selection.Revision;
            }

            // Wake immediately for Windows input; otherwise briefly yield while
            // the latest immutable frame is still waiting for its render owner.
            if (!renderHost.NeedsFramePublication)
                _platform.WaitForEvents(TimeSpan.FromMilliseconds(2));

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
            renderHost.Dispose();
            renderHost.ThrowIfFaulted();
            ClientVisualQualificationSnapshot? measuredQualification = renderHost.LatestQualification;
            if (smokeRates?.LastRunningRates is { } runningRates && measuredQualification is { } measured)
                measuredQualification = measured with { LastMeasuredRunningRates = runningRates };
            WriteVisualQualificationReport(
                visualQualificationOutput,
                renderInstanceCount,
                qualificationWindow,
                measuredQualification, scenario.RuntimeSettings.Composition);
        }

        return 0;
        }
        catch (Exception error) when (!activated && error is not ClientSessionLoadingException)
        {
            throw ClientSessionLoadingException.From(error, loadingPhase);
        }
    }

    private bool PumpSessionLoadingEvents(IWindow window, InputState input)
    {
        input.BeginFrame();
        if (!window.IsOpen || !_platform.PumpEvents()) return false;
        DrainWindowEvents(window);
        DrainInputEvents(window, input);
        return window.IsOpen;
    }

    private void WriteVisualQualificationReport(
        string outputPath,
        int renderStressInstances,
        in ClientWindowQualificationSnapshot window,
        ClientVisualQualificationSnapshot? qualification, MatchComposition composition)
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
                    "match-client",
                scenarioKey = composition.Key,
                scenarioIdentity = composition.Identity,
                renderPreparation = metrics.RenderWork,
                framePublication = metrics.Publication,
                renderStressInstances,
                settings =
                    new
                    {
                        path =
                            _settingsPath,
                        referenceZoom = _settings.ReferenceZoom.ToString(),
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
            case PlayerActionRequestKind.SubmitStopCombat:
            case PlayerActionRequestKind.SubmitHoldPosition:
                tacticalTargetingController.Cancel();
                if (buildingPlacementController.IsActive)
                    buildingPlacementController.Cancel(presentationInteraction);
                break;

            case PlayerActionRequestKind.BeginBuildingPlacement:
                tacticalTargetingController.Cancel();
                if (buildingPlacementController.IsActive) buildingPlacementController.Cancel(presentationInteraction);
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
        ref PlayerCommandSubmissionReceipt? lastCommandReceipt,
        RtsBuildingPlacementController placement,
        PresentationInteractionState interaction,
        ref ulong placementHostSequence,
        OperationsController operations)
    {
        while (simulationHost.TryReadSubmissionCompletion(
                   out ClientSubmissionCompletion completion))
        {
            operations.ObserveRuleSubmission(completion.HostSequence, completion.Receipt);
            if (completion.HostSequence == placementHostSequence)
            {
                placement.ObserveSubmission(completion.Receipt, interaction);
                placementHostSequence = 0;
            }
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
        int entityCount,
        CancellationToken cancellationToken = default)
    {
        int side = checked((int)Math.Ceiling(Math.Sqrt(entityCount)));
        const float spacing = 12.0f;
        float halfSpan = (side - 1) * spacing * 0.5f;

        for (int index = 0; index < entityCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
        ITerrainQuery terrain,
        RtsCommandFeedback tacticalFeedback,
        float minimumMarkerRadius,
        StrategicOverlayMode strategicOverlayMode,
        StrategicOverlaySnapshot? strategicOverlaySnapshot,
        Matrix4x4 viewProjection)
    {
        gameplayDraw.Clear();

        bool interactionFeedback =
            selectionController.Selection.Count > 0 ||
            selectionController.InspectedEntity.IsValid ||
            selectionController.HoveredEntity.IsValid ||
            buildingPlacementController.IsActive ||
            tacticalTargeting.HasPointerTarget ||
            tacticalFeedback.IsVisible ||
            selectionController.CommandFeedback.IsVisible ||
            strategicOverlayMode !=
                StrategicOverlayMode.None;
        gameplayDraw.Enabled =
            interactionFeedback;

        if (!gameplayDraw.Enabled)
        {
            return;
        }

        selectionController.CommandFeedback.Draw(gameplayDraw);
        tacticalFeedback.Draw(gameplayDraw);

        Vector4 selectedColor = RtsSelectionVisualStyle.Selected;
        Vector4 hoveredColor = RtsSelectionVisualStyle.Hovered;
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

        if (buildingPlacementController.GridCenter is { } gridCenter)
            ConstructionGridOverlay.Draw(gameplayDraw, terrain, gridCenter);

        if (buildingPlacementController.Preview is
            BuildingPlacementPreview placementPreview)
        {
            BuildingConstructionDebugVisualization.DrawPreview(
                gameplayDraw,
                placementPreview,
                placementValidColor,
                placementInvalidColor);
        }

        buildingPlacementController.Templates.Draw(gameplayDraw);
        buildingPlacementController.Conveyor.Draw(gameplayDraw);
        ConveyorNetworkOverlay.Draw(gameplayDraw,renderWorld.CurrentSnapshot,selectionController.Selection.Entities,
            selectionController.HoveredEntity,showAllPorts:buildingPlacementController.Conveyor.IsActive,
            viewProjection:viewProjection);

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
                selectedColor,
                terrain,
                minimumMarkerRadius);
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
                selectedColor,
                terrain,
                minimumMarkerRadius);
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
                hoveredInstance.Selectable.Owner.IsSpecified && hoveredInstance.Selectable.Owner != LocalPlayer
                    ? RtsSelectionVisualStyle.ForeignHover : hoveredColor,
                terrain,
                minimumMarkerRadius,
                foreignOwned: hoveredInstance.Selectable.Owner.IsSpecified && hoveredInstance.Selectable.Owner != LocalPlayer);
        }
    }

    private static void BuildDevelopmentDebugOverlay(
        DebugDraw debugDraw,
        in DebugOverlayView overlay,
        BattlefieldDefinition prototypeBattlefield,
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

            BattlefieldDebugVisualization.Draw(
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
            _settingsPath, Bindings: _gameplayBindings);
    }

    internal static bool ConsumeKeyPress(
        InputState inputState,
        PlatformKey key,
        ref bool held)
    {
        bool down =
            inputState.IsKeyDown(key);
        bool pressed =
            inputState.WasKeyPressed(key) ||
            (down && !held);
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

    private static FormationTemplate NextFormation(FormationTemplate formation) => formation switch
    {
        FormationTemplate.Compact => FormationTemplate.Line,
        FormationTemplate.Line => FormationTemplate.Column,
        FormationTemplate.Column => FormationTemplate.Wedge,
        _ => FormationTemplate.Compact
    };

    private void UpdateFormationSelection(
        InputState inputState,
        ref bool held,
        ref FormationTemplate activeFormation)
    {
        bool down = inputState.IsKeyDown(_gameplayBindings.Key(GameplayAction.Formation));

        if (down && !held)
        {
            activeFormation = NextFormation(activeFormation);
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

    private static bool DrainInputEvents(
        IWindow window,
        InputState inputState)
    {
        bool received = false;
        while (window.TryDequeueInputEvent(out PlatformInputEvent inputEvent))
        {
            inputState.Apply(inputEvent);
            received = true;
        }
        return received;
    }

    private static string ResolveSaveDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "FORGELINE",
            "Saves");

    private static bool TrySaveCurrentMatch(
        MatchRuntime scenario,
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
            feedback = exception is MatchPersistenceException
                { Reason: MatchPersistenceFailureReason.DocumentTooLarge }
                    ? "SAVE FAILED - FILE TOO LARGE"
                    : "SAVE FAILED";
            Console.Error.WriteLine(
                $"[save:error] type={exception.GetType().Name} message={exception.Message}");
            return false;
        }
    }

    private static RuntimeAssetCatalog LoadRuntimeAssets(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
            RuntimeAssetCatalog.LoadCancellable(
                runtimeRoot, cancellationToken, requireIntegrity: true);

        Console.WriteLine(
            $"[assets:runtime] mode=player manifest=loaded assets={catalog.AssetIds.Count} root=\"{runtimeRoot}\" manifest=validated gameplay=deferred");
        cancellationToken.ThrowIfCancellationRequested();
        return catalog;
    }

    private static void ValidateGameplayAssets(RuntimeAssetCatalog catalog, CancellationToken cancellationToken)
    {
        GameplayVisualAssetContract.Validate(catalog, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateProductionRuntimeAsset(
            catalog,
            "building.directorate.command_core",
            RuntimeAssetType.Mesh);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateProductionRuntimeAsset(
            catalog,
            "unit.directorate.main_battle_tank",
            RuntimeAssetType.Mesh);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateProductionRuntimeAsset(
            catalog,
            "material.world.terrain.grass_ground",
            RuntimeAssetType.Material);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateProductionRuntimeAsset(
            catalog,
            "texture.world.terrain.rocky_scrub",
            RuntimeAssetType.Texture);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateProductionRuntimeAsset(
            catalog,
            "texture.directorate.material.painted_metal_base",
            RuntimeAssetType.Texture);

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

    private ClientSettingsFrontendAdapter CreateSettingsAdapter()
    {
        string? directory = Path.GetDirectoryName(_settingsPath);
        if (string.IsNullOrWhiteSpace(directory)) return new(new ClientSettingsStore());
        return new(new ClientSettingsStore(Directory.GetParent(directory)?.FullName ?? directory));
    }

    private FrontendSessionSelectionResult RunFrontendSessionSelection(
        IWindow window,
        GameFrontendShell shell,
        NewGameModel newGame,
        IReadOnlyList<LoadGameEntry> startupSaves,
        string initialFeedback = "", bool menuSmoke = false, ClientFrontendRenderHost? initialRenderer = null, RuntimeAssetCatalog? menuAssets = null)
    {
        _startup.Begin(StartupPhase.Frontend);
        shell.Dispatch(
            GameFrontendAction.LoadingCompleted);

        var loadGame =
            new LoadGameModel(startupSaves);
        var settingsInteraction =
            new SettingsInteractionModel();
        var settings = new SettingsModel(ClientSettingsFrontendAdapter.ToSnapshot(_settings));
        using var displayPreview = new ClientDisplayPreview(window);
        string? displayFeedback = null;
        void RestoreDisplay() => settings.SetDisplay(_settings.WindowWidth, _settings.WindowHeight, _settings.BorderlessFullscreen, _settings.UiScale);
        var mainMenu =
            new MainMenuModel(
                loadGame.CanContinue);
        var input = new InputState();
        var frontendLifetime = new FrontendInputLifetime();
        int detailScroll = 0;
        bool enterHeld = false;
        bool escapeHeld = false;
        bool upHeld = false;
        bool downHeld = false;
        bool leftHeld = false;
        bool rightHeld = false;
        bool primaryPointerHeld = false;
        string frontendFeedback = initialFeedback;
        GameFrontendScreen transitionScreen = shell.Screen;

        var target =
            new GraphicsWindowTarget(
                window.NativeHandle.Value,
                window.ClientSize.Width,
                window.ClientSize.Height,
                window.IsMinimized ||
                window.ClientSize.IsEmpty);
        using ClientFrontendRenderHost? ownedRenderer = initialRenderer is null
            ? new ClientFrontendRenderHost(target, _startup, asynchronousStartup: true) : null;
        ClientFrontendRenderHost renderer = initialRenderer ?? ownedRenderer!;
        renderer.SetMenuAssets(menuAssets);
        _startup.End(StartupPhase.Frontend);
        _startup.Mark(StartupPhase.FrontendReady);

        while (window.IsOpen)
        {
            input.BeginFrame();
            if (!_platform.PumpEvents())
            {
                return FrontendSessionSelectionResult.Exit();
            }

            DrainWindowEvents(window);
            DrainInputEvents(window, input);
            _startup.MenuInteractive();
            if (menuSmoke && _startup.ApplicationReadiness == StartupReadiness.Ready)
                return FrontendSessionSelectionResult.Exit();

            FrontendLayout frontendLayout = FrontendDesign.ResolveLayout(window.ClientSize.Width,
                window.ClientSize.Height, settings.Settings.UiScale, window.Dpi, shell.Screen == GameFrontendScreen.Controls);
            if (frontendLifetime.Update(input, frontendLayout.Geometry, (int)shell.Screen,
                window.IsFocused && !window.IsMinimized && !window.ClientSize.IsEmpty))
            {
                primaryPointerHeld = enterHeld = escapeHeld = upHeld = downHeld = leftHeld = rightHeld = false;
            }
            if (frontendLayout.Geometry.IsEmpty || window.IsMinimized)
            {
                renderer.UpdateSurface(window.ClientSize.Width, window.ClientSize.Height, true);
                _platform.WaitForEvents(IdleWait);
                continue;
            }
            settingsInteraction.VisibleCapacity = frontendLayout.Geometry.VisibleDetailRows;
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
            if (displayPreview.Expire())
            {
                RestoreDisplay();
                displayFeedback = null;
            }
            if (displayPreview.Active && escape)
            {
                displayPreview.Cancel();
                RestoreDisplay();
                displayFeedback = null;
                escape = false;
            }
            if (displayPreview.Active)
                displayFeedback = GraphicsSettingsText.Get("DisplayPreview") + $" / {displayPreview.RemainingSeconds}s";
            if (shell.Screen != transitionScreen)
            {
                transitionScreen = shell.Screen;
                if (shell.Screen == GameFrontendScreen.NewGame) newGame.RefreshScenarios(InspectScenarios());
                frontendFeedback = string.Empty;
                detailScroll = 0;
            }

            const float transition = 1f; // Render owner supplies decorative motion; input never waits.

            if (shell.Screen == GameFrontendScreen.MainMenu)
            {
                if (input.HasPointerPosition && (input.PointerDelta != Vector2.Zero || primaryPointerPressed))
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
                if (ConsumeKeyPress(input, PlatformKey.Up, ref upHeld)) newGame.MoveScenario(-1);
                if (ConsumeKeyPress(input, PlatformKey.Down, ref downHeld)) newGame.MoveScenario(1);
                if (primaryPointerPressed && input.HasPointerPosition &&
                    FrontendHitTesting.DetailRow(input.PointerPosition.X, input.PointerPosition.Y, frontendLayout, 3) == 0)
                    newGame.MoveScenario(1);
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
                        FrontendPresentationAdapter.NewGame(newGame)) is int seedDirection)
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

                if (newGame.CanStartSelectedScenario && (enter ||
                    (primaryPointerPressed &&
                     input.HasPointerPosition &&
                     FrontendHitTesting.PrimaryAction(
                         input.PointerPosition.X,
                         input.PointerPosition.Y,
                        frontendLayout))))
                {
                    return FrontendSessionSelectionResult.Start(
                        ClientSessionRequest.NewGame(
                            newGame.Configuration.Seed, newGame.SelectedScenario.Key));
                }
            }
            else if (shell.Screen == GameFrontendScreen.LoadGame)
            {
                if (input.HasPointerPosition && (primaryPointerPressed || input.PointerDelta != Vector2.Zero) &&
                    FrontendHitTesting.DetailRow(
                        input.PointerPosition.X,
                        input.PointerPosition.Y,
                            frontendLayout,
                        Math.Min(loadGame.Entries.Count, frontendLayout.Geometry.VisibleDetailRows)) is int saveRow)
                {
                    loadGame.FocusVisible(
                        saveRow,
                        frontendLayout.Geometry.VisibleDetailRows);
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
                    loadGame.TryGetFocusedLoadTarget(out _) && primaryPointerPressed &&
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
                if (!displayPreview.Active)
                {
                    settingsInteraction.HandleSectionInput(input, frontendLayout.Geometry);
                    if (input.HasPointerPosition && FrontendHitTesting.DetailRow(input.PointerPosition.X,
                        input.PointerPosition.Y, frontendLayout, settingsInteraction.VisibleRowCount) is int settingsRow)
                    {
                        var settingsLine = FrontendPresentationAdapter.Settings(settings, settingsInteraction).DetailLines[settingsRow];
                        if (primaryPointerPressed) settingsInteraction.FocusVisible(settingsRow);
                        if (primaryPointerPressed && FrontendHitTesting.DetailAdjust(input.PointerPosition.X,
                            input.PointerPosition.Y, frontendLayout, settingsInteraction.VisibleRowCount) is int settingsDirection &&
                            (settingsDirection < 0 ? settingsLine.CanDecrease : settingsLine.CanIncrease))
                            settingsInteraction.Adjust(settings, settingsDirection);
                    }
                    if (ConsumeKeyPress(input, PlatformKey.Up, ref upHeld)) settingsInteraction.MovePrevious();
                    if (ConsumeKeyPress(input, PlatformKey.Down, ref downHeld)) settingsInteraction.MoveNext();
                    if (ConsumeKeyPress(input, PlatformKey.Left, ref leftHeld)) settingsInteraction.Adjust(settings, -1);
                    if (ConsumeKeyPress(input, PlatformKey.Right, ref rightHeld)) settingsInteraction.Adjust(settings, 1);
                }
                bool applyClicked =
                    primaryPointerPressed &&
                    input.HasPointerPosition &&
                    FrontendHitTesting.PrimaryAction(
                        input.PointerPosition.X,
                        input.PointerPosition.Y,
                        frontendLayout);

                if ((enter || applyClicked) && settingsInteraction.CanApply)
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
                    try
                    {
                        ClientUserSettings candidate = adapter.CreateCandidate(settings);
                        bool displayChanged = candidate.WindowWidth != _settings.WindowWidth || candidate.WindowHeight != _settings.WindowHeight ||
                            candidate.BorderlessFullscreen != _settings.BorderlessFullscreen;
                        if (displayChanged && !displayPreview.Active)
                        {
                            displayPreview.Begin(_settings.CreateWindowConfiguration(), candidate.CreateWindowConfiguration());
                            displayFeedback = GraphicsSettingsText.Get("DisplayPreview") + " / 15s";
                        }
                        else if (!displayPreview.Active || (!window.IsMinimized && renderer.IsSurfacePresented(window.ClientSize.Width, window.ClientSize.Height)))
                        {
                            ClientUserSettings applied = adapter.Apply(settings);
                            displayPreview.Keep();
                            shell.Dispatch(SettingsModel.Back());
                            if (applied != _settings) return FrontendSessionSelectionResult.Restart();
                        }
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
                    {
                        displayPreview.Cancel();
                        RestoreDisplay();
                        displayFeedback = GraphicsSettingsText.Get("ApplyFailure");
                        Console.Error.WriteLine($"[settings:apply-failed] {error.Message}");
                    }
                }
            }

            if (input.WheelDelta != 0)
            {
                if (shell.Screen == GameFrontendScreen.Settings)
                { if (input.WheelDelta < 0) settingsInteraction.MoveNext(); else settingsInteraction.MovePrevious(); }
                if (shell.Screen == GameFrontendScreen.LoadGame)
                { if (input.WheelDelta < 0) loadGame.MoveNext(); else loadGame.MovePrevious(); }
            }
            if (shell.Screen is GameFrontendScreen.Controls or GameFrontendScreen.Credits)
            {
                if (ConsumeKeyPress(input, PlatformKey.Up, ref upHeld) || input.WheelDelta > 0) detailScroll = Math.Max(0, detailScroll - 1);
                if (ConsumeKeyPress(input, PlatformKey.Down, ref downHeld) || input.WheelDelta < 0) detailScroll++;
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
                if (displayPreview.Active)
                {
                    displayPreview.Cancel();
                    RestoreDisplay();
                    displayFeedback = null;
                }
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
                    _settings.CameraBindings,
                    _settings.EdgeScrollEnabled, gameplay: _gameplayBindings, settingsFeedback: displayFeedback, displayPreviewActive: displayPreview.Active, visibleRows: frontendLayout.Geometry.VisibleDetailRows);
            var publishedLayout = FrontendDesign.ResolveLayout(window.ClientSize.Width, window.ClientSize.Height,
                settings.Settings.UiScale, window.Dpi, surface.Kind == FrontendSurfaceKind.Controls);
            if (publishedLayout != frontendLayout || shell.Screen != transitionScreen)
            {
                input.SuppressHeldInput();
                frontendLayout = publishedLayout;
            }
            detailScroll = Math.Clamp(detailScroll, 0, Math.Max(0, surface.DetailLines.Count - frontendLayout.Geometry.VisibleDetailRows));
            surface = surface with { Layout = frontendLayout.Geometry, DetailScroll = detailScroll,
                BackgroundQuality = (RenderQuality)settings.Settings.RenderQuality, FeedbackSettings = settings.Settings.FeedbackSettings, FeedbackActive = window.IsFocused && !window.IsMinimized };
            if(input.HasPointerPosition) surface = FrontendHitTesting.WithMenuPointer(surface, frontendLayout,
                input.PointerPosition.X, input.PointerPosition.Y, primaryPointerDown);
            if (input.HasPointerPosition &&
                shell.Screen != GameFrontendScreen.MainMenu)
            {
                surface = FrontendHitTesting.WithDetailPointer(surface, frontendLayout,
                    input.PointerPosition.X, input.PointerPosition.Y, primaryPointerDown);
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

            renderer.UpdateSurface(window.ClientSize.Width, window.ClientSize.Height, window.IsMinimized || window.ClientSize.IsEmpty);
            renderer.Publish(surface);
            renderer.ThrowIfFaulted();
            _platform.WaitForEvents(
                IdleWait);
        }

        return FrontendSessionSelectionResult.Exit();
    }

    private static NewGameScenarioOption[] InspectScenarios() => ScenarioRegistry.Default.Inspect()
        .Select(entry => new NewGameScenarioOption(entry.Descriptor.Key, entry.Descriptor.DisplayName, entry.Available, entry.Error, entry.Descriptor.Description))
        .ToArray();

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
        RtsCameraBindings cameraBindings,
        bool edgeScrollEnabled, GameplayBindingRegistry? gameplay = null, string? settingsFeedback = null, bool displayPreviewActive = false, int visibleRows = FrontendDesign.MaximumVisibleDetailRows) =>
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
                    loadGame, visibleRows),
            GameFrontendScreen.Settings =>
                FrontendPresentationAdapter.Settings(
                    settings,
                    settingsInteraction, settingsFeedback, displayPreviewActive),
            GameFrontendScreen.Controls =>
                FrontendPresentationAdapter.Controls(
                    cameraBindings, edgeScrollEnabled: edgeScrollEnabled, gameplay: gameplay),
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
        ref PlayerCommandResultReadModel? lastCommandResult,
        RtsBuildingPlacementController placement,
        PresentationInteractionState interaction,
        PlayerActionPanelController actionPanel,
        ResourceCatalog resources,
        OperationsController operations)
    {
        while (commandGateway.Results.TryRead(
                   out PlayerCommandResultReadModel result))
        {
            operations.ObserveRuleResult(result);
            placement.ObserveResult(result, interaction);
            if(result.Kind==PlayerCommandKind.ConveyorConstruction)
                actionPanel.ShowDemolitionFeedback(result.State==PlayerCommandFeedbackState.Accepted?
                    "CONVEYOR CHANGE ACCEPTED":RtsConveyorConstructionController.Explain(result.ConveyorResult));
            if (result.Kind == PlayerCommandKind.Demolition)
                actionPanel.ShowDemolitionFeedback(DescribeDemolitionResult(result, resources));
            lastCommandResult = result;
        }
    }

    private static string DescribeDemolitionResult(
        in PlayerCommandResultReadModel result,
        ResourceCatalog resources)
    {
        BuildingDemolitionTargetResult[] targets = result.DemolitionTargets ?? [];
        if (result.AcceptedTargets == 0)
        {
            BuildingDemolitionFailure reason =
                targets.Length > 0 ? targets[0].Failure : BuildingDemolitionFailure.InvalidTarget;
            string explanation = reason switch
            {
                BuildingDemolitionFailure.ProtectedCommandCore => "COMMAND CORE PROTECTED",
                BuildingDemolitionFailure.NotOwned => "TARGET NOT OWNED",
                BuildingDemolitionFailure.MissingPaymentRecord => "MATERIAL RECEIPT UNAVAILABLE",
                BuildingDemolitionFailure.CommandCoreUnavailable => "COMMAND CORE UNAVAILABLE",
                BuildingDemolitionFailure.InventoryNotEmpty => "EMPTY STORAGE BEFORE DEMOLITION",
                BuildingDemolitionFailure.InsufficientRefundCapacity => "COMMAND CORE NEEDS MORE STORAGE CAPACITY",
                BuildingDemolitionFailure.ConstructionAlreadyCancelling => "CONSTRUCTION ALREADY CANCELLING",
                BuildingDemolitionFailure.ProductionActive => "STOP PRODUCTION / RESEARCH FIRST",
                BuildingDemolitionFailure.ConveyorRemovalRejected =>
                    RtsConveyorConstructionController.Explain(targets[0].ConveyorFailure),
                _ => "NO VALID BUILDINGS SELECTED"
            };
            return "DEMOLITION REJECTED - " + explanation;
        }

        var totals = new Dictionary<ResourceId, double>();
        bool anyRefund = false;
        foreach (BuildingDemolitionTargetResult target in targets)
            if (target.Accepted)
                foreach (BuildingResourceCost refund in target.Refunded)
                {
                    anyRefund = true;
                    totals[refund.ResourceId] = totals.GetValueOrDefault(refund.ResourceId) + refund.Quantity;
                }

        string prefix = anyRefund
            ? $"DEMOLISHED {result.AcceptedTargets}"
            : $"CANCELLED {result.AcceptedTargets} CONSTRUCTION SITE(S)";
        if (totals.Count > 0)
        {
            string materials = string.Join(", ", totals.OrderBy(item => item.Key)
                .Select(item => $"{item.Value:0.##} {resources[item.Key].DisplayName}"));
            prefix += " - REFUNDED " + materials;
        }
        if (result.RejectedTargets > 0)
            prefix += $" - {result.RejectedTargets} REJECTED";
        return prefix;
    }

}
