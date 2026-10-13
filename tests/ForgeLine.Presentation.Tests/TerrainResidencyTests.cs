using System.Numerics;
using ForgeLine.Graphics;
using ForgeLine.Platform.Windows;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

[Collection("Native scene output")]
public sealed class TerrainResidencyTests
{
    [Fact]
    public void CameraJumpsEvictUnusedChunksAndKeepExactEagerPixels()
    {
        using var platform = new WindowsPlatform();
        using var window = platform.CreateWindow(new("FORGELINE Terrain Residency", 320, 200));
        using var graphics = GraphicsDeviceFactory.CreateForWindow(window,
            new() { ForceSoftwareAdapter = true, EnableVSync = false, EnableDebugLayer = false });
        var world = DevelopmentTerrainFactory.CreateRepresentativeWorld(chunkRadius: 3);
        var camera = new RtsCamera(new() { InitialTarget = Vector3.Zero, InitialDistance = 120 });
        using var eager = new TerrainRenderer(graphics, world) { DebugChunksEnabled = false };
        using var streamed = new TerrainRenderer(graphics, world, residency: new(true, 8)) { DebugChunksEnabled = false };
        foreach (Vector3 target in new[] { Vector3.Zero, new(640, 0, 640), new(-512, 0, 512), new(512, 0, -512), Vector3.Zero })
        {
            camera.ApplyState(camera.CaptureState() with { Target = target });
            streamed.Prepare(camera, 320, 200);
            byte[] baseline = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, context => eager.Render(context, camera));
            byte[] actual = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, context =>
            {
                Assert.True(graphics.IsRecording);
                Assert.Throws<InvalidOperationException>(() => streamed.Prepare(camera, 320, 200));
                streamed.Render(context, camera);
            });
            Assert.Equal(baseline, actual);
            Assert.InRange(streamed.LastDiagnostics.ResidentChunks, 1, 8);
            Assert.Equal(eager.LastDiagnostics.VisibleChunks, streamed.LastDiagnostics.VisibleChunks);
            Assert.True(streamed.LastDiagnostics.ResidentGeometryBytes < eager.LastDiagnostics.ResidentGeometryBytes);
            Assert.Equal(streamed.LastDiagnostics.ResidentChunks * 5720L, streamed.LastDiagnostics.ResidentControlBytes);
        }
        Assert.True(streamed.LastDiagnostics.ChunkEvictions > 0);
        long uploads = streamed.LastDiagnostics.ChunkUploads;
        for (int i = 0; i < 10; i++) streamed.Prepare(camera, 320, 200);
        graphics.CaptureFrame(GraphicsColor.ForgeLineClear, context => streamed.Render(context, camera));
        Assert.Equal(uploads, streamed.LastDiagnostics.ChunkUploads);
        Assert.Equal(0, graphics.Diagnostics.Resources.TextureBindingFailureCount);
        Assert.Equal(0, graphics.Diagnostics.Health.SubmissionFaultCount);
    }

    [Fact]
    public void ShadowCoverageResizeAndBudgetFailureNeverDropRequiredTerrain()
    {
        using var platform = new WindowsPlatform();
        using var window = platform.CreateWindow(new("FORGELINE Terrain Shadow Residency", 320, 200));
        using var graphics = GraphicsDeviceFactory.CreateForWindow(window,
            new() { ForceSoftwareAdapter = true, EnableVSync = false, EnableDebugLayer = false });
        graphics.ConfigureDirectionalShadows(new(DirectionalShadowQuality.Low));
        var world = DevelopmentTerrainFactory.CreateRepresentativeWorld(chunkRadius: 2);
        var camera = new RtsCamera(new() { InitialDistance = 120, MaximumDistance = 2000 });
        using var eager = new TerrainRenderer(graphics, world) { DebugChunksEnabled = false };
        using var streamed = new TerrainRenderer(graphics, world, residency: new(true, 25)) { DebugChunksEnabled = false };
        var shadow = DirectionalShadowProjection.Create(camera.CaptureState(), SceneLightingSettings.Default, graphics.DirectionalShadows);
        streamed.Prepare(camera, 320, 200, shadow);
        byte[] baseline = Capture(eager);
        Assert.Equal(baseline, Capture(streamed));
        graphics.Resize(400, 300);
        graphics.RenderFrame(GraphicsColor.ForgeLineClear);
        streamed.Prepare(camera, 400, 300, shadow);
        Assert.Equal(Capture(eager), Capture(streamed));
        using var limited = new TerrainRenderer(graphics, world, residency: new(true, 1));
        long textures = graphics.Diagnostics.Resources.ResidentTextureBytes;
        Assert.Throws<InvalidOperationException>(() => limited.Prepare(camera, 400, 300, shadow));
        Assert.Equal(textures, graphics.Diagnostics.Resources.ResidentTextureBytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerrainResidencySettings(true, 0).Validate());

        byte[] Capture(TerrainRenderer renderer) => graphics.CaptureFrame(GraphicsColor.ForgeLineClear, context =>
        {
            context.BeginDirectionalShadowPass(shadow);
            renderer.RenderShadow(context, shadow, 128);
            context.EndDirectionalShadowPass();
            renderer.Render(context, camera);
        });
    }
}
