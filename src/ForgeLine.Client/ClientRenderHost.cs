using ForgeLine.Presentation.RenderContracts;
using ForgeLine.Audio;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using ForgeLine.World;
using ForgeLine.Platform.Windows;

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
    public RenderAssetFrameMetrics? AssetPreparation { get; init; }
    public ClientRenderWorkMetrics RenderWork { get; init; }
    public ClientPublicationMetrics Publication { get; init; }
    public bool GpuTimingAvailable { get; init; }
    public GraphicsFrameDiagnostics? FramePasses { get; init; }
    public GraphicsDirectionalShadowDiagnostics? DirectionalShadows { get; init; }
    public GraphicsAtmosphereDiagnostics? Atmosphere { get; init; }
    public ParticleVfxDiagnostics Particles { get; init; }
    public GraphicsParticleDiagnostics? ParticleGpu { get; init; }
    public RenderQualityProfile QualityProfile { get; init; }
    public GraphicsPostProcessingDiagnostics? PostProcessing { get; init; }
    public int Lod1Instances { get; init; }
    public int Lod2Instances { get; init; }
    public int AnimatedVegetationInstances { get; init; }
    public bool TerrainSurfaceDetailEnabled { get; init; }
    public int TerrainRootSignatureDwords { get; init; }

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
    HoverTooltipView HoverTooltip = default,
    OperationsView Operations = default, UiFeedbackSettings FeedbackSettings = default, WorldAudioSettings WorldAudio = default,
    NotificationExpansion NotificationExpansion = default, NavigationDiagnosticsView NavigationDiagnostics = default,
    TacticalDisplaySettings TacticalDisplay = default,
    StatisticsOverlayPickerView StatisticsPicker = default);

