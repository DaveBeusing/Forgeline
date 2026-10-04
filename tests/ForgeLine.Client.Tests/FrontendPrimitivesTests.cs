using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class FrontendPrimitivesTests
{
    [Theory]
    [InlineData(1920f, 1080f, 1f, 1f)]
    [InlineData(3840f, 2160f, 1f, 2f)]
    [InlineData(960f, 540f, 1f, 0.75f)]
    [InlineData(2560f, 1440f, 1.25f, 1.6666666f)]
    public void ResolveScaleUsesViewportAndUserScale(
        float width,
        float height,
        float userScale,
        float expected)
    {
        var actual = FrontendDesign.ResolveScale(width, height, userScale);

        Assert.Equal(expected, actual, precision: 5);
    }

    [Fact]
    public void RectScalingPreservesReferenceGeometry()
    {
        var rect = new FrontendRect(48f, 24f, 400f, 56f);

        Assert.Equal(
            new FrontendRect(96f, 48f, 800f, 112f),
            rect.Scale(2f));
    }

    [Fact]
    public void FocusNavigationWrapsInBothDirections()
    {
        var focus = new FrontendFocusModel(["continue", "new-game", "settings"]);

        Assert.Equal("continue", focus.FocusedId);
        Assert.Equal("new-game", focus.MoveNext());
        Assert.Equal("settings", focus.MoveNext());
        Assert.Equal("continue", focus.MoveNext());
        Assert.Equal("settings", focus.MovePrevious());
    }

    [Fact]
    public void ExplicitFocusRejectsUnknownTargetsWithoutChangingFocus()
    {
        var focus = new FrontendFocusModel(["new-game", "settings"]);

        Assert.True(focus.TryFocus("settings"));
        Assert.False(focus.TryFocus("missing"));
        Assert.Equal("settings", focus.FocusedId);
    }
}
