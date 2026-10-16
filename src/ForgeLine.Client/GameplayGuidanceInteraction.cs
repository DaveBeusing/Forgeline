using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Presentation;
using ForgeLine.Simulation;

namespace ForgeLine.Client;

internal sealed class GameplayGuidanceInteraction
{
    private SimulationSessionId _session;
    private GameplayHudLayout _layout;
    private TimeSpan _last, _started;
    private float _from, _collapse;
    private bool _held, _initialized, _focused, _moving;
    private ulong _press;
    public bool Hidden { get; private set; }
    public bool BlocksGameplayThisFrame { get; private set; }
    public bool ShortcutConsumedThisFrame { get; private set; }
    public OnboardingPresentation View { get; private set; }

    public void Update(InputState input, SimulationSessionId session, bool enabled,
        GameplayHudLayout layout = default, TimeSpan now = default, bool animate = false, bool blocked = false, bool keyboardFocusAvailable = true)
    {
        enabled &= session.IsSpecified;
        bool hadSession = _session.IsSpecified;
        bool replaced = session.IsSpecified && session != _session;
        bool changed = _initialized && _layout != layout;
        if (replaced) { _session = session; Hidden = false; _collapse = 0; _moving = false; _focused = false; }
        bool interrupted = !_initialized || replaced || changed || blocked || !enabled || input.FocusLostThisFrame ||
            now < _last || now - _last > TimeSpan.FromMilliseconds(250) || !animate;
        _initialized = true; _layout = layout; _last = now;
        if (interrupted) { _collapse = Hidden ? 1 : 0; _moving = false; }
        else if (_moving)
        {
            float e = UiMotionTimeline.SmoothStep(now - _started, OnboardingPresentation.Duration);
            _collapse = _from + ((Hidden ? 1 : 0) - _from) * e;
            if (e == 1) { _collapse = Hidden ? 1 : 0; _moving = false; }
        }
        bool down = input.IsKeyDown(PlatformKey.F12) &&
            (input.IsKeyDown(PlatformKey.LeftShift) || input.IsKeyDown(PlatformKey.RightShift));
        bool shortcut = down && !_held;
        _held = down;
        BlocksGameplayThisFrame = false; ShortcutConsumedThisFrame = false;
        View = OnboardingPresentation.Resolve(layout, _collapse, enabled && !blocked, _focused);
        ulong press = input.MousePressSequence(PlatformMouseButton.Left);
        bool fresh = press != _press; _press = press;
        if (!enabled || blocked || changed || (replaced && hadSession) || input.FocusLostThisFrame)
        { _focused = false; View = View with { Focused = false }; return; }
        System.Numerics.Vector2 origin = default;
        bool pointer = fresh && input.TryGetMousePressPosition(PlatformMouseButton.Left, out origin);
        bool click = pointer && View.Contains(origin);
        if (pointer) _focused = click;
        // Dock navigation retains Tab while a transient surface owns the keyboard.
        if (View.Visible && keyboardFocusAvailable && input.WasKeyPressed(PlatformKey.Tab))
        {
            _focused = !_focused;
            input.SuppressKeyUntilRelease(PlatformKey.Tab);
        }
        bool keyboard = keyboardFocusAvailable && _focused && (input.WasKeyPressed(PlatformKey.Enter) || input.WasKeyPressed(PlatformKey.Space));
        if (!keyboardFocusAvailable) _focused = false;
        if (shortcut || click || keyboard)
        {
            if (shortcut) _focused = true;
            Hidden = !Hidden; _from = _collapse; _started = now; _moving = animate;
            if (!animate) _collapse = Hidden ? 1 : 0;
            BlocksGameplayThisFrame = true; ShortcutConsumedThisFrame = shortcut;
            if (shortcut) input.SuppressHeldInput();
            if (click) input.SuppressMouseButtonUntilRelease(PlatformMouseButton.Left);
            if (keyboard)
            { input.SuppressKeyUntilRelease(PlatformKey.Enter); input.SuppressKeyUntilRelease(PlatformKey.Space); }
        }
        View = OnboardingPresentation.Resolve(layout, _collapse, enabled && !blocked, _focused);
        if (_moving && View.Visible) View = View with { Phase = Hidden ? OnboardingPhase.Collapsing : OnboardingPhase.Expanding };
    }
}
