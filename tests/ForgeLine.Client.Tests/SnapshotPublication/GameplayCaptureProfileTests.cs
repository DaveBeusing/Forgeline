using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Client.Tests.SnapshotPublication;

public sealed class GameplayCaptureProfileTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    public void CompletedGameplayTicksKeepAuthorizedSnapshotsAcrossRenderRatiosAndPausedReads(int renderRatio)
    {
        string? output = Environment.GetEnvironmentVariable("FORGELINE_SNAPSHOT_PROFILE_OUTPUT");
        int warmup = string.IsNullOrEmpty(output) ? 8 : 32;
        int samples = string.IsNullOrEmpty(output) ? 24 : 128;
        var settings = CentralDivideScenario.CreateSettings(MatchScenarioProfile.Gameplay);
        var runtime = CentralDivideScenario.CreateHeadless(settings.Profile, seed: 9147,
            enableDiagnostics: false, enableDebugCapture: false) with
        {
            Scenario = settings,
            Participants = CentralDivideScenario.CreateDefaultParticipants(
                westComputerControlled: false, eastComputerControlled: true)
        };
        using var scenario = CentralDivideScenario.Create(runtime, TestContext.Current.CancellationToken);
        var buffer = new PresentationSnapshotBuffer();
        var interaction = new PresentationInteractionState();
        var commands = new PlayerCommandGateway(scenario.Simulation, scenario.Services.BuildingCommands,
            scenario.BattlefieldRuntime.MatchStateEntity, 32, scenario.Intelligence,
            scenario.Services.Weapons, scenario.Services.ArtilleryWeapons);
        string? assetRoot = Environment.GetEnvironmentVariable("FORGELINE_RUNTIME_ASSETS");
        var assets = string.IsNullOrEmpty(output) ? null : RuntimeAssetCatalog.LoadCancellable(
            assetRoot ?? throw new InvalidOperationException("Profile requires canonical runtime assets."),
            TestContext.Current.CancellationToken);
        var extractor = new PresentationExtractor(buffer,
            new PresentationExtractionContext(scenario, new PlayerId(1), interaction, commands), assets);
        var probe = new CaptureProbe(extractor);
        scenario.Simulation.RegisterTickObserver(commands);
        scenario.Simulation.RegisterTickObserver(probe);
        var world = new RenderWorld();
        var observations = new List<Observation>(samples);
        PresentationSnapshot? retained = null, previous = null;
        RenderInstance retainedInstance = default;
        int reusedPayloads = 0;
        double checksum = 0;
        int[] collections = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
        for (int i = 0; i < warmup + samples; i++)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            scenario.Simulation.AdvanceOneTick();
            double tickMicros = Stopwatch.GetElapsedTime(started).TotalMicroseconds;
            long tickAllocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Assert.True(buffer.TryReadLatest(out var snapshot));
            Assert.Equal(scenario.Simulation.CurrentTick, snapshot.Tick);
            Assert.Equal(snapshot.Tick, snapshot.CombatGroups!.Tick);
            Assert.Equal(snapshot.Tick, snapshot.PlayerActions!.Tick);
            Assert.Equal(snapshot.Tick, snapshot.PlayerExperience!.Value.Tick);
            Assert.Equal(snapshot.Tick, snapshot.Guidance!.Value.Tick);
            if (previous is not null && ReferenceEquals(previous.CombatGroups!.Members, snapshot.CombatGroups.Members)) reusedPayloads++;
            if (retained is null && snapshot.InstanceCount > 0)
            { retained = snapshot; retainedInstance = snapshot.Instances[0]; }
            allocated = GC.GetAllocatedBytesForCurrentThread();
            started = Stopwatch.GetTimestamp();
            for (int frame = 0; frame < renderRatio; frame++)
            {
                world.Update(buffer);
                for (int instance = 0; instance < world.InstanceCount; instance++)
                    checksum += world.GetInterpolatedInstance(instance, (frame + 1f) / renderRatio).Transform.Position.X;
            }
            double consumerMicros = Stopwatch.GetElapsedTime(started).TotalMicroseconds;
            long consumerAllocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            if (i >= warmup) observations.Add(new(snapshot.Tick.Value, snapshot.InstanceCount,
                snapshot.CombatGroups.Members.Count, tickMicros, tickAllocated, probe.Microseconds,
                probe.AllocatedBytes, consumerMicros, consumerAllocated, world.IndexRetainedBytes));
            previous = snapshot;
        }
        Assert.NotNull(retained);
        Assert.Equal(retainedInstance, retained.Instances[0]);
        var paused = world.CurrentSnapshot;
        for (int i = 0; i < 32; i++) Assert.False(world.Update(buffer));
        Assert.Same(paused, world.CurrentSnapshot);
        Assert.Equal(retainedInstance, retained.Instances[0]);
        Assert.All(observations, sample => Assert.True(sample.CaptureAllocated > 0));
        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, $"gameplay-ratio-{renderRatio}.json"), JsonSerializer.Serialize(new
            {
                Source = typeof(PresentationExtractor).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
                Runtime = RuntimeInformation.FrameworkDescription, Seed = 9147, Warmup = warmup, Samples = samples,
                RuntimeAssetsProvided = assets is not null, RenderRatio = renderRatio, ReusedCombatPayloads = reusedPayloads, Checksum = checksum,
                Method = "Actual Central Divide fixed ticks and PresentationExtractor; CPU RenderWorld consumer. Ratios are injected, not wall-clock FPS. Tick includes capture; consumer includes index/interpolation, no GPU/Present. Thread allocations exclude observation storage; GC counts are process-wide.",
                ProcessCollectionDeltas = new[] { GC.CollectionCount(0)-collections[0], GC.CollectionCount(1)-collections[1], GC.CollectionCount(2)-collections[2] },
                Observations = observations
            }, JsonOptions));
        }
        GC.KeepAlive(checksum);
    }

    private sealed class CaptureProbe(PresentationExtractor extractor) : ISimulationTickObserver
    {
        public double Microseconds { get; private set; }
        public long AllocatedBytes { get; private set; }
        public void OnTickCompleted(SimulationContext context)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            extractor.OnTickCompleted(context);
            Microseconds = Stopwatch.GetElapsedTime(started).TotalMicroseconds;
            AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }

    private readonly record struct Observation(ulong Tick, int Instances, int Members,
        double TickMicroseconds, long TickAllocated, double CaptureMicroseconds, long CaptureAllocated,
        double ConsumerMicroseconds, long ConsumerAllocated, long IndexRetainedBytes);
}
