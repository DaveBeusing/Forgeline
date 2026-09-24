using System.Numerics;

namespace ForgeLine.Presentation;

public readonly record struct CameraMatrices(
    Matrix4x4 View,
    Matrix4x4 Projection,
    Matrix4x4 ViewProjection);
