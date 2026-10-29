using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Filters;
using ForgeLine.Core;
using ForgeLine.Presentation;
using ForgeLine.Presentation.RenderContracts;

namespace ForgeLine.Rendering.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(OptInConfig))]
public class SnapshotCorrespondenceBenchmarks
{
    public sealed class OptInConfig : ManualConfig
    {
        public OptInConfig() => AddFilter(new SimpleFilter(_ =>
            Environment.GetEnvironmentVariable("FORGELINE_SNAPSHOT_CORRESPONDENCE_BENCHMARKS") == "1"));
    }
    public enum PopulationKind { Stable, GenerationChurn, OneRemovalOneAddition }
    public enum OrderKind { Sorted, CurrentReversed }

    [Params(1_000, 10_000, 100_000)] public int Count { get; set; }
    [Params(PopulationKind.Stable, PopulationKind.GenerationChurn, PopulationKind.OneRemovalOneAddition)]
    public PopulationKind Population { get; set; }
    [Params(OrderKind.Sorted, OrderKind.CurrentReversed)] public OrderKind Order { get; set; }
    [Params(1, 3, 7)] public int FramesPerTick { get; set; }
    [Params(0f, .25f, .5f, 1f)] public float InterpolationAlpha { get; set; }

    private static readonly JsonSerializerOptions EvidenceJsonOptions = new() { WriteIndented = true };
    private PresentationSnapshot _previous = null!, _current = null!, _equalTickPrevious = null!;
    private RenderSnapshotIndex _index = null!;
    private RenderWorld _world = null!, _updateWorld = null!;
    private PresentationSnapshotBuffer _updateBuffer = null!;
    private bool _alternate;
    private int _lastMatches;

