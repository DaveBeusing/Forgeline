using System.Numerics;
using ForgeLine.Assets;

namespace ForgeLine.Presentation;

/// <summary>Copied completed-tick cosmetic state; never gameplay authority.</summary>
public readonly record struct MechanicalPose(
    bool IsSpecified,
    double TravelMeters,
    double ActiveSeconds,
    double Seconds,
    double LastShotSeconds,
    float Elevation,
    float Door)
{
    public static MechanicalPose Interpolate(MechanicalPose previous, MechanicalPose current, float alpha)
    {
        if (!previous.IsSpecified || !current.IsSpecified) return current;
        double t = float.IsFinite(alpha) ? Math.Clamp(alpha, 0, 1) : 1;
        return current with
        {
            TravelMeters = previous.TravelMeters + (current.TravelMeters - previous.TravelMeters) * t,
            ActiveSeconds = previous.ActiveSeconds + (current.ActiveSeconds - previous.ActiveSeconds) * t,
            Seconds = previous.Seconds + (current.Seconds - previous.Seconds) * t,
            Elevation = previous.Elevation + (current.Elevation - previous.Elevation) * (float)t,
            Door = previous.Door + (current.Door - previous.Door) * (float)t
        };
    }
}

internal readonly record struct MechanicalPartBinding(MechanicalMeshPart Part, Vector3 Pivot)
{
    public int GeometryPartIndex { get; init; } = -1;
    public Vector3 GeometryOffset { get; init; }
}

/// <summary>Mesh-load-only equivalence: identical indexed triangles up to geometric translation share a draw.</summary>
internal static class MechanicalGeometryBatching
{
    internal static void Assign(RuntimeMeshData data, Span<MechanicalPartBinding> parts)
    {
        for (int i = 0; i < parts.Length; i++)
        {
            int canonical = i; Vector3 offset = Vector3.Zero;
            for (int j = 0; j < i; j++)
                if (parts[j].GeometryPartIndex == j && Equivalent(data, parts[i], parts[j], out var translation))
                { canonical = j; offset = translation; break; }
            parts[i] = parts[i] with { GeometryPartIndex = canonical, GeometryOffset = offset };
        }
    }
    private static bool Equivalent(RuntimeMeshData data, in MechanicalPartBinding a, in MechanicalPartBinding b, out Vector3 offset)
    {
        offset = Vector3.Zero;
        if (a.Part.IndexCount != b.Part.IndexCount) return false;
        RuntimeMeshVertex firstA = data.Vertices[(int)data.Indices[a.Part.FirstIndex]];
        RuntimeMeshVertex firstB = data.Vertices[(int)data.Indices[b.Part.FirstIndex]];
        offset = new(firstA.X - firstB.X, firstA.Y - firstB.Y, firstA.Z - firstB.Z);
        for (int k = 0; k < a.Part.IndexCount; k++)
        {
            RuntimeMeshVertex x = data.Vertices[(int)data.Indices[a.Part.FirstIndex + k]];
            RuntimeMeshVertex y = data.Vertices[(int)data.Indices[b.Part.FirstIndex + k]];
            if (MathF.Abs(x.X - y.X - offset.X) > .00001f || MathF.Abs(x.Y - y.Y - offset.Y) > .00001f || MathF.Abs(x.Z - y.Z - offset.Z) > .00001f ||
                x.NormalX != y.NormalX || x.NormalY != y.NormalY || x.NormalZ != y.NormalZ || x.U != y.U || x.V != y.V ||
                MathF.Abs(x.TangentX - y.TangentX) > .000001f || MathF.Abs(x.TangentY - y.TangentY) > .000001f || MathF.Abs(x.TangentZ - y.TangentZ) > .000001f || x.TangentW != y.TangentW) return false;
        }
        return true;
    }
}


internal static class MechanicalMotionSpace
{
    public static Matrix4x4 Rotation(Vector3 scale, Quaternion rotation)
    {
        if (rotation == Quaternion.Identity) return Matrix4x4.Identity;
        if (!Matrix4x4.Invert(Matrix4x4.CreateScale(scale), out Matrix4x4 inverse))
            return Matrix4x4.CreateFromQuaternion(rotation);
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * inverse;
    }

