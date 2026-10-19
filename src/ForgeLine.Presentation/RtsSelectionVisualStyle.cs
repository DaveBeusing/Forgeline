using System.Numerics;

namespace ForgeLine.Presentation;

/// <summary>Shared player-facing selection colors and physical-pixel marquee geometry.</summary>
public static class RtsSelectionVisualStyle
{
    public static readonly Vector4 Selected = new(0.25f, 1.0f, 0.35f, 1.0f);
    public static readonly Vector4 Hovered = new(0.15f, 0.85f, 1.0f, 1.0f);
    public static readonly Vector4 ForeignHover = new(1.0f, 0.45f, 0.20f, 1.0f);

    public const float MarqueeFillOpacity = 0.12f;
    public const float MarqueeBorderOpacity = 0.92f;
    public const float MarqueeBorderPixels = 1.25f;

    internal static Vector4 MarqueeFill => Selected with { W = MarqueeFillOpacity };
    internal static Vector4 MarqueeBorder => Selected with { W = MarqueeBorderOpacity };
}
