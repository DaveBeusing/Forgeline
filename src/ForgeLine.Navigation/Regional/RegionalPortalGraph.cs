using System.Collections.Immutable;
using System.Numerics;

namespace ForgeLine.Navigation.Regional;

/// <summary>Persistent regional connectivity and directed portals for one complete capability profile.</summary>
internal sealed class RegionalPortalGraph
{
    internal RegionalPortalGraph(RegionalRasterSnapshot raster, NavigationCapabilities capabilities,
        ImmutableSortedDictionary<int, RegionalConnectivityPage> pages, long storedCellCount)
    {
        Raster = raster;
        Capabilities = capabilities;
        Pages = pages;
        StoredCellCount = storedCellCount;
    }

    public RegionalRasterSnapshot Raster { get; }
    public NavigationCapabilities Capabilities { get; }
    public long StoredCellCount { get; }
    public int RegionPageCount => Pages.Count;
    internal ImmutableSortedDictionary<int, RegionalConnectivityPage> Pages { get; }

    public bool Contains(int region) =>
        Pages.TryGetValue(region, out RegionalConnectivityPage? page) && page.ComponentCount != 0;

    public ReadOnlySpan<NavigationSectorEdge> GetEdges(int region) =>
        Pages.TryGetValue(region, out RegionalConnectivityPage? page) ? page.Edges : [];

    public NavigationVersion GetRegionRevision(int region)
    {
        _ = Raster.Layout.GetRegion(region);
        return Pages.TryGetValue(region, out RegionalConnectivityPage? page) ? page.Revision : default;
    }

    public int GetComponent(NavigationCellCoordinate cell)
    {
        if (!Raster.Layout.Baseline.Contains(cell)) return -1;
        int region = Raster.Layout.GetCellRegion(cell);
        if (!Pages.TryGetValue(region, out RegionalConnectivityPage? page)) return -1;
        RegionalCellRange range = Raster.Layout.GetRegionCellRange(region);
        return page.GetComponent((cell.Z - range.MinimumZ) * range.Width + cell.X - range.MinimumX);
    }

    public bool AreConnectedWithinRegion(NavigationCellCoordinate first, NavigationCellCoordinate second)
    {
        if (!Raster.Layout.Baseline.Contains(first) || !Raster.Layout.Baseline.Contains(second) ||
            Raster.Layout.GetCellRegion(first) != Raster.Layout.GetCellRegion(second)) return false;
        int component = GetComponent(first);
        return component >= 0 && component == GetComponent(second);
    }
}

internal sealed class RegionalConnectivityPage
{
    private readonly int[] _components;
    private readonly NavigationSectorEdge[] _edges;

    // Both arrays transfer from the builder; readers receive no mutable container.
    public RegionalConnectivityPage(NavigationVersion revision, int componentCount,
        int[] ownedComponents, NavigationSectorEdge[] ownedEdges)
    {
        Revision = revision;
        ComponentCount = componentCount;
        _components = ownedComponents;
        _edges = ownedEdges;
    }

    public NavigationVersion Revision { get; }
    public int ComponentCount { get; }
    public int CellCount => _components.Length;
    public ReadOnlySpan<NavigationSectorEdge> Edges => _edges;
    public int GetComponent(int index) => _components[index] >= 0 ? _components[index] : -1;
}

/// <summary>Budgeted regional flood fills and boundary runs; no complete graph copy or synchronous rebuild.</summary>
internal sealed class RegionalPortalGraphBuilder : IDisposable
{
    private enum Phase : byte { Schedule, BeginRegion, InitializeCells, FindComponent, Flood, Portals, CommitRegion, Complete }

    private readonly RegionalRasterSnapshot _raster;
    private readonly NavigationCapabilities _capabilities;
    private readonly bool _initialBuild;
    private ImmutableSortedSet<int>.Enumerator _dirtyCursor;
    private ImmutableSortedSet<int>.Enumerator _scheduledCursor;
    private ImmutableSortedSet<int> _scheduled = ImmutableSortedSet<int>.Empty;
    private ImmutableSortedDictionary<int, RegionalConnectivityPage> _pages;
    private RegionalPortalGraph? _result;
    private RegionalCellRange _range;
    private int[]? _components;
    private int[]? _floodQueue;
    private List<NavigationSectorEdge>? _edges;
    private int _nextInitialRegion;
    private int _region;
    private int _cellCursor;
    private int _componentCount;
    private int _queueHead;
    private int _queueTail;
    private int _neighbor;
    private int _boundary;
    private int _boundaryOffset;
    private int _runStart;
    private long _storedCellCount;
    private Phase _phase;
    private bool _disposed;

