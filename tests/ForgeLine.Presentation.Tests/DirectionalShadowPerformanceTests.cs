using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using ForgeLine.Core;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

[Collection("Native scene output")]
public sealed class DirectionalShadowPerformanceTests
{
    private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented = true };
    public static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_SHADOW_BENCHMARK_OUTPUT"));

    [Fact(Skip = "Set FORGELINE_SHADOW_BENCHMARK_OUTPUT for isolated hardware shadow measurement.", SkipUnless = nameof(Enabled))]
    public void CompareEquivalentDenseWorkloadsAcrossQualityAndRtsZoom()
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Shadow Qualification", 1280, 720));
        using var graphics = (D3D12GraphicsDevice)GraphicsDeviceFactory.CreateForWindow(window, new GraphicsConfiguration
        { EnableDebugLayer = false, EnableVSync = false, AllowSoftwareAdapterFallback = false });
        Assert.False(graphics.Diagnostics.Device.IsSoftwareAdapter);
        var terrain = DevelopmentTerrainFactory.CreateRepresentativeWorld(chunkRadius: 1);
        var instances = new RenderInstance[1000];
        for (int i = 0; i < instances.Length; i++) instances[i] = new(new EntityId((uint)i + 1, 1),
            new(new Vector3((i % 40 - 20) * 5, 3, (i / 40 - 12) * 5), Quaternion.Identity, new Vector3(4)),
            new(1), RenderMaterialHandle.Default, RenderVisibilityMask.World);
        var snapshots = new PresentationSnapshotBuffer();
        snapshots.Publish(new PresentationSnapshot(new SimulationTick(1), TimeSpan.FromMilliseconds(50), instances.Length, instances));
        var world = new RenderWorld();
        Assert.True(world.Update(snapshots));
        var results = new List<object>();
        foreach (float distance in new[] { 120f, 420f, 1000f })
        foreach (DirectionalShadowQuality quality in Enum.GetValues<DirectionalShadowQuality>())
        {
            var settings = new GraphicsDirectionalShadowSettings(quality);
            graphics.ConfigureDirectionalShadows(settings);
            using var renderer = new TerrainRenderer(graphics, terrain) { DebugChunksEnabled = false };
            using var objects = new SimpleInstanceRenderer(graphics);
            var camera = new RtsCamera(new RtsCameraSettings { InitialDistance = distance, MaximumDistance = 2000 });
            var frame = settings.Enabled ? DirectionalShadowProjection.Create(camera.CaptureState(), SceneLightingSettings.Default, settings) : default;
            Action<IGraphicsCommandContext> record = context =>
            {
                if (settings.Enabled)
                {
                    context.BeginDirectionalShadowPass(frame);
                    int draws = renderer.RenderShadow(context, frame, 128);
                    objects.RenderShadow(context, camera, world, 1, frame, settings.MaximumCasters - draws, settings.MaximumDraws - draws);
                    context.EndDirectionalShadowPass();
                }
                renderer.Render(context, camera);
                objects.Render(context, camera, world, 1);
                context.BeginPass(GraphicsFramePass.Overlay);
            };
            for (int i = 0; i < 64; i++) graphics.RenderFrame(GraphicsColor.ForgeLineClear, record);
            var cpu = new double[256];
            var gpu = new List<double>();
            var shadowGpu = new List<double>();
            ulong previousFence = 0;
            for (int i = 0; i < cpu.Length; i++)
            {
                long start = Stopwatch.GetTimestamp();
                graphics.RenderFrame(GraphicsColor.ForgeLineClear, record);
                cpu[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var diagnostics = graphics.Diagnostics;
                if (diagnostics.Frame!.Value.GpuSubmissionFence > previousFence)
                {
                    previousFence = diagnostics.Frame.Value.GpuSubmissionFence;
                    if (diagnostics.GpuFrameMilliseconds is double timing) gpu.Add(timing);
                    if (settings.Enabled && diagnostics.DirectionalShadows!.Value.GpuMilliseconds is double shadow) shadowGpu.Add(shadow);
                }
            }
            graphics.WaitForIdle();
            Array.Sort(cpu);
            results.Add(new { distance, quality, cpuMeanMs = cpu.Average(), cpuP99Ms = cpu[(int)(cpu.Length * 0.99)],
                gpuMeanMs = gpu.Count > 0 ? (double?)gpu.Average() : null,
                shadowGpuMeanMs = shadowGpu.Count > 0 ? (double?)shadowGpu.Average() : null,
                cpuSamples = cpu.Length, gpuSamples = gpu.Count, shadows = graphics.Diagnostics.DirectionalShadows,
                terrain = renderer.LastDiagnostics, objects = objects.LastDiagnostics });
        }
        string output = Path.GetFullPath(Environment.GetEnvironmentVariable("FORGELINE_SHADOW_BENCHMARK_OUTPUT")!);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { graphics.Diagnostics.Device, graphics.Diagnostics.Surface,
            driver = Environment.GetEnvironmentVariable("FORGELINE_BENCHMARK_DRIVER"),
            method = "Sequential disabled/low/medium/high pairs, fixed nine terrain chunks and 1000 opaque instances, 64 warmups, 256 samples, VSync off; CPU includes reuse/Present; GPU samples distinct completed fences, excludes Present. No simulation or UI.", results }, ReportOptions));
    }
}
