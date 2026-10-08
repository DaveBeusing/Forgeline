using System.Diagnostics;
using ForgeLine.Assets;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ClientStartupLoopTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static StartupDiagnostics Diagnostics() =>
        new(Stopwatch.GetTimestamp(), Guid.NewGuid(), 1);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SlowLoadingKeepsPumpingAndPublishingBeforeDependenciesComplete(bool showSplash)
    {
        using var release = new ManualResetEventSlim();
        var diagnostics = Diagnostics();
        int assetLoads = 0, saveLoads = 0, pumps = 0, owner = Environment.CurrentManagedThreadId;
        using var coordinator = new ClientStartupCoordinator<object, object>(
            token => { Interlocked.Increment(ref assetLoads); release.Wait(token); return new object(); },
            token => { Interlocked.Increment(ref saveLoads); release.Wait(token); return new object(); },
            TestContext.Current.CancellationToken);
        var views = new List<FrontendSurfaceView>();
        TimeSpan elapsed = TimeSpan.Zero;
        bool result = ClientStartupLoop.Run(coordinator, showSplash, "Settings", diagnostics,
            pumpEvents: () =>
            {
                Assert.Equal(owner, Environment.CurrentManagedThreadId);
                if (++pumps == 30)
                {
                    Assert.False(coordinator.DependenciesReady);
                    Assert.NotEmpty(views);
                    release.Set();
                }
                Assert.True(pumps < 5000, "Startup did not complete after dependencies were released.");
                return true;
            },
            readInput: () => new SplashInputState(true),
            publish: view => { Assert.Equal(owner, Environment.CurrentManagedThreadId); views.Add(view); },
            waitForEvents: () => { elapsed += TimeSpan.FromMilliseconds(100); Thread.Sleep(1); },
            firstFramePresented: () => pumps >= 2,
            enableArtwork: _ => false,
            artworkFailed: () => false,
            elapsedProvider: () => elapsed);
        Assert.True(result);
        Assert.True(pumps >= 30);
        Assert.Equal(1, assetLoads);
        Assert.Equal(1, saveLoads);
        Assert.All(views, view => Assert.False(view.HasProgress));
        Assert.Equal(showSplash, views[0].SplashBootstrap);
        Assert.Contains(views, view => view.Kind == FrontendSurfaceKind.Loading);
        Assert.True(diagnostics.Has(StartupPhase.FrontendDependenciesReady));
    }

    [Fact]
    public void CompletedProductsCannotBypassAnUnpresentedBootSurface()
    {
        using var coordinator = new ClientStartupCoordinator<object, object>(
            _ => new object(), _ => new object(), TestContext.Current.CancellationToken);
        Assert.True(SpinWait.SpinUntil(() => coordinator.DependenciesReady, Timeout));
        int pumps = 0;
        bool result = ClientStartupLoop.Run(coordinator, false, "SmokeMode", Diagnostics(),
            () => { pumps++; return true; }, () => default, _ => { }, () => { },
            () => pumps >= 10, _ => false, () => false);
        Assert.True(result);
        Assert.Equal(10, pumps);
    }

    [Fact]
    public void ArtworkFailureReturnsToBootstrapWithoutBlockingReadiness()
    {
        using var coordinator = new ClientStartupCoordinator<object, object>(
            _ => new object(), _ => new object(), TestContext.Current.CancellationToken);
        Assert.True(SpinWait.SpinUntil(() => coordinator.DependenciesReady, Timeout));
        var diagnostics = Diagnostics();
        var views = new List<FrontendSurfaceView>();
        TimeSpan elapsed = TimeSpan.Zero;
        int artworkLoads = 0, pumps = 0;
        bool result = ClientStartupLoop.Run(coordinator, true, string.Empty, diagnostics,
            () => { Assert.True(++pumps < 100); return true; },
            () => new SplashInputState(true), views.Add,
            () => elapsed += TimeSpan.FromMilliseconds(100), () => true,
            _ => { artworkLoads++; return true; }, () => true, () => elapsed);
        Assert.True(result);
        Assert.Equal(1, artworkLoads);
        Assert.Contains(views, view => view.SplashBootstrap);
        Assert.Contains(diagnostics.Snapshot(), e => e.Phase == StartupPhase.SplashArtwork &&
            e.Kind == StartupEventKind.Failed);
        Assert.False(diagnostics.Has(StartupPhase.ApplicationReady));
    }

    [Fact]
    public void WindowCloseCancelsTheLoopAndJoinsStartupWorkers()
    {
        using var entered = new CountdownEvent(2);
        object Work(CancellationToken token)
        {
            entered.Signal();
            token.WaitHandle.WaitOne();
            token.ThrowIfCancellationRequested();
            return new object();
        }
        var coordinator = new ClientStartupCoordinator<object, object>(Work, Work,
            TestContext.Current.CancellationToken);
        var diagnostics = Diagnostics();
        Assert.True(entered.Wait(Timeout, TestContext.Current.CancellationToken));
        bool result = ClientStartupLoop.Run(coordinator, true, string.Empty, diagnostics,
            () => false, () => default, _ => { }, () => { }, () => false, _ => false, () => false);
        coordinator.Dispose();
        Assert.False(result);
        Assert.True(coordinator.WorkersCompleted);
        Assert.False(diagnostics.Has(StartupPhase.FrontendDependenciesReady));
        Assert.Contains(diagnostics.Snapshot(), e => e.Phase == StartupPhase.StudioSplash &&
            e.Kind == StartupEventKind.Cancelled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrInvalidManifestFailsWithoutFalseReadiness(bool createInvalidManifest)
    {
        string directory = Path.Combine(Path.GetTempPath(), "ForgeLine.Startup.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            if (createInvalidManifest) File.WriteAllText(Path.Combine(directory, RuntimeAssetCatalog.ManifestFileName), "{");
            using var coordinator = new ClientStartupCoordinator<RuntimeAssetCatalog, object>(
                token => RuntimeAssetCatalog.LoadCancellable(directory, token), _ => new object(),
                TestContext.Current.CancellationToken);
            var diagnostics = Diagnostics();
            Exception? error = Record.Exception(() => ClientStartupLoop.Run(coordinator, false, "Settings", diagnostics,
                () => true, () => default, _ => { }, () => Thread.Sleep(1),
                () => true, _ => false, () => false));
            if (createInvalidManifest) Assert.IsAssignableFrom<System.Text.Json.JsonException>(error);
            else Assert.IsType<FileNotFoundException>(error);
            Assert.False(diagnostics.Has(StartupPhase.FrontendDependenciesReady));
            Assert.False(diagnostics.Has(StartupPhase.ApplicationReady));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void CancellingDevelopmentBootstrapTerminatesItsCompilerProcess()
    {
        var info = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe"),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add("Start-Sleep -Seconds 30");
        using Process compiler = Process.Start(info)!;
        try
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() =>
                RuntimeAssetDevelopmentBootstrap.WaitForCompiler(compiler, cancellation.Token));
            Assert.True(compiler.HasExited);
        }
        finally
        {
            if (!compiler.HasExited) { compiler.Kill(entireProcessTree: true); compiler.WaitForExit(); }
        }
    }

    [Fact]
    public void CancelledStartupDoesNotLeakResultsIntoRestart()
    {
        object Work(CancellationToken token)
        {
            token.WaitHandle.WaitOne();
            token.ThrowIfCancellationRequested();
            return new object();
        }
        var cancelled = new ClientStartupCoordinator<object, object>(Work, Work,
            TestContext.Current.CancellationToken);
        cancelled.Dispose();
        Assert.True(cancelled.WorkersCompleted);
        var newProduct = new object();
        using var restarted = new ClientStartupCoordinator<object, object>(
            _ => newProduct, _ => new object(), TestContext.Current.CancellationToken);
        restarted.CompleteSplash();
        Assert.True(SpinWait.SpinUntil(() => restarted.CanEnterFrontend, Timeout));
        Assert.Same(newProduct, restarted.GetResults().Assets);
    }
}
