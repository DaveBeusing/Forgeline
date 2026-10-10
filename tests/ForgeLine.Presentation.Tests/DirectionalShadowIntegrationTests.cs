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
    [Fact]
    public void DenseAuthorizedCastersStopAtTheQualityBudget()
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Shadow Caster Budget Test", 320, 240));
        using IGraphicsDevice graphics = GraphicsDeviceFactory.CreateForWindow(window, new GraphicsConfiguration
        { EnableDebugLayer = false, EnableVSync = false, ForceSoftwareAdapter = true });
        graphics.ConfigureDirectionalShadows(new(DirectionalShadowQuality.Low));
        var camera = new RtsCamera();
        var instances = new RenderInstance[10_000];
        for (int i = 0; i < instances.Length; i++) instances[i] = new(new EntityId((uint)i + 1, 1),
            new(new Vector3(0, 4, 0), Quaternion.Identity, new Vector3(6)), new(1), RenderMaterialHandle.Default, RenderVisibilityMask.World);
        var snapshots = new PresentationSnapshotBuffer();
        snapshots.Publish(new PresentationSnapshot(new SimulationTick(1), TimeSpan.FromMilliseconds(50), instances.Length, instances));
        var world = new RenderWorld();
        Assert.True(world.Update(snapshots));
        using var objects = new SimpleInstanceRenderer(graphics);
        var frame = DirectionalShadowProjection.Create(camera.CaptureState(), SceneLightingSettings.Default, graphics.DirectionalShadows);
        graphics.RenderFrame(GraphicsColor.ForgeLineClear, context =>
        {
            context.BeginDirectionalShadowPass(frame);
            objects.RenderShadow(context, camera, world, 1, frame, 1024, 256);
            context.EndDirectionalShadowPass();
            context.BeginPass(GraphicsFramePass.Overlay);
        });
        graphics.WaitForIdle();
        Assert.Equal(1024, graphics.Diagnostics.DirectionalShadows!.Value.SubmittedCasters);
        Assert.Equal(1, graphics.Diagnostics.DirectionalShadows.Value.DrawCalls);
        Assert.Equal(10_000, world.InstanceCount);
    }

    [Theory]
    [InlineData(false, 25f, 1f)]
    [InlineData(true, 25f, 1f)]
    [InlineData(false, 120f, 1f)]
    [InlineData(false, 120f, 1.5f)]
    [InlineData(false, 120f, 2f)]
    [InlineData(false, 420f, 1f)]
    [InlineData(false, 420f, 1.5f)]
    [InlineData(false, 420f, 2f)]
    [InlineData(false, 1000f, 1f)]
    [InlineData(false, 1000f, 1.5f)]
    [InlineData(false, 1000f, 2f)]
    public void ProductionShadowsChangeGroundPreserveHiddenInformationAndRetire(bool linear, float distance, float rasterScale)
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Directional Shadow Test", (int)(512 * rasterScale), (int)(320 * rasterScale)));
        using var graphics = (D3D12GraphicsDevice)GraphicsDeviceFactory.CreateForWindow(window, new GraphicsConfiguration
        { EnableDebugLayer = false, EnableVSync = false, ForceSoftwareAdapter = true });
        var terrain = DevelopmentTerrainFactory.CreateRepresentativeWorld(chunkRadius: 1);
        var camera = new RtsCamera(new RtsCameraSettings { InitialDistance = distance, MaximumDistance = 2000 });
        var visible = new RenderInstance(new EntityId(1, 1), new RenderTransform(new Vector3(0, distance >= 120 ? 13 : 4, 0), Quaternion.Identity, new Vector3(distance >= 120 ? 24 : 6)),
            new RenderMeshHandle(1), RenderMaterialHandle.Default, RenderVisibilityMask.World);
        var hidden = visible with { Entity = new EntityId(2, 1), Transform = new(new Vector3(8, 5, 0), Quaternion.Identity, new Vector3(12)), Visibility = RenderVisibilityMask.Debug };
        graphics.ConfigureSceneOutput(new(linear, 1.15f, true));
        byte[] baseline = Capture(false, false);
        byte[] shadowed = Capture(true, false);
        Assert.Equal(shadowed, Capture(true, false));
        byte[] withHidden = Capture(true, true);
        Assert.Equal(shadowed, withHidden);
        if (distance == 25)
        {
            byte[] withoutContact = Capture(true, false, 0);
            Assert.True(shadowed.Where((value, index) => value != withoutContact[index]).Any());
            for (int i = 0; i < shadowed.Length; i++) Assert.InRange(shadowed[i], 0, withoutContact[i] + 1);
        }
        Assert.True(baseline.Where((value, index) => value != shadowed[index]).Any());
        string? captureDirectory = Environment.GetEnvironmentVariable("FORGELINE_SHADOW_CAPTURE_DIRECTORY");
        if (!string.IsNullOrEmpty(captureDirectory) && !linear && distance == 25 && rasterScale == 1)
        {
            Directory.CreateDirectory(captureDirectory);
            Export("directional-disabled.ppm", baseline);
            Export("directional-low.ppm", shadowed);
        }
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

        byte[] Capture(bool enabled, bool includeHidden, float contactStrength = 0.12f)
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
                    var frame = DirectionalShadowProjection.Create(camera.CaptureState(), SceneLightingSettings.Default, graphics.DirectionalShadows)
                        with { ContactStrength = contactStrength };
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

        void Export(string name, byte[] data)
        {
            byte[] rgb = new byte[data.Length / 4 * 3];
            for (int i = 0; i < data.Length / 4; i++) data.AsSpan(i * 4, 3).CopyTo(rgb.AsSpan(i * 3));
            using var stream = File.Create(Path.Combine(captureDirectory!, name));
            stream.Write(System.Text.Encoding.ASCII.GetBytes("P6\n512 320\n255\n"));
            stream.Write(rgb);
        }
    }
}
