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

        Assert.Equal(
            FrontendDesign.MaximumDetailTextLength,
            value.Length);
        Assert.EndsWith("...", value, StringComparison.Ordinal);
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
            MainMenuModel.NewGameId,
            FrontendHitTesting.MainMenu(
                320 + 100,
                350,
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
                                index,
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
