using System.Numerics;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public sealed class HudInteractionContext
{
    private SimulationSessionId _sessionId;

    public SimulationSessionId SessionId =>
        _sessionId;

    public bool PointerCaptured { get; private set; }

    public bool KeyboardCaptured { get; private set; }

    public void BeginFrame(
        SimulationSessionId sessionId)
    {
        if (sessionId.IsSpecified &&
            sessionId != _sessionId)
        {
            _sessionId = sessionId;
        }

        PointerCaptured = false;
        KeyboardCaptured = false;
    }

    public void CapturePointer(
        bool captured = true)
    {
        PointerCaptured |= captured;
    }

    public void CaptureKeyboard(
        bool captured = true)
    {
        KeyboardCaptured |= captured;
    }

    public static bool HitTest(
        Vector2 position,
        in HudRect region) =>
        region.Contains(position);

    public static bool BlocksWorldPointer(
        Vector2 position, in GameplayHudLayout layout, bool minimapEnabled, bool actionDockExpanded = true, bool guidanceVisible = false, bool includeActionDock = true, OnboardingPresentation? onboarding = null, bool includePrimaryCommands = true) =>
        layout.TopStatusBar.Contains(position) ||
        (minimapEnabled && RtsMinimapInteractionLayout.IsInteractiveSurface(position, layout)) ||
        (onboarding?.Contains(position) ?? (guidanceVisible && GameplayGuidanceLayout.Resolve(layout).Contains(position)));

    public static bool CapturesWorldPointer(
        InputState input, in GameplayHudLayout layout, bool minimapEnabled, bool actionDockExpanded = true, bool guidanceVisible = false, bool includeActionDock = true, OnboardingPresentation? onboarding = null, bool includePrimaryCommands = true)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.HasPointerPosition) return false;
        // A sliding persistent hint cannot take a world-origin held or release gesture.
        bool movingGesture = input.IsMouseButtonDown(PlatformMouseButton.Left) || input.IsMouseButtonDown(PlatformMouseButton.Right) ||
            input.WasMouseButtonReleased(PlatformMouseButton.Left) || input.WasMouseButtonReleased(PlatformMouseButton.Right);
        if (BlocksWorldPointer(input.PointerPosition, layout, minimapEnabled, actionDockExpanded, guidanceVisible, includeActionDock,
            movingGesture && onboarding.HasValue ? default(OnboardingPresentation) : onboarding, includePrimaryCommands)) return true;
        ReadOnlySpan<PlatformMouseButton> buttons = [PlatformMouseButton.Left, PlatformMouseButton.Right];
        foreach (PlatformMouseButton button in buttons)
        {
            if (input.TryGetMousePressPosition(button, out Vector2 origin) &&
                BlocksWorldPointer(origin, layout, minimapEnabled, actionDockExpanded, guidanceVisible, includeActionDock, onboarding, includePrimaryCommands)) return true;
        }
        return false;
    }

    public void Reset()
    {
        _sessionId =
            SimulationSessionId.None;
        PointerCaptured = false;
        KeyboardCaptured = false;
    }
}
