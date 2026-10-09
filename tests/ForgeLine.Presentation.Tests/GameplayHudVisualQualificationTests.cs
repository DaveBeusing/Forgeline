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
        GameplayHudLayout layout =
            GameplayHudLayout.Create(
                width,
                height,
                dpi);

        AssertRegionInside(
            layout.SafeArea,
            layout.TopStatusBar);
        AssertRegionInside(
            layout.SafeArea,
            layout.SelectionInspector);
        AssertRegionInside(
            layout.SafeArea,
            layout.ActionDock);
        AssertRegionInside(
            layout.SafeArea,
            layout.AlertStack);
        AssertRegionInside(
            layout.SafeArea,
            layout.Minimap);
        AssertRegionInside(
            layout.SafeArea,
            layout.SecondaryView);

        Assert.False(
            layout.TopStatusBar.Intersects(
                layout.ActionDock));
        Assert.False(
            layout.ActionDock.Intersects(
                layout.Minimap));
        Assert.False(
            layout.SelectionInspector.Intersects(
                layout.Minimap));
        Assert.True(
            PlayerActionDockInteractionLayout.MaximumVisibleItems(
                layout) >
            0);
    }

    [Fact]
    public void FullHdNinetySixDpiLayoutMatchesQualifiedGeometry()
    {
        GameplayHudLayout layout =
            GameplayHudLayout.Create(
                1920,
                1080,
                96);

        Assert.Equal(
            new HudRect(
                12.0f,
                12.0f,
                1896.0f,
                1056.0f),
            layout.SafeArea);
        Assert.Equal(
            new HudRect(
                12.0f,
                12.0f,
                1742.0f,
                36.0f),
            layout.TopStatusBar);
        Assert.Equal(new HudRect(1766.0f, 12.0f, 142.0f, 36.0f), layout.RuntimeMetrics);
        Assert.Equal(
            new HudRect(
                1688.0f,
                848.0f,
                220.0f,
                220.0f),
            layout.Minimap);
        Assert.Equal(
            new HudRect(
                12.0f,
                956.0f,
                360.0f,
                112.0f),
            layout.SelectionInspector);
        Assert.Equal(
            new HudRect(
                1300.0f,
                60.0f,
                608.0f,
                360.0f),
            layout.ActionDock);
        Assert.Equal(
            new HudRect(
                12.0f,
                60.0f,
                1276.0f,
                96.0f),
            layout.AlertStack);
        Assert.Equal(
            new HudRect(
                12.0f,
                168.0f,
                1276.0f,
                776.0f),
            layout.SecondaryView);
    }

    [Fact]
    public void QhdOneHundredFortyFourDpiLayoutMatchesQualifiedGeometry()
    {
        GameplayHudLayout layout =
            GameplayHudLayout.Create(
                2560,
                1440,
                144);

        Assert.Equal(
            1.5f,
            layout.Scale);
        Assert.Equal(
            new HudRect(
                18.0f,
                18.0f,
                2524.0f,
                1404.0f),
            layout.SafeArea);
        Assert.Equal(
            new HudRect(
                2212.0f,
                1092.0f,
                330.0f,
                330.0f),
            layout.Minimap);
        Assert.Equal(
            new HudRect(
                1630.0f,
                90.0f,
                912.0f,
                540.0f),
            layout.ActionDock);
        Assert.Equal(
            new HudRect(
                18.0f,
                252.0f,
                1594.0f,
                984.0f),
            layout.SecondaryView);
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