    public static Vector3 RotateOffset(Vector3 offset, Vector3 scale, Quaternion rotation) =>
        Vector3.Transform(offset, Rotation(scale, rotation));
}

internal static class MechanicalTransforms
{
    public static void Compose(ReadOnlySpan<MechanicalPartBinding> parts, in RenderInstance instance,
        Matrix4x4 world, Span<Matrix4x4> transforms, float weight = 1)
    {
        if (parts.Length > MechanicalMeshValidation.MaximumParts || transforms.Length < parts.Length)
            throw new ArgumentException("Insufficient bounded mechanical transform storage.");
        MechanicalPose pose = instance.MechanicalPose;
        bool enabled = pose.IsSpecified && !instance.UnitFeature.IsWreck && !instance.BuildingFeature.IsDestroyed;
        if (!Matrix4x4.Decompose(world, out Vector3 scale, out _, out _)) scale = instance.Transform.Scale;
        for (int i = 0; i < parts.Length; i++)
        {
            MechanicalPartBinding binding = parts[i];
            MechanicalMeshPart part = binding.Part;
            Quaternion rotation = Quaternion.Identity;
            Vector3 translation = Vector3.Zero;
            if (enabled)
            {
                float phase = (float)Math.IEEERemainder(pose.ActiveSeconds * part.Rate, Math.Tau);
                rotation = part.Motion switch
                {
                    MechanicalMotionKind.TurretYaw => Quaternion.CreateFromAxisAngle(Vector3.UnitY, instance.UnitFeature.AimYawRadians),
                    MechanicalMotionKind.Cannon => Quaternion.CreateFromAxisAngle(Vector3.UnitX, -Math.Clamp(pose.Elevation, -0.15f, 0.55f)),
                    MechanicalMotionKind.InfantryStride => Quaternion.CreateFromAxisAngle(Vector3.UnitX,
                        MathF.Sin((float)Math.IEEERemainder(pose.TravelMeters * part.Rate, Math.Tau)) * part.Travel),
                    MechanicalMotionKind.Wheel => Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.IEEERemainder(pose.TravelMeters * part.Rate, Math.Tau)),
                    MechanicalMotionKind.Fan or MechanicalMotionKind.Radar => Quaternion.CreateFromAxisAngle(Vector3.UnitY, phase),
                    MechanicalMotionKind.Crane => Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.Sin(phase) * part.Travel),
                    _ => Quaternion.Identity
                };
                translation = part.Motion switch
                {
                    MechanicalMotionKind.Cannon => new Vector3(0, 0, -Recoil(pose) * part.Travel),
                    MechanicalMotionKind.Door => new Vector3(0, pose.Door * part.Travel, 0),
                    _ => Vector3.Zero
                };
            }
            if (weight <= 0) { rotation = Quaternion.Identity; translation = Vector3.Zero; }
            else if (weight < 1) { rotation = Quaternion.Slerp(Quaternion.Identity, rotation, weight); translation *= weight; }
            Matrix4x4 motion = Matrix4x4.CreateTranslation(translation) * MechanicalMotionSpace.Rotation(scale, rotation);
            Matrix4x4 local = Matrix4x4.CreateTranslation(-binding.Pivot) * motion * Matrix4x4.CreateTranslation(binding.Pivot);
            transforms[i] = local * (part.Parent < 0 ? world : transforms[part.Parent]);
        }
    }

    private static float Recoil(MechanicalPose pose)
    {
        double age = pose.Seconds - pose.LastShotSeconds;
        if (pose.LastShotSeconds < 0 || age < 0 || age >= 0.35) return 0;
        // Fast impulse followed by a smooth, heavier return.
        double t = age < 0.05 ? age / 0.05 : 1 - (age - 0.05) / 0.30;
        return (float)(t * t * (3 - 2 * t));
    }
}
