using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Navigation.Regional;

/// <summary>Sector-aligned addressing over a permanently static, immutable baseline.</summary>
internal sealed class RegionalGridLayout
{
    public RegionalGridLayout(NavigationGrid baseline, NavigationSectorSettings settings,
        float maximumSweptRadiusMeters)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate(baseline);
        if (!float.IsFinite(maximumSweptRadiusMeters) || maximumSweptRadiusMeters < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumSweptRadiusMeters));
        if (settings.SectorSizeCells > 64)
            throw new ArgumentOutOfRangeException(nameof(settings), "Regional storage is bounded to 4096 cells per page.");

        Baseline = baseline;
        RegionSizeCells = settings.SectorSizeCells;
        Width = checked((baseline.Width - 1) / RegionSizeCells + 1);
        Height = checked((baseline.Height - 1) / RegionSizeCells + 1);
        RegionCount = checked(Width * Height);
        MaximumSweptRadiusMeters = maximumSweptRadiusMeters;
        float halo = AddOutward(baseline.Settings.StaticObstacleClearanceMeters, maximumSweptRadiusMeters);
        if (!float.IsFinite(halo))
            throw new ArgumentOutOfRangeException(nameof(maximumSweptRadiusMeters));
        DirtyHaloMeters = halo;
    }

    public NavigationGrid Baseline { get; }
    public int RegionSizeCells { get; }
    public int Width { get; }
    public int Height { get; }
    public int RegionCount { get; }
    public float MaximumSweptRadiusMeters { get; }
    public float DirtyHaloMeters { get; }

    public int GetRegionIndex(NavigationSectorCoordinate region)
    {
        if ((uint)region.X >= (uint)Width || (uint)region.Z >= (uint)Height)
            throw new ArgumentOutOfRangeException(nameof(region));
        return region.Z * Width + region.X;
    }

    public NavigationSectorCoordinate GetRegion(int index)
    {
        if ((uint)index >= (uint)RegionCount) throw new ArgumentOutOfRangeException(nameof(index));
        return new NavigationSectorCoordinate(index % Width, index / Width);
    }

    public int GetCellRegion(NavigationCellCoordinate cell)
    {
        if (!Baseline.Contains(cell)) throw new ArgumentOutOfRangeException(nameof(cell));
        return cell.Z / RegionSizeCells * Width + cell.X / RegionSizeCells;
    }

    public RegionalCellRange GetRegionCellRange(int index)
    {
        NavigationSectorCoordinate region = GetRegion(index);
        int x = region.X * RegionSizeCells;
        int z = region.Z * RegionSizeCells;
        return new RegionalCellRange(x, z,
            Math.Min(x + RegionSizeCells, Baseline.Width), Math.Min(z + RegionSizeCells, Baseline.Height));
    }

    public AxisAlignedBounds GetRegionBounds(int index)
    {
        RegionalCellRange range = GetRegionCellRange(index);
        float size = Baseline.Settings.CellSizeMeters;
        float half = size * 0.5f;
        float firstX = Baseline.Origin.X + range.MinimumX * size + half;
        float firstZ = Baseline.Origin.Y + range.MinimumZ * size + half;
        float lastX = Baseline.Origin.X + (range.EndX - 1) * size + half;
        float lastZ = Baseline.Origin.Y + (range.EndZ - 1) * size + half;
        return new AxisAlignedBounds(new Vector3(firstX - half, 0, firstZ - half),
            new Vector3(lastX + half, 0, lastZ + half));
    }

    public RegionalCellRange GetOverlap(AxisAlignedBounds bounds, float inflation, bool includeRoundingHalo = false)
    {
        if (!float.IsFinite(inflation) || inflation < 0)
            throw new ArgumentOutOfRangeException(nameof(inflation));
        // Preserve the exact float edge predicate of NavigationGridBuilder.
        // The expanded limits may become infinite; binary search safely clips
        // these to the finite grid rather than casting a rounded quotient.
        float lowX = bounds.Minimum.X - inflation;
        float highX = bounds.Maximum.X + inflation;
        float lowZ = bounds.Minimum.Z - inflation;
        float highZ = bounds.Maximum.Z + inflation;
        if (includeRoundingHalo)
        {
            lowX = MathF.BitDecrement(lowX);
            lowZ = MathF.BitDecrement(lowZ);
            highX = MathF.BitIncrement(highX);
            highZ = MathF.BitIncrement(highZ);
        }
        GetAxisOverlap(Baseline.Origin.X, Baseline.Width, lowX, highX, out int minimumX, out int endX);
        GetAxisOverlap(Baseline.Origin.Y, Baseline.Height, lowZ, highZ, out int minimumZ, out int endZ);
        return new RegionalCellRange(minimumX, minimumZ, endX, endZ);
    }

    public IEnumerable<int> GetDirtyRegions(AxisAlignedBounds bounds)
    {
        RegionalCellRange cells = GetOverlap(bounds, DirtyHaloMeters, includeRoundingHalo: true);
        if (cells.IsEmpty) yield break;
        // Include adjacent cells, sector boundaries and both portal neighbours.
        int firstX = Math.Max(0, cells.MinimumX - 1) / RegionSizeCells;
        int firstZ = Math.Max(0, cells.MinimumZ - 1) / RegionSizeCells;
        int lastX = Math.Min(Baseline.Width - 1, cells.EndX) / RegionSizeCells;
        int lastZ = Math.Min(Baseline.Height - 1, cells.EndZ) / RegionSizeCells;
        firstX = Math.Max(0, firstX - 1);
        firstZ = Math.Max(0, firstZ - 1);
        lastX = Math.Min(Width - 1, lastX + 1);
        lastZ = Math.Min(Height - 1, lastZ + 1);
        for (int z = firstZ; z <= lastZ; z++)
            for (int x = firstX; x <= lastX; x++)
                yield return z * Width + x;
    }

    public static bool SameBounds(AxisAlignedBounds? left, AxisAlignedBounds? right)
    {
        if (left is not AxisAlignedBounds first) return right is null;
        return right is AxisAlignedBounds second && first.Minimum == second.Minimum && first.Maximum == second.Maximum;
    }

    public static bool SegmentTouches(Vector3 start, Vector3 end, AxisAlignedBounds bounds, float inflation)
    {
        double minimum = 0;
        double maximum = 1;
        return Clip(start.X, (double)end.X - start.X, (double)bounds.Minimum.X - inflation,
                   (double)bounds.Maximum.X + inflation, ref minimum, ref maximum) &&
               Clip(start.Z, (double)end.Z - start.Z, (double)bounds.Minimum.Z - inflation,
                   (double)bounds.Maximum.Z + inflation, ref minimum, ref maximum);
    }

    public static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    public static float AddOutward(float first, float second)
    {
        double exact = (double)first + second;
        float rounded = (float)exact;
        return rounded < exact ? MathF.BitIncrement(rounded) : rounded;
    }

    private void GetAxisOverlap(float origin, int count, float minimum, float maximum,
        out int first, out int end)
    {
        float size = Baseline.Settings.CellSizeMeters;
        float half = size * 0.5f;
        int lower = 0;
        int upper = count;
        while (lower < upper)
        {
            int middle = lower + (upper - lower) / 2;
            float center = origin + middle * size + half;
            if (center + half < minimum) lower = middle + 1;
            else upper = middle;
        }
        first = lower;
        upper = count;
        while (lower < upper)
        {
            int middle = lower + (upper - lower) / 2;
            float center = origin + middle * size + half;
            if (center - half <= maximum) lower = middle + 1;
            else upper = middle;
        }
        end = lower;
    }

    private static bool Clip(double start, double delta, double minimum, double maximum,
        ref double entry, ref double exit)
    {
        if (delta == 0) return start >= minimum && start <= maximum;
        double first = (minimum - start) / delta;
        double second = (maximum - start) / delta;
        if (first > second) (first, second) = (second, first);
        entry = Math.Max(entry, first);
        exit = Math.Min(exit, second);
        return entry <= exit;
    }
}
