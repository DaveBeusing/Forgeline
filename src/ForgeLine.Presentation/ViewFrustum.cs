using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public readonly struct ViewFrustum
{
    private readonly Plane _left;
    private readonly Plane _right;
    private readonly Plane _bottom;
    private readonly Plane _top;
    private readonly Plane _near;
    private readonly Plane _far;

    private ViewFrustum(
        Plane left,
        Plane right,
        Plane bottom,
        Plane top,
        Plane near,
        Plane far)
    {
        _left = left;
        _right = right;
        _bottom = bottom;
        _top = top;
        _near = near;
        _far = far;
    }

    public static ViewFrustum FromViewProjection(Matrix4x4 matrix) =>
        new(
            Normalize(
                new Plane(
                    matrix.M11 + matrix.M14,
                    matrix.M21 + matrix.M24,
                    matrix.M31 + matrix.M34,
                    matrix.M41 + matrix.M44)),
            Normalize(
                new Plane(
                    -matrix.M11 + matrix.M14,
                    -matrix.M21 + matrix.M24,
                    -matrix.M31 + matrix.M34,
                    -matrix.M41 + matrix.M44)),
            Normalize(
                new Plane(
                    matrix.M12 + matrix.M14,
                    matrix.M22 + matrix.M24,
                    matrix.M32 + matrix.M34,
                    matrix.M42 + matrix.M44)),
            Normalize(
                new Plane(
                    -matrix.M12 + matrix.M14,
                    -matrix.M22 + matrix.M24,
                    -matrix.M32 + matrix.M34,
                    -matrix.M42 + matrix.M44)),
            Normalize(
                new Plane(
                    matrix.M13,
                    matrix.M23,
                    matrix.M33,
                    matrix.M43)),
            Normalize(
                new Plane(
                    matrix.M14 - matrix.M13,
                    matrix.M24 - matrix.M23,
                    matrix.M34 - matrix.M33,
                    matrix.M44 - matrix.M43)));

    public bool Intersects(AxisAlignedBounds bounds) =>
        IsInside(_left, bounds) &&
        IsInside(_right, bounds) &&
        IsInside(_bottom, bounds) &&
        IsInside(_top, bounds) &&
        IsInside(_near, bounds) &&
        IsInside(_far, bounds);

    private static bool IsInside(Plane plane, AxisAlignedBounds bounds)
    {
        Vector3 positive = new(
            plane.Normal.X >= 0.0f
                ? bounds.Maximum.X
                : bounds.Minimum.X,
            plane.Normal.Y >= 0.0f
                ? bounds.Maximum.Y
                : bounds.Minimum.Y,
            plane.Normal.Z >= 0.0f
                ? bounds.Maximum.Z
                : bounds.Minimum.Z);

        return Vector3.Dot(plane.Normal, positive) + plane.D >= 0.0f;
    }

    private static Plane Normalize(Plane plane)
    {
        float length = plane.Normal.Length();
        if (!float.IsFinite(length) || length <= float.Epsilon)
        {
            throw new ArgumentException(
                "View-projection matrix produced an invalid frustum plane.");
        }

        float inverseLength = 1.0f / length;
        return new Plane(
            plane.Normal * inverseLength,
            plane.D * inverseLength);
    }
}
