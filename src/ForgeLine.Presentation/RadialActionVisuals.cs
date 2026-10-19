using System.Numerics;

namespace ForgeLine.Presentation;

/// <summary>Radial panel material alpha is independent of text, icons and the presentation fade.</summary>
internal static class RadialActionVisuals
{
    internal const float BackgroundOpacity = .30f;

    internal static Vector4 Background(bool enabled, bool hovered, bool pressed, bool pending, float visibility)
    {
        Vector4 color = !enabled ? GameplayHudVisualStyle.PanelDisabled :
            pending ? GameplayHudVisualStyle.PanelSelected :
            pressed ? GameplayHudVisualStyle.PanelPressed :
            hovered ? GameplayHudVisualStyle.PanelHovered :
            GameplayHudVisualStyle.PanelRaised;
        // The native overlay pipeline uses straight source alpha. Do not multiply by the
        // token's material alpha as well, or composite overlapping sector fills.
        return color with { W = BackgroundOpacity * Math.Clamp(visibility, 0, 1) };
    }
}
