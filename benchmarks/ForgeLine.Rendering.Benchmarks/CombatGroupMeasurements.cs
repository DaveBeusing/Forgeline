using System.Diagnostics;
using System.Text.Json;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Rendering.Benchmarks;

internal static class CombatGroupMeasurements
{
    private const int Warmup = 128;
    private const int Samples = 256;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Run(string output)
    {
        var results = new List<Measurement>();
        foreach (int count in new[] { 1, 10, 100, 1000 })
            foreach (bool mixed in new[] { false, true })
            {
                var members = Enumerable.Range(1, count).Select(i => new CombatGroupMemberReadModel(
                    new EntityId((uint)i, 1), ControllableEntityCategory.Unit, true, 0.2, true,
                    BattlefieldSupplyStatus.Critical, 0.3, 0.4, true, 0.5, 0.8, false, default, false, default,
                    false, default, mixed && i % 2 == 0 ? UnitIds.SupplyTruck : UnitIds.MainBattleTank, !mixed || i % 2 != 0)).ToArray();
                var selection = new SelectionSet();
                selection.Replace(members.Select(static member => member.Entity).ToArray());
                var tick = new SimulationTick(4);
                var session = new SimulationSessionId(1);
                var operational = new CombatGroupOperationalSnapshot(tick, members, session);
                var group = SelectedCombatGroup.Create(operational, selection);
                var view = new CombatGroupOverviewView([], [], group);
                var experience = default(PlayerExperienceSnapshot) with { Tick = tick, Player = new(1),
                    Selection = PlayerSelectionSummary.Empty with { Count = count, Kind = PlayerSelectionKind.Unit } };
                var snapshot = new PresentationSnapshot(tick, TimeSpan.Zero, count, [], sessionId: session,
                    playerExperience: experience, combatGroups: operational);
                using var device = new PresentationBenchmarks.NullGraphicsDevice();
                using var renderer = new GameplayHudRenderer(device);
                var graphics = new PresentationBenchmarks.NullGraphicsCommandContext();
                var camera = new RtsCamera();
                var controller = new CombatGroupCardController();
                var input = new InputState();
                var layout = GameplayHudLayout.Create(1600, 900, 96);
                var bounds = new AxisAlignedBounds(System.Numerics.Vector3.Zero, new(2048, 100, 2048));
                void Frame()
                {
                    _ = controller.Update(input, snapshot, selection, layout);
                    renderer.Render(graphics, camera, snapshot, bounds, RtsInformationLayerView.Empty,
                        default, default, FormationTemplate.Compact, view, default, 96, 1);
                }
                results.Add(Measure("HUD and idle input", count, mixed, Frame, 0));
                results.Add(Measure("Selection summary rebuild", count, mixed,
                    () => _ = SelectedCombatGroup.Create(operational, selection), 512));
                results.Add(Measure("Owned snapshot copy and index", count, mixed,
                    () => _ = new CombatGroupOperationalSnapshot(tick, members, session), count * 512L + 8192));
            }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            BuildVersion = typeof(SelectedCombatGroup).Assembly.GetName().Version?.ToString(),
            Backend = "CPU null graphics; copied member indexing, summary rebuild, retained full HUD and idle input; excludes ECS extraction, GPU, waits and Present",
            Warmup, Samples,
            TimingPolicy = "Timing is observational. HUD/input has zero warm allocation; summaries have constant bounded storage; snapshot copies have linear bounded storage.",
            Results = results
        }, JsonOptions));
        Console.WriteLine($"Combat group measurements: {results.Count} cases; allocation contracts passed; {Path.GetFullPath(output)}");
    }

    private static Measurement Measure(string domain, int count, bool mixed, Action frame, long limitPerSample)
    {
        for (int i = 0; i < Warmup; i++) frame();
        var times = new double[Samples];
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Samples; i++)
        {
            long started = Stopwatch.GetTimestamp();
            frame();
            times[i] = Stopwatch.GetElapsedTime(started).TotalMicroseconds;
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (bytes > limitPerSample * Samples)
            throw new InvalidOperationException($"{domain} exceeded its allocation bound for {count} members: {bytes} bytes.");
        Array.Sort(times);
        return new(domain, count, mixed, bytes, bytes / (double)Samples,
            times[Samples / 2], times[(int)(Samples * .95)], times[(int)(Samples * .99)]);
    }

    private sealed record Measurement(string Domain, int Members, bool Mixed, long AllocatedBytes,
        double BytesPerSample, double P50Microseconds, double P95Microseconds, double P99Microseconds);
}
