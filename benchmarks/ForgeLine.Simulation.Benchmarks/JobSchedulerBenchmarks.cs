using BenchmarkDotNet.Attributes;
using ForgeLine.Jobs;

namespace ForgeLine.Simulation.Benchmarks;

[MemoryDiagnoser]
public class JobSchedulerBenchmarks
{
    private JobScheduler _scheduler = null!;
    private int[] _sequentialOutput = null!;
    private int[] _parallelOutput = null!;
    private JobRangeAction _parallelAction = null!;

    [Params(65_536, 262_144)]
    public int ItemCount { get; set; }

    [Params(256, 1_024)]
    public int BatchSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _sequentialOutput = new int[ItemCount];
        _parallelOutput = new int[ItemCount];
        _parallelAction = ProcessParallelRange;
        _scheduler = new JobScheduler(
            new JobSchedulerOptions
            {
                WorkerCount = Math.Max(1, Environment.ProcessorCount - 1),
                WorkerNamePrefix = "ForgeLine Benchmark Worker"
            });
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _scheduler.Dispose();
    }

    [Benchmark(Baseline = true)]
    public int SequentialRange()
    {
        for (int index = 0; index < _sequentialOutput.Length; index++)
        {
            _sequentialOutput[index] = Transform(index);
        }

        return _sequentialOutput[^1];
    }

    [Benchmark]
    public int ParallelRange()
    {
        JobHandle handle = _scheduler.ParallelFor(
            0,
            _parallelOutput.Length,
            BatchSize,
            _parallelAction);

        _scheduler.Wait(handle);
        return _parallelOutput[^1];
    }

    private void ProcessParallelRange(
        int startInclusive,
        int endExclusive,
        CancellationToken cancellationToken)
    {
        for (int index = startInclusive; index < endExclusive; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _parallelOutput[index] = Transform(index);
        }
    }

    private static int Transform(int value)
    {
        uint mixed = (uint)value;
        mixed ^= mixed << 13;
        mixed ^= mixed >> 17;
        mixed ^= mixed << 5;
        return unchecked((int)mixed);
    }
}
