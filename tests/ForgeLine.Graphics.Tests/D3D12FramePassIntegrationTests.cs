using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Graphics.Tests;

public sealed class D3D12FramePassIntegrationTests
{
    [Fact]
    public void PassMeasurementsFollowSubmissionFenceAndResize()
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Frame Pass Test", 320, 240));
        using IGraphicsDevice graphics = GraphicsDeviceFactory.CreateForWindow(window, new GraphicsConfiguration
        {
            EnableDebugLayer = false, EnableVSync = false, AllowSoftwareAdapterFallback = true
        });
        IGraphicsCommandContext? escaped = null;
        for (int frame = 0; frame < 8; frame++)
            graphics.RenderFrame(GraphicsColor.ForgeLineClear, context =>
            {
                context.BeginPass(GraphicsFramePass.World);
                context.BeginPass(GraphicsFramePass.Overlay);
                context.BeginPass(GraphicsFramePass.Overlay);
                escaped = context;
            });
        graphics.WaitForIdle();
        Assert.NotNull(escaped);
        Assert.Throws<ObjectDisposedException>(() => escaped.BeginPass(GraphicsFramePass.Overlay));
        GraphicsFrameDiagnostics measured = graphics.Diagnostics.Frame!.Value;
        Assert.Equal(graphics.Diagnostics.Surface.SubmittedFrameCount, measured.CpuSubmission);
        Assert.NotNull(measured.OverlayCpuMilliseconds);
        Assert.Equal(0, measured.Plan.TransientPayloadBytes);
        if (graphics.Diagnostics.GpuTimingAvailable)
        {
            Assert.True(measured.GpuSubmissionFence > 0);
            Assert.NotNull(measured.WorldGpuMilliseconds);
            Assert.NotNull(measured.OverlayGpuMilliseconds);
            Assert.InRange(measured.WorldGpuMilliseconds.Value + measured.OverlayGpuMilliseconds.Value,
                0, graphics.Diagnostics.GpuFrameMilliseconds!.Value + 0.000001);
        }

        graphics.Resize(0, 0);
        graphics.RenderFrame(GraphicsColor.ForgeLineClear, _ => throw new InvalidOperationException("Suspended frames cannot record."));
        Assert.Equal(measured.CpuSubmission, graphics.Diagnostics.Frame!.Value.CpuSubmission);
        graphics.Resize(400, 300);
        graphics.RenderFrame(GraphicsColor.ForgeLineClear);
        graphics.WaitForIdle();
        GraphicsFrameDiagnostics resized = graphics.Diagnostics.Frame!.Value;
        Assert.Equal(400, resized.Plan.Output.Width);
        Assert.Equal(300, resized.Plan.Depth.Height);
        Assert.Null(resized.OverlayCpuMilliseconds);
        Assert.Null(resized.OverlayGpuMilliseconds);
    }

    [Fact]
    public void ReturningToWorldFaultsBeforeSubmission()
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Pass Fault Test", 320, 240));
        using IGraphicsDevice graphics = GraphicsDeviceFactory.CreateForWindow(window, new GraphicsConfiguration
        {
            EnableDebugLayer = false, EnableVSync = false
        });
        Assert.Throws<InvalidOperationException>(() => graphics.RenderFrame(GraphicsColor.ForgeLineClear, context =>
        {
            context.BeginPass(GraphicsFramePass.Overlay);
            context.BeginPass(GraphicsFramePass.World);
        }));
        Assert.Equal(0UL, graphics.Diagnostics.Surface.SubmittedFrameCount);
        Assert.Equal(1, graphics.Diagnostics.Health.SubmissionFaultCount);
    }
}
