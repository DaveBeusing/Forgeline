using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

internal static class NotificationSurfaceVisuals
{
    internal const float BackgroundOpacity = .30f;
}

/// <summary>Decorative, bounded notification visibility. The controller always uses the live layout.</summary>
internal sealed class NotificationSurfaceMotion
{
    internal static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(180);
    private readonly Track[] _current = new Track[4];
    private readonly Track[] _retired = new Track[4];
    private readonly float[] _opacities = new float[4];
    private int _count, _retiredCount;
    private bool _initialized;
    private TimeSpan _last;
    private SimulationSessionId _session;
    private GameplayHudLayout _layout;

    internal float Opacity(int index) => index >= 0 && index < _count ? _opacities[index] : 0;
    internal int RetiredCount => _retiredCount;
    internal (HudRect Bounds, float Opacity) Retired(int index) =>
        index >= 0 && index < _retiredCount ? (_retired[index].Bounds, _retired[index].Opacity) : default;

    internal void Update(in NotificationStackLayout stack, SimulationSessionId session,
        in GameplayHudLayout layout, TimeSpan now, bool animate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(now, TimeSpan.Zero);
        bool settle = !_initialized || !animate || _session != session || _layout != layout ||
            now < _last || now - _last > TimeSpan.FromMilliseconds(250);
        _initialized = true; _last = now; _session = session; _layout = layout;
        if (settle)
        {
            _retiredCount = 0;
            _count = stack.Count;
            for (int i = 0; i < _count; i++)
            {
                var card = stack.Card(i);
                _current[i] = new(new(card.Item, card.Identity, card.Revision), card.Bounds, 1, 1, now);
                _opacities[i] = 1;
            }
            return;
        }

        Span<Track> previous = stackalloc Track[4];
        int previousCount = _count;
        _current.AsSpan(0, _count).CopyTo(previous);
        for (int j = 0; j < _retiredCount;)
        {
            var old = _retired[j];
            old = old with { Opacity = Interpolate(old.From, 0, now - old.Start) };
            if (old.Opacity <= 0) _retired[j] = _retired[--_retiredCount];
            else { _retired[j++] = old; }
        }

        _count = stack.Count;
        for (int i = 0; i < _count; i++)
        {
            var card = stack.Card(i);
            var key = new Key(card.Item, card.Identity, card.Revision);
            int match = -1;
            for (int j = 0; j < previousCount; j++)
                if (previous[j].Key == key) { match = j; break; }
            var track = match < 0 ? new Track(key, card.Bounds, 0, 0, now) :
                previous[match] with { Bounds = card.Bounds };
            track = track with { Opacity = Interpolate(track.From, 1, now - track.Start) };
            _current[i] = track;
            _opacities[i] = track.Opacity;
        }
        for (int j = 0; j < previousCount; j++)
        {
            var old = previous[j];
            bool current = false;
            for (int i = 0; i < _count; i++) if (_current[i].Key == old.Key) { current = true; break; }
            if (!current && old.Opacity > 0 && _retiredCount < _retired.Length)
                _retired[_retiredCount++] = old with { From = old.Opacity, Start = now };
        }
    }

    private static float Interpolate(float from, float to, TimeSpan elapsed) =>
        from + (to - from) * UiMotionTimeline.SmoothStep(elapsed, Duration);

    private readonly record struct Key(AlertNotification Item, AlertIdentity Identity, ulong Revision);
    private readonly record struct Track(Key Key, HudRect Bounds, float From, float Opacity, TimeSpan Start);
}
