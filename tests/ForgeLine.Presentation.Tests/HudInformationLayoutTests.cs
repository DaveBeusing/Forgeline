using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class HudInformationLayoutTests
{
    [Fact]
    public void EveryResourceAndPowerRemainVisibleThroughoutDisplayMatrix()
    {
        Span<char> buffer = stackalloc char[40];
        foreach (var display in new[] { (1280, 720), (1920, 1080), (2560, 1440), (3440, 1440), (3840, 2160) })
        foreach (uint dpi in new uint[] { 96, 144, 192 })
        foreach (float scale in new[] { .75f, 1f, 2f })
        {
            var layout = GameplayHudLayout.Create(display.Item1, display.Item2, dpi, scale);
            var strip = ResourceStripLayout.Create(layout);
            Assert.True(strip.Scale > 0);
            Inside(layout.TopStatusBar, strip.Power);
            foreach (string state in new[] { "STABLE", "CONSTRAINED", "UNAVAILABLE" })
                Assert.True(UiFontMetrics.Default.Measure("POWER / " + state, MathF.Max(11, 12 * strip.Scale)) <=
                    strip.Power.Width - 12 * strip.Scale);
            for (int i = 0; i < 7; i++)
            {
                var cell = strip.Resource(i);
                Inside(layout.TopStatusBar, cell);
                Assert.False(cell.Intersects(strip.Power));
                Assert.True(cell.Width >= 48 * strip.Scale);
                Assert.True(16 * strip.Scale >= 12);
                string fullLabel = ResourcePowerHudModel.GetResourceLabel(i);
                Assert.NotEmpty(fullLabel);
                Assert.True(UiFontMetrics.Default.MeasureLineBounds(fullLabel, MathF.Max(11, 12 * strip.Scale)).Width <=
                    cell.Width - 23 * strip.Scale, $"Full resource label {fullLabel} exceeds its cell.");
                foreach (double amount in new[] { 0, 9999, 99999, 1e6, 1.25e9, 1e15, double.MaxValue, double.NaN })
                {
                    var text = new ResourcePowerHudRenderer.HudTextBuilder(buffer);
                    ResourcePowerHudRenderer.AppendQuantity(ref text, amount);
                    Assert.True(UiFontMetrics.Default.MeasureLineBounds(text.Written, 16 * strip.Scale, true).Width <=
                        cell.Width - 23 * strip.Scale, $"Quantity {text.Written.ToString()} exceeds resource cell.");
                }
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

    [Theory]
    [InlineData(640, 360, 96)]
    [InlineData(800, 600, 144)]
    public void DiagnosticClientsUseTheSameTwoRowDecisionAsThePublishedStrip(int width, int height, uint dpi)
    {
        var layout = GameplayHudLayout.Create(width, height, dpi);
        var strip = ResourceStripLayout.Create(layout);
        Assert.True(strip.TwoRows);
        Assert.Equal(layout.TopStatusBar.Height, 80 * strip.Scale, 3);
        for (int index = 0; index < 7; index++) Inside(layout.TopStatusBar, strip.Resource(index));
    }

    [Theory]
    [InlineData(1360, 1f)]
    [InlineData(1100, .75f)]
    public void FullLabelsFitAcrossTheSingleToDoubleRowTransition(int width, float userScale)
    {
        var strip = ResourceStripLayout.Create(GameplayHudLayout.Create(width, 720, 96, userScale));
        for (int index = 0; index < 7; index++)
        {
            float inkWidth = UiFontMetrics.Default.MeasureLineBounds(ResourcePowerHudModel.GetResourceLabel(index),
                MathF.Max(11, 12 * strip.Scale)).Width;
            Assert.True(inkWidth <= strip.Resource(index).Width - 23 * strip.Scale,
                $"{width}px: {ResourcePowerHudModel.GetResourceLabel(index)} needs {inkWidth}px.");
        }
    }

    private static void Inside(HudRect outer, HudRect inner)
    {
        Assert.False(inner.IsEmpty);
        Assert.True(inner.X >= outer.X && inner.Y >= outer.Y);
        Assert.True(inner.Right <= outer.Right + .01f && inner.Bottom <= outer.Bottom + .01f);
    }
}
