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

internal readonly record struct MechanicalPartBinding(MechanicalMeshPart Part, Vector3 Pivot);

internal static class MechanicalTransforms
{
    public static void Compose(ReadOnlySpan<MechanicalPartBinding> parts, in RenderInstance instance,
        Matrix4x4 world, Span<Matrix4x4> transforms, float weight = 1)
    {
        if (parts.Length > MechanicalMeshValidation.MaximumParts || transforms.Length < parts.Length)
            throw new ArgumentException("Insufficient bounded mechanical transform storage.");
        MechanicalPose pose = instance.MechanicalPose;
        bool enabled = pose.IsSpecified && !instance.UnitFeature.IsWreck && !instance.BuildingFeature.IsDestroyed;
        for (int i = 0; i < parts.Length; i++)
        {
            MechanicalPartBinding binding = parts[i];
            MechanicalMeshPart part = binding.Part;
            Matrix4x4 motion = Matrix4x4.Identity;
            if (enabled)
            {
                float phase = (float)Math.IEEERemainder(pose.ActiveSeconds * part.Rate, Math.Tau);
                motion = part.Motion switch
                {
                    MechanicalMotionKind.TurretYaw => Matrix4x4.CreateRotationY(instance.UnitFeature.AimYawRadians),
                    MechanicalMotionKind.Cannon => Matrix4x4.CreateTranslation(0, 0, -Recoil(pose) * part.Travel) *
                        Matrix4x4.CreateRotationX(-Math.Clamp(pose.Elevation, -0.15f, 0.55f)),
                    MechanicalMotionKind.Wheel => Matrix4x4.CreateRotationX((float)Math.IEEERemainder(pose.TravelMeters * part.Rate, Math.Tau)),
                    MechanicalMotionKind.InfantryStride => Matrix4x4.CreateRotationX(
                        MathF.Sin((float)Math.IEEERemainder(pose.TravelMeters * part.Rate, Math.Tau)) * part.Travel),
                    MechanicalMotionKind.Fan or MechanicalMotionKind.Radar => Matrix4x4.CreateRotationY(phase),
                    MechanicalMotionKind.Door => Matrix4x4.CreateTranslation(0, pose.Door * part.Travel, 0),
                    MechanicalMotionKind.Crane => Matrix4x4.CreateRotationY(MathF.Sin(phase) * part.Travel),
                    _ => Matrix4x4.Identity
                };
            }
            if (weight <= 0) motion = Matrix4x4.Identity;
            else if (weight < 1 && Matrix4x4.Decompose(motion, out _, out Quaternion rotation, out Vector3 translation))
                motion = Matrix4x4.CreateFromQuaternion(Quaternion.Slerp(Quaternion.Identity, rotation, weight)) *
                    Matrix4x4.CreateTranslation(translation * weight);
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
