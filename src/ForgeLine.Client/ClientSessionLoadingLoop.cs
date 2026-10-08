using ForgeLine.Presentation;

namespace ForgeLine.Client;

internal static class ClientSessionLoadingLoop
{
    // Cancellation keeps pumping until both owners finish, before disposal joins them.
    internal static bool WaitForPresentation(Func<bool> pumpEvents, Func<bool> cancelRequested,
        Func<bool> firstFramePresented, Action publish, Action waitForEvents,
        Action requestStop, Func<bool> ownersStopped)
    {
        bool cancelling = false;
        while (true)
        {
            if (!pumpEvents()) { requestStop(); return false; }
            if (!cancelling && cancelRequested()) { cancelling = true; requestStop(); }
            if (cancelling)
            {
                if (ownersStopped()) return false;
            }
            else
            {
                if (firstFramePresented()) return true;
                publish();
            }
            waitForEvents();
        }
    }

    internal static FrontendSurfaceView Surface(ClientSessionLoadProgress progress, bool cancelling = false)
    {
        string label = cancelling ? "CANCELLING SESSION LOAD" : progress.Phase switch
        {
            ClientSessionLoadPhase.Configuration => "VALIDATING SESSION CONFIGURATION",
            ClientSessionLoadPhase.ScenarioAssembly => "ASSEMBLING BATTLEFIELD",
            ClientSessionLoadPhase.Replay => $"RESTORING TICK {progress.CompletedTicks} / {progress.TotalTicks}",
            ClientSessionLoadPhase.HashVerification => "VERIFYING AUTHORITATIVE STATE",
            ClientSessionLoadPhase.PresentationBinding => "BINDING PRESENTATION STATE",
            ClientSessionLoadPhase.RendererReadiness => "PREPARING GAMEPLAY PRESENTATION",
            _ => "PREPARING SESSION"
        };
        return FrontendSurfaceView.Loading(label, !cancelling && progress.HasProgress, progress.Progress)
            with { Feedback = "ESC  RETURN TO MENU" };
    }

    internal static bool Wait<TSession>(ClientSessionLoadingCoordinator<TSession> coordinator,
        Func<bool> pumpEvents, Func<bool> cancelRequested, Action<FrontendSurfaceView> publish,
        Action waitForEvents) where TSession : class, IDisposable
    {
        while (true)
        {
            if (!pumpEvents()) { coordinator.Cancel(); return false; }
            if (cancelRequested()) coordinator.Cancel();
            publish(Surface(coordinator.Progress, coordinator.CancellationRequested));
            if (coordinator.Completed)
            {
                if (coordinator.Failure is ClientSessionLoadingException error) throw error;
                return !coordinator.CancellationRequested;
            }
            waitForEvents();
        }
    }
}
