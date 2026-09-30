using System.Diagnostics;
using System.Runtime.ExceptionServices;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Client;

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
    DebugLabel[] DebugLabels);

internal sealed class ClientRenderHost : IDisposable
{
    private static readonly TimeSpan IdleWait =
        TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan DiagnosticInterval =
        TimeSpan.FromSeconds(1);

    private readonly object _frameGate = new();
    private readonly GraphicsWindowTarget _initialTarget;
    private readonly TerrainWorld _terrain;
    private readonly PresentationSnapshotBuffer _snapshots;
    private readonly RtsCameraSettings _cameraSettings;
    private readonly AutoResetEvent _signal = new(false);
    private readonly ManualResetEventSlim _started = new(false);
    private readonly Thread _thread;

    private ClientRenderFrame? _latestFrame;
    private ExceptionDispatchInfo? _failure;
    private int _stopping;
    private bool _disposed;

    public ClientRenderHost(
        in GraphicsWindowTarget initialTarget,
        TerrainWorld terrain,
        PresentationSnapshotBuffer snapshots,
        RtsCameraSettings cameraSettings)
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

        _thread =
            new Thread(RenderLoop)
            {
                IsBackground = true,
                Name = "ForgeLine Render"
            };
        _thread.Start();
        _started.Wait();
        ThrowIfFaulted();
    }

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
                    frame.DebugLabels.ToArray()
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

        _started.Dispose();
        _signal.Dispose();
        _disposed = true;
    }

    private void RenderLoop()
    {
        try
        {
            using IGraphicsDevice graphics =
                GraphicsDeviceFactory.CreateForWindowTarget(
                    _initialTarget);
            using var terrainRenderer =
                new TerrainRenderer(
                    graphics,
                    _terrain);
            using var instanceRenderer =
                new SimpleInstanceRenderer(
                    graphics);
            using var debugDrawRenderer =
                new DebugDrawRenderer(
                    graphics);
            using var overlayRenderer =
                new DevelopmentOverlayRenderer(
                    graphics);

            var renderWorld =
                new RenderWorld();
            var renderCamera =
                new RtsCamera(
                    _cameraSettings);
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
                _initialTarget.Width;
            int height =
                _initialTarget.Height;

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

                if (current.ViewportWidth != width ||
                    current.ViewportHeight != height)
                {
                    graphics.Resize(
                        current.ViewportWidth,
                        current.ViewportHeight);
                    width =
                        current.ViewportWidth;
                    height =
                        current.ViewportHeight;
                }

                if (width <= 0 ||
                    height <= 0)
                {
                    continue;
                }

                renderCamera.ApplyState(
                    current.Camera);

                if (renderWorld.Update(
                        _snapshots))
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
                                current.ActiveFormation);
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

                if (StopwatchElapsed(
                        nextDiagnosticAt,
                        renderFinishedAt) >=
                    DiagnosticInterval)
                {
                    GraphicsDiagnostics diagnostics =
                        graphics.Diagnostics;
                    Console.WriteLine(
                        $"[render:frame] thread={Environment.CurrentManagedThreadId} " +
                        $"tick={snapshot.Tick.Value} fps={frameTiming.FramesPerSecond:F1} " +
                        $"size={diagnostics.Surface.Width}x{diagnostics.Surface.Height} " +
                        $"instances={instanceRenderer.LastDiagnostics.VisibleInstances}/{renderWorld.InstanceCount}");
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
            _started.Set();
        }
    }

    private ClientRenderFrame? TakeLatestFrame()
    {
        lock (_frameGate)
        {
            return _latestFrame;
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
