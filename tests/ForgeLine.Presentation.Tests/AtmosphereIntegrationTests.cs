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
public sealed class AtmosphereIntegrationTests
{
    [Theory]
    [InlineData(120f, 1f)] [InlineData(120f, 1.5f)] [InlineData(120f, 2f)]
    [InlineData(420f, 1f)] [InlineData(420f, 1.5f)] [InlineData(420f, 2f)]
    [InlineData(1000f, 1f)] [InlineData(1000f, 1.5f)] [InlineData(1000f, 2f)]
    public void ProductionDepthIsStableAcrossZoomScaleAndHiddenCasters(float distance, float scale)
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Atmospheric Scene Test", (int)(512*scale), (int)(320*scale)));
        using var graphics = (D3D12GraphicsDevice)GraphicsDeviceFactory.CreateForWindow(window,
            new() { ForceSoftwareAdapter = true, EnableDebugLayer = false, EnableVSync = false });
        SceneLightingSettings lighting = SceneLightingSettings.Default;
        graphics.ConfigureSceneOutput(new(true, lighting.Exposure, true));
        var shadows = new GraphicsDirectionalShadowSettings(distance == 120 && scale == 1 ? DirectionalShadowQuality.Low : DirectionalShadowQuality.Disabled);
        graphics.ConfigureDirectionalShadows(shadows);
        TerrainWorld terrain = DevelopmentTerrainFactory.CreateRepresentativeWorld(chunkRadius: 1);
        var camera = new RtsCamera(new() { InitialDistance = distance, MaximumDistance = 2000 });
        Assert.Equal(distance, camera.Distance);
        using var terrainRenderer = new TerrainRenderer(graphics, terrain, lighting: lighting) { DebugChunksEnabled = false };
        using var objects = new SimpleInstanceRenderer(graphics, lighting: lighting);
        var world = new RenderWorld();
        var snapshots = new PresentationSnapshotBuffer();
        var visible = new RenderInstance(new EntityId(1,1), new(new Vector3(0, 8, 0), Quaternion.Identity, new Vector3(10)),
            new(1), RenderMaterialHandle.Default, RenderVisibilityMask.Default);
        Publish(false);
        byte[] off = Capture(AtmosphereQuality.Disabled);
        byte[] on = Capture(AtmosphereQuality.Height);
        Assert.NotEqual(off, on);
        Assert.Equal(on, Capture(AtmosphereQuality.Height));
        Publish(true);
        Assert.Equal(on, Capture(AtmosphereQuality.Height));
        Assert.Equal(off, Capture(AtmosphereQuality.Disabled));
        Assert.Equal(0, graphics.Diagnostics.Health.SubmissionFaultCount);
        string? directory = Environment.GetEnvironmentVariable("FORGELINE_ATMOSPHERE_CAPTURE_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(directory) && scale == 1)
        {
            Directory.CreateDirectory(directory);
            Save(Path.Combine(directory, $"atmosphere-{distance}-off.ppm"), off);
            Save(Path.Combine(directory, $"atmosphere-{distance}-height.ppm"), on);
        }

        void Publish(bool hidden)
        {
            RenderInstance secret = visible with { Entity = new EntityId(2,1), Visibility = RenderVisibilityMask.Debug,
                Transform = new(new Vector3(0,25,0), Quaternion.Identity, new Vector3(40)) };
            snapshots.Publish(new(new SimulationTick(hidden ? 2UL : 1UL), TimeSpan.FromMilliseconds(50), 1,
                hidden ? [visible, secret] : [visible]));
            Assert.True(world.Update(snapshots));
        }

        byte[] Capture(AtmosphereQuality quality)
        {
            graphics.ConfigureAtmosphere(GraphicsAtmosphereSettings.Default with { Quality = quality });
            byte[] result = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, context =>
            {
                if (quality != AtmosphereQuality.Disabled)
                    context.SetAtmosphereView(SceneAtmosphereSettings.CreateView(camera,context.Width,context.Height));
                if (shadows.Enabled)
                {
                    var frame = DirectionalShadowProjection.Create(camera.CaptureState(), lighting, shadows);
                    context.BeginDirectionalShadowPass(frame);
                    int count = terrainRenderer.RenderShadow(context, frame, 128);
                    objects.RenderShadow(context,camera,world,1,frame,shadows.MaximumCasters-count,shadows.MaximumDraws-count);
                    context.EndDirectionalShadowPass();
                }
                terrainRenderer.Render(context,camera);
                objects.Render(context,camera,world,1);
                context.BeginPass(GraphicsFramePass.Overlay);
            });
            var diagnostics = graphics.Diagnostics.Atmosphere!.Value;
            Assert.Equal(quality != AtmosphereQuality.Disabled, diagnostics.Applied);
            Assert.Equal(quality == AtmosphereQuality.Disabled ? 0 : 1, diagnostics.DepthDescriptors);
            Assert.Equal(0, diagnostics.AdditionalTargetBytes);
            return result;
        }

        void Save(string path, byte[] rgba)
        {
            using var file = File.Create(path);
            file.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{graphics.Diagnostics.Surface.Width} {graphics.Diagnostics.Surface.Height}\n255\n"));
            for (int pixel=0; pixel<rgba.Length; pixel+=4) file.Write(rgba.AsSpan(pixel,3));
        }
    }
}
