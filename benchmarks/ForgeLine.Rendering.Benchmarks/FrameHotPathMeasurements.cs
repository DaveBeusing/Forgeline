using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ForgeLine.Client;
using ForgeLine.Game;
using ForgeLine.Presentation;
using ForgeLine.Simulation;

namespace ForgeLine.Rendering.Benchmarks;

internal static class FrameHotPathMeasurements
{
    private const int Warmup = 1024;
    private const int Samples = 8192;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Run(string output)
    {
        using var renderer = new PresentationBenchmarks();
        renderer.Setup();
        var simulation = new SimulationCoordinator();
        var snapshots = new PresentationSnapshotBuffer();
        simulation.RegisterTickObserver(new PresentationExtractor(snapshots));
        for (int index = 0; index < 1000; index++)
        {
            var entity = simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(entity,
                new WorldTransform(new Vector3(index % 32, 0, index / 32), Quaternion.Identity, Vector3.One));
            simulation.Entities.AddComponent(entity, new VisualIdentity(1));
        }

        using var host = new ClientRenderHost(static _ => { });
        var frame = new ClientRenderFrame(new RtsCamera().CaptureState(), 1600, 900, false,
            DebugOverlayView.Disabled, default, default, FormationTemplate.Compact,
            new DebugLine[128], new DebugLabel[64], new DebugLine[128], new DebugLabel[64]);
        var results = new List<object>
        {
            Measure("render-near-1000", () => renderer.Submit1000NearFieldInstances(), () => renderer.SubmissionMetrics),
            Measure("render-culled-5000", () => renderer.Submit5000InstancesWithCulling(), () => renderer.SubmissionMetrics),
            Measure("render-mixed-tactical", () => renderer.SubmitRepresentativeVerticalSliceTacticalView(), () => renderer.SubmissionMetrics),
            Measure("render-mixed-normal", () => renderer.SubmitRepresentativeVerticalSliceNormalRtsView(), () => renderer.SubmissionMetrics),
            Measure("render-mixed-strategic", () => renderer.SubmitRepresentativeVerticalSliceStrategicView(), () => renderer.SubmissionMetrics),
            Measure("extraction-1000", () => { simulation.AdvanceOneTick(); return snapshots.TryReadLatest(out var s) ? s.InstanceCount : 0; }),
            Measure("publish-128-lines-64-labels-per-layer", () => host.Publish(frame))
        };
        string? directory = Path.GetDirectoryName(Path.GetFullPath(output));
        Directory.CreateDirectory(directory!);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            Backend = "CPU null graphics; excludes GPU uploads, waits and presentation",
            Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            Processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
            BuildVersion = typeof(SimpleInstanceRenderer).Assembly.GetName().Version?.ToString(),
            Warmup,
            Samples,
            Viewport = "1600x900",
            Alpha = 1.0f,
            Results = results
        }, JsonOptions));
        Console.WriteLine($"Frame hot-path measurements: {Path.GetFullPath(output)}");
    }

    private static object Measure<T>(string name, Func<T> action, Func<object>? submission = null)
    {
        for (int i = 0; i < Warmup; i++) _ = action();
        // Settle retained warm state before each independent stage, outside sampling.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var elapsed = new double[Samples];
        long allocation = GC.GetAllocatedBytesForCurrentThread();
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        T? last = default;
        long started = Stopwatch.GetTimestamp();
        for (int i = 0; i < Samples; i++)
        {
            long before = Stopwatch.GetTimestamp();
            last = action();
            elapsed[i] = Stopwatch.GetElapsedTime(before).TotalMicroseconds;
        }
        double seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocation;
        int[] collections = [GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2];
        Array.Sort(elapsed);
        return new
        {
            Name = name,
            BytesPerOperation = (double)bytes / Samples,
            P50Microseconds = elapsed[(int)(Samples * 0.50)],
            P95Microseconds = elapsed[(int)(Samples * 0.95)],
            P99Microseconds = elapsed[(int)(Samples * 0.99)],
            OperationsPerSecond = Samples / seconds,
            GenCollections = collections,
            LastResult = last,
            Submission = submission?.Invoke()
        };
    }
}
