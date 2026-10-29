using System.Numerics;

namespace ForgeLine.Presentation;

/// <summary>Immutable public infrastructure footprints; never changes collision or construction permission.</summary>
public sealed partial class TerrainInfrastructureMask
{
    public const int MaximumRegions = 4096;
    internal const int MaximumRegionsPerChunk = 64;
    internal readonly Region[] Regions;
    public static TerrainInfrastructureMask Empty { get; } = new([]);

    public TerrainInfrastructureMask(IEnumerable<TerrainBlendRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        var input = regions.Take(MaximumRegions + 1).ToArray();
        if (input.Length > MaximumRegions || input.Distinct().Count() != input.Length)
            throw new ArgumentException("Infrastructure regions must be unique and bounded.", nameof(regions));
        foreach (var r in input)
            if (r.Slot is not (TerrainMaterialSlot.Dirt or TerrainMaterialSlot.Mud or TerrainMaterialSlot.Gravel or
                    TerrainMaterialSlot.IndustrialGround or TerrainMaterialSlot.Concrete) ||
                !float.IsFinite(r.Center.X) || !float.IsFinite(r.Center.Y) ||
                MathF.Abs(r.Center.X) > 1_000_000 || MathF.Abs(r.Center.Y) > 1_000_000 ||
                !float.IsFinite(r.HalfExtents.X) || !float.IsFinite(r.HalfExtents.Y) ||
                r.HalfExtents.X is <= 0 or > 1_000_000 || r.HalfExtents.Y is <= 0 or > 1_000_000 ||
                !float.IsFinite(r.FeatherMeters) || r.FeatherMeters is < 0 or > 1024 ||
                !float.IsFinite(r.RotationRadians) || MathF.Abs(r.RotationRadians) > MathF.PI ||
                !float.IsFinite(r.Strength) || r.Strength is < 0 or > 1)
                throw new ArgumentException("Invalid infrastructure material or finite footprint.", nameof(regions));
        Regions = input.OrderBy(r => Priority(r.Slot)).ThenBy(r => r.Center.Y).ThenBy(r => r.Center.X)
            .ThenBy(r => r.HalfExtents.Y).ThenBy(r => r.HalfExtents.X).ThenBy(r => r.RotationRadians)
            .ThenBy(r => r.FeatherMeters).ThenBy(r => r.Strength).Select(r => new Region(r)).ToArray();
    }

    public static TerrainBlendRegion Road(Vector2 start, Vector2 end, float widthMeters, float featherMeters = 4)
    {
        Vector2 delta = end - start;
        if (!float.IsFinite(delta.Length()) || delta.Length() <= 0 || !float.IsFinite(widthMeters) || widthMeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(widthMeters));
        return new(TerrainMaterialSlot.Gravel, (start + end) * .5f,
            new(delta.Length() * .5f, widthMeters * .5f), featherMeters, MathF.Atan2(delta.Y, delta.X), 1);
    }

    private static int Priority(TerrainMaterialSlot slot) => slot switch
    {
        TerrainMaterialSlot.Mud => 0, TerrainMaterialSlot.Dirt => 1, TerrainMaterialSlot.Gravel => 2,
        TerrainMaterialSlot.IndustrialGround => 3, _ => 4
    };

    internal sealed class Region(TerrainBlendRegion value)
    {
        internal TerrainBlendRegion Value { get; } = value;
        private readonly float _cosine = MathF.Cos(value.RotationRadians), _sine = MathF.Sin(value.RotationRadians);

        internal bool Touches(float minX, float minZ, float maxX, float maxZ, float padding)
        {
            Vector2 half = Value.HalfExtents + new Vector2(padding);
            float x = MathF.Abs(_cosine) * half.X + MathF.Abs(_sine) * half.Y + Value.FeatherMeters;
            float z = MathF.Abs(_sine) * half.X + MathF.Abs(_cosine) * half.Y + Value.FeatherMeters;
            return Value.Strength > 0 && Value.Center.X + x >= minX && Value.Center.X - x <= maxX &&
                Value.Center.Y + z >= minZ && Value.Center.Y - z <= maxZ;
        }

        internal float Weight(Vector2 point, float padding)
        {
            Vector2 offset = point - Value.Center;
            float dx = MathF.Max(0, MathF.Abs(offset.X * _cosine + offset.Y * _sine) - Value.HalfExtents.X - padding);
            float dz = MathF.Max(0, MathF.Abs(-offset.X * _sine + offset.Y * _cosine) - Value.HalfExtents.Y - padding);
            float distance = MathF.Sqrt(dx * dx + dz * dz);
            if (distance == 0) return Value.Strength;
            if (Value.FeatherMeters == 0 || distance >= Value.FeatherMeters) return 0;
            float t = 1 - distance / Value.FeatherMeters;
            return Value.Strength * t * t * (3 - 2 * t);
        }
    }
}
