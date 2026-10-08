using System.Numerics;

namespace ForgeLine.Presentation;

/// <summary>Presentation-only startup animation data.</summary>
public sealed record SplashDefinition(
    string Id,
    TimeSpan Duration,
    TimeSpan MinimumVisibleDuration,
    bool AllowSkip,
    IReadOnlyList<SplashLayerDefinition> Layers)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Duration <= TimeSpan.Zero ||
            MinimumVisibleDuration < TimeSpan.Zero || MinimumVisibleDuration > Duration ||
            Layers is null || Layers.Count == 0)
        {
            throw new ArgumentException("Invalid splash definition.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (SplashLayerDefinition layer in Layers)
        {
            if (string.IsNullOrWhiteSpace(layer.Id) ||
                string.IsNullOrWhiteSpace(layer.AssetId) ||
                !ids.Add(layer.Id) || layer.Keyframes is null ||
                layer.Keyframes.Count == 0 ||
                !float.IsFinite(layer.Anchor.X) ||
                !float.IsFinite(layer.Anchor.Y) ||
                layer.Anchor.X < 0f || layer.Anchor.X > 1f ||
                layer.Anchor.Y < 0f || layer.Anchor.Y > 1f ||
                !float.IsFinite(layer.NormalizedSize.X) ||
                !float.IsFinite(layer.NormalizedSize.Y) ||
                layer.NormalizedSize.X <= 0 || layer.NormalizedSize.Y <= 0)
            {
                throw new ArgumentException("Invalid splash layer.");
            }

            float previous = -1;
            foreach (SplashKeyframe frame in layer.Keyframes)
            {
                if (!float.IsFinite(frame.TimeSeconds) ||
                    frame.TimeSeconds < 0 ||
                    frame.TimeSeconds > Duration.TotalSeconds ||
                    frame.TimeSeconds < previous ||
                    !float.IsFinite(frame.Opacity) ||
                    frame.Opacity < 0 || frame.Opacity > 1 ||
                    !float.IsFinite(frame.Scale) || frame.Scale < 0 ||
                    !float.IsFinite(frame.Offset.X) || !float.IsFinite(frame.Offset.Y) ||
                    !float.IsFinite(frame.GlowIntensity) || frame.GlowIntensity < 0 ||
                    !Enum.IsDefined(frame.Easing))
                {
                    throw new ArgumentException("Invalid splash keyframe.");
                }

                previous = frame.TimeSeconds;
            }
        }
    }
}

public sealed record SplashLayerDefinition(
    string Id,
    string AssetId,
    int SortOrder,
    Vector2 Anchor,
    Vector2 NormalizedSize,
    IReadOnlyList<SplashKeyframe> Keyframes);

public enum SplashEasing
{
    Linear,
    EaseIn,
    EaseOut,
    EaseInOut
}

public readonly record struct SplashKeyframe(
    float TimeSeconds,
    float Opacity,
    float Scale,
    Vector2 Offset,
    float GlowIntensity,
    SplashEasing Easing = SplashEasing.Linear);

public readonly record struct SplashLayerState(
    float Opacity,
    float Scale,
    Vector2 Offset,
    float GlowIntensity);

public static class SplashTimelineEvaluator
{
    public static SplashLayerState Evaluate(
        ReadOnlySpan<SplashKeyframe> keyframes,
        float timeSeconds)
    {
        if (keyframes.IsEmpty || !float.IsFinite(timeSeconds))
        {
            throw new ArgumentException("Keyframes and finite time are required.");
        }

        if (timeSeconds < keyframes[0].TimeSeconds)
        {
            return State(keyframes[0]);
        }

        for (int i = 1; i < keyframes.Length; i++)
        {
            ref readonly SplashKeyframe right = ref keyframes[i];
            if (timeSeconds >= right.TimeSeconds &&
                i + 1 < keyframes.Length && keyframes[i + 1].TimeSeconds == right.TimeSeconds)
            {
                continue;
            }

            if (timeSeconds > right.TimeSeconds)
            {
                continue;
            }

            ref readonly SplashKeyframe left = ref keyframes[i - 1];
            float duration = right.TimeSeconds - left.TimeSeconds;
            if (duration <= 0)
            {
                return State(right);
            }

            float t = Math.Clamp((timeSeconds - left.TimeSeconds) / duration, 0, 1);
            t = right.Easing switch
            {
                SplashEasing.EaseIn => t * t,
                SplashEasing.EaseOut => 1 - (1 - t) * (1 - t),
                SplashEasing.EaseInOut => t * t * (3 - 2 * t),
                _ => t
            };

            return new SplashLayerState(
                Math.Clamp(left.Opacity + (right.Opacity - left.Opacity) * t, 0, 1),
                Math.Max(0, left.Scale + (right.Scale - left.Scale) * t),
                Vector2.Lerp(left.Offset, right.Offset, t),
                Math.Max(0, left.GlowIntensity + (right.GlowIntensity - left.GlowIntensity) * t));
        }

        return State(keyframes[^1]);
    }

    private static SplashLayerState State(in SplashKeyframe frame) =>
        new(frame.Opacity, frame.Scale, frame.Offset, frame.GlowIntensity);
}
