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
    double TerrainCpuSubmissionMilliseconds = 0.0);

internal readonly record struct ClientRenderFrame(
    RtsCameraState Camera,
    int ViewportWidth,
    int ViewportHeight,
    bool OverlayEnabled,
    bool WorldDebugEnabled,
    PlayerActionPanelView ActionPanel,
    TacticalTargetingView TacticalTargeting,
    FormationTemplate ActiveFormation,
    DebugLine[] DebugLines,
    DebugLabel[] DebugLabels,
    uint Dpi = 96,
    RtsInformationLayerView InformationLayer = default,
    float UiScale = 1.0f,
    PreAlphaUxView PreAlphaUx = default,
    FrontendSurfaceView? Frontend = null,
    bool SurfaceSuspended = false);

internal sealed class ClientRenderHost : IDisposable
{
    private static readonly TimeSpan IdleWait =
        TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan DiagnosticInterval =
        TimeSpan.FromSeconds(1);

    private readonly object _frameGate = new();
    private readonly GraphicsWindowTarget? _initialTarget;
    private readonly TerrainWorld? _terrain;
    private readonly PresentationSnapshotBuffer? _snapshots;
    private readonly RtsCameraSettings? _cameraSettings;
    private readonly RuntimeAssetCatalog? _runtimeAssets;
    private readonly Action<ClientRenderFrame>? _testRenderAction;
    private readonly AutoResetEvent _signal = new(false);
    private readonly ManualResetEventSlim _started = new(false);
    private readonly ManualResetEventSlim _faulted = new(false);
    private readonly Thread _thread;

    private ClientRenderFrame? _latestFrame;
    private ClientVisualQualificationSnapshot? _latestQualification;
    private ExceptionDispatchInfo? _failure;
    private int _stopping;
    private bool _disposed;

    public ClientRenderHost(
        in GraphicsWindowTarget initialTarget,
        TerrainWorld terrain,
        PresentationSnapshotBuffer snapshots,
        RtsCameraSettings cameraSettings,
        RuntimeAssetCatalog? runtimeAssets = null)
    {
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

        _thread =
            CreateThread();
        _thread.Start();
        _started.Wait();
        ThrowIfFaulted();
    }

    internal ClientRenderHost(
        Action<ClientRenderFrame> renderAction)
    {
        _testRenderAction =
            renderAction ??
            throw new ArgumentNullException(nameof(renderAction));
        _thread =
            CreateThread();
        _thread.Start();
        _started.Wait();
        ThrowIfFaulted();
    }

