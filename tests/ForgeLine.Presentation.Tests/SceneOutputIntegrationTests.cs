using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class SceneOutputIntegrationTests
{
    [Theory]
    [InlineData(25.0f, TerrainDebugVisualizationMode.None)]
    [InlineData(90.0f, TerrainDebugVisualizationMode.None)]
    [InlineData(240.0f, TerrainDebugVisualizationMode.None)]
    [InlineData(90.0f, TerrainDebugVisualizationMode.ControlRed)]
    [InlineData(90.0f, TerrainDebugVisualizationMode.MaterialPalette)]
    public void ProductionTerrainObjectsAndDebugPaletteKeepTheirOutput(float distance, TerrainDebugVisualizationMode mode)
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Scene Presentation Test", 512, 320));
        using var graphics = (D3D12GraphicsDevice)GraphicsDeviceFactory.CreateForWindow(window, new GraphicsConfiguration
        {
            EnableDebugLayer = false, EnableVSync = false, ForceSoftwareAdapter = true
        });
        TerrainWorld terrain = DevelopmentTerrainFactory.CreateRepresentativeWorld(chunkRadius: 1);
        var camera = new RtsCamera(new RtsCameraSettings { InitialDistance = distance });
        var snapshots = new PresentationSnapshotBuffer();
        snapshots.Publish(new PresentationSnapshot(new SimulationTick(1), TimeSpan.FromMilliseconds(50), 1,
            [new RenderInstance(new EntityId(1, 1), new RenderTransform(new Vector3(0, 4, 0), Quaternion.Identity, new Vector3(6)),
                new RenderMeshHandle(1), RenderMaterialHandle.Default, RenderVisibilityMask.Default)]));
        var world = new RenderWorld();
        Assert.True(world.Update(snapshots));
        byte[] legacy = Capture(false);
        byte[] composed = Capture(true);
        Assert.Equal(legacy.Length, composed.Length);
        for (int pixel = 0; pixel < legacy.Length; pixel++)
            Assert.InRange(Math.Abs(legacy[pixel] - composed[pixel]), 0, 1);

        byte[] Capture(bool enabled)
        {
            SceneLightingSettings lighting = SceneLightingSettings.Default;
            graphics.ConfigureSceneOutput(new(enabled, lighting.Exposure, true));
            using var terrainRenderer = new TerrainRenderer(graphics, terrain, lighting: lighting)
            {
                DebugVisualizationMode = mode, DebugChunksEnabled = true
            };
            using var objects = new SimpleInstanceRenderer(graphics, lighting: lighting);
            using var lines = new DebugDrawRenderer(graphics, depthEnabled: true);
            var debug = new DebugDraw { Enabled = true };
            debug.Line(new Vector3(-10, 6, 0), new Vector3(10, 6, 0), new Vector4(0.1f, 0.8f, 0.3f, 1));
            return graphics.CaptureFrame(GraphicsColor.ForgeLineClear, context =>
            {
                terrainRenderer.Render(context, camera);
                objects.Render(context, camera, world, 1);
                context.BeginPass(GraphicsFramePass.Overlay);
                lines.Render(context, camera, debug);
            });
        }
    }
}
