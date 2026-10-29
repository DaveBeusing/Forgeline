using System.Security.Cryptography;

namespace ForgeLine.World;

public enum ScatterAreaKind : byte { Rectangle = 1, Corridor = 2, Yard = 3 }
public readonly record struct ScatterArea(ScatterAreaKind Kind, long X0, long Z0, long X1, long Z1, int PaddingMillimeters = 0);

/// <summary>Queries a frozen complete gameplay-clearance snapshot, including candidate footprints.</summary>
public interface IScatterClearance
{
    string Sha256 { get; }
    bool IsBlocked(long xMillimeters, long zMillimeters, int radiusMillimeters);
    bool IsYard(long xMillimeters, long zMillimeters, int radiusMillimeters);
    bool OverlapsYard(long xMillimeters, long zMillimeters, int radiusMillimeters);
}

/// <summary>Immutable 64 m spatial buckets; no entities, global query buffers or quadratic scene scans.</summary>
public sealed class ScatterClearanceIndex : IScatterClearance
{
    public const int MaximumAreas = 65_536;
    public const int MaximumCellReferences = 2_000_000;
    private const int CellSize = 64_000;
    private readonly ScatterArea[] _areas;
    private readonly Dictionary<(long X, long Z), int[]> _cells;

    public ScatterClearanceIndex(IEnumerable<ScatterArea> areas)
    {
        ArgumentNullException.ThrowIfNull(areas);
        var list = new List<ScatterArea>();
        foreach (var a in areas)
        {
            if (list.Count == MaximumAreas || !Enum.IsDefined(a.Kind) ||
                a.X0 is < -1_000_000_000 or > 1_000_000_000 || a.Z0 is < -1_000_000_000 or > 1_000_000_000 ||
                a.X1 is < -1_000_000_000 or > 1_000_000_000 || a.Z1 is < -1_000_000_000 or > 1_000_000_000 ||
                a.PaddingMillimeters is < 0 or > 128_000 ||
                a.Kind == ScatterAreaKind.Yard && a.PaddingMillimeters != 0 ||
                a.Kind != ScatterAreaKind.Corridor && (a.X0 > a.X1 || a.Z0 > a.Z1))
                throw new ArgumentException("Clearance area exceeds supported geometry or count limits.", nameof(areas));
            list.Add(a);
        }
        _areas = list.Distinct().OrderBy(a => a.Kind).ThenBy(a => a.Z0).ThenBy(a => a.X0)
            .ThenBy(a => a.Z1).ThenBy(a => a.X1).ThenBy(a => a.PaddingMillimeters).ToArray();
        var buckets = new Dictionary<(long X, long Z), List<int>>();
        long references = 0;
        var writer = new WorldCanonicalWriter(); writer.WriteUInt32(1); writer.WriteInt32(_areas.Length);
        for (int i = 0; i < _areas.Length; i++)
        {
            var a = _areas[i];
            writer.WriteUInt32((uint)a.Kind); writer.WriteInt64(a.X0); writer.WriteInt64(a.Z0);
            writer.WriteInt64(a.X1); writer.WriteInt64(a.Z1); writer.WriteInt32(a.PaddingMillimeters);
            long minX = Floor(Math.Min(a.X0, a.X1) - a.PaddingMillimeters), maxX = Floor(Math.Max(a.X0, a.X1) + a.PaddingMillimeters);
            long minZ = Floor(Math.Min(a.Z0, a.Z1) - a.PaddingMillimeters), maxZ = Floor(Math.Max(a.Z0, a.Z1) + a.PaddingMillimeters);
            references += checked((maxX - minX + 1) * (maxZ - minZ + 1));
            if (references > MaximumCellReferences) throw new ArgumentException("Clearance spatial-index budget exceeded.", nameof(areas));
            for (long z = minZ; z <= maxZ; z++) for (long x = minX; x <= maxX; x++)
            { if (!buckets.TryGetValue((x, z), out var bucket)) buckets.Add((x, z), bucket = []); bucket.Add(i); }
        }
        _cells = buckets.ToDictionary(p => p.Key, p => p.Value.ToArray());
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(writer.ToArray()));
    }

    public string Sha256 { get; }
    public int AreaCount => _areas.Length;
    public int OccupiedCellCount => _cells.Count;
    public bool IsBlocked(long xMillimeters, long zMillimeters, int radiusMillimeters) => Query(xMillimeters, zMillimeters, radiusMillimeters, 0);
    public bool IsYard(long xMillimeters, long zMillimeters, int radiusMillimeters) => Query(xMillimeters, zMillimeters, radiusMillimeters, 1);
    public bool OverlapsYard(long xMillimeters, long zMillimeters, int radiusMillimeters) => Query(xMillimeters, zMillimeters, radiusMillimeters, 2);

    private bool Query(long x, long z, int radius, int mode)
    {
        if (x is < -1_000_000_000 or > 1_000_000_000 || z is < -1_000_000_000 or > 1_000_000_000 || radius is < 0 or > 16_000)
            throw new ArgumentOutOfRangeException(nameof(radius));
        for (long cz = Floor(z - radius); cz <= Floor(z + radius); cz++)
        for (long cx = Floor(x - radius); cx <= Floor(x + radius); cx++)
        {
            if (!_cells.TryGetValue((cx, cz), out var indices)) continue;
            foreach (int index in indices)
            {
                var a = _areas[index];
                if ((mode != 0) != (a.Kind == ScatterAreaKind.Yard)) continue;
                if (mode == 1)
                {
                    if (x - radius >= a.X0 && x + radius <= a.X1 && z - radius >= a.Z0 && z + radius <= a.Z1) return true;
                }
                else if (Intersects(a, x, z, radius)) return true;
            }
        }
        return false;
    }

    private static bool Intersects(ScatterArea a, long x, long z, int radius)
    {
        long r = (long)radius + a.PaddingMillimeters;
        if (a.Kind != ScatterAreaKind.Corridor)
        {
            long dx = x - Math.Clamp(x, a.X0, a.X1), dz = z - Math.Clamp(z, a.Z0, a.Z1);
            return (Int128)dx * dx + (Int128)dz * dz <= (Int128)r * r;
        }
        long sx = a.X1 - a.X0, sz = a.Z1 - a.Z0, px = x - a.X0, pz = z - a.Z0;
        Int128 length = (Int128)sx * sx + (Int128)sz * sz, dot = (Int128)px * sx + (Int128)pz * sz;
        if (dot <= 0 || length == 0) return (Int128)px * px + (Int128)pz * pz <= (Int128)r * r;
        if (dot >= length) { px = x - a.X1; pz = z - a.Z1; return (Int128)px * px + (Int128)pz * pz <= (Int128)r * r; }
        Int128 cross = (Int128)px * sz - (Int128)pz * sx;
        return cross * cross <= (Int128)r * r * length;
    }

    private static long Floor(long value) => value >= 0 ? value / CellSize : (value + 1) / CellSize - 1;
}
