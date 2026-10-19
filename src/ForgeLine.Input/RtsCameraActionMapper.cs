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

    public RtsCameraInputFrame Map(InputState state, bool pointerCaptured = false,
        bool contextOwnsQe = false)
    {
        ArgumentNullException.ThrowIfNull(state);

        float panX =
            Axis(
                IsEitherDown(state, Bindings.PanRight, Bindings.PanRightAlternate, contextOwnsQe),
                IsEitherDown(state, Bindings.PanLeft, Bindings.PanLeftAlternate, contextOwnsQe));
        float panY =
            Axis(
                IsEitherDown(state, Bindings.PanForward, Bindings.PanForwardAlternate, contextOwnsQe),
                IsEitherDown(state, Bindings.PanBackward, Bindings.PanBackwardAlternate, contextOwnsQe));

        var pan = new Vector2(panX, panY);
        if (pan.LengthSquared() > 1.0f)
        {
            pan = Vector2.Normalize(pan);
        }

        float rotation = Axis(
            ContextKeyDown(state, Bindings.RotateRight, contextOwnsQe),
            ContextKeyDown(state, Bindings.RotateLeft, contextOwnsQe));
        float pitch = Axis(
            ContextKeyDown(state, Bindings.PitchUp, contextOwnsQe),
            ContextKeyDown(state, Bindings.PitchDown, contextOwnsQe));

        return new RtsCameraInputFrame(
            pan,
            rotation,
            pitch,
            pointerCaptured ? 0 : state.WheelDelta / 120.0f,
            !pointerCaptured && state.IsMouseButtonDown(Bindings.DragPanButton),
            !pointerCaptured && state.HasPointerPosition,
            state.PointerPosition,
            pointerCaptured ? Vector2.Zero : state.PointerDelta);
    }

    private static bool ContextKeyDown(InputState state, PlatformKey key, bool contextOwnsQe) =>
        (!contextOwnsQe || key is not (PlatformKey.Q or PlatformKey.E)) &&
        state.IsKeyDown(key);

    private static bool IsEitherDown(
        InputState state,
        PlatformKey primary,
        PlatformKey alternate, bool contextOwnsQe) =>
        ContextKeyDown(state, primary, contextOwnsQe) ||
        ContextKeyDown(state, alternate, contextOwnsQe);

    private static float Axis(bool positive, bool negative) =>
        (positive ? 1.0f : 0.0f) - (negative ? 1.0f : 0.0f);
}
