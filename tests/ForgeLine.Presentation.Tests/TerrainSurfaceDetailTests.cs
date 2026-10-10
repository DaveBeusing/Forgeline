using System.Numerics;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

[Collection("Native scene output")]
public sealed class TerrainSurfaceDetailTests
{
    [Fact]
    public void InvalidDetailFailsBeforeNativeAllocation()
    {
        var valid = TerrainSurfaceDetailSettings.Default;
        valid.Validate();
        foreach (var invalid in new[] { valid with { MicroTileMeters = float.NaN }, valid with { MicroTileMeters = 0 },
            valid with { MicroColorStrength = .09f }, valid with { MicroNormalStrength = -1 },
            valid with { RockSlopeEnd = valid.RockSlopeStart }, valid with { RockSlopeStart = float.PositiveInfinity } })
            Assert.Throws<ArgumentOutOfRangeException>(() => invalid.Validate());
    }

    [Theory]
    [InlineData(120f, false)] [InlineData(420f, true)] [InlineData(1000f, false)]
    public void DetailKeepsTextureRootAndDrawBudgetsAcrossSceneModes(float distance, bool shadowed)
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Terrain Detail Test", 512, 320));
        using var graphics = (D3D12GraphicsDevice)GraphicsDeviceFactory.CreateForWindow(window,
            new() { ForceSoftwareAdapter = true, EnableDebugLayer = false, EnableVSync = false });
        graphics.ConfigureSceneOutput(new(shadowed, 1.15f, true));
        graphics.ConfigureDirectionalShadows(new(shadowed ? DirectionalShadowQuality.Low : DirectionalShadowQuality.Disabled));
        if (shadowed) graphics.ConfigureAtmosphere(GraphicsAtmosphereSettings.Default with { Quality = AtmosphereQuality.Height });
        var terrain = DevelopmentTerrainFactory.CreateRepresentativeWorld(chunkRadius: 1);
        var camera = new RtsCamera(new() { InitialDistance = distance, MaximumDistance = 2000 });
        Assert.Equal(distance, camera.Distance);
        byte[] disabled = Capture(TerrainSurfaceDetailSettings.Disabled, out var before, out long baselineBytes);
        byte[] detailed = Capture(TerrainSurfaceDetailSettings.Default, out var after, out long detailBytes);
        Assert.Equal(detailed, Capture(TerrainSurfaceDetailSettings.Default, out _, out _));
        Assert.Equal(disabled, Capture(TerrainSurfaceDetailSettings.Disabled, out _, out _));
        Assert.Equal(before.DrawCalls, after.DrawCalls);
        Assert.Equal(before.TextureBindingsPerDraw, after.TextureBindingsPerDraw);
        Assert.Equal(13, after.TextureBindingsPerDraw);
        Assert.Equal(13, after.MaximumTextureSamplesPerPixel);
        Assert.InRange(after.RootSignatureDwords, 1, 64);
        Assert.Equal(shadowed ? 18 : 63, after.RootSignatureDwords);
        Assert.Equal(baselineBytes, detailBytes);
        Assert.Equal(0, graphics.Diagnostics.Health.SubmissionFaultCount);
        graphics.Resize(640, 400);
        graphics.RenderFrame(GraphicsColor.ForgeLineClear);
        Assert.Equal(640 * 400 * 4, Capture(TerrainSurfaceDetailSettings.Default, out _, out _).Length);

        byte[] Capture(TerrainSurfaceDetailSettings settings, out TerrainRenderDiagnostics diagnostics, out long bytes)
        {
            using var renderer = new TerrainRenderer(graphics, terrain, surfaceDetail: settings) { DebugChunksEnabled = false };
            byte[] pixels = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, context =>
            {
                if (shadowed)
                {
                    context.SetAtmosphereView(SceneAtmosphereSettings.CreateView(camera, context.Width, context.Height));
                    var frame = DirectionalShadowProjection.Create(camera.CaptureState(), SceneLightingSettings.Default, graphics.DirectionalShadows);
                    context.BeginDirectionalShadowPass(frame);
                    renderer.RenderShadow(context, frame, 128);
                    context.EndDirectionalShadowPass();
                }
                renderer.Render(context, camera);
                context.BeginPass(GraphicsFramePass.Overlay);
            });
            diagnostics = renderer.LastDiagnostics;
            bytes = graphics.Diagnostics.Resources.ResidentTextureBytes;
            return pixels;
        }
    }

    [Fact]
    public void StrategicPixelFootprintRemovesMicroDetailInsteadOfAliasing()
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Terrain Minification Test", 256, 160));
        using var graphics = (D3D12GraphicsDevice)GraphicsDeviceFactory.CreateForWindow(window,
            new() { ForceSoftwareAdapter = true, EnableDebugLayer = false, EnableVSync = false });
        var terrain = new TerrainWorld(new WorldGridSettings { ChunkSizeMeters = 2048, HeightSamplesPerSide = 2 },
            [new TerrainChunk(new(0, 0), new TerrainHeightfield(2, 2048, new float[4]))]);
        var camera = new RtsCamera(new() { InitialTarget = new(1024, 0, 1024), InitialDistance = 1000, MaximumDistance = 2000 });
        var settings = TerrainSurfaceDetailSettings.Default with { MicroTileMeters = 1 };
        Assert.Equal(Capture(settings with { MicroColorStrength = 0, MicroNormalStrength = 0 }), Capture(settings));

        byte[] Capture(TerrainSurfaceDetailSettings detail)
        {
            using var renderer = new TerrainRenderer(graphics, terrain, surfaceDetail: detail) { DebugChunksEnabled = false };
            return graphics.CaptureFrame(GraphicsColor.ForgeLineClear, context => renderer.Render(context, camera));
        }
    }
}
