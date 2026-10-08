using System.Diagnostics;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ClientSessionLoadingLoopTests
{
    private sealed class Session : IDisposable { public void Dispose() { } }

    [Fact]
    public void BlockedWorkerKeepsPumpingOnWindowOwnerAndPublishesActualTickProgress()
    {
        using var release = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        using var coordinator = new ClientSessionLoadingCoordinator<Session>((token, report) =>
        {
            report(new(ClientSessionLoadPhase.Replay, 256, 1024));
            entered.Set();
            release.Wait(token);
            return new Session();
        }, TestContext.Current.CancellationToken);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        int pumps = 0, owner = Environment.CurrentManagedThreadId;
        var surfaces = new List<FrontendSurfaceView>();
        Assert.True(ClientSessionLoadingLoop.Wait(coordinator,
            () => { Assert.Equal(owner, Environment.CurrentManagedThreadId); if (++pumps == 20) release.Set(); Assert.True(pumps < 5000); return true; },
            () => false, surface => surfaces.Add(surface), () => Thread.Sleep(1)));
        Assert.True(pumps >= 20);
        Assert.All(surfaces, surface => { Assert.True(surface.HasProgress); Assert.Equal(0.25f, surface.Progress); });
        Assert.Contains("256 / 1024", surfaces[0].Status);
        Assert.True(coordinator.TryTake(out Session? session));
        session!.Dispose();
    }

    [Fact]
    public void CancellationPumpsUntilWorkerCompletesAndCanRetry()
    {
        using var entered = new ManualResetEventSlim();
        using var coordinator = new ClientSessionLoadingCoordinator<Session>((token, report) =>
        {
            report(new(ClientSessionLoadPhase.ScenarioAssembly)); entered.Set();
            token.WaitHandle.WaitOne();
            Thread.Sleep(20);
            token.ThrowIfCancellationRequested();
            return new Session();
        }, TestContext.Current.CancellationToken);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        int pumps = 0;
        Assert.False(ClientSessionLoadingLoop.Wait(coordinator, () => { pumps++; Assert.True(pumps < 5000); return true; },
            () => pumps >= 5, _ => { }, () => Thread.Sleep(1)));
        Assert.True(pumps > 5);
        coordinator.Dispose();
        Assert.False(coordinator.WorkerAlive);
        using var retry = new ClientSessionLoadingCoordinator<Session>((_, _) => new Session(), TestContext.Current.CancellationToken);
        Assert.True(ClientSessionLoadingLoop.Wait(retry, () => true, () => false, _ => { }, () => Thread.Sleep(1)));
        Assert.True(retry.TryTake(out Session? session));
        session!.Dispose();
    }

    [Fact]
    public void WorkerFailureIsRecoverableAndDoesNotTransferAResult()
    {
        using var coordinator = new ClientSessionLoadingCoordinator<Session>((_, report) =>
        {
            report(new(ClientSessionLoadPhase.HashVerification));
            throw new InvalidDataException("Reconstructed hash differs.");
        }, TestContext.Current.CancellationToken);
        ClientSessionLoadingException error = Assert.Throws<ClientSessionLoadingException>(() =>
            ClientSessionLoadingLoop.Wait(coordinator, () => true, () => false, _ => { }, () => Thread.Sleep(1)));
        Assert.Equal(ClientSessionLoadError.StateVerification, error.Category);
        Assert.Equal("SAVED STATE DOES NOT MATCH", error.UserMessage);
        Assert.Equal("Reconstructed hash differs.", error.InnerException!.Message);
        Assert.False(coordinator.TryTake(out _));
    }

    [Fact]
    public void ActivationWaitsForActualPresentationAndCancellationWaitsForOwnerShutdown()
    {
        int pumps = 0, published = 0;
        Assert.True(ClientSessionLoadingLoop.WaitForPresentation(() => { pumps++; return true; },
            () => false, () => pumps >= 20, () => published++, () => { }, () => throw new InvalidOperationException(), () => false));
        Assert.Equal(20, pumps);
        Assert.Equal(19, published);
        pumps = 0; int stops = 0;
        Assert.False(ClientSessionLoadingLoop.WaitForPresentation(() => { pumps++; return true; },
            () => pumps >= 5, () => false, () => { }, () => { }, () => stops++, () => pumps >= 20));
        Assert.Equal(20, pumps);
        Assert.Equal(1, stops);
    }

    [Fact]
    public void RetryGetsNewDiagnosticCorrelationWithoutErasingEarlierFailure()
    {
        var diagnostics = new StartupDiagnostics(Stopwatch.GetTimestamp(), Guid.NewGuid(), 1);
        diagnostics.Mark(StartupPhase.MainMenuFirstFrame); diagnostics.MenuInteractive();
        diagnostics.BeginSession(); diagnostics.Begin(StartupPhase.SessionReconstruction, diagnostics.CurrentSessionId);
        diagnostics.SessionLoadEnded(false, "StateVerification");
        Assert.Equal(StartupReadiness.Ready, diagnostics.ApplicationReadiness);
        Assert.Equal(StartupReadiness.Failed, diagnostics.SessionReadiness);
        diagnostics.BeginSession();
        Assert.Equal(2, diagnostics.CurrentSessionId);
        Assert.Equal(StartupReadiness.Pending, diagnostics.SessionReadiness);
        Assert.False(diagnostics.Has(StartupPhase.SessionReady, 2));
        diagnostics.GameplayPresented();
        Assert.Equal(StartupReadiness.Ready, diagnostics.SessionReadiness);
        Assert.Contains(diagnostics.Snapshot(), e => e.Phase == StartupPhase.SessionLoadFailed && e.SessionId == 1);
        Assert.Contains(diagnostics.Snapshot(), e => e.Phase == StartupPhase.SessionReady && e.SessionId == 2);
    }
}
