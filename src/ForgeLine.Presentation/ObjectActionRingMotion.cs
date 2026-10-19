namespace ForgeLine.Presentation;

public readonly record struct RingMotionViewport(int Width, int Height, uint Dpi, float UiScale);

/// <summary>One bounded alpha track. Geometry never scales or moves because of animation.</summary>
public sealed class ObjectActionRingMotion
{
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(160);
    private ObjectActionRingView _visual;
    private TimeSpan _last, _started;
    private bool _initialized, _open;
    private float _from, _opacity;
    private RingMotionViewport _viewport;
    public ObjectActionRingView Update(in ObjectActionRingView current, TimeSpan now, bool animate, RingMotionViewport viewport = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(now, TimeSpan.Zero);
        bool interrupted = !_initialized || now < _last || now - _last > TimeSpan.FromMilliseconds(250) || viewport != _viewport;
        bool open = current.Open && current.Geometry.IsValid;
        bool identityChanged = _visual.Entity != current.Entity || _visual.Session != current.Session || _visual.SelectionRevision != current.SelectionRevision;
        if (identityChanged)
        {
            _visual = current; _opacity = 0; _from = 0; _started = now;
        }
        if (!animate || interrupted || !current.UsesRing)
        {
            _opacity = open ? 1 : 0; _started = now; _from = _opacity;
        }
        else if (open != _open || identityChanged) { _from = _opacity; _started = now; }
        _initialized = true; _last = now; _open = open; _viewport = viewport;
        float eased = UiMotionTimeline.SmoothStep(now - _started, Duration);
        _opacity = _from + ((open ? 1 : 0) - _from) * eased;
        if (current.Open && (!current.Resolved || !current.Geometry.IsValid))
        { _opacity = 0; return current with { Opacity = 0, HoveredSlot = -1, Pressed = false }; }
        if (open) _visual = current;
        if (!open && (_opacity <= 0 || identityChanged || !current.UsesRing)) return current with { Opacity = 0 };
        return open ? current with { Opacity = _opacity } :
            _visual with { Open = false, Opacity = _opacity, HoveredSlot = -1, Pressed = false };
    }
}
