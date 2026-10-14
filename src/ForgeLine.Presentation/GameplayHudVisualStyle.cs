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

    // Content hierarchy is local to each named HUD region; command-dock metrics stay independent.
    public const float CaptionTextSize = 9.0f;
    public const float ValueTextSize = 14.0f;
    public const float BodyTextSize = 12.0f;
    public static readonly Vector4 PanelHeader = ForgeLineUiPalette.BackgroundRaised;
    public static readonly Vector4 QuietBorder = ForgeLineUiPalette.BorderSubtle;

    public static readonly Vector4 PanelBackground =
        ForgeLineUiPalette.Surface;

    public static readonly Vector4 PanelRaised =
        ForgeLineUiPalette.SurfaceRaised;

    public static readonly Vector4 PanelSelected =
        ForgeLineUiPalette.SurfaceActive;

    public static readonly Vector4 PanelHovered =
        ForgeLineUiPalette.SurfaceHover;

    public static readonly Vector4 PanelPressed =
        ForgeLineUiPalette.SurfacePressed;

    public static readonly Vector4 PanelDisabled =
        ForgeLineUiPalette.SurfaceDisabled;

    public static readonly Vector4 Border =
        ForgeLineUiPalette.Border;

    public static readonly Vector4 TextPrimary =
        ForgeLineUiPalette.TextPrimary;

    public static readonly Vector4 TextSecondary =
        ForgeLineUiPalette.TextSecondary;

    public static readonly Vector4 Focus =
        ForgeLineUiPalette.Accent;

    public static readonly Vector4 Warning =
        ForgeLineUiPalette.Warning;

    public static readonly Vector4 Critical =
        ForgeLineUiPalette.Critical;

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
