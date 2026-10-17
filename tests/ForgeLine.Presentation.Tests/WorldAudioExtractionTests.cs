using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using Xunit;
using Cue = ForgeLine.Presentation.WorldAudioCue;

namespace ForgeLine.Presentation.Tests;

public sealed class WorldAudioExtractionTests
{
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };

    [Fact]
    public void RealCompletedTickPublicationKeepsAuthoritativeStateIdenticalAndOwnsAudioMemory()
    {
        using var reference = WorldHoverExtractionTests.CreateScenario();
        using var scenario = WorldHoverExtractionTests.CreateScenario();
        var buffer = WorldHoverExtractionTests.Observe(scenario, new());
        reference.Simulation.RunTicks(16, TestContext.Current.CancellationToken);
        scenario.Simulation.RunTicks(16, TestContext.Current.CancellationToken);
        string expected = MatchAuthoritativeSnapshot.Capture(reference).ComputeSha256();
        string actual = MatchAuthoritativeSnapshot.Capture(scenario).ComputeSha256();
        Assert.Equal(expected, actual);
        Assert.True(buffer.TryReadLatest(out var first));
        var audio = Assert.IsType<WorldAudioSnapshot>(first.WorldAudio);
        Assert.Equal(first.SessionId, audio.Session); Assert.Equal(first.Tick, audio.Tick); Assert.Equal(new PlayerId(1), audio.Player);
        Assert.Contains(audio.Loops.ToArray(), x => x.Cue == Cue.AmbienceWindLoop);
        Assert.All(audio.Loops.ToArray().Where(x => x.Entity.IsValid), x =>
            Assert.Equal(new PlayerId(1), scenario.Simulation.Entities.GetComponent<ControllableEntity>(x.Entity).Owner));
        var saved = audio.Loops.ToArray();
        scenario.Simulation.RunTicks(4, TestContext.Current.CancellationToken);
        Assert.Equal(saved, audio.Loops.ToArray()); Assert.Equal(16UL, audio.Tick.Value);
        string? path = Environment.GetEnvironmentVariable("FORGELINE_WORLD_AUDIO_DETERMINISM_REPORT");
        if (!string.IsNullOrWhiteSpace(path)) File.WriteAllText(path, JsonSerializer.Serialize(new
        { Seed = 4209, Ticks = 16, ReferenceSha256 = expected, ObservedSha256 = actual, Equal = true }, s_json));
    }

    [Fact]
    public void ActualExtractionSanitizesVisibleImpactsAndNeverExposesHiddenShotsOrForeignLoops()
    {
        using var scenario = WorldHoverExtractionTests.CreateScenario();
        var entities = scenario.Simulation.Entities;
        var enemy = scenario.GetBase(new PlayerId(2)).CommandCore;
        var localCore = scenario.GetBase(new PlayerId(1)).CommandCore;
        Vector3 visible = entities.GetComponent<WorldTransform>(localCore).Position;
        scenario.Simulation.RegisterTickObserver(new Observer(context =>
        {
            var events = Assert.IsAssignableFrom<IList<CombatEvent>>(scenario.Services.CombatRuntime.Events);
            Vector3 hidden = entities.GetComponent<WorldTransform>(enemy).Position;
            events.Add(new(context.Tick, CombatEventType.ShotFired, enemy, default, default, DirectorateContent.WeaponIds.Rifle, hidden, 1));
            events.Add(new(context.Tick, CombatEventType.Impact, enemy, enemy, default, DirectorateContent.WeaponIds.Rifle, visible, 1));
        }));
        var buffer = WorldHoverExtractionTests.Observe(scenario, new());
        scenario.Simulation.AdvanceOneTick(); // Establish the silent baseline.
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var snapshot));
        Assert.DoesNotContain(snapshot.Instances.ToArray(), x => x.Entity == enemy);
        var audio = Assert.IsType<WorldAudioSnapshot>(snapshot.WorldAudio);
        var impact = Assert.Single(audio.Events.ToArray(), x => x.CreatedAt == snapshot.Tick);
        Assert.Equal(Cue.CombatImpactMetal, impact.Cue);
        Assert.Equal(EntityId.Invalid, impact.Entity); Assert.Equal(visible, impact.Position);
        Assert.DoesNotContain(audio.Loops.ToArray(), x => x.Entity == enemy);
        Assert.DoesNotContain(audio.Events.ToArray(), x => x.Cue is >= Cue.CombatRifleFire01 and <= Cue.CombatArtilleryFire03);
    }

    [Fact]
    public void RealOwnedUnitMovementAndDestructionReplaceTheCompleteLoopSet()
    {
        using var scenario = WorldHoverExtractionTests.CreateScenario();
        var entities = scenario.Simulation.Entities;
        var entity = entities.CreateEntity();
        Vector3 origin = entities.GetComponent<WorldTransform>(scenario.GetBase(new PlayerId(1)).CommandCore).Position;
        entities.AddComponent(entity, new WorldTransform(origin, Quaternion.Identity, Vector3.One));
        entities.AddComponent(entity, new VisualIdentity(1));
        entities.AddComponent(entity, new ControllableEntity(new PlayerId(1), ControllableEntityCategory.Unit));
        entities.AddComponent(entity, new UnitIdentity(UnitIds.CargoTruck, new FactionId(1)));
        var buffer = WorldHoverExtractionTests.Observe(scenario, new());
        scenario.Simulation.AdvanceOneTick();
        entities.SetComponent(entity, new WorldTransform(origin + Vector3.One, Quaternion.Identity, Vector3.One));
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var moving));
        Assert.Contains(moving.WorldAudio!.Loops.ToArray(), x => x.Entity == entity && x.Cue == Cue.VehiclesTruckMoveLoop);
        Assert.Contains(moving.WorldAudio.Events.ToArray(), x => x.Entity == entity && x.Cue == Cue.VehiclesEngineStart);
        entities.DestroyEntity(entity);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var removed));
        Assert.DoesNotContain(removed.WorldAudio!.Loops.ToArray(), x => x.Entity == entity);
        Assert.Contains(moving.WorldAudio.Loops.ToArray(), x => x.Entity == entity);
    }

    [Fact]
    public void UnscopedDiagnosticExtractionCannotBecomeAnAudioAuthority()
    {
        var simulation = new SimulationCoordinator(); var buffer = new PresentationSnapshotBuffer();
        simulation.RegisterTickObserver(new PresentationExtractor(buffer));
        simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var snapshot)); Assert.Null(snapshot.WorldAudio);
    }

    [Theory]
    [InlineData(0)] [InlineData(100)] [InlineData(1000)]
    public void PublicationRemainsBoundedUnderSustainedSourcePressure(int count)
    {
        var bridge = new WorldAudioEventBridge(); var times = new double[64]; var allocations = new long[64];
        WorldAudioSnapshot? last = null;
        for (ulong tick = 1; tick <= 128; tick++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread(), started = Stopwatch.GetTimestamp();
            Assert.True(bridge.Begin(new(1), new(1), new(tick), TimeSpan.FromMilliseconds(50)));
            for (uint id = 1; id <= count; id++)
                bridge.ObserveEntity(new(new(id, 1), new(id, 0, 0), new(UnitIds.CargoTruck, UnitPresentationDamageState.Intact),
                    default, true, Moving: tick % 2 == 0));
            for (int shot = 0; shot < 16; shot++) bridge.ObserveCombat(VfxEffectKind.MuzzleTankCannon, default, Vector3.Zero, new(tick));
            last = bridge.Complete(default(PlayerExperienceSnapshot) with { Player = new(1), Tick = new(tick), MatchStatus = PlayerMatchStatus.Active });
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            double milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (tick > 64) { times[tick - 65] = milliseconds; allocations[tick - 65] = bytes; }
            Assert.NotNull(last);
            Assert.InRange(last.Loops.Length, 0, WorldAudioEventBridge.MaximumLoops);
            Assert.InRange(last.Events.Length, 0, WorldAudioEventBridge.MaximumEvents);
        }
        Assert.All(allocations, value => Assert.InRange(value, 0, 65536));
        string? prefix = Environment.GetEnvironmentVariable("FORGELINE_WORLD_AUDIO_MEASUREMENT_PREFIX");
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            Array.Sort(times);
            File.WriteAllText(prefix + count + ".json", JsonSerializer.Serialize(new
            { Sources = count, Samples = 64, WarmupTicks = 64, MedianMilliseconds = times[32], P95Milliseconds = times[60],
                MaximumManagedBytesPerPublication = allocations.Max(), Loops = last!.Loops.Length,
                Events = last.Events.Length, Stage = "Extraction and immutable publication; no playback or acoustic latency" }, s_json));
        }
    }

    private sealed class Observer(Action<SimulationContext> action) : ISimulationTickObserver
    { public void OnTickCompleted(SimulationContext context) => action(context); }
}
