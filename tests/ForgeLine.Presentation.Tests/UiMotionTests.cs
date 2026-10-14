using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class UiMotionTests
{
    [Fact]
    public void InjectedMonotonicClockCompletesWithoutInputOrSimulationGates()
    {
        var motion = new UiMotionTimeline();
        Assert.Equal(UiMotionView.Settled, motion.Update(TimeSpan.Zero, true, true, false, true));
        var start = motion.Update(TimeSpan.FromMilliseconds(10), true, true, true, true);
        Assert.Equal(new UiMotionView(0, 0, 1), start);
        var middle = motion.Update(TimeSpan.FromMilliseconds(50), false, false, false, true);
        Assert.InRange(middle.Reveal, .01f, .99f);
        Assert.InRange(middle.Focus, middle.Reveal, 1);
        Assert.InRange(middle.Result, .01f, .99f);
        Assert.Equal(UiMotionView.Settled, motion.Update(TimeSpan.FromMilliseconds(230), false, false, false, true));
    }

    [Fact]
    public void ReducedMotionStallsClockRollbackAndCancellationSettleImmediately()
    {
        var motion = new UiMotionTimeline();
        motion.Update(TimeSpan.Zero, false, false, false, true);
        motion.Update(TimeSpan.FromMilliseconds(1), true, true, true, true);
        Assert.Equal(UiMotionView.Settled, motion.Update(TimeSpan.FromMilliseconds(5), false, false, false, false));
        motion.Update(TimeSpan.FromMilliseconds(6), true, true, true, true);
        Assert.Equal(UiMotionView.Settled, motion.Update(TimeSpan.FromSeconds(2), false, false, false, true));
        Assert.Equal(UiMotionView.Settled, motion.Update(TimeSpan.Zero, true, true, true, true));
        motion.Cancel();
        Assert.Equal(UiMotionView.Settled, motion.Update(TimeSpan.FromMilliseconds(1), true, true, true, true));
        Assert.False(new UiFeedbackSettings().Animate(RenderQuality.Low));
        Assert.False(new UiFeedbackSettings(ReducedMotion: true).Animate(RenderQuality.Ultra));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(144)]
    public void PresentationRateHasTheSameEndState(int fps)
    {
        var motion = new UiMotionTimeline();
        motion.Update(TimeSpan.Zero, false, false, false, true);
        motion.Update(TimeSpan.FromSeconds(1.0 / fps), true, true, true, true);
        UiMotionView view = default;
        for (int i = 2; i <= fps; i++) view = motion.Update(TimeSpan.FromSeconds((double)i / fps), false, false, false, true);
        Assert.Equal(UiMotionView.Settled, view);
    }

    [Fact]
    public void WarmMotionHasNoManagedAllocation()
    {
        var motion = new UiMotionTimeline();
        for (int i = 0; i < 256; i++) motion.Update(TimeSpan.FromMilliseconds(i), i % 7 == 0, i % 3 == 0, i % 19 == 0, true);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 256; i < 512; i++) motion.Update(TimeSpan.FromMilliseconds(i), i % 7 == 0, i % 3 == 0, i % 19 == 0, true);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
