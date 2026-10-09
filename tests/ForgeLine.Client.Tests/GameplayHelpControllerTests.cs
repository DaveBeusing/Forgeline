using ForgeLine.Input;
using ForgeLine.Platform;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class GameplayHelpControllerTests
{
    [Fact]
    public void BackButtonDismissalConsumesPointerAndClosingFrame()
    {
        var input = new InputState();
        var help = new GameplayHelpController();
        Press(input, PlatformKey.F1);
        help.Update(input, true);
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown,
            PlatformMouseButton.Left, 100, 100));
        help.Dismiss(input);
        Assert.False(help.Visible);
        Assert.True(help.BlocksGameplayThisFrame);
        Assert.False(input.IsMouseButtonDown(PlatformMouseButton.Left));
    }

    [Theory]
    [InlineData(PlatformKey.F1)]
    [InlineData(PlatformKey.F12)]
    public void HelpOpensOnFirstPressAndClosesWithoutLeakingHeldActions(PlatformKey key)
    {
        var input = new InputState();
        var help = new GameplayHelpController();
        Press(input, PlatformKey.D);
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown,
            PlatformMouseButton.Middle, 0, 0));
        Press(input, key);
        Assert.True(help.Update(input, true));
        Assert.True(help.Visible);
        Assert.True(help.BlocksGameplayThisFrame);
        Assert.Equal(default, new RtsCameraActionMapper().Map(input));

        Press(input, key); // Native key repeat cannot toggle the surface.
        Assert.False(help.Update(input, true));
        Assert.True(help.Visible);
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, key));
        help.Update(input, true);
        Press(input, PlatformKey.B);
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown,
            PlatformMouseButton.Right, 100, 100));
        Press(input, key);
        Assert.True(help.Update(input, true));
        Assert.False(help.Visible);
        Assert.True(help.BlocksGameplayThisFrame);
        Assert.False(input.IsKeyDown(PlatformKey.B));
        Assert.False(input.IsMouseButtonDown(PlatformMouseButton.Right));
        Press(input, PlatformKey.D);
        Assert.False(input.IsKeyDown(PlatformKey.D));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, PlatformKey.D));
        Press(input, PlatformKey.D);
        Assert.True(input.IsKeyDown(PlatformKey.D));
    }

    [Fact]
    public void EscapeClosesHelpAndConsumesTheClosingFrame()
    {
        var input = new InputState();
        var help = new GameplayHelpController();
        Press(input, PlatformKey.F1);
        help.Update(input, true);
        Press(input, PlatformKey.Escape);
        Assert.True(help.Update(input, true));
        Assert.False(help.Visible);
        Assert.True(help.BlocksGameplayThisFrame);
        Assert.False(input.IsKeyDown(PlatformKey.Escape));
        help.Update(input, true);
        Assert.False(help.BlocksGameplayThisFrame);
    }

    [Fact]
    public void ShiftF1RemainsMetricsAndPauseOwnsItsControlsScreen()
    {
        var input = new InputState();
        var help = new GameplayHelpController();
        Press(input, PlatformKey.LeftShift);
        Press(input, PlatformKey.F1);
        Assert.False(help.Update(input, true));
        Assert.False(help.Visible);
        input.Reset();
        help.Update(input, false);
        Press(input, PlatformKey.F1);
        Assert.False(help.Update(input, false));
        Assert.False(help.Visible);
    }

    [Fact]
    public void FocusLossWhileHelpIsOpenDoesNotResumeCameraDrag()
    {
        var input = new InputState();
        var help = new GameplayHelpController();
        Press(input, PlatformKey.F1);
        help.Update(input, true);
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown,
            PlatformMouseButton.Middle, 0, 0));
        input.Apply(PlatformInputEvent.FocusLost());
        Assert.False(help.Update(input, true));
        Assert.True(help.Visible);
        Press(input, PlatformKey.F1);
        Assert.True(help.Update(input, true));
        Assert.False(help.Visible);
        Assert.Equal(default, new RtsCameraActionMapper().Map(input));
    }

    private static void Press(InputState input, PlatformKey key) =>
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, key));
}
