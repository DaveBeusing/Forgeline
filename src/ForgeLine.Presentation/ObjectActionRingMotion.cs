namespace ForgeLine.Presentation;

/// <summary>One bounded alpha track. Geometry never scales or moves because of animation.</summary>
public sealed class ObjectActionRingMotion
{
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(160);
    private ObjectActionRingView _visual;
    private TimeSpan _last, _started;
    private bool _initialized, _open;
    private float _from, _opacity;
    public ObjectActionRingView Update(in ObjectActionRingView current, TimeSpan now, bool animate)
    {
        bool interrupted = !_initialized || now < _last || now - _last > TimeSpan.FromMilliseconds(250);
        bool open = current.Open && current.Geometry.IsValid;
        bool identityChanged = _visual.Entity != current.Entity || _visual.Session != current.Session;
        if (!animate || interrupted || identityChanged || !current.SingleOwned)
        {
            _opacity = open ? 1 : 0; _started = now; _from = _opacity;
        }
        else if (open != _open) { _from = _opacity; _started = now; }
        _initialized = true; _last = now; _open = open;
        float eased = UiMotionTimeline.SmoothStep(now - _started, Duration);
        _opacity = _from + ((open ? 1 : 0) - _from) * eased;
        if (open) _visual = current;
        return open ? current with { Opacity = _opacity } :
            _visual with { Open = false, Opacity = _opacity, HoveredSlot = -1, Pressed = false };
    }
}