internal sealed partial class ClientRenderHost : IDisposable
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
    private readonly LiveSessionOrigin? _sessionOrigin;
    private readonly ClientFrontendRenderHost? _frontendOwner;
    private readonly Func<ClientSimulationTelemetry>? _simulationTelemetry;
    private readonly Func<PlatformWorkloadTelemetry>? _platformWorkload;
    private readonly int _ownerObservationVersion, _ownerCycleCapacity;
    private OwnerCycleObservations? _ownerCycles;
    private int _observedInstanceHighWater;
    private readonly ClientRenderPublication _publication = new();
    private object _frameGate => _publication.Gate;
    private ObjectActionRingView _latestActionRing;
    public ObjectActionRingView LatestActionRing { get { lock (_frameGate) return _latestActionRing; } }
    private readonly object _disposeGate = new();
    private readonly object _faultWaitGate = new();
    private readonly GraphicsWindowTarget? _initialTarget;
    private readonly TerrainWorld? _terrain;
    private readonly TerrainPresentationProfile? _terrainProfile;
    private readonly PresentationSnapshotBuffer? _snapshots;
    private readonly RtsCameraSettings? _cameraSettings;
    private readonly RuntimeAssetCatalog? _runtimeAssets;
    private readonly SceneLightingSettings _sceneLighting;
    private readonly SceneAtmosphereSettings _sceneAtmosphere = SceneAtmosphereSettings.Default;
    private readonly ParticleSettings _particleSettings = ParticleSettings.Default;
    private readonly RenderQualitySettings _renderQuality = new();
    private readonly Action<ClientRenderFrame>? _testRenderAction;
    private readonly AutoResetEvent _signal = new(false);
    private readonly ManualResetEventSlim _started = new(false);
    private readonly ManualResetEventSlim _faulted = new(false);
    private readonly Thread _thread;

    private ClientVisualQualificationSnapshot? _latestQualification;
    private ExceptionDispatchInfo? _failure;
    private int _stopping;
    private int _telemetryWindowPending = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_RENDER_TELEMETRY_OUTPUT")) ? 0 : 1;
    internal bool TelemetryWindowCompleted => Volatile.Read(ref _telemetryWindowPending) == 0;
    private int _terrainPreparing;
    internal bool IsPreparingTerrain => Volatile.Read(ref _terrainPreparing) != 0;
    internal bool NeedsFramePublication => _publication.NeedsFramePublication;
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
        Func<ClientSimulationTelemetry>? simulationTelemetry = null,
        SceneAtmosphereSettings? sceneAtmosphere = null,
        ParticleSettings? particles = null,
        RenderQualitySettings? renderQuality = null,
        TerrainPresentationProfile? terrainProfile = null,
        ReferenceFixtureRecipe? referenceFixture = null, LiveSessionOrigin? sessionOrigin = null,
        int ownerObservationVersion = 0,int ownerCycleCapacity = 8192,Func<PlatformWorkloadTelemetry>? platformWorkload = null)
    {
        _startup = startup ?? StartupDiagnostics.Disabled;
        _asynchronousStartup = asynchronousStartup;
        _expectedSession = expectedSession;
        _sessionOrigin = sessionOrigin;
        _frontendOwner = transitionHost;
        _simulationTelemetry = simulationTelemetry;
        _platformWorkload=platformWorkload;
        _ownerObservationVersion=referenceFixture?.OwnerObservationVersion ?? ownerObservationVersion;
        _ownerCycleCapacity=referenceFixture?.OwnerCycleCapacity ?? ownerCycleCapacity;
        if(_ownerObservationVersion is not (0 or 1))throw new ArgumentOutOfRangeException(nameof(ownerObservationVersion));
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
        _terrainProfile = terrainProfile;
        _referenceFixture = referenceFixture;
        if (referenceFixture is not null)
        {
            _fixtureSequence = new(referenceFixture.FrameCount);
            _fixtureFrames = new ReferenceFixtureFrameObservation[referenceFixture.Samples];
            if (referenceFixture.CollectorMode != ReferenceCollectorMode.Full) _fixtureCollectorControl = new(referenceFixture.Warmups, referenceFixture.Samples, referenceFixture.CollectorMode);
        }
        _sceneLighting =
            sceneLighting ??
            SceneLightingSettings.Default;
        _sceneLighting.Validate();
        _sceneAtmosphere = sceneAtmosphere ?? SceneAtmosphereSettings.Default;
        _sceneAtmosphere.Validate();
        _particleSettings = particles ?? ParticleSettings.Default;
        _particleSettings.Validate();
        _renderQuality = renderQuality ?? new();
        _renderQuality.Validate();

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

        return StoreFrame(ClientRenderPublication.Copy(frame));
    }

    public bool Publish(in ClientRenderFrame frame, DebugDraw gameplay, DebugDraw debug,
        IReadOnlyCollection<ForgeLine.Core.EntityId> selection)
    {
        ThrowIfDisposed();
        ThrowIfFaulted();
        return StoreFrame(ClientRenderPublication.CopyDraws(frame, gameplay, debug, selection));
    }

    private bool StoreFrame(in ClientRenderFrame copied)
    {
        lock (_frameGate)
        {
            ThrowIfDisposed();
            ThrowIfFaulted();
            if (Volatile.Read(ref _stopping) != 0)
            {
                return false;
            }
            _publication.StoreCopied(copied);
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

            using var assetPreparation = new AssetPreparationScope(() => Volatile.Read(ref _stopping) != 0);

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
                ? GraphicsDeviceFactory.CreateForWindowTarget(initialTarget, _referenceFixture is null ? null : new()
                { EnableVSync = false, EnableMemoryDiagnostics = true, ForceSoftwareAdapter = _referenceFixture.Software, AllowSoftwareAdapterFallback = _referenceFixture.Software }) : null;
            IGraphicsDevice graphics = existingDevice ?? ownedGraphics!;
            using var liveTelemetry = _referenceFixture is null ? LiveRenderTelemetry.FromEnvironment(graphics, _runtimeAssets)
                : ReferenceCollectorControl.CreateOptionalCollector(_referenceFixture.CollectorMode, (IGraphicsRenderTelemetry)graphics, _referenceFixture.Samples,
                    Path.Combine(_referenceFixture.OutputRoot, "owner.json"), _referenceFixture.Warmups,
                    _referenceFixture.RuntimeFingerprint, graphics.Diagnostics.Device);
            liveTelemetry?.BindSessionOrigin(_referenceFixture is null ? _sessionOrigin : null);
            if (_referenceFixture is null && liveTelemetry is not null && _runtimeAssets is not null)
            {
                object seal = ReferenceRuntimeSeal.VerifyLoaded(_runtimeAssets);
                string runtime = VisualReferenceCapture.RuntimeFingerprint(_runtimeAssets.RuntimeRoot);
                var binding = ReferenceAdapterCheckpoint.PublishAndWait(
                    Environment.GetEnvironmentVariable("FORGELINE_RENDER_TELEMETRY_ADAPTER_BINDING"), graphics, runtime,
                    initialTarget.NativeHandle.ToInt64(), () => Volatile.Read(ref _stopping) == 0);
                liveTelemetry.BindProducerEvidence(binding?.Evidence, seal);
            }
            if (_referenceFixture is { } adapterFixture)
                _fixtureAdapterBinding = ReferenceAdapterCheckpoint.PublishAndWait(adapterFixture.AdapterBindingDirectory, graphics, adapterFixture.RuntimeFingerprint, initialTarget.NativeHandle.ToInt64(), () => Volatile.Read(ref _stopping) == 0)?.Evidence;
            var qualitySettings = _referenceFixture?.Settings ?? ClientGraphicsOverrides.Read(_renderQuality with
            {
                Atmosphere = _renderQuality.Atmosphere ?? (_sceneAtmosphere.Quality != AtmosphereQuality.Disabled ? _sceneAtmosphere.Quality : null),
                Particles = _renderQuality.Particles ?? (_particleSettings != ParticleSettings.Default ? _particleSettings.Quality : null)
            });
            var composition = ClientRenderConfiguration.Resolve(graphics, qualitySettings,
                initialTarget.Width, initialTarget.Height, _sceneLighting, _referenceFixture?.Atmosphere ?? _sceneAtmosphere.Graphics);
            composition.Apply(graphics);
            ConfigurePostWindowControl(liveTelemetry,composition);
            if (_referenceFixture is not null) { _fixtureComposition = composition; _fixtureDevice = graphics.Diagnostics.Device; }
            RenderQualityProfile qualityProfile = composition.Profile;
            SceneLightingSettings lighting = composition.Lighting;
            GraphicsAtmosphereSettings atmosphere = composition.Atmosphere;
            var shadowSettings = qualityProfile.Shadows;
            if (existingDevice is not null)
            {
                graphics.WaitForIdle();
                graphics.Resize(initialTarget.Suspended ? 0 : initialTarget.Width, initialTarget.Suspended ? 0 : initialTarget.Height);
            }
            using var terrainRenderer = new TerrainRenderer(graphics, terrain, presentationProfile: _terrainProfile, runtimeAssets: _runtimeAssets,
                lighting: lighting, surfaceDetail: qualityProfile.TerrainDetail, textureDetail: qualityProfile.Textures,
                residency: qualityProfile.TerrainResidency, environment: qualityProfile.Environment);
            using var instanceRenderer = new SimpleInstanceRenderer(graphics, _runtimeAssets, lighting,
                lodDiameterScale: qualityProfile.LodDiameterScale, textureDetail: qualityProfile.Textures, vegetationMotion: qualityProfile.VegetationMotion,
                environment: qualityProfile.Environment, vegetation: qualityProfile.Vegetation);
            if (_referenceFixture is not null) instanceRenderer.EnableContentEvidence();
            using var particleRenderer = new ParticleVfxRenderer(graphics, _runtimeAssets, qualityProfile.Particles, lighting);
            long lastParticleFrame = Stopwatch.GetTimestamp();
            var dockFeedback = new UiInteractionFeedback();
            var ringMotion = new ObjectActionRingMotion();
            var frontendFeedback = new UiInteractionFeedback();
            using var uiAudio = new WaveOutUiAudio(observeWorkload:_ownerObservationVersion==1);
            using var worldAudio = new WaveOutWorldAudio(expectedSession: _expectedSession,observeWorkload:_ownerObservationVersion==1);
            if(_ownerObservationVersion==1)
            {
                if(_referenceFixture is null && liveTelemetry is null)throw new InvalidOperationException("Live owner observations require the explicit telemetry output stream.");
                OwnerWorkloadFacts CaptureFacts()
                {
                    long started=Stopwatch.GetTimestamp();
                    OwnerWorkloadSnapshot simulation=_simulationTelemetry?.Invoke().Workload ?? default;
                    PlatformWorkloadTelemetry platform=_platformWorkload?.Invoke() ?? default;
                    var ui=uiAudio.Workload;var audio=worldAudio.Workload;
                    OwnerSnapshotPopulation population=default;
                    PresentationSnapshot? immutable=_referenceFixture?.Snapshot;
                    if(immutable is not null || snapshots.TryReadLatest(out immutable))
                    {
                        _observedInstanceHighWater=Math.Max(_observedInstanceHighWater,immutable.InstanceCount);
                        population=new(true,immutable.SessionId.Value,immutable.Tick.Value,immutable.SimulationEntityCount,immutable.InstanceCount,_observedInstanceHighWater,_referenceFixture is null ? "latest-published-observed" : "actual-authored-recipe");
                    }
                    return new(started,Stopwatch.GetTimestamp(),simulation,platform,ui,audio,_referenceFixture is not null,_platformWorkload is not null,population);
                }
                _ownerCycles=new(_ownerCycleCapacity,liveTelemetry is not null,CaptureFacts);
                if(liveTelemetry is not null){liveTelemetry.ObserveAbsoluteReadBrackets=true;liveTelemetry.OwnerCycles=_ownerCycles;}
            }
            using var gameplayOverlayRenderer =
                new DebugDrawRenderer(
                    graphics,
                    depthEnabled: false,
                    lineWidthPixels: 3.0f);
            using var selectedOrderRenderer =
                new DebugDrawRenderer(graphics, depthEnabled: false, lineWidthPixels: 1.0f,
                    maximumLines: 8192);
            var selectedOrderOverlay = new SelectedUnitOrderOverlay();
            using var debugDrawRenderer =
                new DebugDrawRenderer(
                    graphics,
                    depthEnabled: true);
            using var dockingZoneRenderer =
                new DebugDrawRenderer(graphics, depthEnabled: true, lineWidthPixels: 2.0f);
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

            if (_runtimeAssets is not null) frontendRenderer.PrepareSkin(_runtimeAssets);

            var renderWorld =
                _referenceFixture?.CreateWorld() ?? new RenderWorld();
            var renderCamera =
                new RtsCamera(
                    cameraSettings);
            if(_referenceFixture is {CaptureStageDiagnostics:true} inputFixture)
                _fixtureWorldInputInspector=ReferenceWorldInputDiagnostics.Create(inputFixture.OutputRoot,"host-fixed","actual-render-host",inputFixture.RuntimeFingerprint,
                    inputFixture.Fingerprint,inputFixture.InputBytesBase64,qualityProfile,inputFixture.RepeatCaptures,terrainRenderer,instanceRenderer,particleRenderer,graphics,renderCamera);
            var gameplayDraw =
                new DebugDraw();
            var debugDraw =
                new DebugDraw();
            var dockingZoneDraw = new DebugDraw { Enabled = true };
            var selectedOrderDraw = new DebugDraw { Enabled = true };
            var frameTimingTracker =
                new FrameTimingTracker();
            var runtimeMetricsSampler = new RuntimeMetricsSampler();
            RuntimeMetricsView? lastMeasuredRunningRates = null;

            long previousFrameAt =
                Stopwatch.GetTimestamp();
            long visualMotionStartedAt = previousFrameAt;
            long snapshotObservedAt =
                previousFrameAt;
            long nextDiagnosticAt =
                previousFrameAt;
            FrameTimingMetrics frameTiming =
                default;
            double debugOverlayCpuMilliseconds =
                0.0;
            var surface = new ClientRenderSurface(initialTarget.Width, initialTarget.Height, initialTarget.Suspended);

            Action<IGraphicsCommandContext>? fixtureRecord = null;
            Action? fixturePrepareReadback = null;
            if (_referenceFixture is { } fixtureSetup)
            {
                renderCamera.ApplyState(fixtureSetup.InitialCamera);
                particleRenderer.Prepare(renderWorld, renderCamera, initialTarget.Width, initialTarget.Height, 0);
                particleRenderer.Prepare(renderWorld, renderCamera, initialTarget.Width, initialTarget.Height, .1);
                particleRenderer.Prepare(renderWorld, renderCamera, initialTarget.Width, initialTarget.Height, .1);
                var setupShadow = shadowSettings.Enabled ? DirectionalShadowProjection.Create(fixtureSetup.InitialCamera, lighting, shadowSettings) : (GraphicsDirectionalShadowFrame?)null;
                int setupWidth = Math.Max(1, (int)Math.Ceiling(initialTarget.Width * (double)qualityProfile.ResolutionScale));
                int setupHeight = Math.Max(1, (int)Math.Ceiling(initialTarget.Height * (double)qualityProfile.ResolutionScale));
                terrainRenderer.Prepare(renderCamera, setupWidth, setupHeight, setupShadow);
                instanceRenderer.Prepare(renderCamera, renderWorld, new(setupWidth, setupHeight, 1, setupShadow, shadowSettings.MaximumCasters, fixtureSetup.VisualSeconds, true), particleRenderer);
            }
            if (_asynchronousStartup)
                _startup.End(StartupPhase.GameplayRenderer, sessionId: _startup.CurrentSessionId);
            _started.Set();

            bool presentedLastIteration = false;
            long terrainDeferredFrames = 0;
            bool cycleLoopCompleted=false;
            try
            {
            while (Volatile.Read(ref _stopping) == 0 && ObserveOwnerCycleEdge(_ownerCycles,this))
            {
                // Active rendering is paced by the device, not an extra 16 ms
                // mailbox sleep. Suspended/occluded/empty work still sleeps.
                long signalWaitStart = liveTelemetry is null ? 0 : Stopwatch.GetTimestamp();
                _signal.WaitOne(presentedLastIteration ? TimeSpan.Zero : IdleWait);
                long signalWaitEnd = liveTelemetry is null ? 0 : Stopwatch.GetTimestamp();
                presentedLastIteration = false;

                if (Volatile.Read(ref _stopping) != 0)
                {
                    break;
                }

                int fixtureOrdinal = -1;
                if (_referenceFixture is not null)
                {
                    if (Volatile.Read(ref _fixtureCaptureRequested) != 0 && fixtureRecord is not null && fixturePrepareReadback is not null)
                    {
                        graphics.WaitForIdle(); liveTelemetry?.DrainGpu();
                        CaptureFixtureReadback(graphics, fixtureRecord, fixturePrepareReadback, instanceRenderer, renderCamera.CaptureState);
                        continue;
                    }
                    if (!_fixtureSequence!.TryRead(out fixtureOrdinal)) continue;
                }
                long collectorControlStart = _fixtureCollectorControl is null ? 0 : Stopwatch.GetTimestamp();
                OwnerWorkloadFacts beginFacts=_ownerCycles?.Facts() ?? default;
                LiveTelemetryStart liveStart = liveTelemetry?.Begin(new(signalWaitStart, signalWaitEnd)) ?? default;
                OwnerWorkloadFacts beginFactsAfter=_ownerCycles?.Facts() ?? default;
                _ownerCycles?.BeginPayload(liveStart,beginFacts,beginFactsAfter);
                ClientRenderFrame? frame =
                    TakeLatestFrame();

                if (!frame.HasValue)
                {
                    RejectFixtureEarlyExit("empty");
                    CompleteLiveTelemetry(liveTelemetry, liveStart, "empty");
                    continue;
                }

                ClientRenderFrame current =
                    frame.Value;
                frontendRenderer.ConfigureDisplay(current.TacticalDisplay);

                if (!surface.Update(graphics, current.ViewportWidth, current.ViewportHeight, current.SurfaceSuspended, out bool surfaceChanged))
                {
                    dockFeedback.Cancel(); frontendFeedback.Cancel(); uiAudio.StopPlayback(); worldAudio.StopPlayback();
                    runtimeMetricsSampler.Reset();
                    RejectFixtureEarlyExit("suspended");
                    CompleteLiveTelemetry(liveTelemetry, liveStart, "suspended");
                    continue;
                }

                if (!current.MetricsActive) { dockFeedback.Cancel(); frontendFeedback.Cancel(); uiAudio.StopPlayback(); worldAudio.StopPlayback(); }
                if (surfaceChanged)
                {
                    dockFeedback.Cancel(); frontendFeedback.Cancel(); uiAudio.StopPlayback(); worldAudio.StopPlayback();
                    runtimeMetricsSampler.Reset();
                    previousFrameAt = lastParticleFrame = Stopwatch.GetTimestamp();
                    frameTimingTracker = new FrameTimingTracker();
                    frameTiming = default;
                }
                int width = surface.Width, height = surface.Height;

                renderCamera.ApplyState(
                    current.Camera);

                if (_referenceFixture is null && renderWorld.Update(
                        snapshots))
                {
                    snapshotObservedAt =
                        Stopwatch.GetTimestamp();
                }

                PresentationSnapshot? snapshot =
                    renderWorld.CurrentSnapshot;

                if (snapshot is null)
                {
                    worldAudio.StopPlayback();
                    runtimeMetricsSampler.Reset();
                    ulong loadingPresented = graphics.PresentedFrameCount;
                    graphics.RenderFrame(GraphicsColor.ForgeLineClear, context =>
                    {
                        context.BeginPass(GraphicsFramePass.Overlay);
                        gameplayHudRenderer.Render(context, renderCamera, null, terrain.WorldBounds,
                            current.InformationLayer, current.ActionPanel, current.TacticalTargeting,
                            current.ActiveFormation, current.CombatGroups ?? CombatGroupOverviewView.Empty,
                            current.PreAlphaUx, current.Dpi, current.UiScale);
                        if (current.Frontend is FrontendSurfaceView loading)
                            frontendRenderer.Render(context, loading, current.UiScale);
                    });
                    presentedLastIteration = current.MetricsActive && graphics.PresentedFrameCount > loadingPresented;
                    RejectFixtureEarlyExit("loading");
                    CompleteLiveTelemetry(liveTelemetry, liveStart, "loading");
                    continue;
                }

                worldAudio.Update(snapshot, current.Camera, current.WorldAudio,
                    current.MetricsActive && !current.SurfaceSuspended && current.Frontend is null && current.PreAlphaUx.Mode == PreAlphaUxMode.None &&
                    (!_expectedSession.HasValue || snapshot.SessionId == _expectedSession.Value));
                long now =
                    Stopwatch.GetTimestamp();
                TimeSpan uiNow = TimeSpan.FromSeconds((double)now / Stopwatch.Frequency);
                var animatedPanel = dockFeedback.Gameplay(current.ActionPanel, snapshot, uiNow,
                    current.MetricsActive && current.Frontend is null && current.PreAlphaUx.Mode == PreAlphaUxMode.None, current.FeedbackSettings, qualityProfile.Active);
                FrontendSurfaceView? animatedFrontend = current.Frontend.HasValue ? frontendFeedback.Frontend(
                    current.Frontend.Value with { FeedbackSettings = current.FeedbackSettings, BackgroundQuality = qualityProfile.Active },
                    uiNow, current.MetricsActive, graphics.Diagnostics.Device.IsSoftwareAdapter) : null;
                if (animatedFrontend.HasValue ? frontendFeedback.ResetAudio : dockFeedback.ResetAudio) uiAudio.StopPlayback();
                uiAudio.Update(animatedFrontend.HasValue ? frontendFeedback.Cue : dockFeedback.Cue,
                    current.FeedbackSettings, uiNow, current.MetricsActive && (current.Frontend.HasValue || current.PreAlphaUx.Mode == PreAlphaUxMode.None));
                TimeSpan sinceSnapshot =
                    StopwatchElapsed(
                        snapshotObservedAt,
                        now);
                float renderAlpha =
                    RenderInterpolation.CalculateAlpha(
                        sinceSnapshot,
                        snapshot.TickDuration);
                if (_referenceFixture is not null) renderAlpha = 1;
                double particleDelta = _referenceFixture is not null ? 0 : Stopwatch.GetElapsedTime(lastParticleFrame,now).TotalSeconds;

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
                if (_referenceFixture is null && current.PreAlphaUx.Mode == PreAlphaUxMode.None &&
                    current.Frontend is null && !current.SurfaceSuspended)
                {
                    selectedOrderOverlay.Draw(
                        selectedOrderDraw,
                        renderWorld,
                        current.InformationLayer.SelectedEntities,
                        snapshot.SessionId,
                        renderCamera,
                        renderAlpha,
                        current.ViewportHeight,
                        RtsUiLayout.ScaleForDpi(current.Dpi) * current.UiScale,
                        uiNow,
                        current.FeedbackSettings.ReducedMotion ||
                            qualityProfile.Active == RenderQuality.Low);
                }
                else
                {
                    selectedOrderDraw.Clear();
                }
                dockingZoneDraw.Clear();
                if (current.MetricsActive && current.PreAlphaUx.Mode == PreAlphaUxMode.None)
                    LogisticsZoneOverlay.Draw(dockingZoneDraw, snapshot,
                        current.InformationLayer.SelectedEntities,
                        snapshot.Hover?.Entity ?? default,
                        current.ActionPanel.Mode == PlayerActionPanelMode.Logistics);

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

                ulong presentedBefore = graphics.PresentedFrameCount;
                int worldWidth = Math.Max(1, (int)Math.Ceiling(width * (double)qualityProfile.ResolutionScale));
                int worldHeight = Math.Max(1, (int)Math.Ceiling(height * (double)qualityProfile.ResolutionScale));
                GraphicsDirectionalShadowFrame? preparedShadow = shadowSettings.Enabled
                    ? DirectionalShadowProjection.Create(current.Camera, lighting, shadowSettings) : null;
                long terrainStarted = Stopwatch.GetTimestamp();
                bool terrainReady;
                if (_referenceFixture is not null) { terrainRenderer.Prepare(renderCamera, worldWidth, worldHeight, preparedShadow); terrainReady = true; }
                else terrainReady = terrainRenderer.PrepareStep(renderCamera, worldWidth, worldHeight, preparedShadow);
                long terrainEndedAt = Stopwatch.GetTimestamp();
                double terrainPrepareMs = Stopwatch.GetElapsedTime(terrainStarted, terrainEndedAt).TotalMilliseconds;
                if (!terrainReady)
                {
                    Volatile.Write(ref _terrainPreparing, 1);
                    terrainDeferredFrames++;
                    // Never record a partial main/light union. Keep the event owner
                    // responsive and show a truthful preparation surface instead.
                    graphics.RenderFrame(GraphicsColor.ForgeLineClear, context =>
                    {
                        context.BeginPass(GraphicsFramePass.Overlay);
                        frontendRenderer.Render(context, current.Frontend ??
                            FrontendSurfaceView.Loading("Preparing terrain", false, 0), current.UiScale);
                    });
                    presentedLastIteration = current.MetricsActive && graphics.PresentedFrameCount > presentedBefore;
                    lock (_frameGate)
                    {
                        _latestActionRing = default;
                        if (_latestQualification is { } pending)
                            _latestQualification = pending with
                            {
                                RenderWork = new(terrainPrepareMs, 0, true, terrainDeferredFrames,
                                    instanceRenderer.PreparationReuse),
                                Publication = _publication.Metrics
                            };
                    }
                    Interlocked.Increment(ref _completedFrames);
                    Volatile.Write(ref _lastCompletedFrameAt, Stopwatch.GetTimestamp());
                    RejectFixtureEarlyExit("terrain-preparing");
                    CompleteLiveTelemetry(liveTelemetry, liveStart, "terrain-preparing", terrainStarted, terrainEndedAt);
                    continue;
                }
                lastParticleFrame = now;
                long assetsStarted = Stopwatch.GetTimestamp();
                if (_referenceFixture is null || !instanceRenderer.AssetsPrepared) instanceRenderer.Prepare(renderCamera, renderWorld, new(
                    worldWidth, worldHeight, renderAlpha, preparedShadow, shadowSettings.MaximumCasters,
                    _referenceFixture?.VisualSeconds ?? (double)(renderStartedAt - visualMotionStartedAt) / Stopwatch.Frequency, _referenceFixture is not null || current.MetricsActive), particleRenderer);
                double assetPrepareMs = Stopwatch.GetElapsedTime(assetsStarted).TotalMilliseconds;
                ObjectActionRingView inputRing = default;
                long livePrepareEndedAt = Stopwatch.GetTimestamp();
                Action<IGraphicsCommandContext> recordFrame = context =>
                    {
                        var worldPasses = new ClientWorldPassRecorder(terrainRenderer, instanceRenderer, particleRenderer,
                            renderWorld, renderCamera, renderAlpha,
                            _referenceFixture?.VisualSeconds ?? (double)(renderStartedAt - visualMotionStartedAt) / Stopwatch.Frequency, particleDelta,
                            _referenceFixture is not null || current.PreAlphaUx.Mode is PreAlphaUxMode.Paused or PreAlphaUxMode.Help || !current.MetricsActive);
                        ClientWorldPassSequence.Record(context,
                            atmosphere.Enabled ? SceneAtmosphereSettings.CreateView(renderCamera, context.Width, context.Height) : null,
                            preparedShadow, shadowSettings, ref worldPasses);
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

                        dockingZoneRenderer.Render(context, renderCamera, dockingZoneDraw);
                        selectedOrderRenderer.Render(
                            context, renderCamera, selectedOrderDraw,
                            RtsUiLayout.ScaleForDpi(current.Dpi) * current.UiScale);
                        if (_referenceFixture is not null) gameplayOverlayRenderer.Render(context, renderCamera, gameplayDraw);
                        else gameplayOverlayRenderer.Render(
                            context,
                            renderCamera,
                            gameplayDraw,
                            RtsUiLayout.ScaleForDpi(current.Dpi) * current.UiScale);
                        if (_referenceFixture is not null)
                        {
                            gameplayHudRenderer.Render(context, renderCamera, snapshot, terrain.WorldBounds, current.InformationLayer,
                                current.ActionPanel, current.TacticalTargeting, current.ActiveFormation, CombatGroupOverviewView.Empty, default, current.Dpi, current.UiScale,
                                runtimeMetrics: new RuntimeMetricsView(60, 20, RuntimeSimulationState.Running), showRuntimeMetrics: false);
                            return;
                        }
                        var ringLayout = GameplayHudLayout.Create(context.Width, context.Height, current.Dpi, current.UiScale);
                        if (current.InformationLayer.MinimapEnabled &&
                            !current.InformationLayer.MinimapBounds.IsEmpty &&
                            current.InformationLayer.MinimapBounds.X >= ringLayout.SafeArea.X &&
                            current.InformationLayer.MinimapBounds.Y >= ringLayout.SafeArea.Y &&
                            current.InformationLayer.MinimapBounds.Right <= ringLayout.SafeArea.Right &&
                            current.InformationLayer.MinimapBounds.Bottom <= ringLayout.SafeArea.Bottom)
                            ringLayout = ringLayout with { Minimap = current.InformationLayer.MinimapBounds };
                        Span<HudRect> occupied = stackalloc HudRect[8];
                        RadialActionLayout.ResolveObstacles(occupied,ringLayout,current.PreAlphaUx.Onboarding,current.Operations,current.CombatGroups,
                            ActionableAlertLayout.Resolve(snapshot,ringLayout,localFeedback:current.PreAlphaUx.Placement.Visible ? "" : current.PreAlphaUx.InteractionHint,
                                expansion: current.NotificationExpansion));
                        animatedPanel = animatedPanel with { Ring = animatedPanel.Ring.Project(renderWorld, renderCamera,
                            renderAlpha, snapshot, ringLayout, occupied) };
                        inputRing = animatedPanel.Ring;
                        animatedPanel = animatedPanel with { Ring = ringMotion.Update(animatedPanel.Ring, uiNow,
                            current.MetricsActive && !current.SurfaceSuspended && current.PreAlphaUx.Mode == PreAlphaUxMode.None &&
                            current.FeedbackSettings.Animate(qualityProfile.Active),
                            new RingMotionViewport(context.Width, context.Height, current.Dpi, current.UiScale)) };
                        gameplayHudRenderer.Render(
                            context,
                            renderCamera,
                            snapshot,
                            terrain.WorldBounds,
                            current.InformationLayer,
                            animatedPanel,
                            current.TacticalTargeting,
                            current.ActiveFormation,
                            current.CombatGroups ??
                                CombatGroupOverviewView.Empty,
                            current.PreAlphaUx,
                            current.Dpi,
                            current.UiScale,
                            gameplayDraw,
                            runtimeMetrics,
                            current.HoverTooltip,
                            showRuntimeMetrics: false,
                            operations: current.Operations,
                            presentationTime: uiNow,
                            animateNotifications: current.MetricsActive && !current.SurfaceSuspended &&
                                current.PreAlphaUx.Mode == PreAlphaUxMode.None &&
                                current.FeedbackSettings.Animate(qualityProfile.Active),
                            animateOperations: current.MetricsActive && !current.SurfaceSuspended &&
                                current.PreAlphaUx.Mode == PreAlphaUxMode.None &&
                                current.FeedbackSettings.Animate(qualityProfile.Active),
                            notificationExpansion: current.NotificationExpansion, navigationDiagnostics: current.NavigationDiagnostics,
                            displaySettings: current.TacticalDisplay);
                        developmentOverlayRenderer.Render(
                            context,
                            overlayMetrics,
                            renderCamera,
                            debugDraw,
                            showDevelopmentMetrics:
                                current.OverlayEnabled,
                            uiScale:
                                RtsUiLayout.ScaleForDpi(current.Dpi) * current.UiScale,
                            statisticsPicker: current.StatisticsPicker,
                            publicMetricsOnly: !current.StatisticsPicker.DeveloperAuthorized);
                        if (animatedFrontend is FrontendSurfaceView frontend)
                        {
                            frontendRenderer.Render(
                                context,
                                frontend,
                                current.UiScale);
                        }
                    };
                var fixtureBeforeSubmission = _referenceFixture is null ? null : (graphics as IGraphicsRenderTelemetry)?.LastSubmission;
                graphics.RenderFrame(GraphicsColor.ForgeLineClear, recordFrame);
                if (_referenceFixture is not null)
                {
                    fixtureRecord = recordFrame;
                    fixturePrepareReadback = () =>
                    {
                        terrainRenderer.Prepare(renderCamera, worldWidth, worldHeight, preparedShadow);
                        if (!instanceRenderer.AssetsPrepared) instanceRenderer.Prepare(renderCamera, renderWorld,
                            new(worldWidth, worldHeight, 1, preparedShadow, shadowSettings.MaximumCasters, _referenceFixture.VisualSeconds, true), particleRenderer);
                    };
                }

                presentedLastIteration = current.MetricsActive && graphics.PresentedFrameCount > presentedBefore;
                if (graphics.PresentedFrameCount > presentedBefore)
                {
                    Volatile.Write(ref _terrainPreparing, 0);
                    lock (_frameGate) _latestActionRing = inputRing;
                }

                if (!_startupPresented && graphics.PresentedFrameCount > presentedBefore && current.Frontend is null &&
                    (!_expectedSession.HasValue || snapshot.SessionId == _expectedSession.Value))
                {
                    _startup.GameplayPresented();
                    _startupPresented = true;
                    Volatile.Write(ref _gameplayPresented, 1);
                }

                instanceRenderer.Submit();
                instanceRenderer.Retire();

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
                instanceRenderer.ObserveFrameDiagnostics(diagnostics);
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
                    lighting, gameplayHudRenderer.State, gameplayHudRenderer.LastRenderedVertexCount,
                    runtimeMetrics, lastMeasuredRunningRates,particleRenderer.Diagnostics, qualityProfile);
                lock (_frameGate)
                    if (_latestQualification is { } measured)
                        _latestQualification = measured with
                        {
                            AssetPreparation = instanceRenderer.AssetFrameMetrics,
                            RenderWork = new(terrainPrepareMs, assetPrepareMs, false, terrainDeferredFrames,
                                instanceRenderer.PreparationReuse),
                            Publication = _publication.Metrics
                        };

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
                        $"terrainPrepareMs={terrainPrepareMs:F3} assetPrepareMs={assetPrepareMs:F3} " +
                        $"terrainDeferredFrames={terrainDeferredFrames} " +
                        $"skinDeforms={instanceRenderer.PreparationReuse.DeformedPoses} skinReuse={instanceRenderer.PreparationReuse.ReusedPoses} " +
                        $"terrainTextures={terrainRenderer.LastDiagnostics.TextureBindingsPerDraw} " +
                        $"light={_sceneLighting.DirectionalIntensity:F2}/{_sceneLighting.AmbientIntensity:F2} " +
                        $"exposure={_sceneLighting.Exposure:F2} " +
                        $"tone={_sceneLighting.ToneMapping} " +
                        $"vfx={snapshot.VfxMetrics.ActiveTransientEffects}/{snapshot.VfxMetrics.PoolCapacity} " +
                        $"vfxDropped={snapshot.VfxMetrics.TotalDropped} " +
                        $"particles={particleRenderer.Diagnostics.ActiveParticles}/{particleRenderer.Diagnostics.Capacity} " +
                        $"particleDropped={particleRenderer.Diagnostics.DroppedParticles} " +
                        $"particleBytes={particleRenderer.Diagnostics.BufferBytes} " +
                        $"particleGpuMs={diagnostics.Particles?.UpdateAndRenderGpuMilliseconds:F3} " +
                        $"quality={qualityProfile.Active} requestedQuality={qualityProfile.Requested} " +
                        $"aa={diagnostics.PostProcessing?.Antialiasing} scale={diagnostics.PostProcessing?.ResolutionScale:F2} " +
                        $"postGpuMs={diagnostics.PostProcessing?.GpuMilliseconds:F3} qualityFallback={qualityProfile.FallbackReason}");
                    nextDiagnosticAt =
                        renderFinishedAt;
                }
                if (_referenceFixture is not null)
                {
                    try { ReferenceFixturePresentation.RequireSuccessful(presentedBefore, graphics.PresentedFrameCount, fixtureBeforeSubmission, (graphics as IGraphicsRenderTelemetry)?.LastSubmission); }
                    catch (InvalidDataException)
                    {
                        liveTelemetry?.RecordFixtureFailure(liveStart, "fixture-not-presented");
                        throw;
                    }
                }
                CompleteLiveTelemetry(liveTelemetry, liveStart, "gameplay", terrainStarted, livePrepareEndedAt,
                    liveTelemetry is not null && _referenceFixture is null && _expectedSession.HasValue && _simulationTelemetry is not null
                        ? new LiveSimulationProvenance(_expectedSession.Value.Value, snapshot.SessionId.Value, snapshot.Tick.Value,
                            telemetry.SessionId.Value, telemetry.CompletedTicks, telemetry.State == RuntimeSimulationState.Running) : null);
                if (_referenceFixture is not null && graphics.PresentedFrameCount > presentedBefore)
                    AcknowledgeFixtureFrame(fixtureOrdinal, renderCamera.CaptureState(), width, height, snapshot, (IGraphicsRenderTelemetry)graphics, instanceRenderer, diagnostics.Surface.BufferCount, gameplayHudRenderer.LastRenderedVertexCount, particleRenderer.Diagnostics);
                if(_referenceFixture is not null && graphics.PresentedFrameCount>presentedBefore)
                    _ownerCycles?.FixtureFrame(fixtureOrdinal,_referenceFixture.Warmups,_referenceFixture.Samples,((IGraphicsRenderTelemetry)graphics).LastSubmission!.Value);
                if (_fixtureCollectorControl is not null && fixtureOrdinal < _referenceFixture!.Warmups + _referenceFixture.Samples)
                {
                    long commonProbeEnd=Stopwatch.GetTimestamp();
                    _fixtureCollectorControl.Record(fixtureOrdinal, collectorControlStart, commonProbeEnd,
                        ((IGraphicsRenderTelemetry)graphics).LastSubmission!.Value, renderCamera.CaptureState(), width, height);
                    _ownerCycles?.CommonProbeEnd(commonProbeEnd);
                }
            }

            cycleLoopCompleted=true;
            }
            finally { _ownerCycles?.Close(cycleLoopCompleted); }

            graphics.WaitForIdle();
            if (_referenceFixture is not null && liveTelemetry is not null) liveTelemetry.ValidateFixtureCorrespondence(_fixtureFrames!);
            GraphicsDiagnostics completedGraphics = graphics.Diagnostics;
            lock (_frameGate)
            {
                if (_latestQualification is { } qualification)
                {
                    _latestQualification = CompleteGpuQualification(qualification, completedGraphics);
                }
            }
        }
        catch (OperationCanceledException) when (Volatile.Read(ref _stopping) != 0)
        {
            _started.Set();
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

    private static bool ObserveOwnerCycleEdge(OwnerCycleObservations? observation,ClientRenderHost owner)
    {
        observation?.Edge();owner.ObservePostWindowControl(observation);return true;
    }

    private void CompleteLiveTelemetry(LiveRenderTelemetry? collector, in LiveTelemetryStart start,
        string state, long prepareStart = 0, long prepareEnd = 0, LiveSimulationProvenance? simulation = null)
    {
        OwnerWorkloadFacts before=_ownerCycles?.Facts() ?? default;
        int previous=collector?.Count ?? 0;
        collector?.Complete(start, state, prepareStart, prepareEnd, simulation);
        OwnerWorkloadFacts after=_ownerCycles?.Facts() ?? default;
        int? payload=collector is not null && collector.Count>previous ? previous : null;
        _ownerCycles?.EndPayload(state,payload,collector?.LastAbsoluteRead,before,after,collector?.WindowCompleted==true);
        if(payload.HasValue && collector?.LastObservedFrame is { } observed)_ownerCycles?.Submission(observed);
        if (collector?.WindowCompleted == true) Volatile.Write(ref _telemetryWindowPending, 0);
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

    private ClientRenderFrame? TakeLatestFrame() => _publication.ReadLatest();

    internal static ClientVisualQualificationSnapshot CompleteGpuQualification(
        in ClientVisualQualificationSnapshot snapshot,
        GraphicsDiagnostics completedGraphics) => snapshot with
        {
            GpuTimingAvailable = completedGraphics.GpuTimingAvailable,
            FramePasses = completedGraphics.Frame,
            DirectionalShadows = completedGraphics.DirectionalShadows,
            Atmosphere = completedGraphics.Atmosphere,
            ParticleGpu = completedGraphics.Particles,
            PostProcessing = completedGraphics.PostProcessing,
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
        RuntimeMetricsView? lastMeasuredRunningRates,
        ParticleVfxDiagnostics particles = default, RenderQualityProfile qualityProfile = default)
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
                debug.DrawCalls + particles.DrawCalls + (graphics.Frame?.CompositeDrawCalls ?? 0) + (graphics.DirectionalShadows?.DrawCalls ?? 0),
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
                Lod1Instances = instances.Lod1Instances,
                Lod2Instances = instances.Lod2Instances,
                AnimatedVegetationInstances = instances.AnimatedVegetationInstances,
                TerrainSurfaceDetailEnabled = terrain.SurfaceDetailEnabled,
                TerrainRootSignatureDwords = terrain.RootSignatureDwords,
                GameplayHudVertexCount = hudVertexCount,
                RuntimeMetrics = runtimeMetrics,
                LastMeasuredRunningRates = lastMeasuredRunningRates,
                GpuTimingAvailable =
                    graphics.GpuTimingAvailable,
                FramePasses = graphics.Frame,
                DirectionalShadows = graphics.DirectionalShadows,
                Atmosphere = graphics.Atmosphere,
                Particles = particles,
                ParticleGpu = graphics.Particles,
                QualityProfile = qualityProfile,
                PostProcessing = graphics.PostProcessing,
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

internal readonly record struct ClientRenderWorkMetrics(double TerrainPrepareMilliseconds,
    double InstancePrepareMilliseconds, bool TerrainPending, long TerrainDeferredFrames,
    RenderPreparationReuseMetrics Reuse);
