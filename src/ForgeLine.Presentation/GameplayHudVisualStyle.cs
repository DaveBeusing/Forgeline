using System.Numerics;

namespace ForgeLine.Presentation;

internal enum HudStatePattern : byte
{
    None = 0,
    Underline = 1,
    LeftRail = 2,
    Outline = 3,
    DoubleRail = 4
}

internal readonly record struct HudStateVisual(
    Vector4 Fill,
    Vector4 Border,
    HudStatePattern Pattern);

internal static class GameplayHudVisualStyle
{
    public const float MinimumSafeMargin = 8.0f;
    public const float SafeMargin = 12.0f;
    public const float TopStatusBarHeight = 36.0f;
    public const float MinimapSize = 220.0f;
    public const float SelectionInspectorWidth = 360.0f;
    public const float SelectionInspectorHeight = 112.0f;
    public const float ActionDockWidth = 608.0f;
    public const float ActionDockHeight = 360.0f;
    public const float AlertStackHeight = 96.0f;
    public const float PanelPadding = 7.0f;
    public const float CompactPadding = 6.0f;
    public const float CompactGap = 6.0f;
    public const float SmallIconSize = 12.0f;
    public const float ActionIconSize = 13.0f;
    public const float PrimaryIconSize = 18.0f;
    public const float StateRailThickness = 3.0f;
    public const float BorderThickness = 1.0f;
    public const float ProgressBarThickness = 5.0f;

    public static readonly Vector4 PanelBackground =
        new(0.05f, 0.06f, 0.06f, 0.97f);

    public static readonly Vector4 PanelRaised =
        new(0.075f, 0.09f, 0.09f, 0.98f);

    public static readonly Vector4 PanelSelected =
        new(0.12f, 0.18f, 0.19f, 0.98f);

    public static readonly Vector4 PanelHovered =
        new(0.09f, 0.12f, 0.12f, 0.98f);

    public static readonly Vector4 PanelPressed =
        new(0.10f, 0.15f, 0.16f, 0.98f);

    public static readonly Vector4 PanelDisabled =
        new(0.055f, 0.06f, 0.06f, 0.88f);

    public static readonly Vector4 Border =
        new(0.32f, 0.35f, 0.34f, 0.90f);

    public static readonly Vector4 TextPrimary =
        new(0.93f, 0.96f, 0.95f, 1.0f);

    public static readonly Vector4 TextSecondary =
        new(0.65f, 0.70f, 0.69f, 1.0f);

    public static readonly Vector4 Focus =
        new(0.88f, 0.80f, 0.42f, 1.0f);

    public static readonly Vector4 Warning =
        new(0.94f, 0.66f, 0.24f, 1.0f);

    public static readonly Vector4 Critical =
        new(0.96f, 0.34f, 0.20f, 1.0f);

    public static float ResolveSafeMargin(float scale) =>
        MathF.Max(
            MinimumSafeMargin,
            SafeMargin * scale);

    public static HudStateVisual ResolveItemState(
        bool selected,
        bool hovered,
        bool pressed,
        bool enabled)
    {
        if (!enabled)
        {
            return new HudStateVisual(
                PanelDisabled,
                Border,
                HudStatePattern.DoubleRail);
        }

        if (pressed)
        {
            return new HudStateVisual(
                PanelPressed,
                Focus,
                HudStatePattern.Underline);
        }

        if (selected)
        {
            return new HudStateVisual(
                PanelSelected,
                Focus,
                HudStatePattern.LeftRail);
        }

        if (hovered)
        {
            return new HudStateVisual(
                PanelHovered,
                Border,
                HudStatePattern.Outline);
        }

        return new HudStateVisual(
            PanelRaised,
            Border,
            HudStatePattern.None);
    }
}
