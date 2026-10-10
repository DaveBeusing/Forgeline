using System.Diagnostics;
using System.Text.Json;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

[Collection("Native scene output")]
public sealed class AtmospherePerformanceQualificationTests
{
    private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented = true };
    public static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_ATMOSPHERE_BENCHMARK_OUTPUT"));
    [Fact(Skip = "Set FORGELINE_ATMOSPHERE_BENCHMARK_OUTPUT for isolated hardware measurement.", SkipUnless = nameof(Enabled))]
    public void CompareEquivalentTerrainAcrossAtmosphericModesAndZooms()
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Atmosphere Qualification",1280,720));
        using var graphics = (D3D12GraphicsDevice)GraphicsDeviceFactory.CreateForWindow(window,
            new() { EnableDebugLayer=false, EnableVSync=false, AllowSoftwareAdapterFallback=false });
        Assert.False(graphics.Diagnostics.Device.IsSoftwareAdapter);
        TerrainWorld terrain = DevelopmentTerrainFactory.CreateRepresentativeWorld(chunkRadius:1);
        SceneLightingSettings lighting = SceneLightingSettings.Default;
        var results = new List<object>();
        foreach (float distance in new[] { 25f,90f,120f,240f,420f,1000f })
        foreach (string mode in new[] { "Direct","Linear","Distance","Height" })
        {
            graphics.ConfigureAtmosphere(GraphicsAtmosphereSettings.Default);
            graphics.ConfigureSceneOutput(new(mode!="Direct",lighting.Exposure,true));
            AtmosphereQuality quality = mode switch { "Distance" => AtmosphereQuality.Distance, "Height" => AtmosphereQuality.Height, _ => AtmosphereQuality.Disabled };
            graphics.ConfigureAtmosphere(GraphicsAtmosphereSettings.Default with { Quality=quality });
            using var renderer = new TerrainRenderer(graphics,terrain,lighting:lighting) { DebugChunksEnabled=false };
            var camera = new RtsCamera(new() { InitialDistance=distance, MaximumDistance=2000 });
            Assert.Equal(distance,camera.Distance);
            Action<IGraphicsCommandContext> record = context =>
            {
                if (quality!=AtmosphereQuality.Disabled)
                    context.SetAtmosphereView(SceneAtmosphereSettings.CreateView(camera,context.Width,context.Height));
                renderer.Render(context,camera);
                context.BeginPass(GraphicsFramePass.Overlay);
            };
            for (int warmup=0;warmup<64;warmup++) graphics.RenderFrame(GraphicsColor.ForgeLineClear,record);
            var cpu = new double[512]; var gpu = new List<double>(); var composition = new List<double>();
            ulong lastFence=0;
            for (int sample=0;sample<cpu.Length;sample++)
            {
                long start=Stopwatch.GetTimestamp();
                graphics.RenderFrame(GraphicsColor.ForgeLineClear,record);
                cpu[sample]=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var diagnostics=graphics.Diagnostics;
                if (diagnostics.Frame is { } frame && frame.GpuSubmissionFence>lastFence)
                {
                    lastFence=frame.GpuSubmissionFence;
                    if (diagnostics.GpuFrameMilliseconds is { } total) gpu.Add(total);
                    if (frame.CompositeGpuMilliseconds is { } timing) composition.Add(timing);
                }
            }
            graphics.WaitForIdle(); Array.Sort(cpu);
            results.Add(new { mode,distance,cpuSamples=cpu.Length,gpuSamples=gpu.Count,
                cpuMeanMs=cpu.Average(),cpuP99Ms=cpu[(int)(cpu.Length*0.99)],
                gpuMeanMs=gpu.Count>0 ? (double?)gpu.Average():null,
                compositeGpuMeanMs=composition.Count>0 ? (double?)composition.Average():null,
                renderer.LastDiagnostics.VisibleChunks,renderer.LastDiagnostics.DrawCalls,
                targets=graphics.Diagnostics.Frame!.Value.Plan,atmosphere=graphics.Diagnostics.Atmosphere });
        }
        string output=Path.GetFullPath(Environment.GetEnvironmentVariable("FORGELINE_ATMOSPHERE_BENCHMARK_OUTPUT")!);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output,JsonSerializer.Serialize(new { graphics.Diagnostics.Device,graphics.Diagnostics.Surface,
            driver=Environment.GetEnvironmentVariable("FORGELINE_BENCHMARK_DRIVER"),
            method="Sequential Direct/Linear/Distance/Height pairs; identical nine chunks, camera, exposure/ACES and 1280x720; 64 warmups and 512 samples; VSync off; CPU includes frame reuse/Present; distinct completed fences only. No portable performance threshold.",
            workload="Production terrain renderer, no simulation, objects or gameplay HUD. 25/90/240m match the pre-change scene baseline; 120/420/1000m extend the RTS range.",results },ReportOptions));
    }
}
