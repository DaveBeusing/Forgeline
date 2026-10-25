using System.Collections.Immutable;
using System.Numerics;

namespace ForgeLine.Navigation.Regional;

/// <summary>Immutable sector pages over a baseline containing only permanent geometry.</summary>
internal sealed class RegionalRasterSnapshot
{
    private RegionalRasterSnapshot(RegionalGridLayout layout, NavigationVersion revision,
        NavigationVersion baselineRevision, ImmutableSortedDictionary<int, RegionalRasterPage> pages,
        long storedCellCount)
    {
        Layout = layout;
        Revision = revision;
        BaselineRevision = baselineRevision;
        Pages = pages;
        StoredCellCount = storedCellCount;
    }

    public RegionalGridLayout Layout { get; }
    public NavigationVersion Revision { get; }
    public NavigationVersion BaselineRevision { get; }
    public long StoredCellCount { get; }
    public int RegionPageCount => Pages.Count;
    internal ImmutableSortedDictionary<int, RegionalRasterPage> Pages { get; }

    public static RegionalRasterSnapshot CreateBaseline(RegionalGridLayout layout, NavigationVersion revision)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (!revision.IsValid) throw new ArgumentOutOfRangeException(nameof(revision));
        return new RegionalRasterSnapshot(layout, revision, revision,
            ImmutableSortedDictionary<int, RegionalRasterPage>.Empty, 0);
    }

    internal RegionalRasterSnapshot ReplacePages(NavigationVersion revision,
        ImmutableSortedDictionary<int, RegionalRasterPage> pages, long storedCellCount) =>
        new(Layout, revision, BaselineRevision, pages, storedCellCount);

    public NavigationCellSample GetCell(NavigationCellCoordinate cell)
    {
        int region = Layout.GetCellRegion(cell);
        if (!Pages.TryGetValue(region, out RegionalRasterPage? page))
            return Layout.Baseline.GetCell(cell);
        RegionalCellRange range = Layout.GetRegionCellRange(region);
        return page.GetCell((cell.Z - range.MinimumZ) * range.Width + cell.X - range.MinimumX);
    }

    public NavigationVersion GetRegionRevision(int region)
    {
        _ = Layout.GetRegion(region);
        return Pages.TryGetValue(region, out RegionalRasterPage? page) ? page.Revision : BaselineRevision;
    }

    public object GetRegionIdentity(int region)
    {
        _ = Layout.GetRegion(region);
        return Pages.TryGetValue(region, out RegionalRasterPage? page) ? page.Identity : Layout;
    }

    public bool IsTraversable(NavigationCellCoordinate cell, in NavigationCapabilities capabilities)
    {
        if (!Layout.Baseline.Contains(cell)) return false;
        NavigationCellSample sample = GetCell(cell);
        return sample.HasTerrain && !sample.StaticBlocked && sample.SlopeDegrees <= capabilities.MaximumSlopeDegrees;
    }

    public Vector3 GetCellCenter(NavigationCellCoordinate cell)
    {
        NavigationCellSample sample = GetCell(cell);
        NavigationGrid baseline = Layout.Baseline;
        float size = baseline.Settings.CellSizeMeters;
        float half = size * 0.5f;
        return new Vector3(baseline.Origin.X + cell.X * size + half, sample.Height,
            baseline.Origin.Y + cell.Z * size + half);
    }
}

internal sealed class RegionalRasterPage
{
    private readonly NavigationCellSample[] _cells;

    // Ownership transfers from the builder; no mutable view escapes this object.
    internal RegionalRasterPage(NavigationVersion revision, NavigationCellSample[] ownedCells)
    {
        Revision = revision;
        _cells = ownedCells;
    }

    public NavigationVersion Revision { get; }
    public object Identity { get; } = new();
    public int CellCount => _cells.Length;
    public NavigationCellSample GetCell(int index) => _cells[index];
}
