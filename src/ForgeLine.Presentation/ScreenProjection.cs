using System.Numerics;

namespace ForgeLine.Presentation;

public readonly record struct ScreenProjection(
    Vector2 Position,
    float Depth,
    bool IsVisible);
