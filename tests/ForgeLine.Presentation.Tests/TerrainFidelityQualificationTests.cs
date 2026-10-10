using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.Core;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

[Collection("Native scene output")]
public sealed class TerrainFidelityQualificationTests
{
    private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented = true, IncludeFields = true };
    public static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_TERRAIN_QUALIFICATION_OUTPUT"));

    [Fact(Skip = "Set FORGELINE_TERRAIN_QUALIFICATION_OUTPUT for isolated production terrain measurement.", SkipUnless = nameof(Enabled))]
    public void MeasureProductionMaterialsAtFixedCentralDivideViewpoints()
    {
        string repository = FindRepository();
        string output = Path.GetFullPath(Environment.GetEnvironmentVariable("FORGELINE_TERRAIN_QUALIFICATION_OUTPUT")!, repository);
        string assetRoot = Environment.GetEnvironmentVariable("FORGELINE_TERRAIN_RUNTIME_ROOT") ?? "assets/runtime";
        RuntimeAssetCatalog catalog = RuntimeAssetCatalog.Load(Path.GetFullPath(assetRoot, repository));
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Terrain Qualification", 1280, 720));
        using var graphics = (D3D12GraphicsDevice)GraphicsDeviceFactory.CreateForWindow(window,
            new() { EnableDebugLayer = false, EnableVSync = false, AllowSoftwareAdapterFallback = false });
        Assert.False(graphics.Diagnostics.Device.IsSoftwareAdapter);
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        SceneLightingSettings lighting = SceneLightingSettings.Default;
        graphics.ConfigureSceneOutput(new(true, lighting.Exposure, true));
        using var renderer = new TerrainRenderer(graphics, terrain, runtimeAssets: catalog, lighting: lighting) { DebugChunksEnabled = false };
        var results = new List<object>();
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        foreach (var site in new[] { ("ridge-road", new Vector2(1536, 1536)), ("bridge", new Vector2(1536, 920)), ("industrial", new Vector2(650, 520)) })
        foreach (float distance in new[] { 120f, 420f, 1000f })
        {
            Assert.True(terrain.TrySampleHeight(site.Item2.X, site.Item2.Y, out float height));
            var camera = new RtsCamera(new() { InitialTarget = new(site.Item2.X, height, site.Item2.Y), InitialDistance = distance, MaximumDistance = 2000 });
            Assert.Equal(distance, camera.Distance);
            Action<IGraphicsCommandContext> record = context => { renderer.Render(context, camera); context.BeginPass(GraphicsFramePass.Overlay); };
            for (int warmup = 0; warmup < 64; warmup++) graphics.RenderFrame(GraphicsColor.ForgeLineClear, record);
            var cpu = new double[512];
            var total = new List<double>();
            var world = new List<double>();
            ulong lastFence = 0;
            for (int sample = 0; sample < cpu.Length; sample++)
            {
                long start = Stopwatch.GetTimestamp();
                graphics.RenderFrame(GraphicsColor.ForgeLineClear, record);
                cpu[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                GraphicsDiagnostics diagnostics = graphics.Diagnostics;
                if (diagnostics.Frame is { } frame && frame.GpuSubmissionFence > lastFence)
                {
                    lastFence = frame.GpuSubmissionFence;
                    if (diagnostics.GpuFrameMilliseconds is { } gpu) total.Add(gpu);
                    if (frame.WorldGpuMilliseconds is { } gpuWorld) world.Add(gpuWorld);
                }
            }
            graphics.WaitForIdle();
            byte[] pixels = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, record);
            Assert.Equal(pixels, graphics.CaptureFrame(GraphicsColor.ForgeLineClear, record));
            using (var file = File.Create(Path.Combine(Path.GetDirectoryName(output)!, $"{Path.GetFileNameWithoutExtension(output)}-{site.Item1}-{distance}.ppm")))
            {
                file.Write(System.Text.Encoding.ASCII.GetBytes("P6\n1280 720\n255\n"));
                for (int pixel = 0; pixel < pixels.Length; pixel += 4) file.Write(pixels.AsSpan(pixel, 3));
            }
            Array.Sort(cpu);
            Assert.NotEmpty(world);
            results.Add(new { site = site.Item1, camera = camera.CaptureState(), distance, cpuSamples = cpu.Length, gpuSamples = world.Count,
                cpuMeanMs = cpu.Average(), cpuP99Ms = cpu[(int)(cpu.Length * .99)], gpuTotalMeanMs = total.Average(), terrainGpuMeanMs = world.Average(),
                terrain = renderer.LastDiagnostics, resources = graphics.Diagnostics.Resources, targets = graphics.Diagnostics.Frame!.Value.Plan });
        }
        File.WriteAllText(output, JsonSerializer.Serialize(new { graphics.Diagnostics.Device, graphics.Diagnostics.Surface,
            driver = Environment.GetEnvironmentVariable("FORGELINE_BENCHMARK_DRIVER"),
            method = "Compiled production materials; canonical 144-chunk Central Divide; three fixed viewpoints and actual 120/420/1000m zoom; 1280x720, exposure 1.15/ACES, no shadows/atmosphere/objects, VSync off; 64 warmups, 512 samples; unique completed GPU fences; CPU includes Present/frame reuse. Run baseline and changed revision sequentially without other GPU workloads.", results },
            ReportOptions));
    }

    private static string FindRepository()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "ForgeLine.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Terrain qualification requires a source checkout or an absolute runtime root.");
    }

    [Fact(Skip = "Set FORGELINE_TERRAIN_QUALIFICATION_OUTPUT for isolated vegetation measurement.", SkipUnless = nameof(Enabled))]
    public void MeasureVegetationMotionWithIdenticalProductionBatches()
    {
        string repository = FindRepository();
        string output = Path.GetFullPath(Environment.GetEnvironmentVariable("FORGELINE_TERRAIN_QUALIFICATION_OUTPUT")!, repository);
        var catalog = RuntimeAssetCatalog.Load(Path.GetFullPath(Environment.GetEnvironmentVariable("FORGELINE_TERRAIN_RUNTIME_ROOT") ?? "assets/runtime", repository));
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Vegetation Qualification", 1280, 720));
        using var graphics = (D3D12GraphicsDevice)GraphicsDeviceFactory.CreateForWindow(window,
            new() { EnableDebugLayer = false, EnableVSync = false, AllowSoftwareAdapterFallback = false });
        Assert.False(graphics.Diagnostics.Device.IsSoftwareAdapter);
        graphics.ConfigureSceneOutput(new(true, 1.15f, true));
        var instances = new RenderInstance[257];
        for (int i = 0; i < 256; i++)
            instances[i] = new(new EntityId((uint)i + 1, 1), new(new((i % 16 - 7.5f) * 6, 0, (i / 16 - 7.5f) * 6), Quaternion.Identity, new(8, 20, 8)),
                new(1), RenderMaterialHandle.Default, RenderVisibilityMask.Default,
                WorldFeature: new(WorldVisualId.VegetationConifer, WorldPresentationKind.Vegetation, default, false));
        instances[256] = instances[0] with { Entity = new(257, 1), Visibility = RenderVisibilityMask.Debug };
        var snapshots = new PresentationSnapshotBuffer();
        snapshots.Publish(new(new SimulationTick(1), TimeSpan.FromMilliseconds(50), instances.Length, instances));
        var world = new RenderWorld();
        Assert.True(world.Update(snapshots));
        using var renderer = new SimpleInstanceRenderer(graphics, catalog);
        var results = new List<object>();
        foreach (float distance in new[] { 120f, 420f, 1000f })
        {
            var camera = new RtsCamera(new() { InitialDistance = distance, MaximumDistance = 2000 });
            byte[]? baseline = null;
            int baselineDraws = 0;
            foreach (bool moving in new[] { false, true })
            {
                Action<IGraphicsCommandContext> record = context =>
                {
                    renderer.Render(context, camera, world, 1, moving ? 1.0 : null);
                    context.BeginPass(GraphicsFramePass.Overlay);
                };
                for (int warmup = 0; warmup < 64; warmup++) graphics.RenderFrame(GraphicsColor.ForgeLineClear, record);
                var cpu = new double[512];
                var gpu = new List<double>();
                ulong lastFence = 0;
                for (int sample = 0; sample < cpu.Length; sample++)
                {
                    long start = Stopwatch.GetTimestamp();
                    graphics.RenderFrame(GraphicsColor.ForgeLineClear, record);
                    cpu[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    var diagnostics = graphics.Diagnostics;
                    if (diagnostics.Frame is { } frame && frame.GpuSubmissionFence > lastFence)
                    {
                        lastFence = frame.GpuSubmissionFence;
                        if (diagnostics.GpuFrameMilliseconds is { } timing) gpu.Add(timing);
                    }
                }
                graphics.WaitForIdle();
                byte[] pixels = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, record);
                if (!moving) { baseline = pixels; baselineDraws = renderer.LastDiagnostics.DrawCalls; }
                else
                {
                    Assert.Equal(baselineDraws, renderer.LastDiagnostics.DrawCalls);
                    Assert.InRange(renderer.LastDiagnostics.VisibleInstances, 1, 256);
                    if (distance == 120) Assert.NotEqual(baseline, pixels);
                }
                Array.Sort(cpu);
                results.Add(new { distance, moving, cpuMeanMs = cpu.Average(), cpuP99Ms = cpu[(int)(cpu.Length * .99)], gpuSamples = gpu.Count,
                    gpuMeanMs = gpu.Average(), instances = renderer.LastDiagnostics, submission = renderer.SubmissionMetrics, resources = graphics.Diagnostics.Resources });
            }
        }
        File.WriteAllText(Path.ChangeExtension(output, ".vegetation.json"), JsonSerializer.Serialize(new { graphics.Diagnostics.Device,
            method = "256 identical compiled conifers and one excluded Debug-only instance; paired static/explicit 1s visual time, 120/420/1000m cameras, 1280x720 linear output/ACES, VSync off, 64 warmups/512 samples, unique completed fences; no terrain, shadows, simulation or overlay geometry.", results }, ReportOptions));
    }
}
