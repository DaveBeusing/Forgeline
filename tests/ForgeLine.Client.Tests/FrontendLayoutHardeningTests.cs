using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class FrontendLayoutHardeningTests
{
    [Fact]
    public void MatrixKeepsEveryActionBoundedAndEveryGapUncaptured()
    {
        foreach (var display in new[] { (1280, 720), (1600, 900), (1920, 1080), (1920, 1200), (2560, 1440), (3440, 1440), (3840, 2160) })
        foreach (float dpi in new[] { 96f, 144f, 192f })
        foreach (float scale in new[] { .75f, 1f, 2f })
        foreach (bool controls in new[] { false, true })
        {
            var layout = FrontendDesign.ResolveLayout(display.Item1, display.Item2, scale, dpi, controls);
            var g = layout.Geometry;
            var menu = new MainMenuModel(true);
            Assert.False(g.IsEmpty);
            foreach (var r in new[] { g.Header, g.Title, g.Navigation, g.Details, g.PrimaryAction, g.SecondaryAction, g.Footer, g.Feedback })
            {
                Assert.InRange(r.X, 0, display.Item1);
                Assert.InRange(r.Y, 0, display.Item2);
                Assert.InRange(r.Right, 0, display.Item1 + 1);
                Assert.InRange(r.Bottom, 0, display.Item2 + 1);
            }
            for (int i = 0; i < menu.Items.Count; i++)
            {
                var r = g.MenuRow(i);
                Assert.Equal(menu.Items[i].Id, FrontendHitTesting.MainMenu(r.X + 1, r.Y + 1, layout, menu.Items));
                foreach (var point in new[] { (r.X - .1f, r.Y + 1), (r.Right, r.Y + 1), (r.X + 1, r.Y - .1f), (r.X + 1, r.Bottom) })
                    Assert.Null(FrontendHitTesting.MainMenu(point.Item1, point.Item2, layout, menu.Items));
            }
            for (int i = 0; i < g.VisibleDetailRows; i++)
            {
                var r = g.DetailRow(i);
                Assert.Equal(i, FrontendHitTesting.DetailRow(r.X + 1, r.Y + 1, layout, 7));
                Assert.Null(FrontendHitTesting.DetailRow(r.X + 1, r.Bottom, layout, 7));
                Assert.True(r.Bottom < g.PrimaryAction.Y);
                var minus = g.Decrease(i); var plus = g.Increase(i);
                Assert.Equal(-1, FrontendHitTesting.DetailAdjust(minus.X + 1, minus.Y + 1, layout, 7));
                Assert.Equal(1, FrontendHitTesting.DetailAdjust(plus.X + 1, plus.Y + 1, layout, 7));
                Assert.Null(FrontendHitTesting.DetailAdjust(minus.Right, minus.Y + 1, layout, 7));
                Assert.Null(FrontendHitTesting.DetailAdjust(plus.Right, plus.Y + 1, layout, 7));
            }
            Assert.True(FrontendHitTesting.PrimaryAction(g.PrimaryAction.X + 1, g.PrimaryAction.Y + 1, layout));
            Assert.False(FrontendHitTesting.PrimaryAction(g.PrimaryAction.Right, g.PrimaryAction.Y + 1, layout));
            Assert.True(FrontendHitTesting.SecondaryAction(g.SecondaryAction.X + 1, g.SecondaryAction.Y + 1, layout));
            Assert.False(FrontendHitTesting.SecondaryAction(g.SecondaryAction.Right, g.SecondaryAction.Y + 1, layout));
            Assert.True(g.SecondaryAction.Right < g.PrimaryAction.X);
            Assert.True(g.PrimaryAction.Bottom < g.Feedback.Y);
            Assert.True(g.MenuRow(6).Bottom < g.Feedback.Y);
            Assert.False(g.Decorative.Contains(g.Decorative.X + 1, g.Decorative.Y + 1));
            Assert.Null(FrontendHitTesting.MainMenu(1, display.Item2 / 2f, layout, menu.Items));
        }
    }

    [Fact]
    public void DpiAndUserScaleChangeReadableGeometryWithoutLeavingTheViewport()
    {
        var normal = FrontendDesign.ResolveLayout(1920, 1080).Geometry;
        var large = FrontendDesign.ResolveLayout(1920, 1080, 2, 192).Geometry;
        Assert.True(large.Scale > normal.Scale);
        Assert.True(large.VisibleDetailRows < normal.VisibleDetailRows);
        Assert.True(large.Decorative.Width < normal.Decorative.Width);
        Assert.True(FrontendDesign.ResolveLayout(0, 0).Geometry.IsEmpty);
        Assert.True(FrontendDesign.ResolveLayout(float.NaN, 720).Geometry.IsEmpty);
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
