using System.Numerics;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

// Logical deposit bounds describe extraction volumes, not mineral silhouette height.
internal static class ResourceDepositVisualTransform
{
    public static RenderTransform Apply(in RenderTransform source, in WorldFeaturePresentationMetadata feature)
    {
        if (feature.Kind != WorldPresentationKind.ResourceDeposit ||
            !float.IsFinite(source.Scale.Y) || source.Scale.Y <= 0)
            return source;
        float minimumHeight = feature.Visual switch
        {
            WorldVisualId.ResourceFerrousOre => 16,
            WorldVisualId.ResourceSilicates => 28,
            WorldVisualId.ResourceVolatiles => 8,
            WorldVisualId.ResourceRareElements => 32,
            _ => 0
        };
        float height = MathF.Max(source.Scale.Y, minimumHeight);
        if (height == source.Scale.Y) return source;
        return new RenderTransform(
            source.Position + Vector3.UnitY * ((height - source.Scale.Y) * .5f),
            source.Rotation,
            new Vector3(source.Scale.X, height, source.Scale.Z));
    }
}
