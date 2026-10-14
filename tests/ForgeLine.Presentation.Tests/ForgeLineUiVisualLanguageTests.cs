using System.Numerics;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class ForgeLineUiVisualLanguageTests
{
    [Fact]
    public void FrontendAndHudUseOneCommandPalette()
    {
        Assert.Equal(ForgeLineUiPalette.Surface, GameplayHudVisualStyle.PanelBackground);
        Assert.Equal(ForgeLineUiPalette.SurfaceRaised, GameplayHudVisualStyle.PanelRaised);
        Assert.Equal(ForgeLineUiPalette.SurfaceActive, GameplayHudVisualStyle.PanelSelected);
        Assert.Equal(ForgeLineUiPalette.Accent, GameplayHudVisualStyle.Focus);
        Assert.Equal(ForgeLineUiPalette.TextPrimary, GameplayHudVisualStyle.TextPrimary);
        Assert.Equal(ForgeLineUiPalette.Warning, GameplayHudVisualStyle.Warning);
        Assert.Equal(ForgeLineUiPalette.Critical, GameplayHudVisualStyle.Critical);
    }

    [Fact]
    public void CommandSurfaceForegroundsRemainLegible()
    {
        Assert.True(Contrast(ForgeLineUiPalette.TextPrimary, ForgeLineUiPalette.SurfaceRaised) >= 7f);
        Assert.True(Contrast(ForgeLineUiPalette.TextSecondary, ForgeLineUiPalette.Surface) >= 4.5f);
        Assert.True(Contrast(ForgeLineUiPalette.Accent, ForgeLineUiPalette.SurfaceActive) >= 4.5f);
        Assert.True(Contrast(ForgeLineUiPalette.TextMuted, ForgeLineUiPalette.Background) >= 4.5f);
    }

    [Fact]
    public void SelectionIsNotConveyedByColorAlone()
    {
        HudStateVisual selected = GameplayHudVisualStyle.ResolveItemState(
            selected: true, hovered: false, pressed: false, enabled: true);
        HudStateVisual disabled = GameplayHudVisualStyle.ResolveItemState(
            selected: false, hovered: false, pressed: false, enabled: false);

        Assert.Equal(HudStatePattern.LeftRail, selected.Pattern);
        Assert.Equal(HudStatePattern.DoubleRail, disabled.Pattern);
        Assert.NotEqual(selected.Fill, disabled.Fill);
    }

    private static float Contrast(Vector4 first, Vector4 second)
    {
        float firstLuminance = Luminance(first);
        float secondLuminance = Luminance(second);
        return (MathF.Max(firstLuminance, secondLuminance) + 0.05f) /
               (MathF.Min(firstLuminance, secondLuminance) + 0.05f);
    }

    private static float Luminance(Vector4 color) =>
        0.2126f * Linearize(color.X) +
        0.7152f * Linearize(color.Y) +
        0.0722f * Linearize(color.Z);

    private static float Linearize(float channel) =>
        channel <= 0.04045f
            ? channel / 12.92f
            : MathF.Pow((channel + 0.055f) / 1.055f, 2.4f);
}
