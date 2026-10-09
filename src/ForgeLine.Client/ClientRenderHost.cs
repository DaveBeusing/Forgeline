using System.Diagnostics;
using System.Runtime.ExceptionServices;
using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Client;

internal readonly record struct ClientVisualQualificationSnapshot(
    string AdapterName,
    ulong DedicatedVideoMemoryBytes,
    double FramesPerSecond,
    double FrameMilliseconds,
    double CpuRenderMilliseconds,
    double? GpuMilliseconds,
    int VisibleTerrainChunks,
    int TotalTerrainChunks,
    long SubmittedTerrainTriangles,
    int TerrainDrawCalls,
    int InstanceDrawCalls,
    int TotalMeasuredDrawCalls,
    int VisibleInstances,
    int TotalInstances,
    int HighLodInstances,
    int ReducedLodInstances,
    int ActiveVfxEffects,
    int VfxPoolCapacity,
    ulong DroppedVfxEffects,
    int LoadedTextureCount,
    long ResidentTextureBytes,
    int ShaderResourceDescriptorsUsed,
    int ShaderResourceDescriptorCapacity,
    long TextureBindingFailureCount,
    int LoadedMaterialCount,
    int LoadedMaterialAssetTextureCount,
    long MaterialBindingFailureCount,
    GraphicsSurfaceInfo Surface = default,
    int TerrainControlTextureCount = 0,
    int TerrainTextureBindingsPerDraw = 0,
    int TerrainMaximumTextureSamplesPerPixel = 0,
    double TerrainCpuSubmissionMilliseconds = 0.0)
{
    public bool GpuTimingAvailable { get; init; }

    public GameplayHudState GameplayHudState { get; init; }

    public int GameplayHudVertexCount { get; init; }

    public RuntimeMetricsView RuntimeMetrics { get; init; }

    public RuntimeMetricsView? LastMeasuredRunningRates { get; init; }

    public bool DebugLayerEnabled { get; init; }

    public int PeakLoadedTextureCount { get; init; }

    public long PeakResidentTextureBytes { get; init; }

    public int PeakShaderResourceDescriptorsUsed { get; init; }

    public long TextureUploadCount { get; init; }

    public long TextureReleaseCount { get; init; }

    public long DebugLayerWarningCount { get; init; }

    public long DebugLayerErrorCount { get; init; }

    public int RuntimeMeshInstances { get; init; }

    public int TexturedRuntimeMeshInstances { get; init; }

    public int FallbackMeshInstances { get; init; }

    public float LightingDirectionX { get; init; }

    public float LightingDirectionY { get; init; }

    public float LightingDirectionZ { get; init; }

    public float LightingDirectionalIntensity { get; init; }

    public float LightingAmbientIntensity { get; init; }

    public float LightingExposure { get; init; }

    public string LightingToneMapping { get; init; } =
        string.Empty;
}

internal readonly record struct ClientRenderFrame(
    RtsCameraState Camera,
    int ViewportWidth,
    int ViewportHeight,
    bool OverlayEnabled,
    DebugOverlayView DebugOverlay,
    PlayerActionPanelView ActionPanel,
    TacticalTargetingView TacticalTargeting,
    FormationTemplate ActiveFormation,
    DebugLine[] GameplayLines,
    DebugLabel[] GameplayLabels,
    DebugLine[] DebugLines,
    DebugLabel[] DebugLabels,
    uint Dpi = 96,
    RtsInformationLayerView InformationLayer = default,
    float UiScale = 1.0f,
    PreAlphaUxView PreAlphaUx = default,
    FrontendSurfaceView? Frontend = null,
    bool SurfaceSuspended = false,
    CombatGroupOverviewView? CombatGroups = null,
    bool MetricsActive = true,
    HoverTooltipView HoverTooltip = default);

internal sealed class ClientRenderHost : IDisposable
{
    private static readonly TimeSpan IdleWait =
        TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan DiagnosticInterval =
        TimeSpan.FromSeconds(1);

