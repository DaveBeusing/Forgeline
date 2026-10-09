using System.Numerics;

namespace ForgeLine.Presentation;

public sealed class RtsCommandFeedback
{
    private float _remainingSeconds;

    public bool IsVisible => _remainingSeconds > 0.0f;
    public Vector3 Position { get; private set; }
    public bool IsValid { get; private set; }
    public bool IsAttack { get; private set; }

    public void Show(Vector3 position, bool valid, bool attack = false)
    {
        Position = position;
        IsValid = valid;
        IsAttack = attack;
        _remainingSeconds = 0.8f;
    }

    public void Advance(TimeSpan elapsed)
    {
        if (elapsed > TimeSpan.Zero)
        {
            _remainingSeconds = MathF.Max(0.0f, _remainingSeconds - (float)elapsed.TotalSeconds);
        }
    }

    public void Clear() => _remainingSeconds = 0.0f;

    public void Draw(DebugDraw draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        if (!IsVisible)
        {
            return;
        }
        Vector4 color = IsAttack ? new(1.0f, 0.72f, 0.18f, 1.0f) : new(0.2f, 0.95f, 0.85f, 1.0f);
        RtsWorldMarkerVisualization.DrawTarget(draw, Position, IsValid, color, new(1.0f, 0.18f, 0.12f, 1.0f));
        if (IsValid && IsAttack)
        {
            draw.Circle(Position + Vector3.UnitY * 0.2f, 2.0f, color, 12);
        }
    }
}
