using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Graphics;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation.RenderContracts;
using Xunit;

namespace ForgeLine.Presentation.Tests;

[Collection("Native scene output")]
public sealed class ChunkDecorationNativeTests
{
    private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented = true };

    [Fact]
    public void DenseChunksRenderWithBoundedUploadsCompiledLodsAndShadows()
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-decoration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var catalog = CreateCatalog(root);
            using var platform = new WindowsPlatform();
            using var window = platform.CreateWindow(new("FORGELINE Chunk Decoration", 640, 360));
            using var graphics = GraphicsDeviceFactory.CreateForWindow(window,
                new() { ForceSoftwareAdapter = true, EnableVSync = false, EnableDebugLayer = false });
            graphics.ConfigureDirectionalShadows(new(DirectionalShadowQuality.Low));
            var reports = new List<object>();
            foreach (int count in new[] { 1000, 10000 })
            {
                int side = count == 1000 ? 2 : 5;
                var snapshot = new ChunkDecorationSnapshot(ChunkDecorationFixture.Generate(count, side));
                var world = ChunkDecorationFixture.World(snapshot);
                var camera = ChunkDecorationFixture.Camera(new(side * 128, 0, side * 128), side * 600);
                using var renderer = new SimpleInstanceRenderer(graphics, catalog);
                var shadow = DirectionalShadowProjection.Create(camera.CaptureState(), SceneLightingSettings.Default, graphics.DirectionalShadows);
                var cpu = new List<double>(); var gpu = new List<double>(); var fences = new HashSet<ulong>();
                for (int sample = 0; sample < 12; sample++)
                {
                    long started = Stopwatch.GetTimestamp();
                    renderer.Prepare(camera, world, new(640, 360, 1, shadow, 1024, 2.0, true));
                    graphics.RenderFrame(GraphicsColor.ForgeLineClear, Draw);
                    renderer.Submit();
                    var diagnostics = graphics.Diagnostics;
                    renderer.ObserveFrameDiagnostics(diagnostics);
                    renderer.Retire();
                    if (sample >= 4)
                    {
                        cpu.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                        if (diagnostics.Frame is { WorldGpuMilliseconds: { } milliseconds } frame && fences.Add(frame.GpuSubmissionFence))
                            gpu.Add(milliseconds);
                    }
                }
                renderer.Prepare(camera, world, new(640, 360, 1, shadow, 1024, 2.0));
                byte[] pixels = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, Draw);
                renderer.Submit(); renderer.Retire();
                renderer.Prepare(camera, world, new(640, 360, 1, shadow, 1024, 2.0));
                Assert.Equal(pixels, graphics.CaptureFrame(GraphicsColor.ForgeLineClear, Draw));
                renderer.Submit(); renderer.Retire();
                var result = renderer.DecorationDiagnostics;
                Assert.Equal(count, result.Render.VisibleInstances);
                Assert.Equal(count, result.Render.RuntimeMeshInstances);
                Assert.Equal(0, result.Render.FallbackMeshInstances);
                Assert.InRange(result.Render.DrawCalls, 1, 2);
                Assert.Equal(0, result.Admissions);
                Assert.Equal(count * 112L, result.UploadBytes);
                Assert.InRange(result.SharedAssets.LoadedMeshCount, 1, 2);
                Assert.InRange(graphics.Diagnostics.DirectionalShadows!.Value.SubmittedCasters, 1, 1024);
                Assert.Equal(0, world.InstanceCount);
                Assert.Equal(0, graphics.Diagnostics.Resources.TextureBindingFailureCount);
                Assert.Equal(0, graphics.Diagnostics.Health.SubmissionFaultCount);
                string? output = Environment.GetEnvironmentVariable("FORGELINE_DECORATION_CAPTURE");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Directory.CreateDirectory(output);
                    TerrainCaptureImage.WritePng(Path.Combine(output, $"decoration-{count}.png"), 640, 360, pixels);
                }
                reports.Add(new { count, side, Device = graphics.Diagnostics.Device, result, CpuSamples = cpu, WorldGpuSamples = gpu,
                    PixelSha256 = Convert.ToHexStringLower(SHA256.HashData(pixels)),
                    Qualification = "Windows D3D12 WARP; synthetic tall-grass stress mesh with nonunit offset bounds; no production art or physical GPU performance acceptance." });

                void Draw(IGraphicsCommandContext context)
                {
                    context.BeginDirectionalShadowPass(shadow);
                    renderer.RenderShadow(context, camera, world, 1, shadow, 1024, 256, 2.0);
                    context.EndDirectionalShadowPass();
                    renderer.Render(context, camera, world, 1, 2.0);
                }
            }
            string? destination = Environment.GetEnvironmentVariable("FORGELINE_DECORATION_CAPTURE");
            if (!string.IsNullOrWhiteSpace(destination))
                File.WriteAllText(Path.Combine(destination, "decoration.json"), JsonSerializer.Serialize(reports, ReportOptions));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MixedSpeciesKeepCompiledVariantIdsPivotsWindAndShadowAdmission()
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-decoration-mixed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var chunks = ChunkDecorationFixture.Mixed();
            var classes = chunks.SelectMany(c => c.Instances.ToArray()).Select(i => i.Classification).Distinct().ToArray();
            Assert.Equal(5, classes.Length);
            var assets = chunks.SelectMany(c => c.Profile.Species).Select(s => s.AssetId).Distinct().ToArray();
            var snapshot = new ChunkDecorationSnapshot(chunks);
            var catalog = CreateCatalog(root, assets);
            using var platform = new WindowsPlatform();
            using var window = platform.CreateWindow(new("FORGELINE Mixed Decoration", 640, 360));
            using var graphics = GraphicsDeviceFactory.CreateForWindow(window,
                new() { ForceSoftwareAdapter = true, EnableVSync = false, EnableDebugLayer = false });
            graphics.ConfigureDirectionalShadows(new(DirectionalShadowQuality.Low));
            var world = ChunkDecorationFixture.World(snapshot);
            var camera = ChunkDecorationFixture.Camera(new(256,0,256), 750);
            using var renderer = new SimpleInstanceRenderer(graphics, catalog);
            var shadow = DirectionalShadowProjection.Create(camera.CaptureState(), SceneLightingSettings.Default, graphics.DirectionalShadows);
            renderer.Prepare(camera, world, new(640,360,1,shadow,1024,2.0));
            byte[] pixels = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, context =>
            {
                context.BeginDirectionalShadowPass(shadow);
                renderer.RenderShadow(context,camera,world,1,shadow,1024,256,2.0);
                context.EndDirectionalShadowPass();
                renderer.Render(context,camera,world,1,2.0);
            });
            renderer.Submit(); renderer.Retire();
            Assert.Equal(snapshot.InstanceCount, renderer.DecorationDiagnostics.Render.RuntimeMeshInstances);
            Assert.Equal(0, renderer.DecorationDiagnostics.Render.FallbackMeshInstances);
            Assert.InRange(renderer.DecorationDiagnostics.SharedAssets.LoadedMeshCount, assets.Length, assets.Length * 2);
            Assert.InRange(renderer.DecorationDiagnostics.Render.DrawCalls, 5, assets.Length * 2);
            Assert.True(renderer.DecorationDiagnostics.Render.AnimatedVegetationInstances > 0);
            Assert.True(renderer.DecorationDiagnostics.Shadow.VisibleInstances > 0);
            Assert.Equal(0, graphics.Diagnostics.Resources.TextureBindingFailureCount);
            string? output = Environment.GetEnvironmentVariable("FORGELINE_DECORATION_CAPTURE");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Directory.CreateDirectory(output);
                TerrainCaptureImage.WritePng(Path.Combine(output,"decoration-mixed.png"),640,360,pixels);
                File.WriteAllText(Path.Combine(output,"decoration-mixed.json"),JsonSerializer.Serialize(
                    new { snapshot.InstanceCount, classes, assets, renderer.DecorationDiagnostics,
                        Qualification = "All five real scatter classifications and eleven asset IDs; synthetic compiled mesh/material fixtures, WARP, not production art." },ReportOptions));
            }
        }
        finally { Directory.Delete(root,true); }
    }

    internal static RuntimeAssetCatalog CreateCatalog(string root, IEnumerable<string>? meshAssets = null)
    {
        const string high = "mesh.world.vegetation.grass_clump", material = "material.world.vegetation";
        var records = new List<RuntimeAssetRecord>();
        RuntimeMeshVertex[] vertices = [
            V(-2,3,-4),V(4,3,-4),V(4,11,-4),V(-2,11,-4),
            V(-2,3,2),V(4,3,2),V(4,11,2),V(-2,11,2)];
        uint[] indices = [0,2,1,0,3,2,4,5,6,4,6,7,0,4,7,0,7,3,1,2,6,1,6,5,3,7,6,3,6,2,0,1,5,0,5,4];
        foreach (string family in meshAssets ?? [high])
        foreach (string id in new[] { family, family + "_lod1" })
        {
            string file = id + ".flasset";
            var mesh = new RuntimeMeshData(RuntimeMeshAttributes.Normal | RuntimeMeshAttributes.Uv0 | RuntimeMeshAttributes.Tangent,
                id == family ? vertices : vertices.Select(v => V((v.X-1)*5-5,(v.Y-3)*5-10,(v.Z+1)*5-15)).ToArray(), id == family ? indices : indices[..12], [material], [new(0, id == family ? indices.Length : 12, 0)]);
            RuntimeAssetFile.Write(Path.Combine(root, file), RuntimeAssetType.Mesh, [], mesh.ToPayload());
            records.Add(new() { Id = id, Type = RuntimeAssetType.Mesh, SourcePath = file, RuntimePath = file,
                SourceHash = "test", BuildHash = "test", RuntimeVersion = 1, CompilerVersion = "test",
                Bounds = id == family ? new(-2,3,-4,4,11,2) : new(-20,-10,-30,10,30,0), Lods = id == family ? [new(1,family + "_lod1",100)] : [] });
        }
        string materialFile = material + ".flasset";
        RuntimeAssetFile.Write(Path.Combine(root, materialFile), RuntimeAssetType.Material, [], "{}"u8.ToArray());
        records.Add(new() { Id = material, Type = RuntimeAssetType.Material, SourcePath = materialFile, RuntimePath = materialFile,
            SourceHash = "test", BuildHash = "test", RuntimeVersion = 1, CompilerVersion = "test" });
        File.WriteAllText(Path.Combine(root, RuntimeAssetCatalog.ManifestFileName),
            JsonSerializer.Serialize(new RuntimeAssetManifest { CompilerVersion = "test", Assets = records }, RuntimeAssetCatalog.CreateJsonOptions()));
        return RuntimeAssetCatalog.Load(root);
        static RuntimeMeshVertex V(float x,float y,float z) => new(x,y,z,0,1,0,0,0,1,0,0,1);
    }
}
