using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Intelligence;

public enum VisualTraceResult : byte { Clear, AreaOccluded, TerrainOccluded, BudgetExceeded, Invalid }

/// <summary>Ground visual sensing only. Weapon line of fire and radar detection have separate owners.</summary>
public sealed class VisualOcclusionPolicy
{
    private readonly ITerrainQuery _terrain;
    private readonly TacticalAreaSet _areas;
    public VisualOcclusionPolicy(ITerrainQuery terrain, TacticalAreaSet areas)
    {
        _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
        _areas = areas ?? throw new ArgumentNullException(nameof(areas));
    }
    public const int MaximumTerrainSamples = 64;
    public const float SampleSpacingMeters = 8;
    public VisualTraceResult Evaluate(Vector3 source, Vector3 target)
    {
        if (!Finite(source) || !Finite(target)) return VisualTraceResult.Invalid;
        if (_areas.BlocksVisual(source, target)) return VisualTraceResult.AreaOccluded;
        Vector3 start = source + Vector3.UnitY * 2, end = target + Vector3.UnitY * 1.5f;
        Vector3 delta = end - start;
        float distance = MathF.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
        double steps = Math.Ceiling(distance / SampleSpacingMeters);
        if (steps > MaximumTerrainSamples + 1) return VisualTraceResult.BudgetExceeded;
        int count = Math.Max(1, (int)steps);
        for (int index = 1; index < count; index++)
        {
            Vector3 point = Vector3.Lerp(start, end, index / (float)count);
            if (!_terrain.TrySampleHeight(point.X, point.Z, out float height)) return VisualTraceResult.Invalid;
            if (height + .1f >= point.Y) return VisualTraceResult.TerrainOccluded;
        }
        return VisualTraceResult.Clear;
    }
    private static bool Finite(Vector3 position) =>
        float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z);
}
