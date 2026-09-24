using System.Numerics;

namespace ForgeLine.Input;

public readonly record struct RtsCameraInputFrame(
    Vector2 Pan,
    float Rotation,
    float Pitch,
    float ZoomSteps,
    bool DragPan,
    bool HasPointerPosition,
    Vector2 PointerPosition,
    Vector2 PointerDelta);
