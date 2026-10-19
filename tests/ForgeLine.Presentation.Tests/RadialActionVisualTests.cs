using System.Numerics;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class RadialActionVisualTests
{
    private static readonly RadialActionLayout Geometry =
        new(new Vector2(500, 400), new Vector2(500, 400), 80, 176, 8);

    private static ObjectActionRingView OpenRing() =>
        new(new(1, 1), new(1), 1, 1, true, true, Geometry, true);

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, false, true)]
    public void EverySettledSectorMaterialUsesThirtyPercentAlpha(
        bool enabled, bool hovered, bool pressed, bool pending)
    {
        var material = RadialActionVisuals.Background(enabled, hovered, pressed, pending, 1);
        Assert.Equal(.30f, material.W);
        Assert.Equal(.15f, RadialActionVisuals.Background(enabled, hovered, pressed, pending, .5f).W);
        Assert.Equal(0, RadialActionVisuals.Background(enabled, hovered, pressed, pending, 0).W);
        Assert.Equal(0, RadialActionVisuals.Background(enabled, hovered, pressed, pending, -1).W);
        Assert.Equal(.30f, RadialActionVisuals.Background(enabled, hovered, pressed, pending, 2).W);
    }

    [Fact]
    public void SectorStatesHaveIndependentTintAndForegroundAlpha()
    {
        var normal = RadialActionVisuals.Background(true, false, false, false, 1);
        var hovered = RadialActionVisuals.Background(true, true, false, false, 1);
        var pressed = RadialActionVisuals.Background(true, true, true, false, 1);
        var disabled = RadialActionVisuals.Background(false, true, true, true, 1);
        var pending = RadialActionVisuals.Background(true, false, false, true, 1);
        Assert.NotEqual(normal.X, hovered.X);
        Assert.NotEqual(hovered.X, pressed.X);
        Assert.NotEqual(normal.X, disabled.X);
        Assert.NotEqual(normal.X, pending.X);
        Assert.Equal(1, GameplayHudVisualStyle.TextPrimary.W);
        Assert.True(GameplayHudVisualStyle.TextSecondary.W > .30f);
    }

    [Fact]
    public void DirectionReversalKeepsContinuousAlphaAndReleasesWorldInput()
    {
        var motion = new ObjectActionRingMotion();
        var start = TimeSpan.FromSeconds(1);
        motion.Update(default, start, true);
        var open = OpenRing();
        var opening = start + TimeSpan.FromMilliseconds(16);
        Assert.Equal(0, motion.Update(open, opening, true).Opacity);
        Assert.Equal(.5f, motion.Update(open, opening + TimeSpan.FromMilliseconds(80), true).Opacity);

        var closed = open with { Open = false };
        var fading = motion.Update(closed, opening + TimeSpan.FromMilliseconds(80), true);
        Assert.Equal(.5f, fading.Opacity);
        Assert.False(fading.Open);
        Assert.False(fading.TryHit(Geometry.LabelCenter(0), out _));

        Assert.Equal(.25f, motion.Update(closed, opening + TimeSpan.FromMilliseconds(160), true).Opacity);
        Assert.Equal(.25f, motion.Update(open, opening + TimeSpan.FromMilliseconds(160), true).Opacity);
        Assert.Equal(1, motion.Update(open, opening + TimeSpan.FromMilliseconds(320), true).Opacity);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(144)]
    public void InjectedPresentationRatesRemainMonotonicAndSettleAt160Milliseconds(int hz)
    {
        var motion = new ObjectActionRingMotion();
        var start = TimeSpan.FromSeconds(1);
        var open = OpenRing();
        motion.Update(default, start, true);
        var opening = start + TimeSpan.FromMilliseconds(16);
        motion.Update(open, opening, true);
        float previous = 0;
        for (int frame = 1; frame * 1000d / hz < 160; frame++)
        {
            float alpha = motion.Update(open, opening + TimeSpan.FromSeconds(frame / (double)hz), true).Opacity;
            Assert.InRange(alpha, previous, 1);
            previous = alpha;
        }
        Assert.Equal(1, motion.Update(open, opening + TimeSpan.FromMilliseconds(160), true).Opacity);
    }

    [Fact]
    public void DisplayChangePauseInvalidProjectionAndClockStallSettleImmediately()
    {
        var motion = new ObjectActionRingMotion();
        var first = new RingMotionViewport(1920, 1080, 96, 1);
        var second = new RingMotionViewport(3440, 1440, 144, 1.25f);
        var start = TimeSpan.FromSeconds(1);
        var ring = OpenRing();
        motion.Update(default, start, true, first);
        motion.Update(ring, start + TimeSpan.FromMilliseconds(16), true, first);
        Assert.Equal(.5f, motion.Update(ring, start + TimeSpan.FromMilliseconds(96), true, first).Opacity);
        Assert.Equal(1, motion.Update(ring, start + TimeSpan.FromMilliseconds(112), true, second).Opacity);

        var invalid = ring with { Geometry = default };
        Assert.Equal(0, motion.Update(invalid, start + TimeSpan.FromMilliseconds(128), true, second).Opacity);
        Assert.Equal(1, motion.Update(ring, start + TimeSpan.FromMilliseconds(144), false, second).Opacity);
        Assert.Equal(0, motion.Update(ring with { Open = false }, start + TimeSpan.FromMilliseconds(160), false, second).Opacity);
        Assert.Equal(1, motion.Update(ring, start + TimeSpan.FromMilliseconds(500), true, second).Opacity);
        Assert.Equal(1, motion.Update(ring, start + TimeSpan.FromMilliseconds(400), true, second).Opacity);
    }
}
