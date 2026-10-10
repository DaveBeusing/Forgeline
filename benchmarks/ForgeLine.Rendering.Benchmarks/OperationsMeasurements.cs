using System.Diagnostics;
using System.Text.Json;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Rendering.Benchmarks;

internal static class OperationsMeasurements
{
    private const int Warmup = 128;
    private const int Samples = 256;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static void Run(string output)
    {
        var results = new List<Measurement>();
        foreach (int count in new[] { 1, 10, 100, 1000 })
        {
            var facilities = Enumerable.Range(1, count).Select(i => new OperationsFacility(new((uint)i, 1), "VEHICLE FACTORY",
                i % 2 == 0 ? OperationsCategory.Production : OperationsCategory.Supply, "NoInput", "NoInput",
                "INPUT STOCK INSUFFICIENT - CHECK LOGISTICS", 2, 40, 1000, 10, 10, 100, .7, PlayerActionPanelMode.Production)).ToArray();
            var routes = Enumerable.Range(1, count).Select(i => new OperationsRoute(new((uint)i, 1), new((uint)(i + 1), 1), 100, i % 2 == 0, .7)).ToArray();
            var resources = Enumerable.Range(1, 7).Select(i => new OperationsResource(new((uint)i), "RESOURCE", i * 100, null)).ToArray();
            var data = new OperationsSnapshot(new(1), new(4), new(1), facilities, resources, routes, count, count);
            var experience = default(PlayerExperienceSnapshot) with { Player = new(1), Tick = new(4), MatchStatus = PlayerMatchStatus.Active };
            var snapshot = new PresentationSnapshot(new(4), TimeSpan.FromSeconds(.05), count, [], sessionId: new(1), playerExperience: experience, operations: data);
            using var device = new PresentationBenchmarks.NullGraphicsDevice();
            using var renderer = new GameplayHudRenderer(device);
            var graphics = new PresentationBenchmarks.NullGraphicsCommandContext();
            var camera = new RtsCamera(); var input = new InputState(); var controller = new OperationsController();
            var interaction = new PresentationInteractionState(); var layout = GameplayHudLayout.Create(1600, 900, 96);
            var bounds = new AxisAlignedBounds(System.Numerics.Vector3.Zero, new(2048, 100, 2048));
            var view = new OperationsView(true, Selected: new(1, 1), Session: new(1));
            void Frame()
            {
                _ = controller.Update(input, snapshot, layout, interaction);
                renderer.Render(graphics, camera, snapshot, bounds, default, default, default, default,
                    CombatGroupOverviewView.Empty, default, 96, 1, operations: view);
            }
            results.Add(Measure("retained-full-hud-and-idle-input", count, Frame, 0));
            results.Add(Measure("bounded-snapshot-copy", count, () => _ = new OperationsSnapshot(new(1), new(4), new(1),
                facilities, resources, routes, count, count), 128 * 1024));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            BuildVersion = typeof(OperationsSnapshot).Assembly.GetName().Version?.ToString(),
            Warmup,
            Samples,
            Backend = "CPU null graphics; copied bounded operations data, full HUD and idle input; excludes ECS extraction, GPU, waits and Present",
            TimingPolicy = "Observational timings. Zero warm HUD/input allocation; snapshot copies bounded by retained rows/routes.",
            Results = results
        }, JsonOptions));
        Console.WriteLine($"Operations measurements: {results.Count} cases; allocation contracts passed; {Path.GetFullPath(output)}");
    }
    private static Measurement Measure(string domain, int count, Action frame, long limit)
    {
        for (int i = 0; i < Warmup; i++) frame();
        var times = new double[Samples]; long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Samples; i++)
        { long started = Stopwatch.GetTimestamp(); frame(); times[i] = Stopwatch.GetElapsedTime(started).TotalMicroseconds; }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (bytes > limit * Samples) throw new InvalidOperationException($"{domain} exceeded its allocation bound for {count} facilities: {bytes} bytes.");
        Array.Sort(times);
        return new(domain, count, bytes / (double)Samples, times[Samples / 2], times[(int)(Samples * .95)], times[(int)(Samples * .99)]);
    }
    private sealed record Measurement(string Domain, int Facilities, double BytesPerSample, double P50Microseconds, double P95Microseconds, double P99Microseconds);
}
