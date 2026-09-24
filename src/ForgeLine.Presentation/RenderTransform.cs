using System.Numerics;

namespace ForgeLine.Presentation;

public readonly record struct RenderTransform(
    Vector3 Position,
    Quaternion Rotation,
    Vector3 Scale)
{
    public static RenderTransform Identity =>
        new(Vector3.Zero, Quaternion.Identity, Vector3.One);

    public Matrix4x4 ToMatrix()
    {
        return Matrix4x4.CreateScale(Scale) *
               Matrix4x4.CreateFromQuaternion(Rotation) *
               Matrix4x4.CreateTranslation(Position);
    }

    public static RenderTransform Interpolate(
        in RenderTransform previous,
        in RenderTransform current,
        float alpha)
    {
        float t = Math.Clamp(alpha, 0.0f, 1.0f);

        return new RenderTransform(
            Vector3.Lerp(previous.Position, current.Position, t),
            Quaternion.Normalize(
                Quaternion.Slerp(previous.Rotation, current.Rotation, t)),
            Vector3.Lerp(previous.Scale, current.Scale, t));
    }
}
