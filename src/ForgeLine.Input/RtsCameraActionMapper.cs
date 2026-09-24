using System.Numerics;
using ForgeLine.Platform;

namespace ForgeLine.Input;

public sealed class RtsCameraActionMapper
{
    public RtsCameraActionMapper(RtsCameraBindings? bindings = null)
    {
        Bindings = bindings ?? new RtsCameraBindings();
    }

    public RtsCameraBindings Bindings { get; }

    public RtsCameraInputFrame Map(InputState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        float panX =
            Axis(
                IsEitherDown(state, Bindings.PanRight, Bindings.PanRightAlternate),
                IsEitherDown(state, Bindings.PanLeft, Bindings.PanLeftAlternate));
        float panY =
            Axis(
                IsEitherDown(state, Bindings.PanForward, Bindings.PanForwardAlternate),
                IsEitherDown(state, Bindings.PanBackward, Bindings.PanBackwardAlternate));

        var pan = new Vector2(panX, panY);
        if (pan.LengthSquared() > 1.0f)
        {
            pan = Vector2.Normalize(pan);
        }

        float rotation = Axis(
            state.IsKeyDown(Bindings.RotateRight),
            state.IsKeyDown(Bindings.RotateLeft));
        float pitch = Axis(
            state.IsKeyDown(Bindings.PitchUp),
            state.IsKeyDown(Bindings.PitchDown));

        return new RtsCameraInputFrame(
            pan,
            rotation,
            pitch,
            state.WheelDelta / 120.0f,
            state.IsMouseButtonDown(Bindings.DragPanButton),
            state.HasPointerPosition,
            state.PointerPosition,
            state.PointerDelta);
    }

    private static bool IsEitherDown(
        InputState state,
        PlatformKey primary,
        PlatformKey alternate) =>
        state.IsKeyDown(primary) || state.IsKeyDown(alternate);

    private static float Axis(bool positive, bool negative) =>
        (positive ? 1.0f : 0.0f) - (negative ? 1.0f : 0.0f);
}
