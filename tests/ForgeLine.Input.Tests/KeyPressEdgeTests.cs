using ForgeLine.Platform;
using Xunit;

namespace ForgeLine.Input.Tests;

public sealed class KeyPressEdgeTests
{
    [Fact]
    public void ShortPressSurvivesReleaseButNotAnotherFrameSuppressionOrFocusLoss()
    {
        var input = new InputState();
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.Home));
        ulong sequence = input.KeyPressSequence(PlatformKey.Home);
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.Home));
        Assert.Equal(sequence, input.KeyPressSequence(PlatformKey.Home));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, PlatformKey.Home));
        Assert.True(input.WasKeyPressed(PlatformKey.Home));
        Assert.False(input.IsKeyDown(PlatformKey.Home));
        input.BeginFrame();
        Assert.False(input.WasKeyPressed(PlatformKey.Home));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.Home));
        input.SuppressHeldInput();
        Assert.False(input.WasKeyPressed(PlatformKey.Home));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.Home));
        Assert.False(input.WasKeyPressed(PlatformKey.Home));
        input.Apply(PlatformInputEvent.FocusLost());
        Assert.False(input.WasKeyPressed(PlatformKey.Home));
    }
}
