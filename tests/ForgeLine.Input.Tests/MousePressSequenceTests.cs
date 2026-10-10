using ForgeLine.Platform;
using Xunit;

namespace ForgeLine.Input.Tests;

public sealed class MousePressSequenceTests
{
    [Fact]
    public void QuickEdgesSurviveReleaseWhileRepeatedDownAndSuppressionDoNotAdvance()
    {
        var input = new InputState();
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown,
            PlatformMouseButton.Left, 10, 20));
        ulong sequence = input.MousePressSequence(PlatformMouseButton.Left);
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown,
            PlatformMouseButton.Left, 11, 21));
        Assert.Equal(sequence, input.MousePressSequence(PlatformMouseButton.Left));
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonUp,
            PlatformMouseButton.Left, 11, 21));
        Assert.False(input.IsMouseButtonDown(PlatformMouseButton.Left));
        Assert.True(input.TryGetMousePressPosition(PlatformMouseButton.Left, out var origin));
        Assert.Equal(10, origin.X);
        input.BeginFrame();
        Assert.Equal(sequence, input.MousePressSequence(PlatformMouseButton.Left));
        Assert.False(input.TryGetMousePressPosition(PlatformMouseButton.Left, out _));
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown,
            PlatformMouseButton.Left, 30, 40));
        Assert.Equal(sequence + 1, input.MousePressSequence(PlatformMouseButton.Left));
        input.SuppressHeldInput();
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown,
            PlatformMouseButton.Left, 30, 40));
        Assert.Equal(sequence + 1, input.MousePressSequence(PlatformMouseButton.Left));
    }
}
