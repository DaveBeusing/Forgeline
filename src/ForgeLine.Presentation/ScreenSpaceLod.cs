using System.Numerics;

namespace ForgeLine.Presentation;

/// <summary>Projected bounding-sphere diameter, in reference-display pixels.</summary>
public static class ScreenSpaceLod
{
    public const float Hysteresis = 0.12f;

    public static float ProjectedDiameter(
        Vector3 center, float radius, in CameraMatrices matrices, int viewportHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewportHeight);
        if (!float.IsFinite(radius) || radius < 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(radius));
        }

        Vector3 viewCenter = Vector3.Transform(center, matrices.View);
        float depth = -viewCenter.Z;
        if (!float.IsFinite(depth) || depth <= radius)
        {
            return float.MaxValue;
        }

        // Normalize display height to the authoring reference. Render resolution
        // changes preserve geometric detail, while field of view and size affect it.
        return radius * matrices.Projection.M22 * RtsVisualReference.Height / depth;
    }

    public static int Select(float diameter, int previousLod, float lod1Pixels = 90.0f, float lod2Pixels = 28.0f)
    {
        if (!float.IsFinite(diameter) || diameter < 0.0f ||
            !float.IsFinite(lod1Pixels) || !float.IsFinite(lod2Pixels) ||
            lod2Pixels <= 0.0f || lod1Pixels <= lod2Pixels || previousLod is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(diameter));
        }

        float first = lod1Pixels * (previousLod >= 1 ? 1.0f + Hysteresis : 1.0f - Hysteresis);
        float second = lod2Pixels * (previousLod >= 2 ? 1.0f + Hysteresis : 1.0f - Hysteresis);
        if (diameter < second) return 2;
        return diameter < first ? 1 : 0;
    }
}