    [GlobalSetup]
    public void Setup()
    {
        var previous = new RenderInstance[Count]; var current = new RenderInstance[Count];
        int side = (int)Math.Ceiling(Math.Sqrt(Count));
        for (int i = 0; i < Count; i++)
        {
            previous[i] = Instance(i, side, false);
            current[i] = Instance(i, side, true);
            if (Population == PopulationKind.OneRemovalOneAddition) current[i] = current[i] with { Entity = new((uint)i + 2, 1) };
        }
        if (Order == OrderKind.CurrentReversed) Array.Reverse(current);
        _previous = Snapshot(previous, 31); _current = Snapshot(current, 32);
        _equalTickPrevious = Snapshot(previous, 32);
        _index = new(false); _index.Rebuild(_current, _previous);
        var buffer = new PresentationSnapshotBuffer();
        _world = new(new NoVisibility());
        buffer.Publish(_previous); _world.Update(buffer);
        buffer.Publish(_current); _world.Update(buffer);
        _updateBuffer = new(); _updateWorld = new(new NoVisibility());
        _updateBuffer.Publish(_equalTickPrevious); _updateWorld.Update(_updateBuffer);
        // Prime both current-order and predecessor-order capacities. Cold growth
        // belongs to PrepareGrowth, not the steady-state admission samples.
        _updateBuffer.Publish(_current); _updateWorld.Update(_updateBuffer);
        _updateBuffer.Publish(_equalTickPrevious); _updateWorld.Update(_updateBuffer);

        RenderInstance Instance(int i, int width, bool next)
        {
            uint h = unchecked((uint)i + 2026);
            h ^= h >> 16; h = unchecked(h * 0x7feb352d); h ^= h >> 15;
            h = unchecked(h * 0x846ca68b); h ^= h >> 16;
            float x = (i % width - (width - 1) / 2f) * .1f + ((int)(h & 1023) - 512) / 1024f * .01f;
            float z = (i / width - (width - 1) / 2f) * .1f + ((int)((h >> 10) & 1023) - 512) / 1024f * .01f;
            uint generation = next && Population == PopulationKind.GenerationChurn && i % 10 == 0 ? 2U : 1U;
            return new(new((uint)i + 1, generation), new(new Vector3(x, 0, z) + (next ? new Vector3(.02f, 0, .03f) : Vector3.Zero),
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, next ? .01f : 0), new(.1f, .12f, .1f)),
                new(1), RenderMaterialHandle.Default, RenderVisibilityMask.World);
        }
    }

    [Benchmark]
    public long PrepareWarm()
    {
        _index.Rebuild(_current, _previous);
        return _index.RetainedBytes;
    }

    [Benchmark]
    public long PrepareGrowth()
    {
        var index = new RenderSnapshotIndex(false);
        index.Rebuild(_current, _previous);
        return index.RetainedBytes;
    }

    // Equal ticks remain supported admissions. Visibility is deliberately excluded:
    // that separately owned implementation is not correspondence CPU preparation.
    [Benchmark]
    public bool AcceptedUpdateWithoutVisibility()
    {
        _alternate = !_alternate;
        _updateBuffer.Publish(_alternate ? _current : _equalTickPrevious);
        return _updateWorld.Update(_updateBuffer);
    }

    [Benchmark]
    public double FramesByIndex()
    {
        double checksum = 0;
        for (int frame = 0; frame < FramesPerTick; frame++)
            for (int i = 0; i < Count; i++)
                checksum += _world.GetInterpolatedInstance(i, InterpolationAlpha).Transform.Position.X;
        _lastMatches = Count * FramesPerTick;
        return checksum;
    }

    [Benchmark]
    public double FramesByEntity()
    {
        double checksum = 0; int matches = 0;
        for (int frame = 0; frame < FramesPerTick; frame++)
            for (int i = 0; i < Count; i++)
                if (_world.TryGetInterpolatedInstance(_current.Instances[i].Entity, InterpolationAlpha, out var instance))
                { checksum += instance.Transform.Position.X; matches++; }
        _lastMatches = matches;
        return checksum;
    }

    // Invoked by an isolated caller for raw CPU evidence, without changing the
    // shared benchmark dispatcher. BDN discovers the ordinary targets above.
    public static void Capture(string outputPath, string sourceIdentity, string sourceRoot, int samples = 8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(samples, 2);
        var rows = new List<object>();
        foreach (int count in new[] { 1_000, 10_000, 100_000 })
            foreach (PopulationKind population in Enum.GetValues<PopulationKind>())
                foreach (OrderKind order in Enum.GetValues<OrderKind>())
                {
                    var benchmark = new SnapshotCorrespondenceBenchmarks { Count = count, Population = population, Order = order };
                    benchmark.Setup();
                    for (int warmup = 0; warmup < 128; warmup++) benchmark.PrepareWarm();
                    var growth = Measure(() => benchmark.PrepareGrowth());
                    var preparation = Measure(() => benchmark.PrepareWarm());
                    var admission = Measure(() => benchmark.AcceptedUpdateWithoutVisibility() ? 1 : 0);
                    foreach (int ratio in new[] { 1, 3, 7 })
                    foreach (float alpha in new[] { 0f, .25f, .5f, 1f })
                    {
                        benchmark.InterpolationAlpha = alpha;
                        benchmark.FramesPerTick = ratio;
                        benchmark.FramesByIndex(); benchmark.FramesByEntity();
                        var index = Measure(benchmark.FramesByIndex);
                        var entity = Measure(benchmark.FramesByEntity);
                        rows.Add(new { count, population = population.ToString(), order = order.ToString(), framesPerTick = ratio, alpha,
                            retainedCorrespondenceBytes = benchmark._index.RetainedBytes, growth, preparation, admission, index, entity,
                            matchedEntities = benchmark._lastMatches, expectedEntities = count * ratio });
                    }
                }
        string[] components = ["src/ForgeLine.Presentation/RenderWorld.cs", "src/ForgeLine.Presentation/RenderSnapshotIndex.cs",
            "src/ForgeLine.Presentation/SnapshotPublication/SnapshotIdentityOrder.cs",
            "benchmarks/ForgeLine.Rendering.Benchmarks/SnapshotCorrespondenceBenchmarks.cs"];
        var hashes = components.ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(sourceRoot, p)))));
        File.WriteAllText(outputPath, JsonSerializer.Serialize(new { sourceIdentity, components = hashes,
            runtime = RuntimeInformation.FrameworkDescription, operatingSystem = RuntimeInformation.OSDescription,
            processorCount = Environment.ProcessorCount, samples, preparationWarmups = 128, frameWarmups = 1,
            stopwatchFrequency = Stopwatch.Frequency, gpuTiming = "unavailable: CPU-only", rows }, EvidenceJsonOptions));

        CpuSamples Measure(Func<double> action)
        {
            var microseconds = new double[samples]; var allocatedBytes = new long[samples]; double checksum = 0;
            for (int i = 0; i < samples; i++)
            {
                long beforeBytes = GC.GetAllocatedBytesForCurrentThread(), before = Stopwatch.GetTimestamp();
                checksum = action();
                long elapsed = Stopwatch.GetTimestamp() - before;
                allocatedBytes[i] = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
                microseconds[i] = elapsed * 1_000_000d / Stopwatch.Frequency;
            }
            return new(microseconds, allocatedBytes, checksum);
        }
    }

    private sealed record CpuSamples(double[] Microseconds, long[] AllocatedBytes, double Checksum);
    private static PresentationSnapshot Snapshot(RenderInstance[] values, ulong tick) =>
        new(new(tick), TimeSpan.FromMilliseconds(50), values.Length, values.AsSpan(), sessionId: new(1));
    private sealed class NoVisibility : IInstanceVisibilityIndex
    {
        public long RetainedBytes => 0;
        public void Rebuild(PresentationSnapshot current, PresentationSnapshot? previous, IRenderSnapshotCorrespondence correspondence) { }
        public bool IsVisible(int firstIndex, in ViewFrustum frustum) => true;
    }
}
