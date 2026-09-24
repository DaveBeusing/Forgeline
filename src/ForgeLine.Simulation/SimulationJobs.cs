using System.Runtime.InteropServices;
using ForgeLine.Jobs;

namespace ForgeLine.Simulation;

public sealed class SimulationJobs
{
    private readonly JobScheduler? _scheduler;
    private readonly List<JobHandle> _pending = new();

    internal SimulationJobs(JobScheduler? scheduler)
    {
        _scheduler = scheduler;
    }

    public bool IsAvailable => _scheduler is not null;

    public JobHandle Schedule(JobAction action)
    {
        return Track(RequireScheduler().Schedule(action));
    }

    public JobHandle Schedule(
        JobAction action,
        ReadOnlySpan<JobHandle> dependencies)
    {
        return Track(RequireScheduler().Schedule(action, dependencies));
    }

    public JobHandle ParallelFor(
        int startInclusive,
        int endExclusive,
        int batchSize,
        JobRangeAction action)
    {
        return Track(
            RequireScheduler().ParallelFor(
                startInclusive,
                endExclusive,
                batchSize,
                action));
    }

    public JobHandle ParallelFor(
        int startInclusive,
        int endExclusive,
        int batchSize,
        JobRangeAction action,
        ReadOnlySpan<JobHandle> dependencies)
    {
        return Track(
            RequireScheduler().ParallelFor(
                startInclusive,
                endExclusive,
                batchSize,
                action,
                dependencies));
    }

    public void Wait(JobHandle handle)
    {
        RequireScheduler().Wait(handle);
    }

    public JobSchedulerMetrics GetMetrics()
    {
        return RequireScheduler().GetMetrics();
    }

    public bool TryGetMetrics(out JobSchedulerMetrics metrics)
    {
        if (_scheduler is null)
        {
            metrics = default;
            return false;
        }

        metrics = _scheduler.GetMetrics();
        return true;
    }

    internal void CompleteBoundary()
    {
        if (_pending.Count == 0)
        {
            return;
        }

        JobScheduler scheduler = RequireScheduler();
        JobHandle fence = scheduler.CreateFence(CollectionsMarshal.AsSpan(_pending));
        _pending.Clear();
        scheduler.Wait(fence);
    }

    private JobHandle Track(JobHandle handle)
    {
        _pending.Add(handle);
        return handle;
    }

    private JobScheduler RequireScheduler()
    {
        return _scheduler ?? throw new InvalidOperationException(
            "Parallel simulation jobs are not enabled for this coordinator.");
    }
}
