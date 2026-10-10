using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

[Collection("Native scene output")]
public sealed class DirectionalShadowIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionShadowsChangeGroundPreserveHiddenInformationAndRetire(bool linear)
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Directional Shadow Test", 512, 320));
        using var graphics = (D3D12GraphicsDevice)GraphicsDeviceFactory.CreateForWindow(window, new GraphicsConfiguration
        { EnableDebugLayer = false, EnableVSync = false, ForceSoftwareAdapter = true });
        var terrain = DevelopmentTerrainFactory.CreateRepresentativeWorld(chunkRadius: 1);
        var camera = new RtsCamera(new RtsCameraSettings { InitialDistance = 25 });
        var visible = new RenderInstance(new EntityId(1, 1), new RenderTransform(new Vector3(0, 4, 0), Quaternion.Identity, new Vector3(6)),
            new RenderMeshHandle(1), RenderMaterialHandle.Default, RenderVisibilityMask.World);
        var hidden = visible with { Entity = new EntityId(2, 1), Transform = new(new Vector3(8, 5, 0), Quaternion.Identity, new Vector3(12)), Visibility = RenderVisibilityMask.Debug };
        graphics.ConfigureSceneOutput(new(linear, 1.15f, true));
        byte[] baseline = Capture(false, false);
        byte[] shadowed = Capture(true, false);
        byte[] withHidden = Capture(true, true);
        Assert.Equal(shadowed, withHidden);
        Assert.True(baseline.Where((value, index) => value != shadowed[index]).Count() > 100);
        var diagnostics = graphics.Diagnostics.DirectionalShadows!.Value;
        Assert.True(diagnostics.DrawCalls > 0);
        Assert.InRange(diagnostics.SubmittedCasters, 1, 1024);
        Assert.Equal(12L * 1024 * 1024, diagnostics.TexturePayloadBytes);
        Assert.Equal(3, diagnostics.ShaderResourceDescriptors);
        if (graphics.Diagnostics.GpuTimingAvailable) Assert.NotNull(diagnostics.GpuMilliseconds);
        ulong submitted = graphics.Diagnostics.Surface.SubmittedFrameCount;
        graphics.Resize(0, 0);
        graphics.RenderFrame(GraphicsColor.ForgeLineClear, _ => throw new InvalidOperationException());
        Assert.Equal(submitted, graphics.Diagnostics.Surface.SubmittedFrameCount);
        graphics.Resize(400, 300);
        graphics.RenderFrame(GraphicsColor.ForgeLineClear);
        Capture(true, true);
        graphics.ConfigureDirectionalShadows(default);
        Assert.Equal(0, graphics.Diagnostics.DirectionalShadows!.Value.TexturePayloadBytes);
        Assert.Equal(linear ? 3 : 0, graphics.Diagnostics.Resources.ShaderResourceDescriptorsUsed);

        byte[] Capture(bool enabled, bool includeHidden)
        {
            graphics.ConfigureDirectionalShadows(new(enabled ? DirectionalShadowQuality.Low : DirectionalShadowQuality.Disabled));
            using var renderer = new TerrainRenderer(graphics, terrain) { DebugChunksEnabled = false };
            using var objects = new SimpleInstanceRenderer(graphics);
            var snapshots = new PresentationSnapshotBuffer();
            snapshots.Publish(new PresentationSnapshot(new SimulationTick(1), TimeSpan.FromMilliseconds(50), 1,
                includeHidden ? [visible, hidden] : [visible]));
            var world = new RenderWorld();
            Assert.True(world.Update(snapshots));
            return graphics.CaptureFrame(GraphicsColor.ForgeLineClear, context =>
            {
                if (enabled)
                {
                    var frame = DirectionalShadowProjection.Create(camera.CaptureState(), SceneLightingSettings.Default, graphics.DirectionalShadows);
                    context.BeginDirectionalShadowPass(frame);
                    int draws = renderer.RenderShadow(context, frame, 128);
                    objects.RenderShadow(context, camera, world, 1, frame, 1024 - draws, 256 - draws);
                    context.EndDirectionalShadowPass();
                }
                renderer.Render(context, camera);
                objects.Render(context, camera, world, 1);
                context.BeginPass(GraphicsFramePass.Overlay);
            });
        }
    }
}
