using ForgeLine.Platform;
using Xunit;

namespace ForgeLine.Platform.Windows.Tests;

public sealed class WindowLifecycleStateTests
{
    [Fact]
    public void CreationTimeSizeMessageCanEstablishInitialClientSize()
    {
        var state =
            new WindowLifecycleState();

        WindowSizeMessageTransition transition =
            state.ApplySizeMessage(
                1_280,
                720,
                minimized: false);

        state.InitializeClientSize(
            new WindowSize(
                1_280,
                720));

        Assert.True(
            transition.ValidClientSizeObserved);
        Assert.Equal(
            new WindowSize(
                1_280,
                720),
            state.ValidClientSize);
        Assert.False(
            state.IsMinimized);
    }

    [Fact]
    public void TransientZeroSizeDoesNotReplaceLastValidClientSize()
    {
        var state =
            new WindowLifecycleState();
        state.InitializeClientSize(
            new WindowSize(
                1_920,
                1_080));

        WindowSizeMessageTransition transition =
            state.ApplySizeMessage(
                0,
                0,
                minimized: false);

        Assert.False(
            transition.ValidClientSizeObserved);
        Assert.Equal(
            new WindowSize(
                1_920,
                1_080),
            state.ValidClientSize);
        Assert.False(
            state.IsMinimized);
    }

    [Fact]
    public void MinimizeAndRestorePreserveAndThenReplaceValidSize()
    {
        var state =
            new WindowLifecycleState();
        state.InitializeClientSize(
            new WindowSize(
                1_920,
                1_080));

        WindowSizeMessageTransition minimized =
            state.ApplySizeMessage(
                0,
                0,
                minimized: true);

        Assert.True(
            minimized.BecameMinimized);
        Assert.True(
            state.IsMinimized);
        Assert.Equal(
            new WindowSize(
                1_920,
                1_080),
            state.ValidClientSize);

        WindowSizeMessageTransition restored =
            state.ApplySizeMessage(
                2_560,
                1_440,
                minimized: false);

        Assert.True(
            restored.Restored);
        Assert.True(
            restored.ValidClientSizeObserved);
        Assert.False(
            state.IsMinimized);
        Assert.Equal(
            new WindowSize(
                2_560,
                1_440),
            state.ValidClientSize);
    }

    [Fact]
    public void RepeatedModeTransitionsRemainExplicitAndDeterministic()
    {
        var state =
            new WindowLifecycleState();
        state.InitializeClientSize(
            new WindowSize(
                1_600,
                900));

        Assert.True(
            state.BeginModeTransition(
                WindowMode.BorderlessFullscreen));
        Assert.Equal(
            WindowMode.Windowed,
            state.CurrentMode);
        Assert.Equal(
            WindowMode.BorderlessFullscreen,
            state.RequestedMode);
        Assert.True(
            state.TransitionInProgress);

        state.ApplySizeMessage(
            1_920,
            1_080,
            minimized: false);
        state.CompleteModeTransition(
            new WindowSize(
                1_920,
                1_080));

        Assert.Equal(
            WindowMode.BorderlessFullscreen,
            state.CurrentMode);
        Assert.False(
            state.TransitionInProgress);

        Assert.True(
            state.BeginModeTransition(
                WindowMode.Windowed));
        state.ApplySizeMessage(
            1_600,
            900,
            minimized: false);
        state.CompleteModeTransition(
            new WindowSize(
                1_600,
                900));

        Assert.Equal(
            WindowMode.Windowed,
            state.CurrentMode);
        Assert.Equal(
            new WindowSize(
                1_600,
                900),
            state.ValidClientSize);
        Assert.False(
            state.TransitionInProgress);
    }

    [Fact]
    public void AbortedTransitionReturnsRequestedModeToCurrentMode()
    {
        var state =
            new WindowLifecycleState();
        state.InitializeClientSize(
            new WindowSize(
                1_280,
                720));
        _ = state.BeginModeTransition(
            WindowMode.BorderlessFullscreen);

        state.AbortModeTransition();

        Assert.Equal(
            WindowMode.Windowed,
            state.CurrentMode);
        Assert.Equal(
            WindowMode.Windowed,
            state.RequestedMode);
        Assert.False(
            state.TransitionInProgress);
    }
}
