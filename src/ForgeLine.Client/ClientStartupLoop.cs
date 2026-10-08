using System.Diagnostics;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

internal static class ClientStartupLoop
{
    internal static FrontendSurfaceView InitialSurface(bool showSplash) =>
        showSplash ? FrontendSurfaceView.StudioSplashBootstrap() :
            FrontendSurfaceView.Loading("LOADING RUNTIME ASSETS", false, 0);

    // All callbacks run on the platform owner; only the coordinator executes background work.
    internal static bool Run<TAssets, TFrontend>(
        ClientStartupCoordinator<TAssets, TFrontend> coordinator,
        bool showSplash,
        string bypassReason,
        StartupDiagnostics diagnostics,
        Func<bool> pumpEvents,
        Func<SplashInputState> readInput,
        Action<FrontendSurfaceView> publish,
        Action waitForEvents,
        Func<bool> firstFramePresented,
        Func<TAssets, bool> enableArtwork,
        Func<bool> artworkFailed,
        Func<TimeSpan>? elapsedProvider = null)
        where TAssets : class
        where TFrontend : class
    {
        var timer = Stopwatch.StartNew();
        elapsedProvider ??= () => timer.Elapsed;
        var controller = new SplashScreenController(UndefinedBehaviorStudioSplash.Create());
        controller.Start();
        bool timingStarted = false;
        bool artworkAttempted = false;
        bool useArtwork = false;
        bool artworkFailureReported = false;
        TimeSpan previous = TimeSpan.Zero;
        diagnostics.Begin(StartupPhase.StudioSplash);
        if (!showSplash)
        {
            coordinator.CompleteSplash();
            diagnostics.End(StartupPhase.StudioSplash, StartupEventKind.Skipped, detail: bypassReason);
        }
        publish(InitialSurface(showSplash));
        try
        {
            while (true)
            {
                if (!pumpEvents())
                {
                    coordinator.Cancel();
                    diagnostics.End(StartupPhase.StudioSplash, StartupEventKind.Cancelled);
                    return false;
                }
                coordinator.ThrowIfFaulted();
                bool presented = firstFramePresented();
                TimeSpan now = elapsedProvider();
                if (showSplash && !coordinator.SplashCompletedOrSkipped)
                {
                    TimeSpan delta = timingStarted ? now - previous : TimeSpan.Zero;
                    timingStarted |= presented;
                    previous = now;
                    controller.Update(delta, readInput());
                    if (controller.IsComplete)
                    {
                        coordinator.CompleteSplash();
                        diagnostics.End(StartupPhase.StudioSplash,
                            controller.WasSkipped ? StartupEventKind.Skipped : StartupEventKind.Completed);
                    }
                    else if (presented && !artworkAttempted && coordinator.TryGetAssets(out TAssets? assets))
                    {
                        artworkAttempted = true;
                        diagnostics.Begin(StartupPhase.SplashArtwork);
                        useArtwork = enableArtwork(assets);
                        diagnostics.End(StartupPhase.SplashArtwork, useArtwork
                            ? StartupEventKind.Completed : StartupEventKind.Skipped,
                            detail: useArtwork ? null : "TextFallback");
                    }
                    if (useArtwork && artworkFailed())
                    {
                        useArtwork = false;
                        if (!artworkFailureReported)
                        {
                            artworkFailureReported = true;
                            diagnostics.Begin(StartupPhase.SplashArtwork);
                            diagnostics.End(StartupPhase.SplashArtwork, StartupEventKind.Failed,
                                detail: "RendererUnavailable");
                        }
                    }
                }
                if (coordinator.DependenciesReady)
                    diagnostics.Mark(StartupPhase.FrontendDependenciesReady);
                if (coordinator.CanEnterFrontend && presented) return true;

                FrontendSurfaceView view = showSplash && !coordinator.SplashCompletedOrSkipped
                    ? useArtwork ? FrontendSurfaceView.StudioSplash((float)controller.Elapsed.TotalSeconds,
                        controller.MasterOpacity)
                    : FrontendSurfaceView.StudioSplashBootstrap((float)controller.Elapsed.TotalSeconds,
                        controller.MasterOpacity)
                    : FrontendSurfaceView.Loading("PREPARING COMMAND INTERFACE", false, 0);
                publish(view);
                waitForEvents();
            }
        }
        catch (Exception error)
        {
            coordinator.Cancel();
            diagnostics.End(StartupPhase.StudioSplash, error is OperationCanceledException
                ? StartupEventKind.Cancelled : StartupEventKind.Failed, detail: error.GetType().Name);
            throw;
        }
        finally { controller.Stop(); }
    }
}
