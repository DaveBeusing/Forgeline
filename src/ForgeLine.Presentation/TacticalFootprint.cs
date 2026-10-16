using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

/// <summary>Terrain-conforming oriented footprint geometry shared by production components.</summary>
internal static class TacticalFootprint
{
    internal static Vector3 Corner(in RenderInstance instance, int corner, float insetX = 0, float insetZ = 0, ITerrainQuery? terrain = null)
    {
        Vector3 extent = PresentationBounds.ResolveLocalHalfExtents(instance);
        float x = (corner is 0 or 3 ? -1 : 1) * MathF.Max(0, extent.X * 1.1f - insetX);
        float z = (corner is 0 or 1 ? -1 : 1) * MathF.Max(0, extent.Z * 1.1f - insetZ);
        Vector3 offset = Vector3.Transform(new Vector3(x,0,z), PresentationBounds.ResolveRotation(instance.Transform.Rotation));
        Vector3 point = instance.Transform.Position + offset;
        float height = PresentationBounds.ResolveGroundPlaneY(instance);
        if (terrain is not null && terrain.TrySampleHeight(point.X, point.Z, out float sampled) && float.IsFinite(sampled)) height = sampled;
        return new(point.X, height + .08f, point.Z);
    }
}
