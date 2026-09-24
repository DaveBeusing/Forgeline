using System.Numerics;
using ForgeLine.Core;
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
        var simulation = new SimulationCoordinator(
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

        simulation.RegisterSystem(groundMovementSystem);
        simulation.RegisterSystem(new SpatialIndexSystem(spatialSynchronizer));
        simulation.RegisterSystem(new SpatialIndexCleanupSystem(spatialSynchronizer));
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
        var selectionController = new RtsSelectionController(
            new SelectionFilter(
                LocalPlayer,
                ControllableEntityCategory.Unit |
                ControllableEntityCategory.Logistics));
        var debugDraw = new DebugDraw();
        var frameTimingTracker = new FrameTimingTracker();

        MoveEntitiesCommand? lastMovementCommand = null;
        SimulationCommandEnvelope? lastMovementEnvelope = null;
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
        WriteInteractionState(
            "started",
            selectionController,
            lastMovementEnvelope,
            lastMovementCommand);

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
                    simulation.CurrentTick);

                lastMovementEnvelope = simulation.SubmitCommand(
                    command,
                    targetTick,
                    new SimulationCommandSource(LocalPlayer.Value));
                lastMovementCommand = command;
            }

            BuildWorldDebugVisualization(
                debugDraw,
                worldDebugEnabled,
                renderWorld,
                renderAlpha,
                camera,
                selectionController,
                spatialIndex,
                groundMovementSystem.CaptureDebugSnapshot());

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
                    lastMovementCommand);
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
            lastMovementCommand);
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

    private static void BuildWorldDebugVisualization(
        DebugDraw debugDraw,
        bool worldDebugEnabled,
        RenderWorld renderWorld,
        float alpha,
        RtsCamera camera,
        RtsSelectionController selectionController,
        SpatialGridIndex spatialIndex,
        GroundMovementDebugSnapshot movementSnapshot)
    {
        debugDraw.Clear();

        bool interactionFeedback =
            selectionController.Selection.Count > 0 ||
            selectionController.HoveredEntity.IsValid;
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
        MoveEntitiesCommand? movementCommand)
    {
        EntityId hovered = selectionController.HoveredEntity;
        string hoveredText = hovered.IsValid
            ? hovered.ToString()
            : "none";
        string commandText = movementEnvelope.HasValue
            ? movementEnvelope.Value.Sequence.ToString(
                System.Globalization.CultureInfo.InvariantCulture)
            : "none";

        Console.WriteLine(
            $"[interaction:{state}] selected={selectionController.Selection.Count} " +
            $"hovered={hoveredText} lastCommand={commandText} " +
            $"acceptedTargets={movementCommand?.AcceptedTargetCount ?? 0} " +
            $"rejectedTargets={movementCommand?.RejectedTargetCount ?? 0} " +
            $"executedTick={movementCommand?.ExecutedAtTick.Value ?? 0}");
    }
}
