using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Presentation;
using ForgeLine.World;

namespace ForgeLine.Rendering.Benchmarks;

internal static class GameplayUiMeasurements
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static void Run(string output)
    {
        const int warmup = 128, samples = 256;
        var results = new List<object>();
        var bindings = new GameplayBindingRegistry(new GameplayBindings().With(GameplayAction.Build, PlatformKey.G)
            .With(GameplayAction.NextItem, PlatformKey.I).With(GameplayAction.Activate, PlatformKey.J));
        foreach (var display in new[] { (1024, 720, 96u), (1920, 1080, 144u), (1920, 1200, 192u), (3440, 1440, 144u), (3840, 2160, 192u) })
            foreach (float scale in new[] { .75f, 1f, 2f })
                foreach (int count in new[] { 1, 1000 })
                    foreach (string state in new[] { "Focus", "Hover", "Pressed", "Pending" })
                    {
                        var instances = Enumerable.Range(1, count).Select(i => new RenderInstance(new((uint)i, 1),
                            new(new Vector3(i % 32 * 30, 0, i / 32 * 30), Quaternion.Identity, Vector3.One), new(1),
                            RenderMaterialHandle.Default, RenderVisibilityMask.World, Selectable: new(new(1), ControllableEntityCategory.Unit))).ToArray();
                        var experience = default(PlayerExperienceSnapshot) with
                        {
                            Tick = new(4),
                            Player = new(1),
                            Alerts = (PlayerAlertState)31,
                            CriticalSupplyUnits = count,
                            BlockedProductionFacilities = count,
                            Selection = PlayerSelectionSummary.Empty with { Count = 1, Kind = PlayerSelectionKind.Building, CommonBuildingId = BuildingIds.CommandCore }
                        };
                        var actions = new PlayerActionSnapshot(new(1), new(4),
                            [new(BuildingIds.PowerPlant, "Power Plant", [], false), new(BuildingIds.VehicleFactory, "Vehicle Factory with a deliberately long qualification label", [], false)], state == "Pending" ? 1 : 0, null, null);
                        var snapshot = new PresentationSnapshot(new(4), TimeSpan.Zero, count, instances, sessionId: new(1), playerExperience: experience,
                            playerActions: actions, alerts: new AlertLifecycleTracker().Capture(new(1), experience, []));
                        using var device = new PresentationBenchmarks.NullGraphicsDevice();
                        using var renderer = new GameplayHudRenderer(device);
                        var graphics = new PresentationBenchmarks.NullGraphicsCommandContext { Width = display.Item1, Height = display.Item2 };
                        var camera = new RtsCamera(); var controller = new PlayerActionPanelController(bindings); var input = new InputState();
                        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.G));
                        controller.Update(input, snapshot, display.Item1, display.Item2, display.Item3, scale);
                        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, PlatformKey.G)); input.BeginFrame();
                        var panel = controller.CreateView(display.Item1, display.Item2, actions, display.Item3, scale) with
                        { ContextualHoveredIndex = state is "Hover" or "Pressed" ? 0 : -1, ContextualPressed = state == "Pressed" };
                        var ux = default(PreAlphaUxView) with { Bindings = bindings, ShowOnboarding = true };
                        var bounds = new AxisAlignedBounds(Vector3.Zero, new(2048, 100, 2048));
                        void Frame()
                        {
                            controller.Update(input, snapshot, display.Item1, display.Item2, display.Item3, scale);
                            renderer.Render(graphics, camera, snapshot, bounds, RtsInformationLayerView.Empty, panel, default,
                                default, CombatGroupOverviewView.Empty, ux, display.Item3, scale);
                        }
                        for (int i = 0; i < warmup; i++) Frame();
                        var times = new double[samples];
                        long uploaded = device.UploadedBytes, calls = device.UploadCalls, before = GC.GetAllocatedBytesForCurrentThread();
                        for (int i = 0; i < samples; i++) { long started = Stopwatch.GetTimestamp(); Frame(); times[i] = Stopwatch.GetElapsedTime(started).TotalMicroseconds; }
                        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                        if (bytes != 0) throw new InvalidOperationException($"Rebound gameplay HUD allocated {bytes} warm bytes for {display}/{scale}/{count}/{state}.");
                        Array.Sort(times);
                        results.Add(new
                        {
                            Width = display.Item1,
                            Height = display.Item2,
                            Dpi = display.Item3,
                            UiScale = scale,
                            Entities = count,
                            State = state,
                            AllocatedBytes = bytes,
                            HudVertices = renderer.LastRenderedVertexCount,
                            RequestedUploadBytesPerFrame = (device.UploadedBytes - uploaded) / samples,
                            UploadCallsPerFrame = (device.UploadCalls - calls) / samples,
                            P50Microseconds = times[samples / 2],
                            P95Microseconds = times[(int)(samples * .95)],
                            P99Microseconds = times[(int)(samples * .99)]
                        });
                    }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            BuildVersion = typeof(GameplayHudRenderer).Assembly.GetName().Version?.ToString(),
            Backend = "CPU full HUD rebuild/glyph geometry and recorded vertex upload requests; null graphics; excludes actual GPU copies/timing, world extraction, waits and Present",
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "Runtime default",
            Warmup = warmup,
            Samples = samples,
            Results = results
        }, JsonOptions));
        Console.WriteLine($"Rebound gameplay UI: {results.Count} cases; zero warm managed allocations; glyph/upload requests recorded.");
    }
}
