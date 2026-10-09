using System.Numerics;
using ForgeLine.Platform;
using Xunit;

namespace ForgeLine.Input.Tests;

public sealed class RtsCameraInputTests
{
    [Fact]
    public void LeavingViewportStopsEdgeScrollAndReentryDoesNotAccumulateDragDelta()
    {
        var input = new InputState();
        input.Apply(PlatformInputEvent.PointerMoved(1, 450));
        input.Apply(PlatformInputEvent.PointerLeft());
        var frame = new RtsCameraActionMapper().Map(input);
        Assert.False(frame.HasPointerPosition);
        Assert.Equal(Vector2.Zero, frame.PointerDelta);
        input.Apply(PlatformInputEvent.PointerMoved(1500, 450));
        Assert.True(input.HasPointerPosition);
        Assert.Equal(Vector2.Zero, input.PointerDelta);
    }

    [Fact]
    public void HudPointerCaptureSuppressesWheelDragAndEdgePanButPreservesKeyboardPan()
    {
        var state = new InputState();
        state.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.D));
        state.Apply(PlatformInputEvent.PointerMoved(0, 20));
        state.Apply(PlatformInputEvent.PointerMoved(0, 30));
        state.Apply(PlatformInputEvent.MouseWheel(0, 30, 120));
        state.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown,
            PlatformMouseButton.Middle, 0, 30));
        var frame = new RtsCameraActionMapper().Map(state, pointerCaptured: true);
        Assert.Equal(Vector2.UnitX, frame.Pan);
        Assert.Equal(0, frame.ZoomSteps);
        Assert.False(frame.DragPan);
        Assert.False(frame.HasPointerPosition);
        Assert.Equal(Vector2.Zero, frame.PointerDelta);
    }

    [Fact]
    public void SuppressedHeldInputRequiresReleaseAndFocusLossClearsSuppression()
    {
        var state = new InputState();
        state.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.W));
        state.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown,
            PlatformMouseButton.Left, 100, 100));
        state.SuppressHeldInput();
        state.BeginFrame();
        state.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.W));
        Assert.False(state.IsKeyDown(PlatformKey.W));
        state.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonUp,
            PlatformMouseButton.Left, 100, 100));
        state.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown,
            PlatformMouseButton.Left, 100, 100));
        Assert.True(state.IsMouseButtonDown(PlatformMouseButton.Left));
        state.Apply(PlatformInputEvent.FocusLost());
        state.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.W));
        Assert.True(state.IsKeyDown(PlatformKey.W));
    }

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
        Assert.True(state.FocusLostThisFrame);

        state.BeginFrame();

        Assert.False(state.FocusLostThisFrame);
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
