using System.Numerics;
using ForgeLine.Input;
using ForgeLine.Platform;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class OnboardingGeometryTests
{
    [Theory]
    [InlineData(0)] [InlineData(.15625f)] [InlineData(.5f)] [InlineData(.84375f)] [InlineData(1)]
    public void ClippedPaintAndWorldOwnershipUseTheSameTransform(float collapse)
    {
        var layout = GameplayHudLayout.Create(1920, 1080, 144, 1);
        var state = OnboardingPresentation.Resolve(layout, collapse);
        Assert.Equal(layout.SafeArea.X, state.Clip.X); Assert.Equal(layout.SafeArea.Y, state.Clip.Y);
        Assert.Equal(OnboardingPresentation.Intersect(state.Panel, state.Clip), state.VisiblePanel);
        for (float x = state.Clip.X; x < state.Clip.Right; x += 3)
        {
            Vector2 point = new(x, state.Clip.Y + 10);
            bool paint = state.VisiblePanel.Contains(point) || state.Tab.Contains(point);
            Assert.Equal(paint, state.Contains(point));
            Assert.Equal(paint, HudInteractionContext.BlocksWorldPointer(point, layout, false, false, false, false, state));
        }
        Assert.False(state.Contains(new(state.Clip.X - 1, state.Clip.Y + 10)));
    }

    [Fact]
    public void SlidingHintCannotCaptureAnExistingWorldDragOrItsRelease()
    {
        var layout = GameplayHudLayout.Create(1920, 1080, 96);
        var state = OnboardingPresentation.Resolve(layout, 0);
        var input = new InputState();
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown, PlatformMouseButton.Left, 900, 500));
        input.BeginFrame(); input.Apply(PlatformInputEvent.PointerMoved((int)state.Clip.X + 5, (int)state.Clip.Y + 5));
        Assert.False(HudInteractionContext.CapturesWorldPointer(input, layout, false, false, false, false, state));
        input.BeginFrame(); input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonUp, PlatformMouseButton.Left, (int)state.Clip.X + 5, (int)state.Clip.Y + 5));
        Assert.False(HudInteractionContext.CapturesWorldPointer(input, layout, false, false, false, false, state));
    }

    [Fact]
    public void SmoothStepIsMonotonicSymmetricAndExactAtEndpoints()
    {
        float previous = 0;
        for (int i = 0; i <= 220; i++)
        {
            float value = UiMotionTimeline.SmoothStep(TimeSpan.FromMilliseconds(i), OnboardingPresentation.Duration);
            Assert.InRange(value, previous, 1); previous = value;
            float reverse = UiMotionTimeline.SmoothStep(TimeSpan.FromMilliseconds(220 - i), OnboardingPresentation.Duration);
            Assert.Equal(1, value + reverse, 5);
        }
        Assert.Equal(0, UiMotionTimeline.SmoothStep(TimeSpan.Zero, OnboardingPresentation.Duration));
        Assert.Equal(1, previous);
    }
}
