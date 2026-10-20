using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ForgeLine.Graphics;
using ForgeLine.Presentation.EffectSubmission;
using Xunit;

namespace ForgeLine.Presentation.Tests.EffectSubmission;

public sealed class ParticleOrderingQualificationTests
{
    private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented = true };
    public static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_PARTICLE_CPU_QUALIFICATION_OUTPUT"));

    [Fact(Skip = "Set FORGELINE_PARTICLE_CPU_QUALIFICATION_OUTPUT for isolated CPU ordering qualification.", SkipUnless = nameof(Enabled))]
    public void MeasureEquivalentParticleOrderingInPairedCohorts()
    {
        string output = Path.GetFullPath(Environment.GetEnvironmentVariable("FORGELINE_PARTICLE_CPU_QUALIFICATION_OUTPUT")!);
        if (File.Exists(output) || Directory.Exists(output))
            throw new IOException("Particle ordering evidence output already exists.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var results = new List<object>();
        foreach (int count in new[] { 512, 2048, 4096 })
        foreach (ParticleVisualKind visual in new[] { ParticleVisualKind.Spark, ParticleVisualKind.Smoke })
        {
            // Unique stationary camera-axis depths give the old radial comparator
            // and production view-depth comparator exactly the same pixel inputs.
            int[] order = Enumerable.Range(1, count).ToArray();
            new Random(51913).Shuffle(order);
            GraphicsParticle[] source = order.Select(i => new GraphicsParticle(new(0, 0, -i, 0), new(0, 0, 0, 2),
                new(i / (float)count, .5f, .25f, .5f), new(.5f, 0, (float)visual, .35f))).ToArray();
            GraphicsParticle[] expected = source.OrderByDescending(x => -x.OriginAge.Z).ToArray();
            var particles = new GraphicsParticle[count];
            var scratch = new ParticleDepthKey[count];
            for (int cohort = 0; cohort < 4; cohort++)
            {
                bool optimized = cohort is 1 or 2;
                for (int warmup = 0; warmup < 64; warmup++)
                {
                    source.CopyTo(particles, 0);
                    Sort(particles, scratch, optimized);
                }
                AssertPayload(expected, particles);
                var samples = new double[256];
                DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
                long totalStart = GC.GetTotalAllocatedBytes(true);
                long threadStart = GC.GetAllocatedBytesForCurrentThread();
                for (int sample = 0; sample < samples.Length; sample++)
                {
                    source.CopyTo(particles, 0);
                    long start = Stopwatch.GetTimestamp();
                    Sort(particles, scratch, optimized);
                    samples[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                }
                long threadBytes = GC.GetAllocatedBytesForCurrentThread() - threadStart;
                long totalBytes = GC.GetTotalAllocatedBytes(true) - totalStart;
                DateTimeOffset completedUtc = DateTimeOffset.UtcNow;
                AssertPayload(expected, particles);
                Assert.Equal(0, threadBytes);
                double[] sorted = samples.Order().ToArray();
                results.Add(new
                {
                    count, visual = visual.ToString(), cohort,
                    implementation = optimized ? "production-view-depth-key-permutation" : "reference-anchor-distance-heap",
                    startedUtc, completedUtc, warmups = 64, samplesMilliseconds = samples,
                    meanMilliseconds = samples.Average(), p50Milliseconds = sorted[(samples.Length - 1) / 2],
                    p95Milliseconds = sorted[(int)Math.Ceiling(samples.Length * .95) - 1],
                    p99Milliseconds = sorted[(int)Math.Ceiling(samples.Length * .99) - 1],
                    currentThreadAllocatedBytes = threadBytes, allThreadAllocatedBytes = totalBytes,
                    exactParticlePayloadBytes = count * 64, payloadParity = true, gpuMilliseconds = (double?)null
                });
            }
        }
        using var file = File.Open(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(file, new
        {
            referenceSource = "cdf97fb9a8ef277cf75d451476a21128d398007c",
            candidateSource = Environment.GetEnvironmentVariable("FORGELINE_PARTICLE_CPU_SOURCE_SHA") ?? "uncommitted-working-tree",
            framework = RuntimeInformation.FrameworkDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            processorCount = Environment.ProcessorCount, stopwatchFrequency = Stopwatch.Frequency,
            method = "Sequential reference/candidate/candidate/reference ordering-only cohorts. Stationary unique -Z camera-axis depths, identity view, identical full 64-byte particle payload order. Array reset is outside timed intervals but included in allocation windows. Smoke retains the zero-age curl branch. No allocation, performance, or native quality threshold is relaxed.",
            limits = "Synthetic CPU ordering evidence only; not complete Prepare, upload, GPU, image or frame-rate acceptance. Admission and moving/off-axis ordering changes require separate native review. All-thread allocation may include test-host work. No GPU or external-process isolation is inferred.",
            results
        }, ReportOptions);
    }

    private static void AssertPayload(GraphicsParticle[] expected, GraphicsParticle[] actual) =>
        Assert.True(MemoryMarshal.AsBytes(expected.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(actual.AsSpan())));

    private static void Sort(GraphicsParticle[] particles, ParticleDepthKey[] scratch, bool optimized)
    {
        if (optimized) ParticleDepthOrdering.Sort(particles, scratch, Matrix4x4.Identity);
        else ReferenceSort(particles.AsSpan(), ReferenceComparer.Instance);
    }

    // Frozen reference from ParticleVfxRenderer at the source identified above.
    private static void ReferenceSort<T>(Span<T> values, IComparer<T> comparer)
    {
        for (int root = values.Length / 2 - 1; root >= 0; root--) ReferenceSift(values, root, comparer);
        for (int end = values.Length - 1; end > 0; end--)
        {
            (values[0], values[end]) = (values[end], values[0]);
            ReferenceSift(values[..end], 0, comparer);
        }
    }

    private static void ReferenceSift<T>(Span<T> values, int root, IComparer<T> comparer)
    {
        while (root * 2 + 1 < values.Length)
        {
            int child = root * 2 + 1;
            if (child + 1 < values.Length && comparer.Compare(values[child], values[child + 1]) < 0) child++;
            if (comparer.Compare(values[root], values[child]) >= 0) return;
            (values[root], values[child]) = (values[child], values[root]);
            root = child;
        }
    }

    private sealed class ReferenceComparer : IComparer<GraphicsParticle>
    {
        internal static ReferenceComparer Instance { get; } = new();
        public Vector3 Camera { get; set; }
        public int Compare(GraphicsParticle a, GraphicsParticle b) => Vector3.DistanceSquared(new(b.OriginAge.X, b.OriginAge.Y, b.OriginAge.Z), Camera)
            .CompareTo(Vector3.DistanceSquared(new(a.OriginAge.X, a.OriginAge.Y, a.OriginAge.Z), Camera));
    }
}
