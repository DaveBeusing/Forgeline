using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Rendering.Benchmarks;

internal static class AlertMapMeasurements
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static void Run(string output)
    {
        const int warmup = 128, samples = 256;
        var results = new List<object>();
        foreach (int count in new[] { 1, 10, 100, 1000 })
            foreach (var mode in Enum.GetValues<StrategicOverlayMode>())
            {
                var instances = Enumerable.Range(1, count).Select(i => new RenderInstance(new((uint)i, 1),
                    new(new Vector3(i % 32 * 30, 0, i / 32 * 30), Quaternion.Identity, Vector3.One), new(1),
                    RenderMaterialHandle.Default, RenderVisibilityMask.World, Selectable: new(new(1), ControllableEntityCategory.Unit))).ToArray();
                var experience = default(PlayerExperienceSnapshot) with
                {
                    Player = new(1),
                    Tick = new(4),
                    MatchStatus = PlayerMatchStatus.Active,
                    Alerts = (PlayerAlertState)31,
                    CriticalSupplyUnits = count,
                    BlockedProductionFacilities = count
                };
                var alerts = new AlertLifecycleTracker().Capture(new(1), experience, []);
                var sensors = Enumerable.Range(1, count).Select(i => new StrategicSensorReadModel(new((uint)i, 1), Vector3.Zero, 10, 20, 5)).ToArray();
                var overlay = new StrategicOverlaySnapshot(new(4), mode, [], [], [], sensors, [], [], [], [], new(1), new(1));
                var snapshot = new PresentationSnapshot(new(4), TimeSpan.FromSeconds(.05), count, instances, sessionId: new(1),
                    playerExperience: experience, strategicOverlay: overlay, alerts: alerts);
                using var device = new PresentationBenchmarks.NullGraphicsDevice(); using var renderer = new GameplayHudRenderer(device);
                var graphics = new PresentationBenchmarks.NullGraphicsCommandContext(); var camera = new RtsCamera();
                var input = new InputState(); var controller = new ActionableAlertController(); var layout = GameplayHudLayout.Create(1600, 900, 96);
                var view = RtsInformationLayerView.Empty with { OverlayMode = mode };
                var bounds = new AxisAlignedBounds(Vector3.Zero, new(2048, 100, 2048));
                void Frame()
                {
                    controller.Update(input, snapshot, layout);
                    renderer.Render(graphics, camera, snapshot, bounds, view, default, default, default, CombatGroupOverviewView.Empty, default, 96, 1);
                }
                for (int i = 0; i < warmup; i++) Frame();
                var times = new double[samples]; long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < samples; i++) { long started = Stopwatch.GetTimestamp(); Frame(); times[i] = Stopwatch.GetElapsedTime(started).TotalMicroseconds; }
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                if (bytes != 0) throw new InvalidOperationException($"Alert/map HUD allocated {bytes} warm bytes for {count} entities/{mode}.");
                Array.Sort(times);
                results.Add(new
                {
                    Entities = count,
                    Layer = mode.ToString(),
                    BytesPerSample = bytes / (double)samples,
                    P50Microseconds = times[samples / 2],
                    P95Microseconds = times[(int)(samples * .95)],
                    P99Microseconds = times[(int)(samples * .99)]
                });
            }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            BuildVersion = typeof(ActionableAlertSnapshot).Assembly.GetName().Version?.ToString(),
            Warmup = warmup,
            Samples = samples,
            Backend = "Full HUD/minimap with active alerts and idle alert input; CPU null graphics; excludes extraction, world overlay drawing, GPU, waits and Present",
            Results = results
        }, JsonOptions));
        Console.WriteLine($"Alert/map measurements: {results.Count} cases; zero warm HUD/input allocations.");
    }
}
