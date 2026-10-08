using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ClientStartupCoordinatorTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void BlockingStartupWorkersHaveDedicatedThreadsSeparateFromTheirOwner()
    {
        using var entered = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        int owner = Environment.CurrentManagedThreadId;
        object Work(CancellationToken token)
        {
            var worker = Thread.CurrentThread;
            entered.Signal();
            release.Wait(token);
            return (worker.ManagedThreadId, worker.IsThreadPoolThread);
        }
        using var coordinator = new ClientStartupCoordinator<object, object>(Work, Work,
            TestContext.Current.CancellationToken);
        Assert.True(entered.Wait(Timeout, TestContext.Current.CancellationToken));
        release.Set();
        coordinator.CompleteSplash();
        Assert.True(SpinWait.SpinUntil(() => coordinator.CanEnterFrontend, Timeout));
        var results = coordinator.GetResults();
        var assets = Assert.IsType<(int Id, bool IsPool)>(results.Assets);
        var frontend = Assert.IsType<(int Id, bool IsPool)>(results.Frontend);
        Assert.False(assets.IsPool);
        Assert.False(frontend.IsPool);
        Assert.NotEqual(owner, assets.Id);
        Assert.NotEqual(owner, frontend.Id);
        Assert.NotEqual(assets.Id, frontend.Id);
    }

    [Fact]
    public void IntroCompletingFirstDoesNotReleaseIncompleteDependencies()
    {
        using var release = new ManualResetEventSlim();
        using var coordinator = new ClientStartupCoordinator<object, object>(
            token => { release.Wait(token); return new object(); }, _ => new object(),
            TestContext.Current.CancellationToken);
        coordinator.CompleteSplash();
        Assert.True(coordinator.SplashCompletedOrSkipped);
        Assert.False(coordinator.CanEnterFrontend);
        release.Set();
        Assert.True(SpinWait.SpinUntil(() => coordinator.CanEnterFrontend, Timeout));
        Assert.NotNull(coordinator.GetResults().Assets);
    }

    [Fact]
    public void DependenciesCompletingFirstStillRequireIntroCompletion()
    {
        using var coordinator = new ClientStartupCoordinator<object, object>(
            _ => new object(), _ => new object(), TestContext.Current.CancellationToken);
        Assert.True(SpinWait.SpinUntil(() => coordinator.DependenciesReady, Timeout));
        Assert.False(coordinator.CanEnterFrontend);
        Assert.Throws<InvalidOperationException>(() => coordinator.GetResults());
        coordinator.CompleteSplash();
        Assert.True(coordinator.CanEnterFrontend);
    }

    [Fact]
    public void SkipAndRepeatedResultReadsDoNotRestartWorkers()
    {
        int assets = 0, frontend = 0;
        var product = new object();
        using var coordinator = new ClientStartupCoordinator<object, object>(
            _ => { Interlocked.Increment(ref assets); return product; },
            _ => { Interlocked.Increment(ref frontend); return new object(); },
            TestContext.Current.CancellationToken);
        coordinator.CompleteSplash();
        coordinator.CompleteSplash();
        Assert.True(SpinWait.SpinUntil(() => coordinator.CanEnterFrontend, Timeout));
        Assert.Same(product, coordinator.GetResults().Assets);
        Assert.Same(product, coordinator.GetResults().Assets);
        Assert.Equal(1, assets);
        Assert.Equal(1, frontend);
    }

    [Fact]
    public void FailureCancelsSiblingAndPreservesOriginalCause()
    {
        using var entered = new ManualResetEventSlim();
        var error = new InvalidDataException("invalid manifest");
        using var coordinator = new ClientStartupCoordinator<object, object>(
            _ => { Assert.True(entered.Wait(Timeout, TestContext.Current.CancellationToken)); throw error; },
            token => { entered.Set(); token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); return new object(); },
            TestContext.Current.CancellationToken);
        Assert.True(SpinWait.SpinUntil(() => coordinator.WorkersCompleted, Timeout));
        Assert.Same(error, Assert.Throws<InvalidDataException>(coordinator.ThrowIfFaulted));
        Assert.Same(error, Assert.Throws<InvalidDataException>(() => coordinator.CanEnterFrontend));
    }

    [Fact]
    public void DisposalCancelsAndJoinsBothWorkers()
    {
        using var entered = new CountdownEvent(2);
        int exited = 0;
        object Work(CancellationToken token)
        {
            entered.Signal();
            try { token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); return new object(); }
            finally { Interlocked.Increment(ref exited); }
        }
        var coordinator = new ClientStartupCoordinator<object, object>(Work, Work,
            TestContext.Current.CancellationToken);
        Assert.True(entered.Wait(Timeout, TestContext.Current.CancellationToken));
        coordinator.Dispose();
        coordinator.Dispose();
        Assert.True(coordinator.WorkersCompleted);
        Assert.Equal(2, exited);
    }
}
