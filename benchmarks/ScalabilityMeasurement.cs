using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace ForgeLine.Benchmarks;

internal static class ScalabilityMeasurement
{
    public const int Warmup = 128;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static object Measure<T>(string name, string scope, Func<T> action, int samples,
        double? budgetMilliseconds = null, Action? prepare = null, Action<int>? observe = null,
        Func<object>? counters = null, int warmup = Warmup, Func<object>? windowCounters = null)
    {
        if (samples is < 16 or > 65536) throw new ArgumentOutOfRangeException(nameof(samples));
        for (int i = 0; i < (prepare is null ? warmup : 4); i++) { prepare?.Invoke(); _ = action(); }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var raw = new double[samples];
        var memory = new List<object>();
        using var process = Process.GetCurrentProcess();
        void CaptureMemory(int completed)
        {
            process.Refresh();
            memory.Add(new
            {
                CompletedSamples = completed,
                process.PrivateMemorySize64,
                process.WorkingSet64,
                ManagedBytes = GC.GetTotalMemory(false),
                GC.GetGCMemoryInfo().HeapSizeBytes,
                GC.GetGCMemoryInfo().FragmentedBytes,
                Counters = windowCounters?.Invoke()
            });
        }
        CaptureMemory(0);
        int[] startGc = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
        long allocated = 0;
        T? last = default;
        long samplingStarted = Stopwatch.GetTimestamp();
        for (int i = 0; i < samples; i++)
        {
            prepare?.Invoke();
            long bytes = GC.GetTotalAllocatedBytes(precise: true);
            long started = Stopwatch.GetTimestamp();
            last = action();
            raw[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            allocated += GC.GetTotalAllocatedBytes(precise: true) - bytes;
            observe?.Invoke(i);
            if ((i + 1) % Math.Max(1, samples / 4) == 0) CaptureMemory(i + 1);
        }
        int[] gc = [GC.CollectionCount(0) - startGc[0], GC.CollectionCount(1) - startGc[1], GC.CollectionCount(2) - startGc[2]];
        return new
        {
            Name = name,
            Scope = scope,
            Samples = samples,
            Warmup = prepare is null ? warmup : 4,
            BudgetMilliseconds = budgetMilliseconds,
            Timing = Summarize(raw, budgetMilliseconds),
            AllocatedBytesPerOperation = (double)allocated / samples,
            GenCollections = gc,
            AllocationScope = "All process threads during action; excludes prepare, observation and report capture. GC includes entire sampling loop.",
            SamplingWallMilliseconds = Stopwatch.GetElapsedTime(samplingStarted).TotalMilliseconds,
            RawMilliseconds = raw,
            Memory = memory,
            LastResult = last,
            Counters = counters?.Invoke()
        };
    }

    public static object Summarize(double[] raw, double? budget = null)
    {
        if (raw.Length == 0) throw new ArgumentException("At least one sample is required.", nameof(raw));
        var sorted = (double[])raw.Clone();
        Array.Sort(sorted);
        double Percentile(double p) => sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];
        return new
        {
            P50Milliseconds = Percentile(.50),
            P95Milliseconds = Percentile(.95),
            P99Milliseconds = Percentile(.99),
            MaximumMilliseconds = sorted[^1],
            OverBudget = budget.HasValue ? raw.Count(x => x > budget.Value) : (int?)null,
            HitchesOver100Milliseconds = raw.Count(x => x > 100)
        };
    }

    public static void Write(string output, string backend, object results, string[] measuredAssemblies)
    {
        string root = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(root)!);
        File.WriteAllText(root, JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            CapturedUtc = DateTimeOffset.UtcNow,
            Backend = backend,
            Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.ProcessorCount,
            Processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
            CpuModel = Environment.GetEnvironmentVariable("FORGELINE_CPU_MODEL"),
            Revision = Environment.GetEnvironmentVariable("FORGELINE_QUALIFICATION_REVISION"),
            HardwareReference = Environment.GetEnvironmentVariable("FORGELINE_HARDWARE_REFERENCE"),
            DriverMetadata = Environment.GetEnvironmentVariable("FORGELINE_GPU_DRIVER"),
            ClockAndPowerSettings = Environment.GetEnvironmentVariable("FORGELINE_CLOCK_SETTINGS"),
            Assemblies = measuredAssemblies.Select(path => new
            {
                Name = Path.GetFileName(path),
                Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            }),
            Policy = "Timing and memory trends are advisory; null metadata is unqualified, never inferred. Process private bytes are not GPU committed memory; working set is not GPU residency.",
            Results = results
        }, JsonOptions));
        Console.WriteLine($"Scalability report: {root}");
    }
}
