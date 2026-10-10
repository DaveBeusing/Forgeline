using System.Diagnostics;
using System.Text.Json;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

[Collection("Native scene output")]
public sealed class ScenePassPerformanceQualificationTests
{
    private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented = true };
    public static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_PASS_BENCHMARK_OUTPUT"));

    [Fact(Skip = "Set FORGELINE_PASS_BENCHMARK_OUTPUT for isolated hardware measurement.", SkipUnless = nameof(Enabled))]
    public void CompareDirectAndLinearSceneOnEquivalentNativeWorkloads()
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Scene Pass Qualification", 1280, 720));
        using var graphics = (D3D12GraphicsDevice)GraphicsDeviceFactory.CreateForWindow(window, new GraphicsConfiguration
        {
            EnableDebugLayer = false, EnableVSync = false, AllowSoftwareAdapterFallback = false
        });
        Assert.False(graphics.Diagnostics.Device.IsSoftwareAdapter);
        TerrainWorld terrain = DevelopmentTerrainFactory.CreateRepresentativeWorld(chunkRadius: 1);
        var results = new List<object>();
        foreach (float distance in new[] { 25.0f, 90.0f, 240.0f })
        foreach (bool enabled in new[] { false, true })
        {
            SceneLightingSettings lighting = SceneLightingSettings.Default;
            graphics.ConfigureSceneOutput(new(enabled, lighting.Exposure, true));
            using var renderer = new TerrainRenderer(graphics, terrain, lighting: lighting) { DebugChunksEnabled = false };
            var camera = new RtsCamera(new RtsCameraSettings { InitialDistance = distance });
            Action<IGraphicsCommandContext> record = context =>
            {
                renderer.Render(context, camera);
                context.BeginPass(GraphicsFramePass.Overlay);
            };
            for (int warmup = 0; warmup < 64; warmup++)
                graphics.RenderFrame(GraphicsColor.ForgeLineClear, record);
            var cpu = new double[512];
            var gpu = new List<double>();
            var compositeGpu = new List<double>();
            ulong previousFence = 0;
            for (int sample = 0; sample < cpu.Length; sample++)
            {
                long started = Stopwatch.GetTimestamp();
                graphics.RenderFrame(GraphicsColor.ForgeLineClear, record);
                cpu[sample] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                GraphicsDiagnostics diagnostics = graphics.Diagnostics;
                if (diagnostics.Frame is { } frame && frame.GpuSubmissionFence > previousFence)
                {
                    previousFence = frame.GpuSubmissionFence;
                    if (diagnostics.GpuFrameMilliseconds is { } timing) gpu.Add(timing);
                    if (frame.CompositeGpuMilliseconds is { } composition) compositeGpu.Add(composition);
                }
            }
            graphics.WaitForIdle();
            Array.Sort(cpu);
            results.Add(new
            {
                linearScene = enabled, distance, cpuSamples = cpu.Length, gpuSamples = gpu.Count,
                cpuMeanMs = cpu.Average(), cpuP50Ms = cpu[cpu.Length / 2], cpuP99Ms = cpu[(int)(cpu.Length * 0.99)],
                gpuMeanMs = gpu.Count > 0 ? (double?)gpu.Average() : null,
                compositeGpuMeanMs = compositeGpu.Count > 0 ? (double?)compositeGpu.Average() : null,
                renderer.LastDiagnostics.VisibleChunks, renderer.LastDiagnostics.DrawCalls,
                targets = graphics.Diagnostics.Frame!.Value.Plan
            });
        }
        string output = Path.GetFullPath(Environment.GetEnvironmentVariable("FORGELINE_PASS_BENCHMARK_OUTPUT")!);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            graphics.Diagnostics.Device, graphics.Diagnostics.Surface,
            driver = Environment.GetEnvironmentVariable("FORGELINE_BENCHMARK_DRIVER"),
            method = "Sequential direct/linear pairs; fixed terrain, 64 warmup and 512 samples; VSync off; CPU includes frame reuse waits; GPU excludes Present; latest completed fence sampled once. No portable performance threshold.",
            workload = "Nine representative terrain chunks, fixed camera, no simulation, objects or gameplay UI.",
            results
        }, ReportOptions));
    }
}
