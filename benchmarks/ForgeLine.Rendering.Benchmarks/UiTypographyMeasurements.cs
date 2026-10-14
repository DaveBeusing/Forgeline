using System.Diagnostics;
using System.Text.Json;
using ForgeLine.Presentation;

namespace ForgeLine.Rendering.Benchmarks;

internal static class UiTypographyMeasurements
{
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };
    public static void Run(string output)
    {
        var results = new List<object>();
        foreach (bool atlas in new[] { false, true })
            foreach (var display in new[] { (1280, 720), (1920, 1080), (2560, 1440), (3840, 2160), (3440, 1440) })
                foreach (float scale in new[] { .75f, 1f, 2f })
                {
                    using var device = new PresentationBenchmarks.NullGraphicsDevice { SupportsUiText = atlas };
                    using var renderer = new FrontendOverlayRenderer(device);
                    var context = new PresentationBenchmarks.NullGraphicsCommandContext { Width = display.Item1, Height = display.Item2 };
                    var view = FrontendSurfaceView.MainMenu([new("continue", "Continue", false, false), new("new-game", "New Game", true, true), new("settings", "Settings", true, false)]);
                    void Frame() => renderer.Render(context, view, scale);
                    for (int i = 0; i < 128; i++) Frame();
                    var samples = new double[256]; long uploads = device.UploadedBytes, calls = device.UploadCalls, before = GC.GetAllocatedBytesForCurrentThread();
                    for (int i = 0; i < samples.Length; i++) { long start = Stopwatch.GetTimestamp(); Frame(); samples[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds; }
                    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                    if (atlas && allocated != 0) throw new InvalidOperationException($"Frontend allocated {allocated} warm bytes.");
                    Array.Sort(samples);
                    results.Add(new
                    {
                        Atlas = atlas,
                        Width = display.Item1,
                        Height = display.Item2,
                        UiScale = scale,
                        AllocatedBytes = allocated,
                        Vertices = renderer.LastRenderedVertexCount,
                        RequestedUploadBytes = (device.UploadedBytes - uploads) / 256,
                        UploadCalls = (device.UploadCalls - calls) / 256,
                        P50Microseconds = samples[128],
                        P95Microseconds = samples[243]
                    });
                }
        File.WriteAllText(output, JsonSerializer.Serialize(new { Contract = "Controlled bitmap debug backend versus atlas; same build, palette and reference layout. CPU and upload requests only; excludes GPU execution.", Cases = results }, s_json));
        Console.WriteLine($"Frontend typography: {results.Count} cases; atlas allocation guard passed.");
    }
}
