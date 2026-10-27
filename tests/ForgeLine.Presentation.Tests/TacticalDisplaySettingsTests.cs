using System.Numerics;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class TacticalDisplaySettingsTests
{
    [Fact]
    public void PalettePreservesOpacityAndKeepsTeamValuesDistinctInGrayscale()
    {
        var standard = new Vector4(.3f, .6f, .2f, .5f);
        Assert.Equal(standard, new TacticalDisplaySettings().TeamColor(false, standard));
        foreach (var palette in new[] { TacticalTeamPalette.BlueOrange, TacticalTeamPalette.Grayscale })
        {
            var settings = new TacticalDisplaySettings { TeamPalette = palette };
            settings.Validate();
            var friendly = settings.TeamColor(false, standard);
            var enemy = settings.TeamColor(true, standard);
            Assert.NotEqual(friendly, enemy);
            Assert.Equal(.5f, friendly.W);
            Assert.Equal(.5f, enemy.W);
            Assert.True(MathF.Abs(Luma(friendly) - Luma(enemy)) > .08f);
        }
    }

    [Theory]
    [InlineData(1024, 720, 96)]
    [InlineData(1280, 720, 192)]
    [InlineData(1920, 1080, 120)]
    [InlineData(3440, 1440, 144)]
    public void DisplayTransitionsKeepVitalRegionsInsideSafeBounds(int width, int height, uint dpi)
    {
        foreach (float scale in new[] { 1f, 1.25f, 1.5f })
        {
            var layout = GameplayHudLayout.Create(width, height, dpi, scale);
            foreach (var rect in new[] { layout.TopStatusBar, layout.Minimap, layout.AlertStack })
            {
                Assert.False(rect.IsEmpty);
                Assert.InRange(rect.X, layout.SafeArea.X, layout.SafeArea.Right);
                Assert.InRange(rect.Right, layout.SafeArea.X, layout.SafeArea.Right + .01f);
                Assert.InRange(rect.Y, layout.SafeArea.Y, layout.SafeArea.Bottom);
                Assert.InRange(rect.Bottom, layout.SafeArea.Y, layout.SafeArea.Bottom + .01f);
            }
            Assert.False(layout.TopStatusBar.Intersects(layout.Minimap));
        }
    }

    private static float Luma(Vector4 value) => .2126f * value.X + .7152f * value.Y + .0722f * value.Z;
}
