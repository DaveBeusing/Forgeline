using System.Numerics;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Input;
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
        var simulation = new SimulationCoordinator(
            diagnosticsOptions: new SimulationDiagnosticsOptions { Enabled = true });
        simulation.RegisterSystem(new LinearMotionSystem());
        simulation.RegisterTickObserver(new PresentationExtractor(snapshotBuffer));
        PopulateSimulationEntities(simulation, terrainWorld, renderInstanceCount);
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
        var debugDraw = new DebugDraw();
        var frameTimingTracker = new FrameTimingTracker();

        bool overlayEnabled = true;
        bool worldDebugEnabled = false;
        bool overlayToggleHeld = false;
        bool worldDebugToggleHeld = false;
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

            BuildWorldDebugVisualization(
                debugDraw,
                worldDebugEnabled,
                renderWorld,
                renderAlpha,
                camera);

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
        WriteGraphicsState("stopped", graphics);
        return 0;
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

            var entity = simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(
                entity,
                new WorldTransform(
                    new Vector3(x, terrainHeight + 3.0f, z),
                    Quaternion.Identity,
                    new Vector3(8.0f, 6.0f, 8.0f)));

            Vector3 velocity = index % 6 == 0
                ? new Vector3(2.0f + (index % 5) * 0.35f, 0.0f, 0.0f)
                : Vector3.Zero;

            simulation.Entities.AddComponent(entity, new LinearVelocity(velocity));
            simulation.Entities.AddComponent(entity, new VisualIdentity(1));
        }
    }

    private static void BuildWorldDebugVisualization(
        DebugDraw debugDraw,
        bool enabled,
        RenderWorld renderWorld,
        float alpha,
        RtsCamera camera)
    {
        debugDraw.Clear();
        debugDraw.Enabled = enabled;

        if (!enabled)
        {
            return;
        }

        Vector4 rangeColor = new(0.2f, 0.75f, 1.0f, 1.0f);
        Vector4 boundsColor = new(1.0f, 0.72f, 0.18f, 1.0f);
        Vector4 pointColor = new(1.0f, 0.25f, 0.18f, 1.0f);

        debugDraw.Circle(camera.Target, 40.0f, rangeColor, segments: 48);
        debugDraw.Point(camera.Target, 8.0f, pointColor);

        int debugCount = Math.Min(
            renderWorld.InstanceCount,
            MaximumDebugInstanceBoxes);

        for (int index = 0; index < debugCount; index++)
        {
            RenderInstance instance = renderWorld.GetInterpolatedInstance(index, alpha);
            Vector3 extents = Vector3.Max(
                Vector3.Abs(instance.Transform.Scale) * 0.5f,
                new Vector3(0.05f));

            debugDraw.Box(
                new AxisAlignedBounds(
                    instance.Transform.Position - extents,
                    instance.Transform.Position + extents),
                boundsColor);

            if (index < MaximumDebugLabels)
            {
                debugDraw.Label(
                    instance.Transform.Position +
                    new Vector3(0.0f, extents.Y + 2.0f, 0.0f),
                    $"E{instance.Entity.Index}",
                    boundsColor);
            }
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
}
