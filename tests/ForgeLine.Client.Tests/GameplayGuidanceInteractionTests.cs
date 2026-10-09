using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class GameplayGuidanceInteractionTests
{
    [Fact]
    public void ToggleConsumesTheFirstGestureAndDoesNotOpenHelpOrDispatchActions()
    {
        var input = new InputState();
        var guide = new GameplayGuidanceInteraction();
        var help = new GameplayHelpController();
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.LeftShift));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.F12));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.B));
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown, PlatformMouseButton.Left, 800, 450));
        guide.Update(input, new SimulationSessionId(1), true);
        Assert.True(guide.Hidden);
        Assert.True(guide.BlocksGameplayThisFrame);
        Assert.False(input.IsKeyDown(PlatformKey.B));
        Assert.False(input.IsMouseButtonDown(PlatformMouseButton.Left));
        Assert.False(help.Update(input, true));
        Assert.False(help.Visible);
        guide.Update(input, new SimulationSessionId(1), true);
        Assert.False(guide.BlocksGameplayThisFrame);
        Assert.True(guide.Hidden);
        input.Reset();
        guide.Update(input, new SimulationSessionId(2), true);
        Assert.False(guide.Hidden);
    }

    [Fact]
    public void DisabledOnboardingAndModalFramesIgnoreToggleAndUnshiftedF12RemainsHelp()
    {
        var input = new InputState();
        var guide = new GameplayGuidanceInteraction();
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.LeftShift));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.F12));
        guide.Update(input, new SimulationSessionId(1), false);
        Assert.False(guide.Hidden);
        Assert.False(guide.BlocksGameplayThisFrame);
        guide.Update(input, new SimulationSessionId(1), true);
        Assert.False(guide.Hidden); // Held input across enable/modal boundaries cannot toggle.
        input.Reset();
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.F12));
        guide.Update(input, new SimulationSessionId(1), true);
        var help = new GameplayHelpController();
        Assert.True(help.Update(input, true));
        Assert.True(help.Visible);
    }
}
