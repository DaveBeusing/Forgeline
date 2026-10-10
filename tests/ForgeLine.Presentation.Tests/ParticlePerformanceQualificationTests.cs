using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class ParticlePerformanceQualificationTests
{
    private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented = true, IncludeFields = true };
    public static bool Enabled => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FORGELINE_PARTICLE_QUALIFICATION_OUTPUT"));

    [Fact(Skip = "Set FORGELINE_PARTICLE_QUALIFICATION_OUTPUT for isolated hardware qualification.", SkipUnless = nameof(Enabled))]
    public void MeasureBoundedProductionEffectsAtRtsViewpoints()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ForgeLine.sln"))) root = root.Parent;
        Assert.NotNull(root);
        string output = Path.GetFullPath(Environment.GetEnvironmentVariable("FORGELINE_PARTICLE_QUALIFICATION_OUTPUT")!, root.FullName);
        var catalog = RuntimeAssetCatalog.Load(Path.Combine(root.FullName, "assets/runtime"));
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Effect Qualification", 1280, 720));
        using var graphics = (D3D12GraphicsDevice)GraphicsDeviceFactory.CreateForWindow(window,
            new() { EnableDebugLayer = false, EnableVSync = false, AllowSoftwareAdapterFallback = false });
        Assert.False(graphics.Diagnostics.Device.IsSoftwareAdapter);
        graphics.ConfigureSceneOutput(new(true, 1.15f, true));
        using var meshes = new SimpleInstanceRenderer(graphics, catalog);
        var instances = new RenderInstance[257];
        VfxEffectKind[] kinds = [VfxEffectKind.DestructionSmokePlume, VfxEffectKind.DestructionDustCloud,
            VfxEffectKind.DestructionSparkEmission, VfxEffectKind.ExplosionBuilding];
        for (int i = 0; i < 256; i++)
        {
            VfxPresentationDefinition definition = VfxPresentationCatalog.Get(kinds[i % kinds.Length]);
            instances[i] = new(new((uint)i + 1, 1), new(new((i % 16 - 7.5f) * 4, 2, (i / 16 - 7.5f) * 4),
                Quaternion.Identity, definition.BaseScale), new(1), RenderMaterialHandle.Default,
                RenderVisibilityMask.Default, VfxFeature: new(definition.Kind));
        }
        instances[256] = instances[0] with { Entity = new(257, 1), Visibility = RenderVisibilityMask.Debug };
        var snapshots = new PresentationSnapshotBuffer();
        snapshots.Publish(new(new SimulationTick(1), TimeSpan.FromMilliseconds(50), instances.Length, instances));
        var world = new RenderWorld();
        Assert.True(world.Update(snapshots));
        var results = new List<object>();
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        foreach (float distance in new[] { 120f, 420f, 1000f })
        {
            var camera = new RtsCamera(new() { InitialTarget = Vector3.Zero, InitialDistance = distance, MaximumDistance = 2000 });
            Action<IGraphicsCommandContext> record = context =>
            {
                meshes.Render(context, camera, world, 1);
                context.BeginPass(GraphicsFramePass.Overlay);
            };
            for (int i = 0; i < 64; i++) graphics.RenderFrame(GraphicsColor.ForgeLineClear, record);
            var cpu = new double[512]; var gpu = new List<double>(); ulong lastFence = 0;
            for (int i = 0; i < cpu.Length; i++)
            {
                long start = Stopwatch.GetTimestamp();
                graphics.RenderFrame(GraphicsColor.ForgeLineClear, record);
                cpu[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var diagnostics = graphics.Diagnostics;
                if (diagnostics.Frame is { } frame && frame.GpuSubmissionFence > lastFence)
                {
                    lastFence = frame.GpuSubmissionFence;
                    if (diagnostics.GpuFrameMilliseconds is { } ms) gpu.Add(ms);
                }
            }
            graphics.WaitForIdle();
            byte[] pixels = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, record);
            using (var file = File.Create(Path.Combine(Path.GetDirectoryName(output)!, $"{Path.GetFileNameWithoutExtension(output)}-{distance}.ppm")))
            {
                file.Write(System.Text.Encoding.ASCII.GetBytes("P6\n1280 720\n255\n"));
                for (int i = 0; i < pixels.Length; i += 4) file.Write(pixels.AsSpan(i, 3));
            }
            Assert.NotEmpty(gpu); Array.Sort(cpu);
            results.Add(new { distance, camera = camera.CaptureState(), cpuMeanMs = cpu.Average(), cpuP99Ms = cpu[506],
                gpuMeanMs = gpu.Average(), gpuSamples = gpu.Count, meshes = meshes.LastDiagnostics, resources = graphics.Diagnostics.Resources });
        }
        File.WriteAllText(output, JsonSerializer.Serialize(new { graphics.Diagnostics.Device,
            driver = Environment.GetEnvironmentVariable("FORGELINE_BENCHMARK_DRIVER"),
            method = "256 copied production smoke/dust/spark/building-explosion effects and one hidden instance; actual 120/420/1000m zoom, 1280x720 FP16/ACES exposure1.15, VSync off, 64 warmups/512 samples, unique completed fences; CPU includes Present and fence reuse.", results },
            ReportOptions));
    }
}
