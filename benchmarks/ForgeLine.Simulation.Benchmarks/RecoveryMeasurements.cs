using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ForgeLine.Game;

namespace ForgeLine.Simulation.Benchmarks;

internal static class RecoveryMeasurements
{
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };
    // One normal-stock match, sampled at fixed ticks. Timings are observations,
    // never a pass/fail threshold or a replacement for deterministic checks.
    internal static void Run(string output)
    {
        using var runtime = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(
            MatchScenarioProfile.Gameplay, seed: 2026, enableDiagnostics: false, enableDebugCapture: false));
        var samples = new List<object>();
        foreach (ulong tick in new ulong[] { 6_000, 12_000, 24_000 })
        {
            while (runtime.Simulation.CurrentTick.Value < tick) runtime.Simulation.AdvanceOneTick();
            var save = MatchPersistenceService.CaptureSave(runtime);
            string document = MatchPersistenceSerializer.SerializeSave(save);
            long start = Stopwatch.GetTimestamp();
            var parsed = MatchPersistenceSerializer.DeserializeSave(document);
            double readMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            long allocated = GC.GetTotalAllocatedBytes(precise: true);
            start = Stopwatch.GetTimestamp();
            using var restored = MatchPersistenceService.Restore(parsed);
            double restoreMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            allocated = GC.GetTotalAllocatedBytes(precise: true) - allocated;
            string actual = MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256();
            if (actual != save.StateSha256 || restored.Simulation.Random.State != save.RandomState)
                throw new InvalidOperationException("Recovery measurement diverged from its checkpoint.");
            samples.Add(new
            {
                Tick = tick, LogicalMinutes = tick / 1200d,
                DocumentBytes = Encoding.UTF8.GetByteCount(document), CommandCount = save.Commands.Count,
                Entities = save.State.Entities.EntityCount, Inventories = save.State.Inventories.Inventories.Count,
                Lifecycle = runtime.GetMatchState().Lifecycle.ToString(), ReadMilliseconds = readMilliseconds,
                RestoreMilliseconds = restoreMilliseconds, ProcessAllocatedBytesDuringRestore = allocated,
                Hash = actual, RandomState = save.RandomState
            });
        }
        string fullPath = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(new
        {
            Seed = 2026, Profile = "Gameplay", TickRate = 20,
            Runtime = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(), Processors = Environment.ProcessorCount,
            Build = typeof(MatchRuntime).Assembly.GetCustomAttributes(false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion,
            Scope = "Single normal-stock two-opponent match; cold restore at each saved tick; no graphics; advisory timing.",
            Samples = samples
        }, s_jsonOptions));
    }
}
