using System.Numerics;
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
        Vector2 position, in GameplayHudLayout layout, bool minimapEnabled, bool actionDockExpanded = true) =>
        layout.TopStatusBar.Contains(position) ||
        layout.SelectionInspector.Contains(position) ||
        (actionDockExpanded
            ? layout.ActionDock.Contains(position)
            : new HudRect(layout.ActionDock.X, layout.ActionDock.Y, layout.ActionDock.Width,
                MathF.Min(layout.ActionDock.Height, 59.0f * layout.Scale)).Contains(position)) ||
        (minimapEnabled && layout.Minimap.Contains(position));

    public void Reset()
    {
        _sessionId =
            SimulationSessionId.None;
        PointerCaptured = false;
        KeyboardCaptured = false;
    }
}
