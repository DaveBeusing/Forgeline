using System.Numerics;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Graphics;
using ForgeLine.Platform.Windows;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

[Collection("Native scene output")]
public sealed class WorldEnvironmentNativeTests
{
    [Fact]
    public void WorldShaderVariantsRetainBudgetsStaticPixelsAndOutputSpaceMarkers()
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-world-appearance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var records = new List<RuntimeAssetRecord>();
            foreach (var material in TerrainPresentationProfile.CreateCentralDivide().Materials)
            {
                string file = material.AssetId + ".flasset";
                RuntimeAssetFile.Write(Path.Combine(root, file), RuntimeAssetType.Material, [], "{}"u8.ToArray());
                records.Add(new() { Id = material.AssetId, Type = RuntimeAssetType.Material, SourcePath = file, RuntimePath = file,
                    SourceHash = "test", BuildHash = "test", RuntimeVersion = 1, CompilerVersion = "test" });
            }
            File.WriteAllText(Path.Combine(root, RuntimeAssetCatalog.ManifestFileName), JsonSerializer.Serialize(
                new RuntimeAssetManifest { CompilerVersion = "test", Assets = records }, RuntimeAssetCatalog.CreateJsonOptions()));
            var catalog = RuntimeAssetCatalog.Load(root);
            using var platform = new WindowsPlatform();
            using var window = platform.CreateWindow(new("FORGELINE World Appearance", 320, 200));
            using var graphics = GraphicsDeviceFactory.CreateForWindow(window,
                new() { ForceSoftwareAdapter = true, EnableVSync = false, EnableDebugLayer = false });
            graphics.ConfigureSceneOutput(new(true, 1.15f, true));
            var terrain = new TerrainWorld(new WorldGridSettings { HeightSamplesPerSide = 2 },
                [new TerrainChunk(new(0, 0), new TerrainHeightfield(2, 256, new float[4]))]);
            var camera = new RtsCamera(new() { InitialTarget = new(128, 0, 128), InitialDistance = 120 });
            var snapshots = new PresentationSnapshotBuffer();
            snapshots.Publish(new(new SimulationTick(1), TimeSpan.FromMilliseconds(50), 1,
                [new RenderInstance(new EntityId(1, 1), new(new(128, 8, 128), Quaternion.Identity, new(10, 10, 10)),
                    new RenderMeshHandle(1), RenderMaterialHandle.Default, RenderVisibilityMask.World)]));
            var world = new RenderWorld(); world.Update(snapshots);
            var compiler = new DxcShaderCompiler();
            var vertex = compiler.Compile("float4 VSMain(uint id:SV_VertexID):SV_Position { float2 uv=float2((id<<1)&2,id&2); return float4(uv*float2(2,-2)+float2(-1,1),0,1); }", GraphicsShaderStage.Vertex, "VSMain", "WorldMarker.hlsl");
            var pixel = compiler.Compile("float4 PSMain(float4 p:SV_Position):SV_Target0 { if(int(p.x)!=9) discard; return float4(1,0,0,1); }", GraphicsShaderStage.Pixel, "PSMain", "WorldMarkerPixel.hlsl");
            using var marker = graphics.CreateGraphicsPipeline(new(vertex, pixel) { CullMode = GraphicsCullMode.None });
            byte[] neutral = Capture(new());
            var appearance = new WorldEnvironmentSettings { Daylight = .3f, CloudCover = 1, Wetness = 1 };
            byte[] altered = Capture(appearance);
            Assert.NotEqual(neutral, altered);
            Assert.Equal(altered, Capture(appearance));
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, altered.AsSpan((100 * 320 + 9) * 4, 4).ToArray());
            Assert.Equal(0, graphics.Diagnostics.Resources.TextureBindingFailureCount);
            Assert.Equal(0, graphics.Diagnostics.Health.SubmissionFaultCount);

            byte[] Capture(WorldEnvironmentSettings environment)
            {
                var lighting = environment.ResolveLighting(SceneLightingSettings.Default);
                using var ground = new TerrainRenderer(graphics, terrain, runtimeAssets: catalog, lighting: lighting,
                    environment: environment, residency: new(true, 1)) { DebugChunksEnabled = false };
                using var objects = new SimpleInstanceRenderer(graphics, catalog, lighting, environment: environment);
                ground.Prepare(camera, 320, 200);
                byte[] pixels = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, context =>
                {
                    ground.Render(context, camera); objects.Render(context, camera, world, 1);
                    context.BeginPass(GraphicsFramePass.Overlay);
                    context.SetPipeline(marker); context.Draw(3);
                });
                Assert.Equal(13, ground.LastDiagnostics.MaximumTextureSamplesPerPixel);
                Assert.Equal(64, ground.LastDiagnostics.RootSignatureDwords);
                Assert.Equal(1, objects.LastDiagnostics.VisibleInstances);
                Assert.Equal(1, ground.LastDiagnostics.ResidentChunks);
                return pixels;
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
