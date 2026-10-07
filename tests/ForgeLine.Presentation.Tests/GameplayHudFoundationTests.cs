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
    [InlineData(3440, 1440, 96)]
    [InlineData(3440, 1440, 144)]
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

    [Theory]
    [InlineData(96, 1.0f)]
    [InlineData(120, 1.25f)]
    [InlineData(144, 1.5f)]
    [InlineData(192, 2.0f)]
    public void SharedVisualMetricsScaleWithDpi(
        uint dpi,
        float expectedScale)
    {
        GameplayHudLayout layout =
            GameplayHudLayout.Create(
                1920,
                1080,
                dpi);

        Assert.Equal(
            expectedScale,
            layout.Scale);
        Assert.True(
            layout.SafeArea.X >=
            GameplayHudVisualStyle.MinimumSafeMargin);
        Assert.Equal(
            GameplayHudVisualStyle.TopStatusBarHeight *
            layout.Scale,
            layout.TopStatusBar.Height);
    }

    [Fact]
    public void ItemStatesCarryNonColorCues()
    {
        HudStateVisual selected =
            GameplayHudVisualStyle.ResolveItemState(
                selected: true,
                enabled: true);
        HudStateVisual disabled =
            GameplayHudVisualStyle.ResolveItemState(
                selected: false,
                enabled: false);

        Assert.Equal(
            HudStatePattern.LeftRail,
            selected.Pattern);
        Assert.Equal(
            HudStatePattern.Cross,
            disabled.Pattern);
        Assert.NotEqual(
            selected.Fill,
            disabled.Fill);
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
    public void EarlierPointerCaptureCannotBeReleasedByLaterSurface()
    {
        var context =
            new HudInteractionContext();

        context.BeginFrame(
            new SimulationSessionId(21));
        context.CapturePointer();
        context.CapturePointer(
            captured: false);

        Assert.True(
            context.PointerCaptured);
    }

    [Fact]
    public void ActionPanelHandlesMissingSnapshotWithoutCapturingWorldInput()
    {
        var controller =
            new PlayerActionPanelController();
        var input =
            new ForgeLine.Input.InputState();

        controller.Update(
            input,
            snapshot: null,
            viewportWidth: 1600,
            viewportHeight: 900,
            dpi: 144,
            uiScale: 1.25f);

        PlayerActionPanelView view =
            controller.CreateView(
                1600,
                900,
                actions: null,
                dpi: 144,
                uiScale: 1.25f);

        Assert.False(
            view.IsOpen);
        Assert.False(
            controller.PointerCaptured);
        Assert.False(
            controller.HasKeyboardFocus);
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
            HudInteractionContext.HitTest(
                new Vector2(
                    layout.ActionDock.X + 1.0f,
                    layout.ActionDock.Y + 1.0f),
                layout.ActionDock));
        Assert.False(
            HudInteractionContext.HitTest(
                Vector2.Zero,
                layout.ActionDock));
    }
}
