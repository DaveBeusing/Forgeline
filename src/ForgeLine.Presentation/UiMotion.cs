namespace ForgeLine.Presentation;

public readonly record struct UiFeedbackSettings(bool ReducedMotion = false, bool SoundEnabled = false, float SoundVolume = .35f)
{
    public void Validate()
    {
        if (!float.IsFinite(SoundVolume) || SoundVolume is < 0 or > 1)
            throw new InvalidDataException("UI sound volume must be finite and between zero and one.");
    }
    public bool Animate(RenderQuality quality) => !ReducedMotion && quality != RenderQuality.Low;
}

public readonly record struct UiMotionView(float Reveal, float Focus, float Result)
{
    public static UiMotionView Settled => new(1, 1, 0);
}

/// <summary>Bounded decoration only. Callers supply monotonic presentation time, never simulation time.</summary>
public sealed class UiMotionTimeline
{
    public static readonly TimeSpan RevealDuration = TimeSpan.FromMilliseconds(140);
    public static readonly TimeSpan FocusDuration = TimeSpan.FromMilliseconds(80);
    public static readonly TimeSpan ResultDuration = TimeSpan.FromMilliseconds(220);
    private TimeSpan _last, _reveal, _focus, _result;
    private bool _initialized, _hasResult;

    public UiMotionView Update(TimeSpan now, bool revealChanged, bool focusChanged, bool resultChanged, bool animate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(now, TimeSpan.Zero);
        bool interrupted = !_initialized || now < _last || now - _last > TimeSpan.FromMilliseconds(250);
        _initialized = true;
        _last = now;
        if (!animate || interrupted)
        {
            _reveal = now - RevealDuration;
            _focus = now - FocusDuration;
            _hasResult = false;
            return UiMotionView.Settled;
        }
        if (revealChanged) _reveal = now;
        if (focusChanged) _focus = now;
        if (resultChanged) { _result = now; _hasResult = true; }
        float result = _hasResult ? 1 - Ease(now - _result, ResultDuration) : 0;
        if (result <= 0) _hasResult = false;
        return new(Ease(now - _reveal, RevealDuration), Ease(now - _focus, FocusDuration), result);
    }

    public void Cancel() { _initialized = false; _hasResult = false; }

    private static float Ease(TimeSpan elapsed, TimeSpan duration)
    {
        float t = (float)Math.Clamp(elapsed.TotalSeconds / duration.TotalSeconds, 0, 1);
        return 1 - (1 - t) * (1 - t) * (1 - t);
    }
}
