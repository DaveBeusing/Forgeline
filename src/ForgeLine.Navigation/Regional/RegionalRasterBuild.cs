using System.Collections.Immutable;
using ForgeLine.World;

namespace ForgeLine.Navigation.Regional;

/// <summary>Single-owner resumable rasterization. Result exists only after every dirty page is complete.</summary>
internal sealed class RegionalRasterBuild : IDisposable
{
    private enum Phase : byte { BeginRegion, CopyBaseline, InspectObstacle, RasterizeObstacle, CommitRegion, Complete }

    private readonly RegionalRasterSnapshot _previous;
    private readonly ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> _obstacles;
    private readonly NavigationVersion _targetRevision;
    private ImmutableSortedSet<int>.Enumerator _regions;
    private ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord>.Enumerator _obstacleCursor;
    private ImmutableSortedDictionary<int, RegionalRasterPage> _pages;
    private RegionalRasterSnapshot? _result;
    private NavigationCellSample[]? _cells;
    private RegionalCellRange _range;
    private RegionalCellRange _overlap;
    private int _region;
    private int _cellCursor;
    private int _rasterX;
    private int _rasterZ;
    private long _storedCellCount;
    private Phase _phase;
    private bool _disposed;

    public RegionalRasterBuild(RegionalRasterSnapshot previous,
        ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> obstacles,
        ImmutableSortedSet<int> dirtyRegions, NavigationVersion targetRevision)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(obstacles);
        ArgumentNullException.ThrowIfNull(dirtyRegions);
        if (!targetRevision.IsValid || targetRevision < previous.Revision ||
            (dirtyRegions.Count != 0 && targetRevision == previous.Revision))
            throw new ArgumentOutOfRangeException(nameof(targetRevision));
        if (dirtyRegions.Count != 0 && (dirtyRegions.Min < 0 || dirtyRegions.Max >= previous.Layout.RegionCount))
            throw new ArgumentOutOfRangeException(nameof(dirtyRegions));
        _previous = previous;
        _obstacles = obstacles;
        _targetRevision = targetRevision;
        _regions = dirtyRegions.GetEnumerator();
        _pages = previous.Pages;
        _storedCellCount = previous.StoredCellCount;
        if (dirtyRegions.Count == 0)
        {
            _result = previous;
            _phase = Phase.Complete;
            Dispose();
        }
    }

    public bool IsComplete => _phase == Phase.Complete;
    public long TotalWork { get; private set; }
    public int CompletedRegionCount { get; private set; }
    public RegionalRasterSnapshot Result => _result ??
        throw new InvalidOperationException("Regional rasterization is not complete.");

    public int Advance(int workAllowance)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workAllowance);
        if (IsComplete) return 0;
        ObjectDisposedException.ThrowIf(_disposed, this);
        int consumed = 0;
        while (consumed < workAllowance && !IsComplete)
        {
            Step();
            consumed++;
            TotalWork = checked(TotalWork + 1);
        }
        return consumed;
    }

    private void Step()
    {
        switch (_phase)
        {
            case Phase.BeginRegion:
                if (!_regions.MoveNext())
                {
                    _result = _previous.ReplacePages(_targetRevision, _pages, _storedCellCount);
                    _phase = Phase.Complete;
                    Dispose();
                    break;
                }
                _region = _regions.Current;
                _range = _previous.Layout.GetRegionCellRange(_region);
                _cells = new NavigationCellSample[checked(_range.Width * _range.Height)];
                _cellCursor = 0;
                _phase = Phase.CopyBaseline;
                break;
            case Phase.CopyBaseline:
                NavigationCellCoordinate cell = new(_range.MinimumX + _cellCursor % _range.Width,
                    _range.MinimumZ + _cellCursor / _range.Width);
                // Never use the prior combined page: deleted obstacles must not become permanent.
                _cells![_cellCursor++] = _previous.Layout.Baseline.GetCell(cell);
                if (_cellCursor == _cells!.Length)
                {
                    _obstacleCursor = _obstacles.GetEnumerator();
                    _phase = Phase.InspectObstacle;
                }
                break;
            case Phase.InspectObstacle:
                if (!_obstacleCursor.MoveNext())
                {
                    _obstacleCursor.Dispose();
                    _phase = Phase.CommitRegion;
                    break;
                }
                if (_obstacleCursor.Current.Value.Bounds is not AxisAlignedBounds bounds) break;
                RegionalCellRange overlap = _previous.Layout.GetOverlap(bounds,
                    _previous.Layout.Baseline.Settings.StaticObstacleClearanceMeters);
                _overlap = new RegionalCellRange(Math.Max(_range.MinimumX, overlap.MinimumX),
                    Math.Max(_range.MinimumZ, overlap.MinimumZ), Math.Min(_range.EndX, overlap.EndX),
                    Math.Min(_range.EndZ, overlap.EndZ));
                if (_overlap.IsEmpty) break;
                _rasterX = _overlap.MinimumX;
                _rasterZ = _overlap.MinimumZ;
                _phase = Phase.RasterizeObstacle;
                break;
            case Phase.RasterizeObstacle:
                int index = (_rasterZ - _range.MinimumZ) * _range.Width + _rasterX - _range.MinimumX;
                _cells![index] = _cells![index] with { StaticBlocked = true };
                if (++_rasterX == _overlap.EndX)
                {
                    _rasterX = _overlap.MinimumX;
                    if (++_rasterZ == _overlap.EndZ) _phase = Phase.InspectObstacle;
                }
                break;
            case Phase.CommitRegion:
                if (_pages.TryGetValue(_region, out RegionalRasterPage? oldPage))
                    _storedCellCount -= oldPage.CellCount;
                _storedCellCount += _cells!.Length;
                _pages = _pages.SetItem(_region, new RegionalRasterPage(_targetRevision, _cells!));
                _cells = null;
                CompletedRegionCount++;
                _phase = Phase.BeginRegion;
                break;
            default:
                throw new InvalidOperationException("Unknown regional raster phase.");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _regions.Dispose();
            _obstacleCursor.Dispose();
            _cells = null;
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
