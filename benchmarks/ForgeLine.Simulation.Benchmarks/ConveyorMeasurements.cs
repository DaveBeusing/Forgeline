using System.Diagnostics;
using System.Globalization;
using ForgeLine.Benchmarks;
using ForgeLine.Game;

namespace ForgeLine.Simulation.Benchmarks;

/// <summary>Bounded warmed windows; liveness and conservation gate evidence, timing remains advisory.</summary>
internal static class ConveyorMeasurements
{
    public const int WarmupTicks = 256;
    private static readonly int[] ModuleCounts = [100, 1000, 4096];
    private static readonly ConveyorWorkloadTopology[] Topologies = [ConveyorWorkloadTopology.Linear, ConveyorWorkloadTopology.SplitMerge];
    private static readonly ConveyorWorkloadState[] States =
        [ConveyorWorkloadState.Powered, ConveyorWorkloadState.Unpowered, ConveyorWorkloadState.Blocked, ConveyorWorkloadState.Congested];

    public static void Run(string output, int samples = 512, int? modules = null)
    {
        if (samples is < 128 or > 4096) throw new ArgumentOutOfRangeException(nameof(samples));
        if (modules.HasValue && !ModuleCounts.Contains(modules.Value)) throw new ArgumentOutOfRangeException(nameof(modules));
        var measurements = new List<Measurement>();
        foreach (int count in ModuleCounts)
        {
            if (modules.HasValue && modules.Value != count) continue;
            foreach (ConveyorWorkloadTopology topology in Topologies)
            {
                foreach (ConveyorWorkloadState state in States)
                {
                    using var scenario = new ConveyorPerformanceScenario(count, topology, state);
                    Measurement measurement = Measure(scenario, samples);
                    measurements.Add(measurement);
                    Write(output, measurements, modules.HasValue);
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"Conveyor {count} {topology} {state}: tick={measurement.MeanTickMilliseconds:F4} ms conveyor={measurement.MeanConveyorMilliseconds:F4} ms allocated/tick={measurement.AllocatedBytesPerTick:F0} conveyor-bytes/tick={measurement.ConveyorAllocatedBytesPerTick:F0} production={measurement.After.ProductionCycles - measurement.Before.ProductionCycles} deliveries={measurement.After.CompletedDeliveries - measurement.Before.CompletedDeliveries} flow-ticks={measurement.ActiveFlowTicks}/{samples} truck-metres={measurement.TruckDistanceMeters:F2} qualified={measurement.Failures.Length == 0}"));
                    if (measurement.Failures.Length != 0)
                        throw new InvalidOperationException($"Conveyor workload {count}/{topology}/{state} failed evidence qualification: {string.Join("; ", measurement.Failures)}");
                }
            }
        }
    }

    private static Measurement Measure(ConveyorPerformanceScenario scenario, int samples)
    {
        for (int tick = 0; tick < WarmupTicks; tick++) scenario.AdvanceTick();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var raw = new double[samples];
        var conveyorRaw = new double[samples];
        var allocations = new long[samples];
        var conveyorAllocations = new long[samples];
        var rawModuleOutput = new double[samples];
        var rawTruckDistance = new double[samples];
        var authoritySamples = new List<AuthoritySample>();
        ConveyorWorkloadCounters before = scenario.CaptureCounters();
        authoritySamples.Add(new(before.Tick, scenario.CaptureAuthorityDigest()));
        scenario.BeginMeasurement();
        using var process = Process.GetCurrentProcess();
        TimeSpan cpuBefore = process.TotalProcessorTime;
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        int activeFlowTicks = 0, activeTruckTicks = 0;
        double flow = 0, distance = 0, firstHalfFlow = 0, secondHalfFlow = 0;
        double firstHalfTruckDistance = 0, secondHalfTruckDistance = 0;
        ConveyorWorkloadObservation last = default;
        for (int tick = 0; tick < samples; tick++)
        {
            long allocated = GC.GetTotalAllocatedBytes(precise: true);
            long started = Stopwatch.GetTimestamp();
            scenario.AdvanceTick();
            raw[tick] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            allocations[tick] = GC.GetTotalAllocatedBytes(precise: true) - allocated;
            conveyorRaw[tick] = scenario.ConveyorMilliseconds;
            conveyorAllocations[tick] = scenario.ConveyorAllocatedBytes;
            // Observation and reporting are deliberately outside both timed/allocation regions.
            last = scenario.Observe();
            rawModuleOutput[tick] = last.ModuleOutputQuantity;
            rawTruckDistance[tick] = last.TruckDistanceMeters;
            if ((tick + 1) % 32 == 0 || tick == samples - 1)
                authoritySamples.Add(new(before.Tick + (ulong)tick + 1, scenario.CaptureAuthorityDigest()));
            flow += last.ModuleOutputQuantity;
            distance += last.TruckDistanceMeters;
            activeFlowTicks += last.ModuleOutputQuantity > 0 ? 1 : 0;
            activeTruckTicks += last.ActiveTrucks > 0 ? 1 : 0;
            if (tick < samples / 2)
            {
                firstHalfFlow += last.ModuleOutputQuantity;
                firstHalfTruckDistance += last.TruckDistanceMeters;
            }
            else
            {
                secondHalfFlow += last.ModuleOutputQuantity;
                secondHalfTruckDistance += last.TruckDistanceMeters;
            }
        }
        process.Refresh();
        double cpu = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
        int[] gc = [GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2];
        ConveyorWorkloadCounters after = scenario.CaptureCounters();
        string[] failures = Validate(scenario, samples, before, after, flow, activeFlowTicks,
            firstHalfFlow, secondHalfFlow, firstHalfTruckDistance, secondHalfTruckDistance, last);
        return new(scenario.ModuleCount, scenario.LaneCount, scenario.TruckCount, scenario.Topology.ToString(),
            scenario.State.ToString(), ConveyorPerformanceScenario.TickRate, ConveyorPerformanceScenario.Seed,
            WarmupTicks, samples, raw.Average(), conveyorRaw.Average(),
            allocations.Average(), conveyorAllocations.Average(), cpu / samples, gc,
            ScalabilityMeasurement.Summarize(raw, 1000.0 / ConveyorPerformanceScenario.TickRate),
            ScalabilityMeasurement.Summarize(conveyorRaw), raw, conveyorRaw, allocations, conveyorAllocations,
            before, after, activeFlowTicks, activeTruckTicks, flow, distance, last,
            firstHalfFlow, secondHalfFlow, firstHalfTruckDistance, secondHalfTruckDistance,
            scenario.CaptureTruckStates(), rawModuleOutput, rawTruckDistance, authoritySamples.ToArray(), failures);
    }

    private static string[] Validate(ConveyorPerformanceScenario scenario, int samples,
        ConveyorWorkloadCounters before, ConveyorWorkloadCounters after, double flow, int activeFlowTicks,
        double firstHalfFlow, double secondHalfFlow, double firstHalfTruckDistance, double secondHalfTruckDistance,
        ConveyorWorkloadObservation last)
    {
        var failures = new List<string>();
        if (after.Tick - before.Tick != (ulong)samples) failures.Add("Incorrect measured tick count.");
        if (after.ProductionCycles <= before.ProductionCycles) failures.Add("Production was inactive during measurement.");
        double produced = after.ProducedSteel - before.ProducedSteel;
        // The unmodified Steel recipe consumes ten Ore and produces ten Steel per completed cycle.
        // Every tracked quantity is an integer multiple of 0.5 at 20 Hz, exactly representable in binary.
        if (after.Steel - before.Steel != produced) failures.Add("Steel does not reconcile with completed recipe output.");
        if (before.FerrousOre - after.FerrousOre != produced) failures.Add("Ore does not reconcile with completed recipe consumption.");
        if (after.LostCargo != before.LostCargo) failures.Add("Cargo was lost during the measured window.");
        if (scenario.State == ConveyorWorkloadState.Powered)
        {
            if (firstHalfFlow <= 0 || secondHalfFlow <= 0 || activeFlowTicks < samples / 4)
                failures.Add("Powered conveyors did not retain active flow throughout the window.");
            if (firstHalfTruckDistance <= 0 || secondHalfTruckDistance <= 0)
                failures.Add("Physical trucks did not move during both halves of the window.");
            if (after.CompletedDeliveries <= before.CompletedDeliveries || after.DeliveredSteel <= before.DeliveredSteel)
                failures.Add("No physical delivery completed during measurement.");
        }
        else
        {
            if (flow != 0) failures.Add("An intentionally stopped conveyor workload transferred cargo.");
            if (scenario.State == ConveyorWorkloadState.Unpowered && last.OfflineModules != scenario.ModuleCount)
                failures.Add("Unpowered modules were not reported offline.");
            if (scenario.State == ConveyorWorkloadState.Blocked && last.BlockedModules == 0)
                failures.Add("The disconnected receiving port did not block upstream modules.");
            if (scenario.State == ConveyorWorkloadState.Congested && last.CongestedModules != scenario.ModuleCount)
                failures.Add("Full buffers and destinations did not retain congestion.");
        }
        return failures.ToArray();
    }

    private static void Write(string output, List<Measurement> measurements, bool subset)
    {
        ScalabilityMeasurement.Write(output, "headless-conveyor", new
        {
            CompleteMatrix = !subset && measurements.Count == ModuleCounts.Length * Topologies.Length * States.Length,
            Qualified = measurements.All(measurement => measurement.Failures.Length == 0),
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            ExpectedMeasurements = subset ? Topologies.Length * States.Length : ModuleCounts.Length * Topologies.Length * States.Length,
            Scope = "One complete fixed tick with real production, conveyor flow, stock policies and, for powered cases, navigation/docking/distribution/physical cargo. Conveyor probe separately measures the existing system Execute call. No rendering, combat or whole-match cost is claimed.",
            Setup = "Finite synthetic fixture; 10000 Ore per Smelter, unmodified Steel recipe. Powered cases begin with zero Steel, so all conveyor and truck Steel comes from production. Congested cases additionally seed each finite buffer and sink to capacity. No replenishment, draining, teleporting or state resets during warmup/measurement.",
            Geometry = "5/50/205 parallel lanes of at most 20 transport modules. SplitMerge inserts a splitter, two independent standard branches and a merger per lane. Module counts exclude buildings and structural supports; scale fixture is not a construction-cost acceptance test.",
            States = "Powered has 1/4/16 physical Cargo Trucks; unpowered disables conveyor generation; blocked reverses and offsets receiving-building sockets beyond connection tolerance; congested fills sinks and buffers. Stopped states intentionally have no trucks and are not called active-flow measurements. Production remains active in every state.",
            Timing = "Full tick wall time includes conveyor probe overhead. Conveyor wall time excludes probe queries. Process CPU per observed tick includes observation/probe overhead and is reported separately from wall latency. Percentiles use nearest rank; raw tick samples are retained.",
            AllocationScope = "All process threads during the full tick or nested conveyor Execute call; setup, warmup, observation and JSON reporting excluded. GC counts include the entire observed sampling loop. No job scheduler is provisioned.",
            Equivalence = "Schema 1: SHA256 at warmup end, every 32 sampled ticks and final tick. Little-endian 64-bit words encode tick/counters, each tracked inventory's total and all seven conveyor resources' quantity/reservation/availability, all module flow/cursor/network fields and buffered quantity, port resource cursors, truck position/cargo lifecycle and dock identity/state. Full entity generations and exact floating-point bits participate. Raw per-tick aggregate module output and truck distance are also retained. Digests are observed outside timing/allocation regions; process CPU includes observations.",
            Acceptance = "Liveness, finite inventory reconciliation and expected stopped states are mandatory. The 50 ms/20 Hz target is observational, not a machine-independent timing threshold or physical hardware acceptance. No optimization gain is inferred.",
            Measurements = measurements
        }, [typeof(ConveyorMeasurements).Assembly.Location, typeof(ConveyorSimulationSystem).Assembly.Location]);
    }

    private sealed record Measurement(int Modules, int ProductionFacilities, int Trucks, string Topology,
        string State, int TickRate, ulong Seed, int WarmupTicks, int MeasuredTicks,
        double MeanTickMilliseconds, double MeanConveyorMilliseconds, double AllocatedBytesPerTick,
        double ConveyorAllocatedBytesPerTick, double ProcessCpuMillisecondsPerObservedTick, int[] GenCollections,
        object TickTiming, object ConveyorTiming, double[] RawTickMilliseconds, double[] RawConveyorMilliseconds,
        long[] RawTickAllocatedBytes, long[] RawConveyorAllocatedBytes,
        ConveyorWorkloadCounters Before, ConveyorWorkloadCounters After, int ActiveFlowTicks, int ActiveTruckTicks,
        double ModuleOutputQuantity, double TruckDistanceMeters, ConveyorWorkloadObservation LastObservation,
        double FirstHalfModuleOutputQuantity, double SecondHalfModuleOutputQuantity,
        double FirstHalfTruckDistanceMeters, double SecondHalfTruckDistanceMeters, object[] FinalTruckStates,
        double[] RawModuleOutputQuantity, double[] RawTruckDistanceMeters, AuthoritySample[] AuthoritySamples, string[] Failures);

    private sealed record AuthoritySample(ulong Tick, string Sha256);
}