    public RegionalPortalGraphBuilder(RegionalPortalGraph? previous, RegionalRasterSnapshot raster,
        ImmutableSortedSet<int> dirtyRegions, NavigationCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(raster);
        ArgumentNullException.ThrowIfNull(dirtyRegions);
        if (!Enum.IsDefined(capabilities.MovementClass))
            throw new ArgumentOutOfRangeException(nameof(capabilities));
        if (previous is not null && (!ReferenceEquals(previous.Raster.Layout, raster.Layout) ||
            previous.Capabilities != capabilities || previous.Raster.Revision > raster.Revision ||
            (previous.Raster.Revision == raster.Revision && !ReferenceEquals(previous.Raster, raster))))
            throw new ArgumentException("Prior connectivity must belong to the same layout and capability profile.", nameof(previous));
        if (dirtyRegions.Count != 0 && (dirtyRegions.Min < 0 || dirtyRegions.Max >= raster.Layout.RegionCount))
            throw new ArgumentOutOfRangeException(nameof(dirtyRegions));
        _raster = raster;
        _capabilities = capabilities;
        _initialBuild = previous is null;
        _dirtyCursor = dirtyRegions.GetEnumerator();
        _pages = previous?.Pages ?? ImmutableSortedDictionary<int, RegionalConnectivityPage>.Empty;
        _storedCellCount = previous?.StoredCellCount ?? 0;
        // Initial construction walks region IDs in Advance instead of allocating a full scheduling set.
        _phase = _initialBuild ? Phase.BeginRegion : Phase.Schedule;
    }

