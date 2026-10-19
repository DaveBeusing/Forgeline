using System.Numerics;

namespace ForgeLine.Presentation;

/// <summary>Radial panel material alpha is independent of text, icons and the presentation fade.</summary>
internal static class RadialActionVisuals
{
    internal const float BackgroundOpacity = .30f;

    internal static Vector4 HoverArc(bool enabled, bool pressed, bool pending, float visibility)
    {
        Vector4 color = !enabled ? GameplayHudVisualStyle.TextSecondary :
            pending ? GameplayHudVisualStyle.Warning :
            pressed ? GameplayHudVisualStyle.Focus :
            GameplayHudVisualStyle.TextPrimary;
        return color with { W = .95f * Math.Clamp(visibility, 0, 1) };
    }

    internal static float HoverArcWidth(float scale) => Math.Clamp(2.25f * scale, 1.5f, 4);

    internal static Vector4 Background(bool enabled, bool hovered, bool pressed, bool pending, float visibility)
    {
        Vector4 color = !enabled ? GameplayHudVisualStyle.PanelDisabled :
            pending ? GameplayHudVisualStyle.PanelSelected :
            pressed ? GameplayHudVisualStyle.PanelPressed :
            hovered ? GameplayHudVisualStyle.PanelHovered :
            GameplayHudVisualStyle.PanelRaised;
        // The native overlay pipeline uses straight source alpha. Do not multiply by the
        // token's material alpha as well, or composite overlapping sector fills.
        if (hovered)
            color = new Vector4(color.X * .37f, color.Y * .37f, color.Z * .37f, color.W);
        return color with { W = BackgroundOpacity * Math.Clamp(visibility, 0, 1) };
    }
}
