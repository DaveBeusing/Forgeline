using System.Diagnostics;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ClientStartupNativeTests
{
    [Fact]
    public void BrandedFramePresentsWhileAssetWorkerIsBlockedAndMenuWaitsForHandoff()
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowConfiguration(
            "FORGELINE Startup Presentation Test", 640, 360));
        using var release = new ManualResetEventSlim();
        var diagnostics = new StartupDiagnostics(Stopwatch.GetTimestamp(), Guid.NewGuid(), 1);
        diagnostics.Begin(StartupPhase.BootRenderer);
        var target = new GraphicsWindowTarget(window.NativeHandle.Value,
            window.ClientSize.Width, window.ClientSize.Height, false);
        using var renderer = new ClientFrontendRenderHost(target, diagnostics,
            ClientStartupLoop.InitialSurface(true), asynchronousStartup: true);
        using var coordinator = new ClientStartupCoordinator<object, object>(
            token =>
            {
                release.Wait(token);
                diagnostics.Mark(StartupPhase.RuntimeAssetsReady);
                return new object();
            }, _ => new object(), TestContext.Current.CancellationToken);
        int presentedPumps = 0;
        var timeout = Stopwatch.StartNew();
        bool ready = ClientStartupLoop.Run(coordinator, true, string.Empty, diagnostics,
            pumpEvents: () =>
            {
                Assert.True(timeout.Elapsed < TimeSpan.FromSeconds(30), "Startup presentation timed out.");
                renderer.ThrowIfFaulted();
                bool open = platform.PumpEvents() && window.IsOpen;
                if (renderer.HasPresentedFrame && !release.IsSet && ++presentedPumps == 10)
                {
                    Assert.False(coordinator.DependenciesReady);
                    Assert.True(diagnostics.Has(StartupPhase.SplashBootstrapFirstFrame));
                    release.Set();
                }
                return open;
            },
            readInput: () => new SplashInputState(true),
            publish: view =>
            {
                renderer.UpdateSurface(window.ClientSize.Width, window.ClientSize.Height,
                    window.IsMinimized || window.ClientSize.IsEmpty);
                renderer.Publish(view);
            },
            waitForEvents: () => platform.WaitForEvents(TimeSpan.FromMilliseconds(16)),
            firstFramePresented: () => renderer.HasPresentedFrame,
            enableArtwork: _ => false,
            artworkFailed: () => renderer.SplashUnavailable);
        Assert.True(ready);
        Assert.Equal(10, presentedPumps);
        Assert.NotNull(coordinator.GetResults().Assets);
        Assert.False(diagnostics.Has(StartupPhase.MainMenuFirstFrame));
        StartupEvent[] events = diagnostics.Snapshot();
        Assert.True(events.Single(e => e.Phase == StartupPhase.SplashBootstrapFirstFrame).Timestamp <
            events.Single(e => e.Phase == StartupPhase.RuntimeAssetsReady).Timestamp);
        renderer.Publish(FrontendSurfaceView.MainMenu([]));
        while (!diagnostics.Has(StartupPhase.MainMenuFirstFrame))
        {
            Assert.True(timeout.Elapsed < TimeSpan.FromSeconds(30), "Menu presentation timed out.");
            renderer.ThrowIfFaulted();
            Assert.True(platform.PumpEvents());
            platform.WaitForEvents(TimeSpan.FromMilliseconds(16));
        }
        diagnostics.MenuInteractive();
        Assert.Equal(StartupReadiness.Ready, diagnostics.ApplicationReadiness);
        Assert.True(diagnostics.Has(StartupPhase.MainMenuInteractive));
    }
}
