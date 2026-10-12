using System.Diagnostics;
using System.Runtime.InteropServices;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ClientStartupNativeTests
{
    [Fact]
    public void EscapeCancelsNativeBootstrapAndPendingEventsAreDrained()
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateBootstrapWindow(new WindowConfiguration("FORGELINE", 640, 360));
        Assert.True(ClientBootstrap.PumpBootstrap(platform, window));
        Assert.False(window.TryDequeueEvent(out _));
        _ = SendMessage(window.NativeHandle.Value, 0x0100, 0x1B, 0);
        Assert.False(ClientBootstrap.PumpBootstrap(platform, window));
        Assert.False(window.IsOpen);
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nuint wParam, nint lParam);

    [Fact]
    public void BrandedFramePresentsWhileAssetWorkerIsBlockedAndMenuWaitsForHandoff()
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateBootstrapWindow(new WindowConfiguration(
            "FORGELINE Startup Presentation Test", 640, 360));
        Assert.True(platform.BootstrapPainted(window));
        nint bootstrapHandle = window.NativeHandle.Value;
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
                    platform.CompleteBootstrap(window);
                    Assert.Equal(bootstrapHandle, window.NativeHandle.Value);
                    Assert.False(coordinator.DependenciesReady);
                    Assert.True(diagnostics.Has(StartupPhase.SplashBootstrapFirstFrame));
                    release.Set();
                }
                return open;
            },
            readInput: () => new SplashInputState(diagnostics.Has(StartupPhase.StudioSplashFirstFrame)),
            publish: view =>
            {
                renderer.UpdateSurface(window.ClientSize.Width, window.ClientSize.Height,
                    window.IsMinimized || window.ClientSize.IsEmpty);
                Assert.False(view.SplashBootstrap);
                renderer.Publish(view);
            },
            waitForEvents: () => platform.WaitForEvents(TimeSpan.FromMilliseconds(16)),
            firstFramePresented: () => renderer.HasPresentedFrame,
            videoUnavailable: () => renderer.SplashUnavailable,
            splashFramePresented: () => renderer.HasPresentedSplashFrame);
        Assert.True(ready);
        Assert.True(diagnostics.Has(StartupPhase.StudioSplashFirstFrame));
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
