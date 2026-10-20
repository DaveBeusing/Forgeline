using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Presentation.TerrainResidency;

/// <summary>Conservative maximum-axis pixel displacement, with independent main/light projections.</summary>
internal readonly record struct TerrainLodProjection(float NearDepth, float MaximumCoordinate, float Focal, bool Orthographic)
{
    internal float Pixels(float heightError)
    {
        if (!float.IsFinite(Focal)) return float.PositiveInfinity;
        if (heightError == 0) return 0;
        if (!float.IsFinite(heightError)) return float.PositiveInfinity;
        if (Orthographic) return heightError * Focal;
        if (NearDepth <= heightError + .01f) return float.PositiveInfinity;
        return Focal * heightError * (1 + MaximumCoordinate / NearDepth) / (NearDepth - heightError);
    }

    internal static TerrainLodProjection Main(AxisAlignedBounds bounds, CameraMatrices matrices, int width, int height)
    {
        Vector3 center = (bounds.Minimum + bounds.Maximum) * .5f;
        Vector3 extent = (bounds.Maximum - bounds.Minimum) * .5f;
        Matrix4x4 v = matrices.View; Vector3 viewCenter = Vector3.Transform(center, v);
        float zRadius = MathF.Abs(v.M13) * extent.X + MathF.Abs(v.M23) * extent.Y + MathF.Abs(v.M33) * extent.Z;
        float xRadius = MathF.Abs(v.M11) * extent.X + MathF.Abs(v.M21) * extent.Y + MathF.Abs(v.M31) * extent.Z;
        float yRadius = MathF.Abs(v.M12) * extent.X + MathF.Abs(v.M22) * extent.Y + MathF.Abs(v.M32) * extent.Z;
        return new(-viewCenter.Z - zRadius, Math.Max(MathF.Abs(viewCenter.X) + xRadius, MathF.Abs(viewCenter.Y) + yRadius),
            Math.Max(width * .5f * MathF.Abs(matrices.Projection.M11), height * .5f * MathF.Abs(matrices.Projection.M22)), false);
    }

    internal static TerrainLodProjection Shadow(Matrix4x4 viewProjection, int resolution)
    {
        // Unusual projective light matrices remain supported through full geometry.
        if (resolution <= 0 || viewProjection.M14 != 0 || viewProjection.M24 != 0 || viewProjection.M34 != 0 || viewProjection.M44 != 1)
            return new(0, 0, float.PositiveInfinity, false);
        // Include depth displacement, not only XY silhouette displacement (D3D depth is 0..1).
        float scale = MathF.Sqrt(viewProjection.M21 * viewProjection.M21 + viewProjection.M22 * viewProjection.M22 +
            4 * viewProjection.M23 * viewProjection.M23) * resolution * .5f;
        return new(0, 0, scale, true);
    }
}

internal static class TerrainLodSelector
{
    internal static int Select(ReadOnlySpan<float> heights, ReadOnlySpan<float> normals, ReadOnlySpan<float> tints,
        TerrainLodProjection projection, TerrainPreparationSettings settings, int previous, bool protectedSurface = false)
    {
        if (!settings.GeometryLodEnabled || protectedSurface) return 0;
        int chosen = 0;
        for (int level = heights.Length - 1; level > 0; level--)
            if (Eligible(heights, normals, tints, projection, settings, level, settings.MaximumPixelError * (2f / 3f))) { chosen = level; break; }
        if (previous > chosen && previous < heights.Length && Eligible(heights, normals, tints, projection, settings, previous, settings.MaximumPixelError)) chosen = previous;
        return chosen;
    }
    private static bool Eligible(ReadOnlySpan<float> heights, ReadOnlySpan<float> normals, ReadOnlySpan<float> tints,
        TerrainLodProjection projection, TerrainPreparationSettings settings, int level, float pixels) =>
        normals[level] <= settings.MaximumNormalDeviation && tints[level] <= settings.MaximumTintDeviation &&
        projection.Pixels(heights[level]) <= pixels;
}
