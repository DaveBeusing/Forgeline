using System.Numerics;

namespace ForgeLine.Presentation;

public readonly record struct RtsCameraDiagnostics(
    Vector3 Position,
    Vector3 Target,
    float YawDegrees,
    float PitchDegrees,
    float Distance,
    bool HasPointerPosition,
    Vector2 PointerPosition);