    public bool IsComplete => _phase == Phase.Complete;
    public long TotalWork { get; private set; }
    public int CompletedRegionCount { get; private set; }
    public RegionalPortalGraph Result => _result ??
        throw new InvalidOperationException("Regional connectivity is not complete.");

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
            case Phase.Schedule:
                if (_dirtyCursor.MoveNext()) ScheduleRegionAndNeighbors(_dirtyCursor.Current);
                else
                {
                    _dirtyCursor.Dispose();
                    _scheduledCursor = _scheduled.GetEnumerator();
                    _phase = Phase.BeginRegion;
                }
                break;
            case Phase.BeginRegion:
                BeginRegion();
                break;
            case Phase.InitializeCells:
                _components![_cellCursor++] = -1;
                if (_cellCursor == _components!.Length)
                {
                    _cellCursor = 0;
                    _phase = Phase.FindComponent;
                }
                break;
            case Phase.FindComponent:
                FindComponent();
                break;
            case Phase.Flood:
                VisitNeighbor();
                break;
            case Phase.Portals:
                VisitBoundary();
                break;
            case Phase.CommitRegion:
                if (_pages.TryGetValue(_region, out RegionalConnectivityPage? prior))
                    _storedCellCount -= prior.CellCount;
                _storedCellCount += _components!.Length;
                _pages = _pages.SetItem(_region, new RegionalConnectivityPage(_raster.Revision,
                    _componentCount, _components!, _edges!.ToArray()));
                _components = null;
                _floodQueue = null;
                _edges = null;
                CompletedRegionCount++;
                _phase = Phase.BeginRegion;
                break;
            default:
                throw new InvalidOperationException("Unknown regional connectivity phase.");
        }
    }

    private void ScheduleRegionAndNeighbors(int region)
    {
        RegionalGridLayout layout = _raster.Layout;
        NavigationSectorCoordinate coordinate = layout.GetRegion(region);
        _scheduled = _scheduled.Add(region);
        if (coordinate.X > 0) _scheduled = _scheduled.Add(region - 1);
        if (coordinate.X + 1 < layout.Width) _scheduled = _scheduled.Add(region + 1);
        if (coordinate.Z > 0) _scheduled = _scheduled.Add(region - layout.Width);
        if (coordinate.Z + 1 < layout.Height) _scheduled = _scheduled.Add(region + layout.Width);
    }

    private void BeginRegion()
    {
        bool hasRegion;
        if (_initialBuild)
        {
            hasRegion = _nextInitialRegion < _raster.Layout.RegionCount;
            if (hasRegion) _region = _nextInitialRegion++;
        }
        else
        {
            hasRegion = _scheduledCursor.MoveNext();
            if (hasRegion) _region = _scheduledCursor.Current;
        }
        if (!hasRegion)
        {
            _result = new RegionalPortalGraph(_raster, _capabilities, _pages, _storedCellCount);
            _phase = Phase.Complete;
            Dispose();
            return;
        }
        _range = _raster.Layout.GetRegionCellRange(_region);
        int cellCount = checked(_range.Width * _range.Height);
        _components = new int[cellCount];
        _floodQueue = new int[cellCount];
        _edges = new List<NavigationSectorEdge>();
        _componentCount = 0;
        _cellCursor = 0;
        _phase = Phase.InitializeCells;
    }

    private void FindComponent()
    {
        int[] components = _components!;
        if (_cellCursor == components.Length)
        {
            _boundary = 0;
            _boundaryOffset = 0;
            _runStart = -1;
            _phase = Phase.Portals;
            return;
        }
        int cell = _cellCursor++;
        if (components[cell] != -1) return;
        if (!_raster.IsTraversable(GetCell(cell), _capabilities))
        {
            components[cell] = -2;
            return;
        }
        components[cell] = _componentCount++;
        _floodQueue![0] = cell;
        _queueHead = 0;
        _queueTail = 1;
        _neighbor = 0;
        _phase = Phase.Flood;
    }

    private void VisitNeighbor()
    {
        int[] components = _components!;
        int[] floodQueue = _floodQueue!;
        int currentIndex = floodQueue[_queueHead];
        NavigationCellCoordinate current = GetCell(currentIndex);
        (int deltaX, int deltaZ) = NeighborOffset(_neighbor);
        int x = current.X + deltaX;
        int z = current.Z + deltaZ;
        if (x >= _range.MinimumX && x < _range.EndX && z >= _range.MinimumZ && z < _range.EndZ)
        {
            int nextIndex = (z - _range.MinimumZ) * _range.Width + x - _range.MinimumX;
            if (components[nextIndex] == -1)
            {
                if (!_raster.IsTraversable(new NavigationCellCoordinate(x, z), _capabilities))
                    components[nextIndex] = -2;
                else if (deltaX == 0 || deltaZ == 0 ||
                    (_raster.IsTraversable(new NavigationCellCoordinate(x, current.Z), _capabilities) &&
                     _raster.IsTraversable(new NavigationCellCoordinate(current.X, z), _capabilities)))
                {
                    components[nextIndex] = components[currentIndex];
                    floodQueue[_queueTail++] = nextIndex;
                }
            }
        }
        if (++_neighbor == 8)
        {
            _neighbor = 0;
            if (++_queueHead == _queueTail) _phase = Phase.FindComponent;
        }
    }

    private void VisitBoundary()
    {
        if (_boundary == 4)
        {
            _phase = Phase.CommitRegion;
            return;
        }
        NavigationSectorCoordinate from = _raster.Layout.GetRegion(_region);
        (int deltaX, int deltaZ) = BoundaryOffset(_boundary);
        NavigationSectorCoordinate to = new(from.X + deltaX, from.Z + deltaZ);
        if ((uint)to.X >= (uint)_raster.Layout.Width || (uint)to.Z >= (uint)_raster.Layout.Height)
        {
            NextBoundary();
            return;
        }
        int length = deltaX == 0 ? _range.Width : _range.Height;
        bool open = false;
        if (_boundaryOffset < length)
        {
            GetBoundaryCells(_boundaryOffset, out NavigationCellCoordinate first, out NavigationCellCoordinate second);
            open = _raster.IsTraversable(first, _capabilities) && _raster.IsTraversable(second, _capabilities);
        }
        if (open && _runStart < 0) _runStart = _boundaryOffset;
        if (!open && _runStart >= 0)
        {
            int midpoint = _runStart + (_boundaryOffset - 1 - _runStart) / 2;
            GetBoundaryCells(midpoint, out NavigationCellCoordinate first, out NavigationCellCoordinate second);
            Vector3 position = (_raster.GetCellCenter(first) + _raster.GetCellCenter(second)) * 0.5f;
            var portal = new NavigationPortal(from, to, first, second, position);
            _edges!.Add(new NavigationSectorEdge(to, portal, 1.0f));
            _runStart = -1;
        }
        if (++_boundaryOffset > length) NextBoundary();
    }

    private void GetBoundaryCells(int offset, out NavigationCellCoordinate first, out NavigationCellCoordinate second)
    {
        (int deltaX, int deltaZ) = BoundaryOffset(_boundary);
        int x = deltaX == 0 ? _range.MinimumX + offset : deltaX < 0 ? _range.MinimumX : _range.EndX - 1;
        int z = deltaZ == 0 ? _range.MinimumZ + offset : deltaZ < 0 ? _range.MinimumZ : _range.EndZ - 1;
        first = new NavigationCellCoordinate(x, z);
        second = new NavigationCellCoordinate(x + deltaX, z + deltaZ);
    }

    private void NextBoundary()
    {
        _boundary++;
        _boundaryOffset = 0;
        _runStart = -1;
    }

    private NavigationCellCoordinate GetCell(int localIndex) =>
        new(_range.MinimumX + localIndex % _range.Width, _range.MinimumZ + localIndex / _range.Width);

    private static (int X, int Z) NeighborOffset(int index) => index switch
    {
        0 => (-1, -1), 1 => (0, -1), 2 => (1, -1), 3 => (-1, 0),
        4 => (1, 0), 5 => (-1, 1), 6 => (0, 1), 7 => (1, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    // Neighbour IDs increase in this order, matching stable row-major region addressing.
    private static (int X, int Z) BoundaryOffset(int index) => index switch
    {
        0 => (0, -1), 1 => (-1, 0), 2 => (1, 0), 3 => (0, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    public void Dispose()
    {
        if (!_disposed)
        {
            _dirtyCursor.Dispose();
            _scheduledCursor.Dispose();
            _components = null;
            _floodQueue = null;
            _edges = null;
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
