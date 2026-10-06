using System.Numerics;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class GameplayHudFoundationTests
{
    [Fact]
    public void LayoutCombinesDpiAndUserScale()
    {
        GameplayHudLayout baseline =
            GameplayHudLayout.Create(
                1920,
                1080,
                96,
                1.0f);
        GameplayHudLayout scaled =
            GameplayHudLayout.Create(
                1920,
                1080,
                144,
                1.25f);

        Assert.Equal(
            1.0f,
            baseline.Scale);
        Assert.Equal(
            1.875f,
            scaled.Scale);
        Assert.True(
            scaled.TopStatusBar.Height >
            baseline.TopStatusBar.Height);
    }

    [Theory]
    [InlineData(1280, 720, 96)]
    [InlineData(1920, 1080, 96)]
    [InlineData(2560, 1440, 144)]
    public void NamedRegionsRemainSeparated(
        int width,
        int height,
        uint dpi)
    {
        GameplayHudLayout layout =
            GameplayHudLayout.Create(
                width,
                height,
                dpi);

        Assert.False(
            layout.TopStatusBar.Intersects(
                layout.ActionDock));
        Assert.False(
            layout.TopStatusBar.Intersects(
                layout.AlertStack));
        Assert.False(
            layout.ActionDock.Intersects(
                layout.AlertStack));
        Assert.False(
            layout.ActionDock.Intersects(
                layout.Minimap));
        Assert.False(
            layout.SelectionInspector.Intersects(
                layout.Minimap));
        Assert.False(
            layout.SelectionInspector.Intersects(
                layout.SecondaryView));
        Assert.False(
            layout.AlertStack.Intersects(
                layout.SecondaryView));

        Assert.True(
            layout.SafeArea.Contains(
                new Vector2(
                    layout.Minimap.X,
                    layout.Minimap.Y)));
        Assert.True(
            layout.SafeArea.Contains(
                new Vector2(
                    layout.Minimap.Right,
                    layout.Minimap.Bottom)));
    }

    [Fact]
    public void InteractionCaptureIsFrameScopedAndSessionAware()
    {
        var context =
            new HudInteractionContext();
        var first =
            new SimulationSessionId(11);
        var second =
            new SimulationSessionId(12);

        context.BeginFrame(first);
        context.CapturePointer();
        context.CaptureKeyboard();

        Assert.Equal(
            first,
            context.SessionId);
        Assert.True(
            context.PointerCaptured);
        Assert.True(
            context.KeyboardCaptured);

        context.BeginFrame(first);

        Assert.False(
            context.PointerCaptured);
        Assert.False(
            context.KeyboardCaptured);

        context.CapturePointer();
        context.BeginFrame(second);

        Assert.Equal(
            second,
            context.SessionId);
        Assert.False(
            context.PointerCaptured);
        Assert.False(
            context.KeyboardCaptured);
    }

    [Fact]
    public void InteractionHitTestUsesNamedRegionBounds()
    {
        GameplayHudLayout layout =
            GameplayHudLayout.Create(
                1600,
                900,
                96);
        var context =
            new HudInteractionContext();

        Assert.True(
            context.HitTest(
                new Vector2(
                    layout.ActionDock.X + 1.0f,
                    layout.ActionDock.Y + 1.0f),
                layout.ActionDock));
        Assert.False(
            context.HitTest(
                Vector2.Zero,
                layout.ActionDock));
    }
}
