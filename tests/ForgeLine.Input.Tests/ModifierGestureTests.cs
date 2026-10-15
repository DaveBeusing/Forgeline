using ForgeLine.Platform;
using Xunit;

namespace ForgeLine.Input.Tests;

public sealed class ModifierGestureTests
{
    [Theory]
    [InlineData(PlatformKey.LeftShift)]
    [InlineData(PlatformKey.RightShift)]
    public void BriefShiftF1PressRetainsModifierAfterBothKeysRelease(PlatformKey shift)
    {
        var input = new InputState();
        Key(input, shift, true); Key(input, PlatformKey.F1, true);
        Key(input, PlatformKey.F1, false); Key(input, shift, false);
        Assert.False(input.IsKeyDown(PlatformKey.F1));
        Assert.True(input.WasKeyPressedWithShift(PlatformKey.F1));
        input.BeginFrame();
        Assert.False(input.WasKeyPressedWithShift(PlatformKey.F1));
    }

    [Fact]
    public void RepeatAndSuppressedOrUnshiftedInputCannotInventShiftGesture()
    {
        var input = new InputState();
        Key(input, PlatformKey.F1, true);
        Key(input, PlatformKey.LeftShift, true);
        Assert.False(input.WasKeyPressedWithShift(PlatformKey.F1));
        input.BeginFrame();
        Key(input, PlatformKey.F1, true);
        Assert.False(input.WasKeyPressedWithShift(PlatformKey.F1));
        input.SuppressHeldInput();
        Key(input, PlatformKey.F1, true);
        Assert.False(input.WasKeyPressedWithShift(PlatformKey.F1));
    }
    private static void Key(InputState input, PlatformKey key, bool down) =>
        input.Apply(PlatformInputEvent.KeyChanged(down ? PlatformInputEventKind.KeyDown : PlatformInputEventKind.KeyUp, key));
}
