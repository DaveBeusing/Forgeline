using ForgeLine.Graphics;
using ForgeLine.Platform;

namespace ForgeLine.Client;

internal sealed class ClientApplication
{
    private static readonly TimeSpan IdleWait = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan SmokeTestDuration = TimeSpan.FromMilliseconds(250);

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

        WriteWindowState("started", window);
        WriteGraphicsState("started", graphics);

        if (smokeTest)
        {
            ValidateShaderCompiler();
        }

        long startedAt = _platform.Clock.GetTimestamp();

        while (window.IsOpen && _platform.PumpEvents())
        {
            DrainWindowEvents(window, graphics);

            if (smokeTest &&
                _platform.Clock.GetElapsedTime(startedAt, _platform.Clock.GetTimestamp()) >= SmokeTestDuration)
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

            graphics.RenderFrame(GraphicsColor.ForgeLineClear);
        }

        DrainWindowEvents(window, graphics);
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

    private static void ValidateShaderCompiler()
    {
        const string shaderSource = """
            float4 VSMain(uint vertexId : SV_VertexID) : SV_Position
            {
                float2 positions[3] =
                {
                    float2(0.0f, 0.5f),
                    float2(0.5f, -0.5f),
                    float2(-0.5f, -0.5f)
                };

                return float4(positions[vertexId], 0.0f, 1.0f);
            }
            """;

        var compiler = new DxcShaderCompiler();
        GraphicsShaderBytecode bytecode = compiler.Compile(
            shaderSource,
            GraphicsShaderStage.Vertex,
            "VSMain",
            "FoundationSmoke.hlsl");

        Console.WriteLine(
            $"[graphics:shader] stage={bytecode.Stage} " +
            $"entry={bytecode.EntryPoint} bytes={bytecode.Data.Length}");
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
}
