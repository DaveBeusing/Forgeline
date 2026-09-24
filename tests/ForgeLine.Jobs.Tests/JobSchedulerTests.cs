using System.Collections.Concurrent;
using Xunit;

namespace ForgeLine.Jobs.Tests;

public sealed class JobSchedulerTests
{
    [Fact]
    public void ParallelForProcessesEveryItemExactlyOnce()
    {
        using var scheduler = CreateScheduler(workerCount: 4);
        var visits = new int[8_192];

        JobHandle handle = scheduler.ParallelFor(
            0,
            visits.Length,
            97,
            (start, end, _) =>
            {
                for (int index = start; index < end; index++)
                {
                    Interlocked.Increment(ref visits[index]);
                }
            });

        scheduler.Wait(handle);

        for (int index = 0; index < visits.Length; index++)
        {
            Assert.Equal(1, visits[index]);
        }
    }

    [Fact]
    public void DependenciesPreserveRequiredOrdering()
    {
        using var scheduler = CreateScheduler(workerCount: 2);
        var order = new ConcurrentQueue<int>();

        JobHandle first = scheduler.Schedule(_ => order.Enqueue(1));
        JobHandle second = scheduler.Schedule(
            _ => order.Enqueue(2),
            new[] { first });

        scheduler.Wait(second);

        Assert.Equal(new[] { 1, 2 }, order.ToArray());
    }

    [Fact]
    public void FenceWaitsForAllDependencies()
    {
        using var scheduler = CreateScheduler(workerCount: 2);
        using var release = new ManualResetEventSlim(false);

        JobHandle first = scheduler.Schedule(
            cancellationToken => release.Wait(cancellationToken));
        JobHandle second = scheduler.Schedule(
            cancellationToken => release.Wait(cancellationToken));
        JobHandle fence = scheduler.CreateFence(new[] { first, second });

        Assert.False(fence.IsCompleted);

        release.Set();
        scheduler.Wait(fence);

        Assert.True(first.IsCompleted);
        Assert.True(second.IsCompleted);
        Assert.True(fence.IsCompleted);
    }

    [Fact]
    public void WorkerExceptionsSurfaceAndPreventDependentExecution()
    {
        using var scheduler = CreateScheduler(workerCount: 2);
        int dependentRuns = 0;

        JobHandle failing = scheduler.Schedule(
            static _ => throw new InvalidOperationException("scheduler-test"));
        JobHandle dependent = scheduler.Schedule(
            _ => Interlocked.Increment(ref dependentRuns),
            new[] { failing });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => scheduler.Wait(dependent));

        Assert.Equal("scheduler-test", exception.Message);
        Assert.Equal(0, dependentRuns);
        Assert.True(failing.IsFaulted);
        Assert.True(dependent.IsFaulted);
    }

    [Fact]
    public void OneWorkerModeExecutesDependencyChains()
    {
        using var scheduler = CreateScheduler(workerCount: 1);
        int value = 0;

        JobHandle first = scheduler.Schedule(_ => value = 10);
        JobHandle second = scheduler.Schedule(
            _ => value += 5,
            new[] { first });
        JobHandle third = scheduler.Schedule(
            _ => value *= 2,
            new[] { second });

        scheduler.Wait(third);

        Assert.Equal(30, value);
    }

    [Fact]
    public void CancelPendingShutdownCancelsQueuedAndCooperativeRunningJobs()
    {
        using var scheduler = CreateScheduler(workerCount: 1);
        using var started = new ManualResetEventSlim(false);

        JobHandle running = scheduler.Schedule(
            cancellationToken =>
            {
                started.Set();
                cancellationToken.WaitHandle.WaitOne();
                cancellationToken.ThrowIfCancellationRequested();
            });

        started.Wait(TestContext.Current.CancellationToken);

        JobHandle queued = scheduler.Schedule(
            static _ => throw new InvalidOperationException(
                "Queued job must not execute during cancellation."));

        scheduler.Shutdown(JobShutdownMode.CancelPending);

        Assert.True(scheduler.IsShutdown);
        Assert.True(running.IsCanceled);
        Assert.True(queued.IsCanceled);
        Assert.Throws<OperationCanceledException>(() => scheduler.Wait(running));
        Assert.Throws<OperationCanceledException>(() => scheduler.Wait(queued));
    }

    [Fact]
    public void ShutdownIsIdempotentAfterDrainingWork()
    {
        using var scheduler = CreateScheduler(workerCount: 2);
        JobHandle handle = scheduler.ParallelFor(
            0,
            10_000,
            128,
            static (start, end, _) =>
            {
                long total = 0;

                for (int index = start; index < end; index++)
                {
                    total += index;
                }

                GC.KeepAlive(total);
            });

        scheduler.Wait(handle);
        scheduler.Shutdown();
        scheduler.Shutdown();

        Assert.True(scheduler.IsShutdown);
    }

    [Fact]
    public void StressRangesAndDependenciesCompleteWithoutDeadlock()
    {
        using var scheduler = CreateScheduler(workerCount: 4);
        var visits = new int[100_000];

        JobHandle ranges = scheduler.ParallelFor(
            0,
            visits.Length,
            64,
            (start, end, _) =>
            {
                for (int index = start; index < end; index++)
                {
                    visits[index] = 1;
                }
            });

        JobHandle verify = scheduler.Schedule(
            _ =>
            {
                for (int index = 0; index < visits.Length; index++)
                {
                    if (visits[index] != 1)
                    {
                        throw new InvalidOperationException(
                            $"Range index {index} was not processed.");
                    }
                }
            },
            new[] { ranges });

        scheduler.Wait(verify);
        Assert.True(verify.IsCompleted);
    }

    [Fact]
    public void MetricsAndTimingObserverReportExecutedWork()
    {
        var samples = new ConcurrentQueue<JobTimingSample>();
        using var scheduler = new JobScheduler(
            new JobSchedulerOptions
            {
                WorkerCount = 2,
                TimingObserver = samples.Enqueue
            });

        JobHandle handle = scheduler.ParallelFor(
            0,
            4_096,
            256,
            static (start, end, _) =>
            {
                for (int index = start; index < end; index++)
                {
                    GC.KeepAlive(index * 31);
                }
            });

        scheduler.Wait(handle);

        JobSchedulerMetrics metrics = scheduler.GetMetrics();

        Assert.True(metrics.SubmittedJobs >= 2);
        Assert.Equal(metrics.SubmittedJobs, metrics.CompletedJobs);
        Assert.Equal(0, metrics.FaultedJobs);
        Assert.Equal(0, metrics.CanceledJobs);
        Assert.Equal(0, metrics.PendingJobs);
        Assert.Equal(0, metrics.RunningJobs);
        Assert.True(metrics.PeakRunningJobs >= 1);
        Assert.Equal(0, metrics.InstrumentationFailures);
        Assert.NotEmpty(samples);
    }

    private static JobScheduler CreateScheduler(int workerCount)
    {
        return new JobScheduler(
            new JobSchedulerOptions
            {
                WorkerCount = workerCount,
                WorkerNamePrefix = "ForgeLine Test Worker"
            });
    }
}
