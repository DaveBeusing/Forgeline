using ForgeLine.Audio;
using ForgeLine.Game;
using ForgeLine.Presentation;
using ForgeLine.Simulation;

namespace ForgeLine.Client;

/// <summary>Render-owner state. Decorative motion and one-shot cues never authorize a request.</summary>
internal sealed class UiInteractionFeedback
{
    private readonly UiMotionTimeline _motion = new();
    private FrontendSurfaceKind _kind;
    private string? _title, _error;
    private int _focus = -1;
    private float _width, _height;
    private bool _pressed, _pending, _seen, _cancelled;
    private SimulationSessionId _session;
    private SimulationTick _lastTick;
    private PlayerActionPanelMode _mode;
    private PlayerCommandFeedback _result;
    internal UiAudioCue Cue { get; private set; }

    internal FrontendSurfaceView Frontend(in FrontendSurfaceView view, TimeSpan now, bool active, bool software = false)
    {
        int focus = -1;
        for (int i = 0; i < view.MenuEntries.Count; i++) if (view.MenuEntries[i].IsEnabled && view.MenuEntries[i].IsFocused) { focus = i; break; }
        for (int i = 0; i < view.DetailLines.Count; i++) if (view.DetailLines[i].IsFocused || view.DetailLines[i].IsHovered) { focus = i + view.DetailScroll; break; }
        bool reveal = _kind != view.Kind || _title != view.Title;
        bool focused = focus != _focus;
        bool pressed = view.PrimaryPressed || view.SecondaryPressed;
        for (int i = 0; i < view.DetailLines.Count; i++)
        {
            var line = view.DetailLines[i];
            pressed |= line.IsPressed && (line.IsSelectable || line.CanDecrease || line.CanIncrease);
        }
        bool error = !string.IsNullOrEmpty(view.Feedback) && _error != view.Feedback;
        bool eligible = active && view.Kind is not (FrontendSurfaceKind.Loading or FrontendSurfaceKind.StudioSplash);
        bool resized = _width != (view.Layout?.Width ?? 0) || _height != (view.Layout?.Height ?? 0);
        Cue = !_seen || _cancelled || !eligible || resized ? UiAudioCue.None : error ? UiAudioCue.Rejected :
            pressed && !_pressed ? UiAudioCue.Intent : reveal || focused ? UiAudioCue.Navigation : UiAudioCue.None;
        if (!eligible || resized) _motion.Cancel();
        var motion = _motion.Update(now, reveal, focused, error, eligible && !software && view.FeedbackSettings.Animate(view.BackgroundQuality));
        _kind = view.Kind; _title = view.Title; _focus = focus; _pressed = pressed; _error = view.Feedback;
        _cancelled = false;
        _width = view.Layout?.Width ?? 0; _height = view.Layout?.Height ?? 0; _seen = true;
        return view with { Motion = motion, Transition = motion.Reveal };
    }

    internal PlayerActionPanelView Gameplay(in PlayerActionPanelView panel, PresentationSnapshot snapshot,
        TimeSpan now, bool active, UiFeedbackSettings settings, RenderQuality quality)
    {
        bool sessionChanged = !_seen || _cancelled || _session != snapshot.SessionId;
        if (sessionChanged) { _result = default; _lastTick = default; }
        bool coherent = snapshot.PlayerExperience is { } experience && experience.Tick == snapshot.Tick && snapshot.Tick.Value >= _lastTick.Value;
        var result = coherent ? snapshot.PlayerExperience!.Value.Feedback : default;
        bool eligible = active && snapshot.SessionId.IsSpecified && coherent && !snapshot.PlayerExperience!.Value.IsMatchComplete;
        bool reveal = panel.Mode != _mode;
        bool focused = panel.SelectedIndex != _focus;
        bool pending = panel.ContextualPending && panel.ContextualSessionId == snapshot.SessionId &&
            panel.ContextualActivationTick is { } activation && activation.Value <= snapshot.Tick.Value ||
            snapshot.PlayerActions is { } actions && actions.SessionId == snapshot.SessionId && actions.Tick == snapshot.Tick && actions.PendingCommandCount > 0;
        bool validResult = coherent && result.Kind != PlayerCommandFeedbackKind.None &&
            result.State is PlayerCommandFeedbackState.Accepted or PlayerCommandFeedbackState.Partial or PlayerCommandFeedbackState.Rejected &&
            result.ResolvedAtTick.Value <= snapshot.Tick.Value;
        bool changed = !sessionChanged && validResult && result != _result && result.ResolvedAtTick.Value >= _result.ResolvedAtTick.Value;
        Cue = !eligible || sessionChanged ? UiAudioCue.None : changed ?
            result.State == PlayerCommandFeedbackState.Accepted ? UiAudioCue.Accepted : UiAudioCue.Rejected :
            pending && !_pending ? UiAudioCue.Intent : reveal || focused ? UiAudioCue.Navigation : UiAudioCue.None;
        if (!eligible || sessionChanged) _motion.Cancel();
        var motion = _motion.Update(now, reveal, focused, changed, eligible && settings.Animate(quality));
        _cancelled = false;
        _session = snapshot.SessionId; _mode = panel.Mode; _focus = panel.SelectedIndex; _pending = pending;
        if (validResult && result.ResolvedAtTick.Value >= _result.ResolvedAtTick.Value) _result = result;
        if (coherent) _lastTick = snapshot.Tick;
        if (!coherent) _cancelled = true;
        _seen = true;
        return panel with { Motion = motion };
    }

    internal void Cancel() { _motion.Cancel(); Cue = UiAudioCue.None; _cancelled = true; }
}