    private readonly StartupDiagnostics _startup = StartupDiagnostics.Disabled;
    private bool _startupPresented;
    private int _gameplayPresented;
    private readonly bool _asynchronousStartup;
    private readonly SimulationSessionId? _expectedSession;
    private readonly ClientFrontendRenderHost? _frontendOwner;
    private readonly Func<ClientSimulationTelemetry>? _simulationTelemetry;
    private readonly object _frameGate = new();
    private readonly object _disposeGate = new();
    private readonly object _faultWaitGate = new();
    private readonly GraphicsWindowTarget? _initialTarget;
    private readonly TerrainWorld? _terrain;
    private readonly PresentationSnapshotBuffer? _snapshots;
    private readonly RtsCameraSettings? _cameraSettings;
    private readonly RuntimeAssetCatalog? _runtimeAssets;
    private readonly SceneLightingSettings _sceneLighting;
    private readonly Action<ClientRenderFrame>? _testRenderAction;
    private readonly AutoResetEvent _signal = new(false);
    private readonly ManualResetEventSlim _started = new(false);
    private readonly ManualResetEventSlim _faulted = new(false);
    private readonly Thread _thread;

    private ClientRenderFrame? _latestFrame;
    private ClientVisualQualificationSnapshot? _latestQualification;
    private ExceptionDispatchInfo? _failure;
    private int _stopping;
    private long _publishedFrames;
    private long _completedFrames;
    private long _lastCompletedFrameAt;
    private bool _disposed;

    public ClientRenderHost(
        in GraphicsWindowTarget initialTarget,
        TerrainWorld terrain,
        PresentationSnapshotBuffer snapshots,
        RtsCameraSettings cameraSettings,
        RuntimeAssetCatalog? runtimeAssets = null,
        SceneLightingSettings? sceneLighting = null,
        StartupDiagnostics? startup = null,
        bool asynchronousStartup = false,
        SimulationSessionId? expectedSession = null,
        ClientFrontendRenderHost? transitionHost = null,
        Func<ClientSimulationTelemetry>? simulationTelemetry = null)
    {
        _startup = startup ?? StartupDiagnostics.Disabled;
        _asynchronousStartup = asynchronousStartup;
        _expectedSession = expectedSession;
        _frontendOwner = transitionHost;
        _simulationTelemetry = simulationTelemetry;
        if (transitionHost is not null && !asynchronousStartup)
            throw new ArgumentException("A renderer continuation requires asynchronous startup.", nameof(asynchronousStartup));
        initialTarget.Validate();
        _initialTarget = initialTarget;
        _terrain =
            terrain ??
            throw new ArgumentNullException(nameof(terrain));
        _snapshots =
            snapshots ??
            throw new ArgumentNullException(nameof(snapshots));
        _cameraSettings =
            cameraSettings ??
            throw new ArgumentNullException(nameof(cameraSettings));
        _runtimeAssets =
            runtimeAssets;
        _sceneLighting =
            sceneLighting ??
            SceneLightingSettings.Default;
        _sceneLighting.Validate();

        _thread = transitionHost?.ExecutionThread ?? CreateThread();
        if (transitionHost is not null) transitionHost.ContinueWith(device => RenderLoop(device));
        else if (asynchronousStartup) _thread.Start();
        else StartAndWait();
    }

    internal ClientRenderHost(
        Action<ClientRenderFrame> renderAction)
    {
        _testRenderAction =
            renderAction ??
            throw new ArgumentNullException(nameof(renderAction));
        _sceneLighting =
            SceneLightingSettings.Default;
        _thread =
            CreateThread();
        StartAndWait();
    }

    internal bool IsExecutionThreadAlive =>
        _thread.IsAlive;
    internal bool HasPresentedGameplayFrame => Volatile.Read(ref _gameplayPresented) != 0;
    internal bool RendererInitialized => _started.IsSet;
    internal void RequestStop()
    {
        lock (_disposeGate)
        {
            if (_disposed) return;
            Volatile.Write(ref _stopping, 1);
            _signal.Set();
        }
    }
    internal bool IsStopping => Volatile.Read(ref _stopping) != 0;
    internal ClientRenderHealth Health => new(
        IsExecutionThreadAlive,
        IsStopping,
        Volatile.Read(ref _publishedFrames),
        Volatile.Read(ref _completedFrames),
        Volatile.Read(ref _lastCompletedFrameAt),
        Volatile.Read(ref _failure) is not null);

    internal ClientVisualQualificationSnapshot? LatestQualification
    {
        get
        {
            lock (_frameGate)
            {
                return _latestQualification;
            }
        }
    }

