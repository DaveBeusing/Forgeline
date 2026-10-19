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
                var experience = default(PlayerExperienceSnapshot) with
                {
                    Tick = tick,
                    Player = new(1),
                    Selection = PlayerSelectionSummary.Empty with { Count = count, PrimaryEntity=members[0].Entity, Kind = PlayerSelectionKind.Unit }
                };
                var instances=members.Select(member=>new RenderInstance(member.Entity,RenderTransform.Identity,default,default,
                    RenderVisibilityMask.World,Selectable:new(new PlayerId(1),ControllableEntityCategory.Unit))).ToArray();
                var snapshot = new PresentationSnapshot(tick, TimeSpan.Zero, count, instances, sessionId: session,
                    playerExperience: experience, combatGroups: operational,
                    playerActions: new PlayerActionSnapshot(session, tick, [], 0, null, null,
                        tactical: new PlayerTacticalActionReadModel(members.Select(static member => member.Entity).ToArray(),
                            count, group.CombatCount, 0, 0, 0, false, false, default, default, [], [])));
                using var device = new PresentationBenchmarks.NullGraphicsDevice();
                using var renderer = new GameplayHudRenderer(device);
                var graphics = new PresentationBenchmarks.NullGraphicsCommandContext();
                var camera = new RtsCamera();
                var actionPanel = new PlayerActionPanelController();
                var input = new InputState();
                var layout = GameplayHudLayout.Create(1600, 900, 96);
                var bounds = new AxisAlignedBounds(System.Numerics.Vector3.Zero, new(2048, 100, 2048));
                // Status recording consumes the retained aggregate; rebuilding it is measured separately.
                var geometry=new RadialActionLayout(new(800,450),new(800,450),80,176,8);
                var buffer=new PresentationSnapshotBuffer();buffer.Publish(snapshot);var world=new RenderWorld();world.Update(buffer);
                var context=count>1 ? GroupActionRingContext.Capture(snapshot,world,selection) : null;
                var statusPanel=default(PlayerActionPanelView) with {Ring=new(members[0].Entity,session,selection.Revision,1,count==1,true,geometry,true) {Group=context}};
                var hover=new HoverTooltipView(session,default,default,geometry.Center,true,1600,900,layout.Scale,
                    SelectionStatus:true,SelectionRevision:selection.Revision,MenuRevision:1);
                void Frame()
                {
                    actionPanel.Update(input, snapshot, 1600, 900, currentSelection: selection);
                    var panel = actionPanel.CreateView(1600, 900, snapshot.PlayerActions) with {Ring=statusPanel.Ring};
                    renderer.Render(graphics, camera, snapshot, bounds, RtsInformationLayerView.Empty,
                        panel, default, FormationTemplate.Compact, view, default, 96, 1,hoverTooltip:hover);
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
            Warmup,
            Samples,
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
