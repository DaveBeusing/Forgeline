using System.Numerics;
using ForgeLine.Graphics;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Presentation;
using ForgeLine.World;

namespace ForgeLine.Client;

internal sealed class ClientApplication
{
    private const float MaximumCameraDeltaSeconds = 0.1f;

    private static readonly TimeSpan IdleWait = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan SmokeTestDuration = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan DiagnosticInterval = TimeSpan.FromSeconds(1);

    private readonly IPlatform _platform;

    internal ClientApplication(IPlatform platform)
    {
        _platform = platform;
    }

    internal int Run(bool smokeTest)
    {
        var configuration = new WindowConfiguration(
            "FORGELINE",
            1600,
            900,
            resizable: true,
            WindowMode.Windowed);

        using IWindow window = _platform.CreateWindow(configuration);
        using IGraphicsDevice graphics = GraphicsDeviceFactory.CreateForWindow(window);

        TerrainWorld world =
            DevelopmentTerrainFactory.CreateRepresentativeWorld();
        using var terrainRenderer = new TerrainRenderer(graphics, world)
        {
            DebugChunksEnabled = true
        };

        float targetHeight = world.TrySampleHeight(
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

        WriteWindowState("started", window);
        WriteGraphicsState("started", graphics);
        WriteWorldState("started", world);
        WriteCameraState("started", camera);

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
            float deltaSeconds = (float)Math.Min(
                _platform.Clock.GetElapsedTime(previousFrameAt, now).TotalSeconds,
                MaximumCameraDeltaSeconds);
            previousFrameAt = now;

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
                deltaSeconds,
                window.ClientSize.Width,
                window.ClientSize.Height);

            graphics.RenderFrame(
                GraphicsColor.ForgeLineClear,
                context => terrainRenderer.Render(context, camera));

            if (_platform.Clock.GetElapsedTime(nextDiagnosticAt, now) >=
                DiagnosticInterval)
            {
                WriteCameraState("frame", camera);
                WriteTerrainState("frame", terrainRenderer);
                nextDiagnosticAt = now;
            }
        }

        DrainWindowEvents(window, graphics);
        DrainInputEvents(window, inputState);
        WriteCameraState("stopped", camera);
        WriteTerrainState("stopped", terrainRenderer);
        WriteGraphicsState("stopped", graphics);
        return 0;
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

    private static void WriteWindowState(
        string state,
        IWindow window)
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

    private static void WriteWorldState(
        string state,
        TerrainWorld world)
    {
        AxisAlignedBounds bounds = world.WorldBounds;

        Console.WriteLine(
            $"[world:{state}] chunks={world.Chunks.Count} " +
            $"chunkMeters={world.Settings.ChunkSizeMeters:F0} " +
            $"heightSamples={world.Settings.HeightSamplesPerSide} " +
            $"boundsMin=({bounds.Minimum.X:F0},{bounds.Minimum.Y:F1},{bounds.Minimum.Z:F0}) " +
            $"boundsMax=({bounds.Maximum.X:F0},{bounds.Maximum.Y:F1},{bounds.Maximum.Z:F0})");
    }

    private static void WriteCameraState(
        string state,
        RtsCamera camera)
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
        TerrainRenderDiagnostics diagnostics =
            terrainRenderer.LastDiagnostics;

        Console.WriteLine(
            $"[terrain:{state}] totalChunks={diagnostics.TotalChunks} " +
            $"visibleChunks={diagnostics.VisibleChunks} " +
            $"culledChunks={diagnostics.CulledChunks} " +
            $"triangles={diagnostics.SubmittedTriangles} " +
            $"drawCalls={diagnostics.DrawCalls} " +
            $"staticBuffers={diagnostics.UploadedBufferCount}");
    }
}
