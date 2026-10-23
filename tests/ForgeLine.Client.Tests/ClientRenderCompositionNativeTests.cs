using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ClientRenderCompositionNativeTests
{
    [Fact]
    public void WarpCompositionReconfiguresScaledTargetsAndRetiresParticleDepthAcrossSuspendAndResize()
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new("FORGELINE Composition Lifecycle", 320, 240));
        using IGraphicsDevice graphics = GraphicsDeviceFactory.CreateForWindow(window, new()
        {
            ForceSoftwareAdapter = true, EnableVSync = false, EnableDebugLayer = false
        });
        var surface = new ClientRenderSurface(320, 240, false);
        foreach (float scale in new[] { 1f, .75f, 1f })
        {
            var configuration = ClientRenderConfiguration.Resolve(graphics, new()
            {
                Quality = RenderQuality.Ultra, ResolutionScale = scale
            }, surface.Width, surface.Height, SceneLightingSettings.Default, GraphicsAtmosphereSettings.Default);
            configuration.Apply(graphics);
            Assert.Equal(RenderQuality.Low, configuration.Profile.Active);
            graphics.ConfigureParticleDepth(graphics.SupportsParticleRendering);
            graphics.RenderFrame(GraphicsColor.ForgeLineClear, context => context.BeginPass(GraphicsFramePass.Overlay));
            graphics.WaitForIdle();
            var plan = graphics.Diagnostics.Frame!.Value.Plan;
            Assert.Equal(surface.Width, plan.Output.Width);
            Assert.Equal((int)Math.Ceiling(surface.Width * (double)configuration.Profile.ResolutionScale), plan.Depth.Width);
            Assert.False(surface.Update(graphics, 0, 0, true, out _));
            bool recorded = false;
            graphics.RenderFrame(GraphicsColor.ForgeLineClear, _ => recorded = true);
            Assert.False(recorded);
            Assert.True(surface.Update(graphics, 400, 300, false, out _));
            graphics.RenderFrame(GraphicsColor.ForgeLineClear, context => context.BeginPass(GraphicsFramePass.Overlay));
            graphics.WaitForIdle();
            Assert.Equal(400, graphics.Diagnostics.Frame!.Value.Plan.Output.Width);
            ClientRenderConfiguration.Reset(graphics);
            Assert.False(graphics.ParticleDepthEnabled);
        }
    }
}