    internal bool WaitForFault(
        TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            timeout,
            TimeSpan.Zero);
        lock (_faultWaitGate)
        {
            ThrowIfDisposed();
            return _faulted.Wait(timeout);
        }
    }

    private Thread CreateThread() =>
        new(RenderLoop)
        {
            IsBackground = true,
            Name = "ForgeLine Render"
        };

    public bool Publish(in ClientRenderFrame frame)
    {
        ThrowIfDisposed();
        ThrowIfFaulted();

        ClientRenderFrame copied =
            frame with
            {
                GameplayLines =
                    frame.GameplayLines.ToArray(),
                GameplayLabels =
                    frame.GameplayLabels.ToArray(),
                DebugLines =
                    frame.DebugLines.ToArray(),
                DebugLabels =
                    frame.DebugLabels.ToArray(),
                InformationLayer =
                    frame.InformationLayer with
                    {
                        SelectedEntities =
                            frame.InformationLayer.SelectedEntities?
                                .ToArray() ??
                            []
                    }
            };

        lock (_frameGate)
        {
            ThrowIfDisposed();
            ThrowIfFaulted();
            if (Volatile.Read(ref _stopping) != 0)
            {
                return false;
            }
            _latestFrame = copied;
            Interlocked.Increment(ref _publishedFrames);
            _signal.Set();
        }

        return true;
    }

    public void ThrowIfFaulted()
    {
        Volatile.Read(ref _failure)?.Throw();
        _frontendOwner?.ThrowIfFaulted();
        if (_frontendOwner is not null && !_thread.IsAlive && !_started.IsSet && Volatile.Read(ref _stopping) == 0)
            throw new InvalidOperationException("The render owner stopped before gameplay initialization.");
    }

    public void Dispose()
    {
        if (Thread.CurrentThread == _thread)
        {
            throw new InvalidOperationException("The render owner cannot join itself.");
        }
        lock (_disposeGate)
        {
            lock (_frameGate)
            {
                if (_disposed)
                {
                    return;
                }
                Volatile.Write(ref _stopping, 1);
                _signal.Set();
            }
            _thread.Join();
            lock (_frameGate)
            {
                lock (_faultWaitGate)
                {
                    _faulted.Dispose();
                    _disposed = true;
                }
                _started.Dispose();
                _signal.Dispose();
                _disposed = true;
            }
        }
    }

    private void StartAndWait()
    {
        _thread.Start();
        try
        {
            _started.Wait();
            ThrowIfFaulted();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void RenderLoop() => RenderLoop(null);

    private void RenderLoop(IGraphicsDevice? existingDevice)
    {
        try
        {
            if (Volatile.Read(ref _stopping) != 0) return;
            if (_testRenderAction is not null)
            {
                RunTestRenderLoop(
                    _testRenderAction);
                return;
            }

            GraphicsWindowTarget initialTarget =
                _initialTarget ??
                throw new InvalidOperationException(
                    "Render target was not configured.");
            TerrainWorld terrain =
                _terrain ??
                throw new InvalidOperationException(
                    "Render terrain was not configured.");
            PresentationSnapshotBuffer snapshots =
                _snapshots ??
                throw new InvalidOperationException(
                    "Render snapshot buffer was not configured.");
            RtsCameraSettings cameraSettings =
                _cameraSettings ??
                throw new InvalidOperationException(
                    "Render camera settings were not configured.");

            using IGraphicsDevice? ownedGraphics = existingDevice is null
                ? GraphicsDeviceFactory.CreateForWindowTarget(initialTarget) : null;
            IGraphicsDevice graphics = existingDevice ?? ownedGraphics!;
            if (existingDevice is not null)
            {
                graphics.WaitForIdle();
                graphics.Resize(initialTarget.Suspended ? 0 : initialTarget.Width,
                    initialTarget.Suspended ? 0 : initialTarget.Height);
            }
            using var terrainRenderer =
                new TerrainRenderer(
                    graphics,
                    terrain,
                    runtimeAssets: _runtimeAssets,
                    lighting: _sceneLighting);
            using var instanceRenderer =
                new SimpleInstanceRenderer(
                    graphics,
                    _runtimeAssets,
                    _sceneLighting);
            using var gameplayOverlayRenderer =
                new DebugDrawRenderer(
                    graphics,
                    depthEnabled: false,
                    lineWidthPixels: 3.0f);
            using var debugDrawRenderer =
                new DebugDrawRenderer(
                    graphics,
                    depthEnabled: true);
            using var gameplayHudRenderer =
                new GameplayHudRenderer(
                    graphics,
                    _runtimeAssets);
            using var developmentOverlayRenderer =
                new DevelopmentOverlayRenderer(
                    graphics);
            using var frontendRenderer =
                new FrontendOverlayRenderer(
                    graphics);

            var renderWorld =
                new RenderWorld();
            var renderCamera =
                new RtsCamera(
                    cameraSettings);
            var gameplayDraw =
                new DebugDraw();
            var debugDraw =
                new DebugDraw();
            var frameTimingTracker =
                new FrameTimingTracker();
            var runtimeMetricsSampler = new RuntimeMetricsSampler();
            RuntimeMetricsView? lastMeasuredRunningRates = null;

            long previousFrameAt =
                Stopwatch.GetTimestamp();
            long snapshotObservedAt =
                previousFrameAt;
            long nextDiagnosticAt =
                previousFrameAt;
            FrameTimingMetrics frameTiming =
                default;
            double debugOverlayCpuMilliseconds =
                0.0;
            int width =
                initialTarget.Width;
            int height =
                initialTarget.Height;
            bool surfaceSuspended =
                initialTarget.Suspended ||
                width <= 0 ||
                height <= 0;

            if (_asynchronousStartup)
                _startup.End(StartupPhase.GameplayRenderer, sessionId: _startup.CurrentSessionId);
            _started.Set();

            while (Volatile.Read(ref _stopping) == 0)
            {
                _signal.WaitOne(IdleWait);

                if (Volatile.Read(ref _stopping) != 0)
                {
                    break;
                }

                ClientRenderFrame? frame =
                    TakeLatestFrame();

                if (!frame.HasValue)
                {
                    continue;
                }

                ClientRenderFrame current =
                    frame.Value;

                if (current.SurfaceSuspended)
                {
                    runtimeMetricsSampler.Reset();
                    if (!surfaceSuspended)
                    {
                        graphics.Resize(
                            0,
                            0);
                        surfaceSuspended =
                            true;
                    }

                    continue;
                }

                if (current.ViewportWidth <= 0 ||
                    current.ViewportHeight <= 0)
                {
                    runtimeMetricsSampler.Reset();
                    continue;
                }

                if (surfaceSuspended ||
                    current.ViewportWidth != width ||
                    current.ViewportHeight != height)
                {
                    graphics.Resize(
                        current.ViewportWidth,
                        current.ViewportHeight);
                    width =
                        current.ViewportWidth;
                    height =
                        current.ViewportHeight;
                    surfaceSuspended =
                        false;
                }

                renderCamera.ApplyState(
                    current.Camera);

                if (renderWorld.Update(
                        snapshots))
                {
                    snapshotObservedAt =
                        Stopwatch.GetTimestamp();
                }

                PresentationSnapshot? snapshot =
                    renderWorld.CurrentSnapshot;

                if (snapshot is null)
                {
                    runtimeMetricsSampler.Reset();
                    graphics.RenderFrame(GraphicsColor.ForgeLineClear, context =>
                    {
                        gameplayHudRenderer.Render(context, renderCamera, null, terrain.WorldBounds,
                            current.InformationLayer, current.ActionPanel, current.TacticalTargeting,
                            current.ActiveFormation, current.CombatGroups ?? CombatGroupOverviewView.Empty,
                            current.PreAlphaUx, current.Dpi, current.UiScale);
                        if (current.Frontend is FrontendSurfaceView loading)
                            frontendRenderer.Render(context, loading, current.UiScale);
                    });
                    continue;
                }

                long now =
                    Stopwatch.GetTimestamp();
                TimeSpan sinceSnapshot =
                    StopwatchElapsed(
                        snapshotObservedAt,
                        now);
                float renderAlpha =
                    RenderInterpolation.CalculateAlpha(
                        sinceSnapshot,
                        snapshot.TickDuration);

                RebuildDraw(
                    gameplayDraw,
                    enabled: true,
                    current.GameplayLines,
                    current.GameplayLabels);
                RebuildDraw(
                    debugDraw,
                    current.DebugOverlay.Enabled,
                    current.DebugLines,
                    current.DebugLabels);

                terrainRenderer.DebugChunksEnabled =
                    current.DebugOverlay.IsEnabled(
                        DebugOverlayCategory.World) ||
                    current.DebugOverlay.IsEnabled(
                        DebugOverlayCategory.Rendering);

                DevelopmentOverlayMetrics overlayMetrics =
                    CreateOverlayMetrics(
                        frameTiming,
                        snapshot,
                        terrainRenderer,
                        instanceRenderer,
                        gameplayOverlayRenderer,
                        debugDrawRenderer,
                        renderWorld,
                        debugOverlayCpuMilliseconds);
                PlayerExperienceSnapshot playerExperience =
                    snapshot.PlayerExperience ??
                    default;

                long renderStartedAt =
                    Stopwatch.GetTimestamp();
                ClientSimulationTelemetry telemetry = _simulationTelemetry?.Invoke() ?? default;
                RuntimeMetricsView runtimeMetrics = runtimeMetricsSampler.Sample(
                    TimeSpan.FromSeconds((double)renderStartedAt / Stopwatch.Frequency),
                    graphics.PresentedFrameCount, telemetry.CompletedTicks, snapshot.SessionId,
                    telemetry.SessionId == snapshot.SessionId ? telemetry.State : RuntimeSimulationState.Unavailable,
                    current.MetricsActive);
                if (runtimeMetrics.SimulationState == RuntimeSimulationState.Running &&
                    runtimeMetrics.FramesPerSecond.HasValue && runtimeMetrics.TicksPerSecond.HasValue)
                    lastMeasuredRunningRates = runtimeMetrics;

                ulong presentedBefore = !_startupPresented ? graphics.PresentedFrameCount : 0;
                graphics.RenderFrame(
                    GraphicsColor.ForgeLineClear,
                    context =>
                    {
                        terrainRenderer.Render(
                            context,
                            renderCamera);
                        instanceRenderer.Render(
                            context,
                            renderCamera,
                            renderWorld,
                            renderAlpha);
                        long debugStartedAt =
                            Stopwatch.GetTimestamp();
                        debugDrawRenderer.Render(
                            context,
                            renderCamera,
                            debugDraw);
                        debugOverlayCpuMilliseconds =
                            StopwatchElapsed(
                                debugStartedAt,
                                Stopwatch.GetTimestamp())
                            .TotalMilliseconds;

                        gameplayOverlayRenderer.Render(
                            context,
                            renderCamera,
                            gameplayDraw,
                            RtsUiLayout.ScaleForDpi(current.Dpi) * current.UiScale);
                        gameplayHudRenderer.Render(
                            context,
                            renderCamera,
                            snapshot,
                            terrain.WorldBounds,
                            current.InformationLayer,
                            current.ActionPanel,
                            current.TacticalTargeting,
                            current.ActiveFormation,
                            current.CombatGroups ??
                                CombatGroupOverviewView.Empty,
                            current.PreAlphaUx,
                            current.Dpi,
                            current.UiScale,
                            gameplayDraw,
                            runtimeMetrics,
                            current.HoverTooltip);
                        developmentOverlayRenderer.Render(
                            context,
                            overlayMetrics,
                            renderCamera,
                            debugDraw,
                            showDevelopmentMetrics:
                                current.OverlayEnabled,
                            uiScale:
                                current.UiScale);
                        if (current.Frontend is FrontendSurfaceView frontend)
                        {
                            frontendRenderer.Render(
                                context,
                                frontend,
                                current.UiScale);
                        }
                    });

                if (!_startupPresented && graphics.PresentedFrameCount > presentedBefore && current.Frontend is null &&
                    (!_expectedSession.HasValue || snapshot.SessionId == _expectedSession.Value))
                {
                    _startup.GameplayPresented();
                    _startupPresented = true;
                    Volatile.Write(ref _gameplayPresented, 1);
                }

                long renderFinishedAt =
                    Stopwatch.GetTimestamp();
                Interlocked.Increment(ref _completedFrames);
                Volatile.Write(ref _lastCompletedFrameAt, renderFinishedAt);
                TimeSpan frameElapsed =
                    StopwatchElapsed(
                        previousFrameAt,
                        renderFinishedAt);
                previousFrameAt =
                    renderFinishedAt;

                frameTiming =
                    frameTimingTracker.Record(
                        frameElapsed,
                        StopwatchElapsed(
                            renderStartedAt,
                            renderFinishedAt));

                GraphicsDiagnostics diagnostics =
                    graphics.Diagnostics;
                PublishQualification(
                    diagnostics,
                    frameTiming,
                    terrainRenderer.LastDiagnostics,
                    instanceRenderer.LastDiagnostics,
                    instanceRenderer.MaterialDiagnostics,
                    gameplayOverlayRenderer.LastDiagnostics,
                    debugDrawRenderer.LastDiagnostics,
                    snapshot.VfxMetrics,
                    renderWorld.InstanceCount,
                    _sceneLighting, gameplayHudRenderer.State, gameplayHudRenderer.LastRenderedVertexCount,
                    runtimeMetrics, lastMeasuredRunningRates);

                if (StopwatchElapsed(
                        nextDiagnosticAt,
                        renderFinishedAt) >=
                    DiagnosticInterval)
                {
                    Console.WriteLine(
                        $"[render:frame] thread={Environment.CurrentManagedThreadId} " +
                        $"tick={snapshot.Tick.Value} fps={frameTiming.FramesPerSecond:F1} " +
                        $"size={diagnostics.Surface.Width}x{diagnostics.Surface.Height} " +
                        $"instances={instanceRenderer.LastDiagnostics.VisibleInstances}/{renderWorld.InstanceCount} " +
                        $"lod={instanceRenderer.LastDiagnostics.HighLodInstances}/{instanceRenderer.LastDiagnostics.ReducedLodInstances} " +
                        $"runtimeMeshes={instanceRenderer.LastDiagnostics.RuntimeMeshInstances} " +
                        $"texturedRuntimeMeshes={instanceRenderer.LastDiagnostics.TexturedRuntimeMeshInstances} " +
                        $"fallbackMeshes={instanceRenderer.LastDiagnostics.FallbackMeshInstances} " +
                        $"draws={terrainRenderer.LastDiagnostics.DrawCalls + instanceRenderer.LastDiagnostics.DrawCalls + gameplayOverlayRenderer.LastDiagnostics.DrawCalls + debugDrawRenderer.LastDiagnostics.DrawCalls} " +
                        $"debugLines={debugDrawRenderer.LastDiagnostics.RenderedLines} " +
                        $"debugCpuMs={debugOverlayCpuMilliseconds:F3} " +
                        $"pendingRetirement={graphics.Diagnostics.Health.PendingRetirementCount} " +
                        $"terrainSubmitMs={terrainRenderer.LastDiagnostics.CpuSubmissionMilliseconds:F3} " +
                        $"terrainTextures={terrainRenderer.LastDiagnostics.TextureBindingsPerDraw} " +
                        $"light={_sceneLighting.DirectionalIntensity:F2}/{_sceneLighting.AmbientIntensity:F2} " +
                        $"exposure={_sceneLighting.Exposure:F2} " +
                        $"tone={_sceneLighting.ToneMapping} " +
                        $"vfx={snapshot.VfxMetrics.ActiveTransientEffects}/{snapshot.VfxMetrics.PoolCapacity} " +
                        $"vfxDropped={snapshot.VfxMetrics.TotalDropped}");
                    nextDiagnosticAt =
                        renderFinishedAt;
                }
            }

            graphics.WaitForIdle();
            GraphicsDiagnostics completedGraphics = graphics.Diagnostics;
            lock (_frameGate)
            {
                if (_latestQualification is { } qualification)
                {
                    _latestQualification = CompleteGpuQualification(qualification, completedGraphics);
                }
            }
        }
        catch (Exception exception)
        {
            Interlocked.CompareExchange(
                ref _failure,
                ExceptionDispatchInfo.Capture(
                    exception),
                null);
            _faulted.Set();
            _started.Set();
        }
    }

    private void RunTestRenderLoop(
        Action<ClientRenderFrame> renderAction)
    {
        _started.Set();

        while (Volatile.Read(ref _stopping) == 0)
        {
            _signal.WaitOne(IdleWait);

            if (Volatile.Read(ref _stopping) != 0)
            {
                break;
            }

            ClientRenderFrame? frame =
                TakeLatestFrame();

            if (frame.HasValue)
            {
                renderAction(
                    frame.Value);
                Interlocked.Increment(ref _completedFrames);
                Volatile.Write(ref _lastCompletedFrameAt, Stopwatch.GetTimestamp());
            }
        }
    }

    private ClientRenderFrame? TakeLatestFrame()
    {
        lock (_frameGate)
        {
            return _latestFrame;
        }
    }

    internal static ClientVisualQualificationSnapshot CompleteGpuQualification(
        in ClientVisualQualificationSnapshot snapshot,
        GraphicsDiagnostics completedGraphics) => snapshot with
        {
            GpuTimingAvailable = completedGraphics.GpuTimingAvailable,
            GpuMilliseconds = completedGraphics.GpuTimingAvailable ? completedGraphics.GpuFrameMilliseconds : null,
            DebugLayerEnabled = completedGraphics.Device.DebugLayerEnabled,
            DebugLayerWarningCount = completedGraphics.Debug.WarningCount,
            DebugLayerErrorCount = completedGraphics.Debug.ErrorCount
        };

    private void PublishQualification(
        GraphicsDiagnostics graphics,
        in FrameTimingMetrics frameTiming,
        in TerrainRenderDiagnostics terrain,
        in InstanceRenderDiagnostics instances,
        in RuntimeMaterialDiagnostics materials,
        in DebugDrawRenderDiagnostics gameplay,
        in DebugDrawRenderDiagnostics debug,
        in VfxPresentationMetrics vfx,
        int totalInstances,
        in SceneLightingSettings lighting,
        GameplayHudState hudState,
        int hudVertexCount,
        in RuntimeMetricsView runtimeMetrics,
        RuntimeMetricsView? lastMeasuredRunningRates)
    {
        var snapshot =
            new ClientVisualQualificationSnapshot(
                graphics.Device.AdapterName,
                graphics.Device.DedicatedVideoMemoryBytes,
                frameTiming.FramesPerSecond,
                frameTiming.FrameMilliseconds,
                frameTiming.CpuRenderMilliseconds,
                graphics.GpuFrameMilliseconds,
                terrain.VisibleChunks,
                terrain.TotalChunks,
                terrain.SubmittedTriangles,
                terrain.DrawCalls,
                instances.DrawCalls,
                terrain.DrawCalls +
                instances.DrawCalls +
                gameplay.DrawCalls +
                debug.DrawCalls,
                instances.VisibleInstances,
                totalInstances,
                instances.HighLodInstances,
                instances.ReducedLodInstances,
                vfx.ActiveTransientEffects,
                vfx.PoolCapacity,
                vfx.TotalDropped,
                graphics.Resources.LoadedTextureCount,
                graphics.Resources.ResidentTextureBytes,
                graphics.Resources.ShaderResourceDescriptorsUsed,
                graphics.Resources.ShaderResourceDescriptorCapacity,
                graphics.Resources.TextureBindingFailureCount,
                materials.LoadedMaterialCount,
                materials.LoadedAssetTextureCount,
                materials.BindingFailureCount,
                graphics.Surface,
                terrain.ControlTextureCount,
                terrain.TextureBindingsPerDraw,
                terrain.MaximumTextureSamplesPerPixel,
                terrain.CpuSubmissionMilliseconds)
            {
                GameplayHudState = hudState,
                GameplayHudVertexCount = hudVertexCount,
                RuntimeMetrics = runtimeMetrics,
                LastMeasuredRunningRates = lastMeasuredRunningRates,
                GpuTimingAvailable =
                    graphics.GpuTimingAvailable,
                DebugLayerEnabled =
                    graphics.Device.DebugLayerEnabled,
                PeakLoadedTextureCount =
                    graphics.Resources.PeakLoadedTextureCount,
                PeakResidentTextureBytes =
                    graphics.Resources.PeakResidentTextureBytes,
                PeakShaderResourceDescriptorsUsed =
                    graphics.Resources.PeakShaderResourceDescriptorsUsed,
                TextureUploadCount =
                    graphics.Resources.TextureUploadCount,
                TextureReleaseCount =
                    graphics.Resources.TextureReleaseCount,
                DebugLayerWarningCount =
                    graphics.Debug.WarningCount,
                DebugLayerErrorCount =
                    graphics.Debug.ErrorCount,
                RuntimeMeshInstances =
                    instances.RuntimeMeshInstances,
                TexturedRuntimeMeshInstances =
                    instances.TexturedRuntimeMeshInstances,
                FallbackMeshInstances =
                    instances.FallbackMeshInstances,
                LightingDirectionX =
                    lighting.DirectionToLight.X,
                LightingDirectionY =
                    lighting.DirectionToLight.Y,
                LightingDirectionZ =
                    lighting.DirectionToLight.Z,
                LightingDirectionalIntensity =
                    lighting.DirectionalIntensity,
                LightingAmbientIntensity =
                    lighting.AmbientIntensity,
                LightingExposure =
                    lighting.Exposure,
                LightingToneMapping =
                    lighting.ToneMapping.ToString()
            };

        lock (_frameGate)
        {
            _latestQualification =
                snapshot;
        }
    }

    private static void RebuildDraw(
        DebugDraw draw,
        bool enabled,
        DebugLine[] lines,
        DebugLabel[] labels)
    {
        draw.Clear();
        draw.Enabled =
            enabled &&
            (lines.Length > 0 ||
             labels.Length > 0);

        if (!draw.Enabled)
        {
            return;
        }

        for (int index = 0;
             index < lines.Length;
             index++)
        {
            DebugLine line =
                lines[index];
            draw.Line(
                line.Start,
                line.End,
                line.Color);
        }

        for (int index = 0;
             index < labels.Length;
             index++)
        {
            DebugLabel label =
                labels[index];
            draw.Label(
                label.Position,
                label.Text,
                label.Color);
        }
    }

    private static DevelopmentOverlayMetrics CreateOverlayMetrics(
        in FrameTimingMetrics frameTiming,
        PresentationSnapshot snapshot,
        TerrainRenderer terrainRenderer,
        SimpleInstanceRenderer instanceRenderer,
        DebugDrawRenderer gameplayOverlayRenderer,
        DebugDrawRenderer debugDrawRenderer,
        RenderWorld renderWorld,
        double debugOverlayCpuMilliseconds)
    {
        TerrainRenderDiagnostics terrain =
            terrainRenderer.LastDiagnostics;
        InstanceRenderDiagnostics instances =
            instanceRenderer.LastDiagnostics;
        DebugDrawRenderDiagnostics gameplay =
            gameplayOverlayRenderer.LastDiagnostics;
        DebugDrawRenderDiagnostics debug =
            debugDrawRenderer.LastDiagnostics;
        SimulationDiagnosticsSnapshot? simulationDiagnostics =
            snapshot.SimulationDiagnostics;

        double jobExecutionMilliseconds =
            simulationDiagnostics?.Jobs?
                .TotalExecutionDuration
                .TotalMilliseconds ??
            0.0;

        return new DevelopmentOverlayMetrics(
            frameTiming.FramesPerSecond,
            frameTiming.FrameMilliseconds,
            frameTiming.CpuRenderMilliseconds,
            snapshot.Tick.Value,
            simulationDiagnostics?
                .LastTickDuration
                .TotalMilliseconds ??
            0.0,
            snapshot.SimulationEntityCount,
            terrain.VisibleChunks,
            terrain.TotalChunks,
            terrain.DrawCalls +
            instances.DrawCalls +
            gameplay.DrawCalls +
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
            0)
        {
            GameplayOverlayLines =
                gameplay.RenderedLines,
            DebugOverlayLines =
                debug.RenderedLines,
            DebugOverlayDroppedLines =
                debug.DroppedLines,
            DebugOverlayDrawCalls =
                debug.DrawCalls,
            DebugOverlayCpuMilliseconds =
                debugOverlayCpuMilliseconds
        };
    }

    private static TimeSpan StopwatchElapsed(
        long start,
        long end) =>
        TimeSpan.FromSeconds(
            (end - start) /
            (double)Stopwatch.Frequency);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed),
            this);
    }
}

internal readonly record struct ClientRenderHealth(
    bool ThreadAlive,
    bool Stopping,
    long PublishedFrames,
    long CompletedFrames,
    long LastCompletedFrameTimestamp,
    bool Faulted);