    internal bool IsExecutionThreadAlive =>
        _thread.IsAlive;

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
        return _faulted.Wait(timeout);
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
            _latestFrame = copied;
        }

        _signal.Set();
        return true;
    }

    public void ThrowIfFaulted()
    {
        Volatile.Read(ref _failure)?.Throw();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Interlocked.Exchange(
            ref _stopping,
            1);
        _signal.Set();

        if (Thread.CurrentThread != _thread)
        {
            _thread.Join();
        }

        _faulted.Dispose();
        _started.Dispose();
        _signal.Dispose();
        _disposed = true;
    }

    private void RenderLoop()
    {
        try
        {
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

            using IGraphicsDevice graphics =
                GraphicsDeviceFactory.CreateForWindowTarget(
                    initialTarget);
            using var terrainRenderer =
                new TerrainRenderer(
                    graphics,
                    terrain,
                    runtimeAssets: _runtimeAssets);
            using var instanceRenderer =
                new SimpleInstanceRenderer(
                    graphics,
                    _runtimeAssets);
            using var debugDrawRenderer =
                new DebugDrawRenderer(
                    graphics);
            using var overlayRenderer =
                new DevelopmentOverlayRenderer(
                    graphics);
            using var informationRenderer =
                new RtsInformationOverlayRenderer(
                    graphics,
                    _runtimeAssets);
            using var frontendRenderer =
                new FrontendOverlayRenderer(
                    graphics);

            var renderWorld =
                new RenderWorld();
            var renderCamera =
                new RtsCamera(
                    cameraSettings);
            var debugDraw =
                new DebugDraw();
            var frameTimingTracker =
                new FrameTimingTracker();

            long previousFrameAt =
                Stopwatch.GetTimestamp();
            long snapshotObservedAt =
                previousFrameAt;
            long nextDiagnosticAt =
                previousFrameAt;
            FrameTimingMetrics frameTiming =
                default;
            int width =
                initialTarget.Width;
            int height =
                initialTarget.Height;
            bool surfaceSuspended =
                initialTarget.Suspended ||
                width <= 0 ||
                height <= 0;

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

                RebuildDebugDraw(
                    debugDraw,
                    current);

                terrainRenderer.DebugChunksEnabled =
                    current.WorldDebugEnabled;

                DevelopmentOverlayMetrics overlayMetrics =
                    CreateOverlayMetrics(
                        frameTiming,
                        snapshot,
                        terrainRenderer,
                        instanceRenderer,
                        debugDrawRenderer,
                        renderWorld);
                PlayerExperienceSnapshot playerExperience =
                    snapshot.PlayerExperience ??
                    default;

                long renderStartedAt =
                    Stopwatch.GetTimestamp();

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
                        debugDrawRenderer.Render(
                            context,
                            renderCamera,
                            debugDraw);
                        informationRenderer.Render(
                            context,
                            renderCamera,
                            snapshot,
                            terrain.WorldBounds,
                            current.InformationLayer,
                            current.Dpi);
                        overlayRenderer.Render(
                            context,
                            overlayMetrics,
                            renderCamera,
                            debugDraw,
                            playerExperience,
                            showDevelopmentMetrics:
                                current.OverlayEnabled,
                            playerActions:
                                snapshot.PlayerActions,
                            actionPanel:
                                current.ActionPanel,
                            tacticalTargeting:
                                current.TacticalTargeting,
                            activeFormation:
                                current.ActiveFormation,
                            preAlphaUx:
                                current.PreAlphaUx,
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

                long renderFinishedAt =
                    Stopwatch.GetTimestamp();
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
                    debugDrawRenderer.LastDiagnostics,
                    snapshot.VfxMetrics,
                    renderWorld.InstanceCount);

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
                        $"draws={terrainRenderer.LastDiagnostics.DrawCalls + instanceRenderer.LastDiagnostics.DrawCalls + debugDrawRenderer.LastDiagnostics.DrawCalls} " +
                        $"terrainSubmitMs={terrainRenderer.LastDiagnostics.CpuSubmissionMilliseconds:F3} " +
                        $"terrainTextures={terrainRenderer.LastDiagnostics.TextureBindingsPerDraw} " +
                        $"vfx={snapshot.VfxMetrics.ActiveTransientEffects}/{snapshot.VfxMetrics.PoolCapacity} " +
                        $"vfxDropped={snapshot.VfxMetrics.TotalDropped}");
                    nextDiagnosticAt =
                        renderFinishedAt;
                }
            }

            graphics.WaitForIdle();
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

    private void PublishQualification(
        GraphicsDiagnostics graphics,
        in FrameTimingMetrics frameTiming,
        in TerrainRenderDiagnostics terrain,
        in InstanceRenderDiagnostics instances,
        in RuntimeMaterialDiagnostics materials,
        in DebugDrawRenderDiagnostics debug,
        in VfxPresentationMetrics vfx,
        int totalInstances)
    {
        var snapshot =
            new ClientVisualQualificationSnapshot(
                graphics.Device.AdapterName,
                graphics.Device.DedicatedVideoMemoryBytes,
                frameTiming.FramesPerSecond,
                frameTiming.FrameMilliseconds,
                frameTiming.CpuRenderMilliseconds,
                GpuMilliseconds: null,
                terrain.VisibleChunks,
                terrain.TotalChunks,
                terrain.SubmittedTriangles,
                terrain.DrawCalls,
                instances.DrawCalls,
                terrain.DrawCalls +
                instances.DrawCalls +
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
                terrain.CpuSubmissionMilliseconds);

        lock (_frameGate)
        {
            _latestQualification =
                snapshot;
        }
    }

    private static void RebuildDebugDraw(
        DebugDraw debugDraw,
        in ClientRenderFrame frame)
    {
        debugDraw.Clear();
        debugDraw.Enabled =
            frame.WorldDebugEnabled ||
            frame.DebugLines.Length > 0 ||
            frame.DebugLabels.Length > 0;

        for (int index = 0;
             index < frame.DebugLines.Length;
             index++)
        {
            DebugLine line =
                frame.DebugLines[index];
            debugDraw.Line(
                line.Start,
                line.End,
                line.Color);
        }

        for (int index = 0;
             index < frame.DebugLabels.Length;
             index++)
        {
            DebugLabel label =
                frame.DebugLabels[index];
            debugDraw.Label(
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

    private static TimeSpan StopwatchElapsed(
        long start,
        long end) =>
        TimeSpan.FromSeconds(
            (end - start) /
            (double)Stopwatch.Frequency);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }
}
