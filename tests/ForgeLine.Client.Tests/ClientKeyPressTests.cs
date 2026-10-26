using ForgeLine.Input;
using ForgeLine.Platform;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ClientKeyPressTests
{
    [Theory]
    [InlineData(PlatformKey.Space)]
    [InlineData(PlatformKey.Escape)]
    [InlineData(PlatformKey.Enter)]
    [InlineData(PlatformKey.Up)]
    [InlineData(PlatformKey.Down)]
    public void TapCompletedBetweenFramesStillActivatesClientAction(PlatformKey key)
    {
        var input = new InputState();
        bool held = false;
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, key));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, key));
        Assert.False(input.IsKeyDown(key));
        Assert.True(ClientApplication.ConsumeKeyPress(input, key, ref held));
        input.BeginFrame();
        Assert.False(ClientApplication.ConsumeKeyPress(input, key, ref held));
    }

    [Fact]
    public void HeldRepeatDoesNotActivateAgainButReleasedSecondTapDoes()
    {
        var input = new InputState();
        bool held = false;
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.Space));
        Assert.True(ClientApplication.ConsumeKeyPress(input, PlatformKey.Space, ref held));
        input.BeginFrame();
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.Space));
        Assert.False(ClientApplication.ConsumeKeyPress(input, PlatformKey.Space, ref held));
        input.BeginFrame();
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, PlatformKey.Space));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.Space));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, PlatformKey.Space));
        Assert.True(ClientApplication.ConsumeKeyPress(input, PlatformKey.Space, ref held));
    }

    [Fact]
    public void SuppressedTapAndFocusLossDoNotActivateClientAction()
    {
        var input = new InputState();
        bool held = false;
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.Escape));
        input.SuppressKeyUntilRelease(PlatformKey.Escape);
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, PlatformKey.Escape));
        Assert.False(ClientApplication.ConsumeKeyPress(input, PlatformKey.Escape, ref held));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.Escape));
        input.Apply(PlatformInputEvent.FocusLost());
        Assert.False(ClientApplication.ConsumeKeyPress(input, PlatformKey.Escape, ref held));
    }
}
