using System.Numerics;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

public readonly record struct VegetationRenderSettings
{
    public VegetationRenderSettings() { }
    public float Density { get; init; } = 1;
    public float MaximumDistanceMeters { get; init; } = 4000;

    public void Validate()
    {
        if (!float.IsFinite(Density) || Density is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(Density));
        if (!float.IsFinite(MaximumDistanceMeters) || MaximumDistanceMeters is < 100 or > 4000)
            throw new ArgumentOutOfRangeException(nameof(MaximumDistanceMeters));
    }

    internal bool Includes(in RenderInstance instance, Vector3 cameraPosition)
    {
        if (instance.WorldFeature.Kind != WorldPresentationKind.Vegetation) return true;
        if (Vector3.DistanceSquared(instance.Transform.Position, cameraPosition) > MaximumDistanceMeters * MaximumDistanceMeters) return false;
        if (Density == 1) return true;
        if (Density == 0) return false;
        uint identity = unchecked(instance.Entity.Index * 0x85ebca6bu ^ instance.DebugIdentity * 0xc2b2ae35u ^ (uint)instance.WorldFeature.Visual);
        uint hash = unchecked(identity * 0x9e3779b9u);
        hash ^= hash >> 16;
        return hash / 4294967296.0 < Density;
    }
}
