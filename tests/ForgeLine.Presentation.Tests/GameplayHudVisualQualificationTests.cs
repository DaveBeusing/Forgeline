using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class GameplayHudVisualQualificationTests
{
    [Theory]
    [InlineData(1280, 720, 96)]
    [InlineData(1920, 1080, 96)]
    [InlineData(2560, 1440, 144)]
    [InlineData(3440, 1440, 96)]
    [InlineData(3440, 1440, 144)]
    [InlineData(1920, 1080, 192)]
    public void ProductionRegionsRemainInsideSafeArea(
        int width,
        int height,
        uint dpi)
    {
        var layout = GameplayHudLayout.Create(width, height, dpi);
        HudRect[] regions = [layout.TopStatusBar, layout.AlertStack, layout.Minimap, layout.SecondaryView];
        foreach (var rect in regions) {
            Assert.False(rect.IsEmpty);
            Assert.True(layout.SafeArea.Contains(new(rect.X, rect.Y)));
            Assert.True(layout.SafeArea.Contains(new(rect.Right, rect.Bottom)));
        }
        for (int i=0;i<regions.Length;i++) for(int j=i+1;j<regions.Length;j++)
            Assert.False(regions[i].Intersects(regions[j]));
    }

    [Theory]
    [InlineData(1920, 1080, 96)]
    [InlineData(2560, 1440, 144)]
    public void StatusAndNotificationsUseTheVersionedRightAlignedContract(int width, int height, uint dpi)
    {
        var layout = GameplayHudLayout.Create(width, height, dpi);
        Assert.Equal(4, GameplayHudLayout.ContractVersion);
        Assert.Equal(layout.SafeArea.Right, layout.TopStatusBar.Right, 3);
        Assert.Equal(layout.SafeArea.Right, layout.AlertStack.Right, 3);
        Assert.Equal(layout.SafeArea.Y, layout.TopStatusBar.Y);
        Assert.True(layout.TopStatusBar.Width <= 2 * layout.AlertStack.Width);
        Assert.False(layout.OnboardingBounds.Intersects(layout.TopStatusBar));
        AssertRegionInside(layout.SafeArea, layout.OnboardingBounds);
    }

    [Fact]
    public void InteractionStatesExposeDistinctNonColorPatterns()
    {
        HudStateVisual normal =
            GameplayHudVisualStyle.ResolveItemState(
                selected: false,
                hovered: false,
                pressed: false,
                enabled: true);
        HudStateVisual hovered =
            GameplayHudVisualStyle.ResolveItemState(
                selected: false,
                hovered: true,
                pressed: false,
                enabled: true);
        HudStateVisual pressed =
            GameplayHudVisualStyle.ResolveItemState(
                selected: false,
                hovered: true,
                pressed: true,
                enabled: true);
        HudStateVisual selected =
            GameplayHudVisualStyle.ResolveItemState(
                selected: true,
                hovered: false,
                pressed: false,
                enabled: true);
        HudStateVisual disabled =
            GameplayHudVisualStyle.ResolveItemState(
                selected: false,
                hovered: false,
                pressed: false,
                enabled: false);

        Assert.Equal(
            HudStatePattern.None,
            normal.Pattern);
        Assert.Equal(
            HudStatePattern.Outline,
            hovered.Pattern);
        Assert.Equal(
            HudStatePattern.Underline,
            pressed.Pattern);
        Assert.Equal(
            HudStatePattern.LeftRail,
            selected.Pattern);
        Assert.Equal(
            HudStatePattern.DoubleRail,
            disabled.Pattern);
    }

    [Theory]
    [InlineData(PlayerMatchStatus.Victory, "VICTORY")]
    [InlineData(PlayerMatchStatus.Defeat, "DEFEAT")]
    [InlineData(PlayerMatchStatus.Draw, "DRAW")]
    public void TerminalStateAlwaysHasExplicitText(
        PlayerMatchStatus status,
        string expected)
    {
        Assert.Equal(
            expected,
            PlayerSystemHudModel.ResolveMatchResultLabel(
                status));
    }

    private static void AssertRegionInside(
        in HudRect outer,
        in HudRect inner)
    {
        Assert.False(
            inner.IsEmpty);
        Assert.True(
            inner.X >=
            outer.X);
        Assert.True(
            inner.Y >=
            outer.Y);
        Assert.True(
            inner.Right <=
            outer.Right +
            0.01f);
        Assert.True(
            inner.Bottom <=
            outer.Bottom +
            0.01f);
    }
}
