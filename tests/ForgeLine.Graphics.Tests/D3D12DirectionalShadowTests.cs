using System.Numerics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Graphics.Tests;

public sealed class D3D12DirectionalShadowTests
{
    [Fact]
    public void DepthTargetsReuseDescriptorsAndRejectRecordingConfigurationChanges()
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Shadow Lifecycle Test", 320, 240));
        using IGraphicsDevice graphics = GraphicsDeviceFactory.CreateForWindow(window, new GraphicsConfiguration
        { EnableDebugLayer = false, EnableVSync = false, ForceSoftwareAdapter = true });
        Assert.Equal(0, graphics.Diagnostics.DirectionalShadows!.Value.TexturePayloadBytes);
        foreach (DirectionalShadowQuality quality in new[] { DirectionalShadowQuality.Low, DirectionalShadowQuality.Medium, DirectionalShadowQuality.Low })
        {
            graphics.ConfigureDirectionalShadows(new(quality));
            Assert.Equal(3, graphics.Diagnostics.Resources.ShaderResourceDescriptorsUsed);
            for (int i = 0; i < 5; i++) graphics.RenderFrame(GraphicsColor.ForgeLineClear, context =>
            {
                Assert.Throws<InvalidOperationException>(() => graphics.ConfigureDirectionalShadows(default));
                context.BeginDirectionalShadowPass(new(Matrix4x4.Identity, Vector3.Zero, 1, 1));
                context.EndDirectionalShadowPass();
                context.BeginPass(GraphicsFramePass.Overlay);
            });
            graphics.WaitForIdle();
            Assert.Equal(graphics.Diagnostics.DirectionalShadows!.Value.TexturePayloadBytes, graphics.Diagnostics.Frame!.Value.Plan.Shadow!.Value.PayloadBytes);
            if (graphics.Diagnostics.GpuTimingAvailable) Assert.NotNull(graphics.Diagnostics.DirectionalShadows!.Value.GpuMilliseconds);
        }
        graphics.ConfigureDirectionalShadows(default);
        Assert.Equal(0, graphics.Diagnostics.Resources.ShaderResourceDescriptorsUsed);
        Assert.Equal(3, graphics.Diagnostics.Resources.PeakShaderResourceDescriptorsUsed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MissingEndReversedAndRecordingFailuresNeverPublishSubmission(int failure)
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Shadow Fault Test", 320, 240));
        using IGraphicsDevice graphics = GraphicsDeviceFactory.CreateForWindow(window, new GraphicsConfiguration
        { EnableDebugLayer = false, EnableVSync = false, ForceSoftwareAdapter = true });
        graphics.ConfigureDirectionalShadows(new(DirectionalShadowQuality.Low));
        Assert.Throws<InvalidOperationException>(() => graphics.RenderFrame(GraphicsColor.ForgeLineClear, context =>
        {
            if (failure == 1) context.BeginPass(GraphicsFramePass.Overlay);
            context.BeginDirectionalShadowPass(new(Matrix4x4.Identity, Vector3.Zero, 1, 1));
            if (failure == 2)
            {
                context.EndDirectionalShadowPass();
                throw new InvalidOperationException("Controlled recording failure.");
            }
        }));
        Assert.Equal(0UL, graphics.Diagnostics.Surface.SubmittedFrameCount);
        Assert.Equal(1, graphics.Diagnostics.Health.SubmissionFaultCount);
        Assert.Null(graphics.Diagnostics.DirectionalShadows!.Value.CpuMilliseconds);
        Assert.Equal(0UL, graphics.Diagnostics.DirectionalShadows.Value.CpuSubmission);
    }
}
