using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class HudInformationLayoutTests
{
    [Fact]
    public void EveryResourceAndPowerRemainVisibleThroughoutDisplayMatrix()
    {
        foreach (var display in new[] { (1280, 720), (1920, 1080), (2560, 1440), (3440, 1440), (3840, 2160) })
        foreach (uint dpi in new uint[] { 96, 144, 192 })
        foreach (float scale in new[] { .75f, 1f, 2f })
        {
            var layout = GameplayHudLayout.Create(display.Item1, display.Item2, dpi, scale);
            var strip = ResourceStripLayout.Create(layout);
            Assert.True(strip.Scale > 0);
            Inside(layout.TopStatusBar, strip.Power);
            for (int i = 0; i < 7; i++)
            {
                var cell = strip.Resource(i);
                Inside(layout.TopStatusBar, cell);
                Assert.False(cell.Intersects(strip.Power));
                Assert.True(cell.Width >= 48 * strip.Scale);
                for (int j = 0; j < i; j++) Assert.False(cell.Intersects(strip.Resource(j)));
            }
            Assert.True(112 * HudInformationLayout.InspectorScale(layout) <= layout.SelectionInspector.Height + .01f);
            Assert.True(360 * HudInformationLayout.InspectorScale(layout) <= layout.SelectionInspector.Width + .01f);
            Inside(layout.AlertStack, HudInformationLayout.Alerts(layout));
            var operations = OperationsLayout.Panel(layout);
            Inside(layout.SafeArea, operations);
            Assert.False(operations.Intersects(layout.SelectionInspector));
            Assert.False(operations.Intersects(layout.ActionDock));
            Assert.False(operations.Intersects(layout.PrimaryCommands));
            for (int control = 0; control < 12; control++) Inside(operations, OperationsLayout.Control(layout, control));
        }
    }

    private static void Inside(HudRect outer, HudRect inner)
    {
        Assert.False(inner.IsEmpty);
        Assert.True(inner.X >= outer.X && inner.Y >= outer.Y);
        Assert.True(inner.Right <= outer.Right + .01f && inner.Bottom <= outer.Bottom + .01f);
    }
}
