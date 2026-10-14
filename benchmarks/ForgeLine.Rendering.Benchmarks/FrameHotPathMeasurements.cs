using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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

    public static void RunCulled(string output)
    {
        var results = new List<object>();
        foreach (bool afterVisible in new[] { false, true })
        {
            using var renderer = new PresentationBenchmarks();
            renderer.Setup();
            if (afterVisible)
                for (int i = 0; i < Warmup; i++) _ = renderer.Submit1000NearFieldInstances();
            InstanceRenderDiagnostics probe = renderer.Submit5000InstancesWithCulling();
            if (probe.TotalInstances != 5000 || probe.VisibleInstances != 0 ||
                probe.CulledInstances != 5000 || probe.DrawCalls != 0)
                throw new InvalidOperationException("The fully culled fixture changed; timing comparison is invalid.");
            results.Add(Measure(afterVisible ? "culled-after-visible" : "culled-fresh",
                () => renderer.Submit5000InstancesWithCulling()));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            Backend = "CPU null graphics; isolated culling without a publication thread",
            Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription,
            ProcessorCount = Environment.ProcessorCount,
            BuildVersion = typeof(SimpleInstanceRenderer).Assembly.GetName().Version?.ToString(),
            RendererAssemblySha256 = Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(typeof(SimpleInstanceRenderer).Assembly.Location))),
            Warmup,
            Samples,
            Viewport = "1600x900",
            Alpha = 1.0f,
            Results = results
        }, JsonOptions));
        Console.WriteLine($"Culled hot-path measurements: {Path.GetFullPath(output)}");
    }

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

        var indexedInstances = Enumerable.Range(1, 1000).Select(i => new RenderInstance(new ForgeLine.Core.EntityId((uint)i, 1),
            new(new Vector3(i % 32, 0, i / 32), Quaternion.Identity, Vector3.One), new(1), RenderMaterialHandle.Default, RenderVisibilityMask.World)).ToArray();
        var indexBuffer = new PresentationSnapshotBuffer();
        var indexedWorld = new RenderWorld();
        var indexFirst = new PresentationSnapshot(new SimulationTick(1), TimeSpan.FromMilliseconds(50), 1000, indexedInstances);
        var indexSecond = new PresentationSnapshot(new SimulationTick(1), TimeSpan.FromMilliseconds(50), 1000, indexedInstances);
        bool alternate = false;
        var results = new List<object>
        {
            Measure("snapshot-index-rebuild-1000", () => { alternate = !alternate; indexBuffer.Publish(alternate ? indexFirst : indexSecond); return indexedWorld.Update(indexBuffer); }),
            Measure("mechanical-static-1000", () => renderer.Submit1000MechanicalInstances(false), () => renderer.SubmissionMetrics),
            Measure("mechanical-posed-1000", () => renderer.Submit1000MechanicalInstances(true), () => renderer.SubmissionMetrics),
            Measure("render-near-1000", () => renderer.Submit1000NearFieldInstances(), () => renderer.SubmissionMetrics),
            Measure("render-culled-5000", () => renderer.Submit5000InstancesWithCulling(), () => renderer.SubmissionMetrics),
            Measure("render-mixed-tactical", () => renderer.SubmitRepresentativeVerticalSliceTacticalView(), () => renderer.SubmissionMetrics),
            Measure("render-mixed-normal", () => renderer.SubmitRepresentativeVerticalSliceNormalRtsView(), () => renderer.SubmissionMetrics),
            Measure("render-mixed-strategic", () => renderer.SubmitRepresentativeVerticalSliceStrategicView(), () => renderer.SubmissionMetrics),
            Measure("extraction-1000", () => { simulation.AdvanceOneTick(); return snapshots.TryReadLatest(out var s) ? s.InstanceCount : 0; })
        };
        // The publication worker wakes periodically even before receiving a frame.
        // Start it only after the independent render/extraction measurements.
        using var host = new ClientRenderHost(static _ => { });
        var frame = new ClientRenderFrame(new RtsCamera().CaptureState(), 1600, 900, false,
            DebugOverlayView.Disabled, default, default, FormationTemplate.Compact,
            new DebugLine[128], new DebugLabel[64], new DebugLine[128], new DebugLabel[64]);
        results.Add(Measure("publish-128-lines-64-labels-per-layer", () => host.Publish(frame)));
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
