using System.Numerics;

namespace ForgeLine.Presentation;

internal enum MinimapHoverPhase : byte
{
    Idle,
    Growing,
    Expanded,
    Shrinking
}

/// <summary>Presentation-only, monotonic minimap expansion with a fixed bottom-right anchor.</summary>
internal sealed class MinimapHoverMotion
{
    internal const float DesiredScale = 1.35f;
    internal static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan ClockStall = TimeSpan.FromMilliseconds(250);

    private HudRect _base;
    private TimeSpan _last;
    private TimeSpan _started;
    private float _from = 1;
    private float _value = 1;
    private float _target = 1;
    private bool _initialized;
    private bool _retained;

    internal MinimapHoverPhase Phase { get; private set; }
    internal HudRect Bounds { get; private set; }

    internal HudRect Update(in GameplayHudLayout layout, Vector2 pointer, bool hasPointer,
        TimeSpan now, bool active, bool animate, bool ownsGesture = false, HudRect ringBounds = default)
    {
        HudRect original = layout.Minimap;
        if (!active || original.IsEmpty || layout.SafeArea.IsEmpty || now < TimeSpan.Zero)
        {
            Reset();
            return original;
        }

        if (_initialized && (original != _base || now < _last || now - _last > ClockStall))
            Reset();

        if (!_initialized)
        {
            _initialized = true;
            _base = original;
            _started = now;
            _last = now;
            _from = _value = _target = 1;
        }

        float allowed = MaxScale(layout);
        HudRect visible = Expand(original, _value);
        bool hovering = ownsGesture ||
            (hasPointer && (_retained
                ? ContainsWithMargin(pointer, visible, 5 * layout.Scale)
                : RtsMinimapInteractionLayout.IsInteractiveSurface(pointer, layout)));
        _retained = hovering;
        float target = hovering ? allowed : 1;
        if (target != _target)
        {
            _from = _value;
            _target = target;
            _started = now;
        }

        if (!animate)
            _value = target;
        else
            _value = _from + (_target - _from) * UiMotionTimeline.SmoothStep(now - _started, Duration);

        _last = now;
        Bounds = Expand(original, _value);
        Phase = _value <= 1 && _target <= 1 ? MinimapHoverPhase.Idle
            : _value >= allowed && _target >= allowed ? MinimapHoverPhase.Expanded
            : _target > _value ? MinimapHoverPhase.Growing : MinimapHoverPhase.Shrinking;
        return Bounds;
    }

    internal void Reset()
    {
        _base = Bounds = default;
        _last = _started = TimeSpan.Zero;
        _from = _value = _target = 1;
        _initialized = _retained = false;
        Phase = MinimapHoverPhase.Idle;
    }

    private static float MaxScale(in GameplayHudLayout layout)
    {
        HudRect original = layout.Minimap;
        HudRect safe = layout.SafeArea;
        float limit = MathF.Min(DesiredScale, MathF.Min(
            (original.Right - safe.X) / original.Width,
            (original.Bottom - safe.Y) / original.Height));

        // Right-aligned upper surfaces cannot be covered by the growing minimap.
        float gap = layout.Gap;
        if (!layout.TopStatusBar.IsEmpty &&
            layout.TopStatusBar.Right > original.Right - original.Width * DesiredScale)
            limit = MathF.Min(limit, (original.Bottom - layout.TopStatusBar.Bottom - gap) / original.Height);
        if (!layout.AlertStack.IsEmpty &&
            layout.AlertStack.Right > original.Right - original.Width * DesiredScale)
            limit = MathF.Min(limit, (original.Bottom - layout.AlertStack.Bottom - gap) / original.Height);
        if (!layout.SecondaryView.IsEmpty &&
            layout.SecondaryView.Bottom > original.Bottom - original.Height * DesiredScale)
            limit = MathF.Min(limit, (original.Right - layout.SecondaryView.Right - gap) / original.Width);

        return Math.Clamp(float.IsFinite(limit) ? limit : 1, 1, DesiredScale);
    }

    private static HudRect Expand(in HudRect original, float scale)
    {
        float width = original.Width * scale;
        float height = original.Height * scale;
        return new HudRect(original.Right - width, original.Bottom - height, width, height);
    }

    private static bool ContainsWithMargin(Vector2 point, in HudRect bounds, float margin) =>
        point.X >= bounds.X - margin && point.X <= bounds.Right + margin &&
        point.Y >= bounds.Y - margin && point.Y <= bounds.Bottom + margin;
}
