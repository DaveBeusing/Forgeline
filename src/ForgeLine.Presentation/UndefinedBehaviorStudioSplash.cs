using System.Numerics;

namespace ForgeLine.Presentation;

/// <summary>Canonical studio presentation; timing remains data, not renderer logic.</summary>
public static class UndefinedBehaviorStudioSplash
{
    public static SplashDefinition Create() => new(
        "undefined_behavior_studios",
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(1.15),
        true,
        new SplashLayerDefinition[]
        {
            Layer("background", "branding.undefined_behavior.splash.background", 0, new Vector2(1, 1),
                Key(0, 1), Key(2.7f, 1), Key(3, 0)),
            Layer("fog", "branding.undefined_behavior.splash.fog", 10, new Vector2(1, 1),
                Key(0, 0), Key(0.65f, 0.32f), Key(2.7f, 0.22f), Key(3, 0)),
            Layer("separator", "branding.undefined_behavior.splash.separator", 15, new Vector2(0.07f, 0.28f),
                Key(0.4f, 0, 1.25f), Key(0.9f, 1), Key(1.2f, 0)),
            Layer("monogram", "branding.undefined_behavior.splash.ub_monogram", 20, new Vector2(0.22f, 0.34f),
                Key(0.8f, 0, 1.04f), Key(1.35f, 1), Key(2.7f, 1), Key(3, 0)),
            Layer("wordmark", "branding.undefined_behavior.splash.wordmark", 30, new Vector2(0.50f, 0.13f),
                Key(1.25f, 0, 1.02f), Key(2.05f, 1), Key(2.7f, 1), Key(3, 0)),
            Layer("subtitle", "branding.undefined_behavior.splash.subtitle", 40, new Vector2(0.44f, 0.06f),
                Key(1.95f, 0), Key(2.35f, 1), Key(2.7f, 1), Key(3, 0))
        });

    private static SplashLayerDefinition Layer(
        string id, string asset, int order, Vector2 size, params SplashKeyframe[] frames) =>
        new(id, asset, order, id switch
        {
            "separator" => new Vector2(0.5f, 0.39f),
            "monogram" => new Vector2(0.5f, 0.37f),
            "wordmark" => new Vector2(0.5f, 0.62f),
            "subtitle" => new Vector2(0.5f, 0.75f),
            _ => new Vector2(0.5f, 0.5f)
        }, size, frames);

    private static SplashKeyframe Key(float at, float opacity, float scale = 1) =>
        new(at, opacity, scale, Vector2.Zero, 0, SplashEasing.EaseInOut);
}
