using System.Numerics;
using ForgeLine.Game;
using ForgeLine.World;

namespace ForgeLine.Presentation;

internal static class VegetationVisualMotion
{
    internal const float MaximumAngle = .025f;

    internal static bool Applies(in RenderInstance instance) =>
        instance.WorldFeature.Kind == WorldPresentationKind.Vegetation &&
        instance.WorldFeature.Visual is WorldVisualId.VegetationConifer or WorldVisualId.VegetationScrub or WorldVisualId.VegetationGrassClump;

    internal static AxisAlignedBounds CullingBounds(in RenderInstance instance)
    {
        AxisAlignedBounds bounds = PresentationBounds.ResolveAxisAlignedBounds(instance);
        if (!Applies(instance)) return bounds;
        // Authored vegetation spans local Y=0..1, with its root at the origin.
        Vector3 center = Vector3.Transform(new Vector3(0, .5f, 0), instance.Transform.ToMatrix());
        Vector3 extents = PresentationBounds.ResolveWorldAxisAlignedHalfExtents(instance);
        // Bound the full sweep around the local ground pivot without changing selection/collision bounds.
        float sweep = Vector3.Abs(instance.Transform.Scale).Length() * MaximumAngle;
        return new(Vector3.Min(bounds.Minimum, center - extents) - new Vector3(sweep),
            Vector3.Max(bounds.Maximum, center + extents) + new Vector3(sweep));
    }

    internal static Matrix4x4 Transform(in RenderInstance instance, float projectedDiameter, double presentationSeconds)
    {
        if (!double.IsFinite(presentationSeconds) || presentationSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(presentationSeconds));
        if (!Applies(instance) || !float.IsFinite(projectedDiameter) || projectedDiameter <= 18)
            return instance.Transform.ToMatrix();
        float fade = Math.Clamp((projectedDiameter - 18) / 54, 0, 1);
        fade = fade * fade * (3 - 2 * fade);
        float amplitude = instance.WorldFeature.Visual switch
        {
            WorldVisualId.VegetationConifer => .008f,
            WorldVisualId.VegetationScrub => .012f,
            _ => .02f
        };
        Vector3 position = instance.Transform.Position;
        float phase = MathF.Sin(position.X * .017f + position.Z * .023f) * MathF.PI;
        float time = (float)(presentationSeconds % 8) * (MathF.Tau / 8);
        float angle = amplitude * fade * (MathF.Sin(time + phase) + .25f * MathF.Sin(2 * time + phase));
        Matrix4x4 bend = Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(new Vector3(1, 0, .6f)), angle);
        return bend * instance.Transform.ToMatrix();
    }
}
