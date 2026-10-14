using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class FrontendLayoutHardeningTests
{
    [Theory]
    [InlineData(1920, 1080, 1.0, 0, 0)]
    [InlineData(2560, 1080, 1.0, 320, 0)]
    [InlineData(1920, 1200, 1.0, 0, 60)]
    public void LayoutCentersReferenceSurface(
        float width,
        float height,
        float userScale,
        float expectedX,
        float expectedY)
    {
        FrontendLayout layout =
            FrontendDesign.ResolveLayout(
                width,
                height,
                userScale);

        Assert.Equal(expectedX, layout.OffsetX);
        Assert.Equal(expectedY, layout.OffsetY);
    }

    [Fact]
    public void DeclaredScaleExtremesKeepDrawnControlsAndPointerTargetsInsideViewport()
    {
        foreach (var display in new[] { (1280, 720), (1920, 1080), (2560, 1440), (3840, 2160), (3440, 1440) })
            foreach (float scale in new[] { .75f, 1f, 2f })
            {
                var layout = FrontendDesign.ResolveLayout(display.Item1, display.Item2, scale);
                Assert.True(layout.ContentWidth <= display.Item1 + .01f);
                Assert.True(layout.ContentHeight <= display.Item2 + .01f);
                var menu = new MainMenuModel(false);
                Assert.Equal("new-game", FrontendHitTesting.MainMenu(layout.OffsetX + 120 * layout.Scale,
                    layout.OffsetY + 422 * layout.Scale, layout, menu.Items));
                Assert.Null(FrontendHitTesting.MainMenu(layout.OffsetX + 120 * layout.Scale,
                    layout.OffsetY + 350 * layout.Scale, layout, menu.Items));
            }
    }

    [Fact]
    public void SmallViewportFitsInsteadOfClippingAtMinimumScale()
    {
        FrontendLayout layout =
            FrontendDesign.ResolveLayout(
                960,
                540);

        Assert.Equal(0.5f, layout.Scale);
        Assert.Equal(960f, layout.ContentWidth);
        Assert.Equal(540f, layout.ContentHeight);
    }

    [Fact]
    public void LongDetailTextIsEllipsizedDeterministically()
    {
        string value =
            FrontendDesign.FitText(
                "CENTRAL-DIVIDE-SAVE-WITH-A-VERY-LONG-NAME");

        Assert.True(ForgeLine.Presentation.UiFontMetrics.Default.Measure(value, 22) <= FrontendDesign.MaximumDetailTextLength * 13f);
        Assert.EndsWith("…", value, StringComparison.Ordinal);
    }

    [Fact]
    public void UltrawideHitTestingUsesCenteredSafeArea()
    {
        FrontendLayout layout =
            FrontendDesign.ResolveLayout(
                2560,
                1080);
        var menu =
            new MainMenuModel(false);

        Assert.Equal(
            "new-game",
            FrontendHitTesting.MainMenu(
                320 + 100,
                422,
                layout,
                menu.Items));
        Assert.Null(
            FrontendHitTesting.MainMenu(
                100,
                350,
                layout,
                menu.Items));
    }

    [Fact]
    public void SaveViewportKeepsFocusVisibleAndMapsPointerRows()
    {
        var model =
            new LoadGameModel(
                Enumerable.Range(0, 12)
                    .Select(
                        index =>
                            new LoadGameEntry(
                                $"save-{index}",
                                $"SAVE {index}",
                                $"save-{index}.save.json",
                                (ulong)index,
                                LoadGameEntryState.Available)));

        model.Focus(10);

        Assert.Equal(
            5,
            model.VisibleStartIndex(
                FrontendDesign.MaximumVisibleDetailRows));

        model.FocusVisible(
            2,
            FrontendDesign.MaximumVisibleDetailRows);

        Assert.Equal(7, model.FocusedIndex);
    }
}
