using System.Numerics;
using ForgeLine.Graphics;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

internal sealed class ClientApplication
{
    private const float MaximumCameraDeltaSeconds = 0.1f;
    private const float ValidationGridSpacing = 10.0f;
    private const int ValidationGridRadius = 12;

    private static readonly TimeSpan IdleWait = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan SmokeTestDuration = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan CameraDiagnosticInterval = TimeSpan.FromSeconds(1);

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
        using IGraphicsPipeline validationPipeline = CreateValidationPipeline(graphics);

        var inputState = new InputState();
        var actionMapper = new RtsCameraActionMapper();
        var camera = new RtsCamera();

        WriteWindowState("started", window);
        WriteGraphicsState("started", graphics);
        WriteCameraState("started", camera);

        long startedAt = _platform.Clock.GetTimestamp();
        long previousFrameAt = startedAt;
        long nextCameraDiagnosticAt = startedAt;

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
                context => DrawValidationScene(context, validationPipeline, camera));

            if (_platform.Clock.GetElapsedTime(nextCameraDiagnosticAt, now) >=
                CameraDiagnosticInterval)
            {
                WriteCameraState("frame", camera);
                nextCameraDiagnosticAt = now;
            }
        }

        DrainWindowEvents(window, graphics);
        DrainInputEvents(window, inputState);
        WriteCameraState("stopped", camera);
        WriteGraphicsState("stopped", graphics);
        return 0;
    }

    private static void DrainWindowEvents(IWindow window, IGraphicsDevice graphics)
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

    private static void DrainInputEvents(IWindow window, InputState inputState)
    {
        while (window.TryDequeueInputEvent(out PlatformInputEvent inputEvent))
        {
            inputState.Apply(inputEvent);
        }
    }

    private static IGraphicsPipeline CreateValidationPipeline(IGraphicsDevice graphics)
    {
        const string vertexShaderSource = """
            float4 VSMain(uint vertexId : SV_VertexID) : SV_Position
            {
                float2 positions[3] =
                {
                    float2(0.0f, -0.85f),
                    float2(0.75f, 0.65f),
                    float2(-0.75f, 0.65f)
                };

                return float4(positions[vertexId], 0.0f, 1.0f);
            }
            """;

        const string pixelShaderSource = """
            float4 PSMain() : SV_Target0
            {
                return float4(0.95f, 0.58f, 0.12f, 1.0f);
            }
            """;

        var compiler = new DxcShaderCompiler();
        GraphicsShaderBytecode vertexShader = compiler.Compile(
            vertexShaderSource,
            GraphicsShaderStage.Vertex,
            "VSMain",
            "RtsCameraValidationVertex.hlsl");
        GraphicsShaderBytecode pixelShader = compiler.Compile(
            pixelShaderSource,
            GraphicsShaderStage.Pixel,
            "PSMain",
            "RtsCameraValidationPixel.hlsl");

        IGraphicsPipeline pipeline = graphics.CreateGraphicsPipeline(
            new GraphicsPipelineDescription(vertexShader, pixelShader));

        Console.WriteLine(
            $"[graphics:shader] vertexBytes={vertexShader.Data.Length} " +
            $"pixelBytes={pixelShader.Data.Length} pipeline=validation-grid");

        return pipeline;
    }

    private static void DrawValidationScene(
        IGraphicsCommandContext context,
        IGraphicsPipeline pipeline,
        RtsCamera camera)
    {
        context.SetPipeline(pipeline);

        float centerX =
            MathF.Round(camera.Target.X / ValidationGridSpacing) *
            ValidationGridSpacing;
        float centerZ =
            MathF.Round(camera.Target.Z / ValidationGridSpacing) *
            ValidationGridSpacing;

        float markerSize = Math.Clamp(
            7.0f * (camera.Settings.PanReferenceDistance / camera.Distance),
            2.0f,
            9.0f);
        float halfMarker = markerSize * 0.5f;

        for (int z = -ValidationGridRadius; z <= ValidationGridRadius; z++)
        {
            for (int x = -ValidationGridRadius; x <= ValidationGridRadius; x++)
            {
                var world = new Vector3(
                    centerX + x * ValidationGridSpacing,
                    0.0f,
                    centerZ + z * ValidationGridSpacing);

                ScreenProjection projected =
                    camera.WorldToScreen(world, context.Width, context.Height);

                if (!projected.IsVisible ||
                    projected.Position.X < halfMarker ||
                    projected.Position.Y < halfMarker ||
                    projected.Position.X >= context.Width - halfMarker ||
                    projected.Position.Y >= context.Height - halfMarker)
                {
                    continue;
                }

                float left = projected.Position.X - halfMarker;
                float top = projected.Position.Y - halfMarker;
                int scissorLeft = Math.Max(0, (int)MathF.Floor(left));
                int scissorTop = Math.Max(0, (int)MathF.Floor(top));
                int scissorRight = Math.Min(
                    context.Width,
                    (int)MathF.Ceiling(left + markerSize));
                int scissorBottom = Math.Min(
                    context.Height,
                    (int)MathF.Ceiling(top + markerSize));

                if (scissorRight <= scissorLeft || scissorBottom <= scissorTop)
                {
                    continue;
                }

                context.SetViewport(left, top, markerSize, markerSize);
                context.SetScissor(
                    scissorLeft,
                    scissorTop,
                    scissorRight,
                    scissorBottom);
                context.Draw(3);
            }
        }

        context.SetViewport(0, 0, context.Width, context.Height);
        context.SetScissor(0, 0, context.Width, context.Height);
    }

    private static void WriteWindowState(string state, IWindow window)
    {
        Console.WriteLine(
            $"[platform:{state}] handle=0x{window.NativeHandle.Value:X} " +
            $"size={window.ClientSize.Width}x{window.ClientSize.Height} " +
            $"dpi={window.Dpi} focused={window.IsFocused} " +
            $"minimized={window.IsMinimized} mode={window.Mode}");
    }

    private static void WriteGraphicsState(string state, IGraphicsDevice graphics)
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
}
