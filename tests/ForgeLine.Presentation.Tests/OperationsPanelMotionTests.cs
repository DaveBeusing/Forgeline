using System.Numerics;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class OperationsPanelMotionTests
{
    private static readonly SimulationSessionId Session = new(1);

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(144)]
    public void OpenAndCloseEaseWithin180MillisecondsAtDifferentPresentationRates(int rate)
    {
        var layout = GameplayHudLayout.Create(1920, 1080, 96);
        var motion = new OperationsPanelMotion();
        TimeSpan start = TimeSpan.FromSeconds(1);
        Assert.Equal(0, motion.Update(false, Session, layout, start, true));
        TimeSpan opened = start + TimeSpan.FromMilliseconds(16);
        Assert.Equal(0, motion.Update(true, Session, layout, opened, true));
        float previous = 0;
        for (int n = 1; n * 1000d / rate < 180; n++)
        {
            float alpha = motion.Update(true, Session, layout, opened + TimeSpan.FromSeconds(n / (double)rate), true);
            Assert.InRange(alpha, previous, 1);
            previous = alpha;
        }
        Assert.Equal(1, motion.Update(true, Session, layout, opened + OperationsPanelMotion.Duration, true));

        TimeSpan closed = opened + OperationsPanelMotion.Duration + TimeSpan.FromMilliseconds(16);
        Assert.Equal(1, motion.Update(false, Session, layout, closed, true));
        Assert.InRange(motion.Update(false, Session, layout, closed + TimeSpan.FromMilliseconds(90), true), .49f, .51f);
        Assert.Equal(0, motion.Update(false, Session, layout, closed + OperationsPanelMotion.Duration, true));
    }

    [Fact]
    public void ReversedTransitionStartsAtCurrentOpacityWithoutAnAlphaJump()
    {
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        var motion = new OperationsPanelMotion();
        TimeSpan t = TimeSpan.FromSeconds(1);
        motion.Update(false, Session, layout, t, true);
        motion.Update(true, Session, layout, t + TimeSpan.FromMilliseconds(16), true);
        float half = motion.Update(true, Session, layout, t + TimeSpan.FromMilliseconds(106), true);
        Assert.InRange(half, .49f, .51f);
        Assert.Equal(half, motion.Update(false, Session, layout, t + TimeSpan.FromMilliseconds(106), true));
        Assert.InRange(motion.Update(false, Session, layout, t + TimeSpan.FromMilliseconds(196), true), .24f, .26f);
        Assert.Equal(0, motion.Update(false, Session, layout, t + TimeSpan.FromMilliseconds(286), true));
    }

    [Fact]
    public void SessionDpiScaleClockAndReducedMotionSettleWithoutStaleVisuals()
    {
        var layout = GameplayHudLayout.Create(1920, 1080, 96);
        var motion = new OperationsPanelMotion();
        TimeSpan t = TimeSpan.FromSeconds(1);
        motion.Update(false, Session, layout, t, true);
        motion.Update(true, Session, layout, t + TimeSpan.FromMilliseconds(16), true);
        Assert.InRange(motion.Update(true, Session, layout, t + TimeSpan.FromMilliseconds(106), true), .49f, .51f);

        Assert.Equal(0, motion.Update(false, new(2), layout, t + TimeSpan.FromMilliseconds(120), true));
        Assert.Equal(1, motion.Update(true, new(2), layout with { Scale = layout.Scale + .5f },
            t + TimeSpan.FromMilliseconds(130), true));
        Assert.Equal(0, motion.Update(false, new(2), layout,
            t + TimeSpan.FromMilliseconds(135), false));
        Assert.Equal(1, motion.Update(true, new(2), layout,
            t + TimeSpan.FromMilliseconds(140), false));
        Assert.Equal(0, motion.Update(false, new(2), layout,
            t + TimeSpan.FromMilliseconds(600), true));
        Assert.Equal(1, motion.Update(true, new(2), layout,
            t + TimeSpan.FromMilliseconds(590), true));
        motion.Reset();
        Assert.Equal(0, motion.Opacity);
    }

    [Fact]
    public void BackgroundOpacityIsSeparateFromMotionAndForegroundAlpha()
    {
        Assert.Equal(.30f, OperationsPanelVisuals.BackgroundOpacity);
        var motion = new OperationsPanelMotion();
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        motion.Update(false, Session, layout, TimeSpan.Zero, true);
        motion.Update(true, Session, layout, TimeSpan.FromMilliseconds(16), true);
        float alpha = motion.Update(true, Session, layout, TimeSpan.FromMilliseconds(106), true);
        Assert.InRange(alpha * OperationsPanelVisuals.BackgroundOpacity, .149f, .151f);
    }

    [Fact]
    public void ClosedPanelIsNotInputOwningEvenWhileDecorativeBackgroundFades()
    {
        var motion = new OperationsPanelMotion();
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        var input = new ForgeLine.Input.InputState();
        var interaction = new PresentationInteractionState();
        var controller = new OperationsController();
        TimeSpan t = TimeSpan.FromSeconds(1);
        motion.Update(false, Session, layout, t, true);
        motion.Update(true, Session, layout, t + TimeSpan.FromMilliseconds(16), true);
        motion.Update(true, Session, layout, t + TimeSpan.FromMilliseconds(196), true);
        controller.OpenCategory(OperationsCategory.All, Session, interaction);
        Vector2 inside = new(layout.SafeArea.X + 20, OperationsLayout.Panel(layout).Y + 30);
        Assert.True(controller.ContainsSurface(inside, layout));
        controller.Close(input, interaction);
        Assert.False(controller.ContainsSurface(inside, layout));
        Assert.Equal(1, motion.Update(false, Session, layout, t + TimeSpan.FromMilliseconds(200), true));
        Assert.True(motion.Update(false, Session, layout, t + TimeSpan.FromMilliseconds(290), true) > 0);
        Assert.False(controller.ContainsSurface(inside, layout));
    }

    [Fact]
    public void WarmSteadyStateProducesNoManagedAllocations()
    {
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        var motion = new OperationsPanelMotion();
        for (int n = 0; n < 128; n++)
            motion.Update(true, Session, layout, TimeSpan.FromMilliseconds(n), true);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int n = 128; n < 1152; n++)
            motion.Update(true, Session, layout, TimeSpan.FromMilliseconds(n), true);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }
}
