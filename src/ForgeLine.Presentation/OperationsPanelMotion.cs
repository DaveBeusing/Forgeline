using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

internal static class OperationsPanelVisuals
{
    internal const float BackgroundOpacity = .30f;
}

/// <summary>Presentation-only panel alpha; input ownership stays with OperationsController.</summary>
internal sealed class OperationsPanelMotion
{
    internal static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(180);

    private GameplayHudLayout _layout;
    private SimulationSessionId _session;
    private TimeSpan _last;
    private TimeSpan _started;
    private float _from;
    private float _opacity;
    private bool _initialized;
    private bool _open;

    internal float Opacity => _opacity;

    internal float Update(bool open, SimulationSessionId session, in GameplayHudLayout layout,
        TimeSpan now, bool animate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(now, TimeSpan.Zero);

        bool interrupted = !_initialized || !animate || _session != session || _layout != layout ||
            now < _last || now - _last > TimeSpan.FromMilliseconds(250);
        if (interrupted)
        {
            _from = _opacity = open ? 1 : 0;
            _started = now;
        }
        else
        {
            if (open != _open)
            {
                _from = _opacity;
                _started = now;
            }
            _opacity = _from + ((open ? 1f : 0f) - _from) *
                UiMotionTimeline.SmoothStep(now - _started, Duration);
        }

        _initialized = true;
        _open = open;
        _layout = layout;
        _session = session;
        _last = now;
        return _opacity;
    }

    internal void Reset()
    {
        _initialized = _open = false;
        _layout = default;
        _session = default;
        _last = _started = default;
        _from = _opacity = 0;
    }
}
