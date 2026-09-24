using System.Numerics;
using ForgeLine.Platform;
using Xunit;

namespace ForgeLine.Input.Tests;

public sealed class RtsCameraInputTests
{
    [Fact]
    public void InputStatePreservesHeldStateAndResetsFrameTransientValues()
    {
        var state = new InputState();

        state.Apply(
            PlatformInputEvent.KeyChanged(
                PlatformInputEventKind.KeyDown,
                PlatformKey.W));
        state.Apply(PlatformInputEvent.PointerMoved(100, 80));
        state.Apply(PlatformInputEvent.PointerMoved(115, 105));
        state.Apply(
            PlatformInputEvent.MouseButtonChanged(
                PlatformInputEventKind.MouseButtonDown,
                PlatformMouseButton.Middle,
                115,
                105));
        state.Apply(PlatformInputEvent.MouseWheel(115, 105, 120));

        Assert.True(state.IsKeyDown(PlatformKey.W));
        Assert.True(state.IsMouseButtonDown(PlatformMouseButton.Middle));
        Assert.True(state.HasPointerPosition);
        Assert.Equal(new Vector2(115, 105), state.PointerPosition);
        Assert.Equal(new Vector2(15, 25), state.PointerDelta);
        Assert.Equal(120, state.WheelDelta);

        state.BeginFrame();

        Assert.True(state.IsKeyDown(PlatformKey.W));
        Assert.True(state.IsMouseButtonDown(PlatformMouseButton.Middle));
        Assert.Equal(Vector2.Zero, state.PointerDelta);
        Assert.Equal(0, state.WheelDelta);
    }

    [Fact]
    public void FocusLossClearsPressedAndTransientInputState()
    {
        var state = new InputState();

        state.Apply(
            PlatformInputEvent.KeyChanged(
                PlatformInputEventKind.KeyDown,
                PlatformKey.D));
        state.Apply(
            PlatformInputEvent.MouseButtonChanged(
                PlatformInputEventKind.MouseButtonDown,
                PlatformMouseButton.Middle,
                20,
                30));
        state.Apply(PlatformInputEvent.PointerMoved(30, 40));
        state.Apply(PlatformInputEvent.MouseWheel(30, 40, -120));

        state.Apply(PlatformInputEvent.FocusLost());

        Assert.False(state.IsKeyDown(PlatformKey.D));
        Assert.False(state.IsMouseButtonDown(PlatformMouseButton.Middle));
        Assert.False(state.HasPointerPosition);
        Assert.Equal(Vector2.Zero, state.PointerDelta);
        Assert.Equal(0, state.WheelDelta);
    }

    [Fact]
    public void ActionMapperProducesNormalizedRtsCameraActions()
    {
        var state = new InputState();
        var mapper = new RtsCameraActionMapper();

        state.Apply(
            PlatformInputEvent.KeyChanged(
                PlatformInputEventKind.KeyDown,
                PlatformKey.W));
        state.Apply(
            PlatformInputEvent.KeyChanged(
                PlatformInputEventKind.KeyDown,
                PlatformKey.D));
        state.Apply(
            PlatformInputEvent.KeyChanged(
                PlatformInputEventKind.KeyDown,
                PlatformKey.E));
        state.Apply(
            PlatformInputEvent.KeyChanged(
                PlatformInputEventKind.KeyDown,
                PlatformKey.R));
        state.Apply(PlatformInputEvent.PointerMoved(400, 300));
        state.Apply(PlatformInputEvent.PointerMoved(405, 294));
        state.Apply(
            PlatformInputEvent.MouseButtonChanged(
                PlatformInputEventKind.MouseButtonDown,
                PlatformMouseButton.Middle,
                405,
                294));
        state.Apply(PlatformInputEvent.MouseWheel(405, 294, 120));

        RtsCameraInputFrame frame = mapper.Map(state);

        Assert.InRange(frame.Pan.X, 0.7070f, 0.7072f);
        Assert.InRange(frame.Pan.Y, 0.7070f, 0.7072f);
        Assert.Equal(1.0f, frame.Rotation);
        Assert.Equal(1.0f, frame.Pitch);
        Assert.Equal(1.0f, frame.ZoomSteps);
        Assert.True(frame.DragPan);
        Assert.True(frame.HasPointerPosition);
        Assert.Equal(new Vector2(405, 294), frame.PointerPosition);
        Assert.Equal(new Vector2(5, -6), frame.PointerDelta);
    }
}
