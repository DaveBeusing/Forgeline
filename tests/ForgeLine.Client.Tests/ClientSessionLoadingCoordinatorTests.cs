using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ClientSessionLoadingCoordinatorTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private sealed class Session : IDisposable
    {
        internal int Disposals;
        public void Dispose() => Interlocked.Increment(ref Disposals);
    }

    [Fact]
    public void ConstructionRunsOnDedicatedOwnerAndTransfersExactlyOnce()
    {
        int owner = Environment.CurrentManagedThreadId, worker = 0;
        var session = new Session();
        using var coordinator = new ClientSessionLoadingCoordinator<Session>((_, report) =>
        {
            worker = Environment.CurrentManagedThreadId;
            report(new(ClientSessionLoadPhase.Replay, 32, 64));
            return session;
        }, TestContext.Current.CancellationToken);
        Assert.True(SpinWait.SpinUntil(() => coordinator.Completed, Timeout));
        Assert.NotEqual(owner, worker);
        Assert.Equal(0.5f, coordinator.Progress.Progress);
        Assert.True(coordinator.TryTake(out Session? taken));
        Assert.Same(session, taken);
        Assert.False(coordinator.TryTake(out _));
        coordinator.Dispose();
        Assert.False(coordinator.WorkerAlive);
        Assert.Equal(0, session.Disposals);
        taken!.Dispose();
        Assert.Equal(1, session.Disposals);
    }

    [Fact]
    public void AbandonedCompletedResultIsDisposedOnce()
    {
        var session = new Session();
        var coordinator = new ClientSessionLoadingCoordinator<Session>((_, _) => session,
            TestContext.Current.CancellationToken);
        Assert.True(SpinWait.SpinUntil(() => coordinator.Completed, Timeout));
        coordinator.Dispose();
        coordinator.Dispose();
        Assert.Equal(1, session.Disposals);
        Assert.False(coordinator.WorkerAlive);
    }

    [Fact]
    public void CancellationAfterCreationDisposesUntransferredResult()
    {
        var session = new Session();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var coordinator = new ClientSessionLoadingCoordinator<Session>((_, _) =>
        {
            cancellation.Cancel();
            return session;
        }, cancellation.Token);
        Assert.True(SpinWait.SpinUntil(() => coordinator.Completed, Timeout));
        Assert.False(coordinator.TryTake(out _));
        Assert.Null(coordinator.Failure);
        Assert.Equal(1, session.Disposals);
    }

    [Fact]
    public void WorkerExceptionRetainsPhaseAndMessageWithoutResult()
    {
        using var coordinator = new ClientSessionLoadingCoordinator<Session>((_, report) =>
        {
            report(new(ClientSessionLoadPhase.Replay, 64, 512));
            throw new InvalidDataException("Control command cannot be applied.");
        }, TestContext.Current.CancellationToken);
        Assert.True(SpinWait.SpinUntil(() => coordinator.Completed, Timeout));
        Assert.Equal(ClientSessionLoadError.Replay, coordinator.Failure!.Category);
        Assert.Contains("Control command", coordinator.Failure.Message);
        Assert.False(coordinator.TryTake(out _));
    }

    [Fact]
    public async Task ConcurrentDisposersWaitForWorkerAndDisposeResultOnce()
    {
        using var entered = new ManualResetEventSlim();
        using var cancelled = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var disposing = new CountdownEvent(2);
        var session = new Session();
        using var coordinator = new ClientSessionLoadingCoordinator<Session>((token, _) =>
        {
            using var registration = token.Register(cancelled.Set);
            entered.Set(); release.Wait(TestContext.Current.CancellationToken);
            return session;
        }, TestContext.Current.CancellationToken);
        Assert.True(entered.Wait(Timeout, TestContext.Current.CancellationToken));
        // Blocking join calls need independent callers, not shared test-pool capacity.
        Task StartDisposer() => Task.Factory.StartNew(() =>
        {
            disposing.Signal();
            coordinator.Dispose();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task first = StartDisposer();
        Task second = StartDisposer();
        try
        {
            Assert.True(disposing.Wait(Timeout, TestContext.Current.CancellationToken));
            Assert.True(cancelled.Wait(Timeout, TestContext.Current.CancellationToken));
            Assert.False(first.IsCompleted); Assert.False(second.IsCompleted);
        }
        finally { release.Set(); }
        await Task.WhenAll(first, second).WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.False(coordinator.WorkerAlive);
        Assert.Equal(1, session.Disposals);
    }
}
