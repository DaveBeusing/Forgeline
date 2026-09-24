using System.Numerics;

namespace ForgeLine.Game;

public readonly record struct WorldTransform(
    Vector3 Position,
    Quaternion Rotation,
    Vector3 Scale)
{
    public static WorldTransform Identity =>
        new(Vector3.Zero, Quaternion.Identity, Vector3.One);

    public Matrix4x4 ToMatrix()
    {
        return Matrix4x4.CreateScale(Scale) *
               Matrix4x4.CreateFromQuaternion(Rotation) *
               Matrix4x4.CreateTranslation(Position);
    }
}
