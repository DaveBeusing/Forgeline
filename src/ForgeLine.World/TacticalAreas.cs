using System.Collections.ObjectModel;
using System.Numerics;

namespace ForgeLine.World;

/// <summary>Explicit authored ground gameplay data, independent of decorative objects.</summary>
public readonly record struct TacticalAreaDefinition(
    string Key, string DisplayName, AxisAlignedBounds Bounds,
    double InfantryDamageMultiplier, bool BlocksVisual);

public readonly record struct InfantryProtection(string AreaKey, double DamageMultiplier)
{
    public static InfantryProtection None => new(string.Empty, 1);
}

/// <summary>One immutable, bounded tactical geography for a match.</summary>
public sealed class TacticalAreaSet
{
    public const int MaximumAreas = 32;
    public const float CloseVisualRangeMeters = 32;
    private readonly TacticalAreaDefinition[] _areas;
    public ReadOnlyCollection<TacticalAreaDefinition> Areas { get; }
    public static TacticalAreaSet Empty { get; } = new([]);

    public TacticalAreaSet(IEnumerable<TacticalAreaDefinition> areas)
    {
        ArgumentNullException.ThrowIfNull(areas);
        _areas = areas.Take(MaximumAreas + 1).OrderBy(a => a.Key, StringComparer.Ordinal).ToArray();
        if (_areas.Length > MaximumAreas) throw new ArgumentException("At most 32 tactical areas are supported.", nameof(areas));
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var area in _areas)
        {
            var b = area.Bounds;
            if (string.IsNullOrWhiteSpace(area.Key) || area.Key.Length > 96 ||
                string.IsNullOrWhiteSpace(area.DisplayName) || area.DisplayName.Length > 96 ||
                !keys.Add(area.Key) || !Finite(b.Minimum) || !Finite(b.Maximum) ||
                b.Maximum.X <= b.Minimum.X || b.Maximum.Z <= b.Minimum.Z || b.Maximum.Y < b.Minimum.Y ||
                !double.IsFinite(area.InfantryDamageMultiplier) || area.InfantryDamageMultiplier < .5 ||
                area.InfantryDamageMultiplier > 1 || !area.BlocksVisual && area.InfantryDamageMultiplier == 1)
                throw new ArgumentException("Invalid tactical area.", nameof(areas));
        }
        Areas = Array.AsReadOnly(_areas);
    }

    public InfantryProtection ProtectionAt(Vector3 position)
    {
        var best = InfantryProtection.None;
        foreach (var area in _areas)
            if (Contains(area.Bounds, position) && area.InfantryDamageMultiplier < best.DamageMultiplier)
                best = new(area.Key, area.InfantryDamageMultiplier);
        return best;
    }

    public string VisualScreenAt(Vector3 position)
    {
        foreach (var area in _areas)
            if (area.BlocksVisual && Contains(area.Bounds, position)) return area.Key;
        return string.Empty;
    }

    public bool BlocksVisual(Vector3 source, Vector3 target)
    {
        float dx = target.X - source.X, dz = target.Z - source.Z;
        if (dx * dx + dz * dz <= CloseVisualRangeMeters * CloseVisualRangeMeters) return false;
        foreach (var area in _areas)
            if (area.BlocksVisual && Intersects(area.Bounds, source, target)) return true;
        return false;
    }

    public static bool Contains(AxisAlignedBounds bounds, Vector3 position) =>
        position.X >= bounds.Minimum.X && position.X <= bounds.Maximum.X &&
        position.Z >= bounds.Minimum.Z && position.Z <= bounds.Maximum.Z;

    private static bool Intersects(AxisAlignedBounds bounds, Vector3 start, Vector3 end)
    {
        float low = 0, high = 1;
        return Clip(start.X, end.X - start.X, bounds.Minimum.X, bounds.Maximum.X, ref low, ref high) &&
            Clip(start.Z, end.Z - start.Z, bounds.Minimum.Z, bounds.Maximum.Z, ref low, ref high);
    }
    private static bool Clip(float origin, float delta, float minimum, float maximum, ref float low, ref float high)
    {
        if (MathF.Abs(delta) < .000001f) return origin >= minimum && origin <= maximum;
        float first = (minimum - origin) / delta, last = (maximum - origin) / delta;
        if (first > last) (first, last) = (last, first);
        low = MathF.Max(low, first); high = MathF.Min(high, last);
        return low <= high;
    }
    private static bool Finite(Vector3 position) =>
        float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z);
}
