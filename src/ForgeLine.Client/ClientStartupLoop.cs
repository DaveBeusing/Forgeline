using System.Diagnostics;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

internal static class ClientStartupLoop
{
    internal static string? SplashBypassReason(
        bool smokeTest, bool skipSplash, bool showStudioSplash, bool applicationRestart) =>
        applicationRestart ? "ApplicationRestart" :
        smokeTest ? "SmokeMode" :
        skipSplash ? "CommandLine" :
        !showStudioSplash ? "Settings" : null;

    internal static FrontendSurfaceView InitialSurface(bool showSplash) =>
        showSplash ? FrontendSurfaceView.StudioSplash(0, 1) :
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
        Func<bool> videoUnavailable,
        Func<TimeSpan>? elapsedProvider = null,
        Func<bool>? splashFramePresented = null)
        where TAssets : class
        where TFrontend : class
    {
        var timer = Stopwatch.StartNew();
        elapsedProvider ??= () => timer.Elapsed;
        splashFramePresented ??= firstFramePresented;
        var controller = new SplashScreenController(UndefinedBehaviorStudioSplash.Create());
        controller.Start();
        bool timingStarted = false;
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
                    timingStarted |= splashFramePresented();
                    previous = now;
                    controller.Update(delta, readInput());
                    if (controller.IsComplete)
                    {
                        coordinator.CompleteSplash();
                        diagnostics.End(StartupPhase.StudioSplash,
                            controller.WasSkipped ? StartupEventKind.Skipped : StartupEventKind.Completed);
                    }
                    else if (videoUnavailable())
                    {
                        coordinator.CompleteSplash();
                        diagnostics.End(StartupPhase.StudioSplash, StartupEventKind.Skipped,
                            detail: "VideoUnavailable");
                    }
                }
                if (coordinator.DependenciesReady)
                    diagnostics.Mark(StartupPhase.FrontendDependenciesReady);
                if (coordinator.CanEnterFrontend && presented) return true;

                FrontendSurfaceView view = showSplash && !coordinator.SplashCompletedOrSkipped
                    ? FrontendSurfaceView.StudioSplash((float)controller.Elapsed.TotalSeconds,
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
