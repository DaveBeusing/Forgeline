using System.Collections.Immutable;

namespace ForgeLine.Navigation.Regional;

internal sealed record RegionalPublication(
    RegionalRasterSnapshot Raster,
    ImmutableArray<RegionalPortalGraph> Graphs,
    ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> Obstacles,
    ulong PublishedTick);

internal sealed record RegionalPreparationState(
    RegionalPublication? Previous,
    RegionalRasterSnapshot Baseline,
    ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> Obstacles,
    ImmutableSortedSet<int> DirtyRegions,
    ImmutableArray<NavigationCapabilities> Profiles,
    bool RebuildAll,
    RegionalRebuildReason RebuildReason,
    ulong StartedTick,
    long WorkConsumed,
    bool IsComplete,
    ulong ReadyAtTick);

/// <summary>Prepares complete geometry and every capability graph before any adoption.</summary>
internal sealed class RegionalPreparation : IDisposable
{
    private enum Phase : byte { EnumerateFull, Raster, Graphs, Ready }

    private readonly RegionalPublication? _previous;
    private readonly RegionalRasterSnapshot _baseline;
    private readonly ImmutableSortedSet<int> _inputDirty;
    private readonly ImmutableArray<NavigationCapabilities> _profiles;
    private readonly bool _rebuildAll;
    private ImmutableSortedSet<int> _dirty;
    private ImmutableArray<RegionalPortalGraph> _graphs = [];
    private RegionalRasterBuild? _rasterBuild;
    private RegionalPortalGraphBuilder? _graphBuild;
    private RegionalRasterSnapshot? _raster;
    private RegionalPublication? _result;
    private int _nextFullRegion;
    private int _profile;
    private int _completedRasterRegions;
    private Phase _phase;
    private bool _disposed;

    public RegionalPreparation(RegionalPublication? previous, RegionalRasterSnapshot baseline,
        ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> obstacles,
        ImmutableSortedSet<int> dirtyRegions, ImmutableArray<NavigationCapabilities> profiles,
        bool rebuildAll, RegionalRebuildReason rebuildReason, ulong startedTick)
    {
        _previous = previous;
        _baseline = baseline;
        Obstacles = obstacles;
        _inputDirty = dirtyRegions;
        _dirty = rebuildAll ? ImmutableSortedSet<int>.Empty : dirtyRegions;
        _profiles = profiles;
        _rebuildAll = rebuildAll;
        RebuildReason = rebuildReason;
        StartedTick = startedTick;
        _phase = rebuildAll ? Phase.EnumerateFull : Phase.Raster;
    }

    public ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> Obstacles { get; }
    public bool IsComplete => _phase == Phase.Ready;
    public ulong StartedTick { get; }
    public ulong ReadyAtTick { get; private set; }
    public long TotalWork { get; private set; }
    public RegionalRebuildReason RebuildReason { get; }
    public int DirtyRegionCount => _rebuildAll ? _baseline.Layout.RegionCount : _dirty.Count;
    public int CompletedRasterRegions => _rasterBuild?.CompletedRegionCount ?? _completedRasterRegions;
    public RegionalPublication Result => _result ?? throw new InvalidOperationException("Preparation is incomplete.");

    public int Advance(ulong tick, int workAllowance)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workAllowance);
        ObjectDisposedException.ThrowIf(_disposed, this);
        int consumed = 0;
        while (consumed < workAllowance && !IsComplete)
        {
            int stepWork = 1;
            switch (_phase)
            {
                case Phase.EnumerateFull:
                    if (_nextFullRegion < _baseline.Layout.RegionCount)
                        _dirty = _dirty.Add(_nextFullRegion++);
                    else
                        _phase = Phase.Raster;
                    break;
                case Phase.Raster:
                    if (_rasterBuild is null)
                    {
                        RegionalRasterSnapshot basis = _previous?.Raster ?? _baseline;
                        var target = new NavigationVersion(checked(basis.Revision.Value + 1));
                        _rasterBuild = new RegionalRasterBuild(basis, Obstacles, _dirty, target);
                    }
                    else if (_rasterBuild.IsComplete)
                    {
                        _raster = _rasterBuild.Result;
                        _completedRasterRegions = _rasterBuild.CompletedRegionCount;
                        _rasterBuild.Dispose();
                        _rasterBuild = null;
                        _phase = Phase.Graphs;
                    }
                    else
                        stepWork = _rasterBuild.Advance(workAllowance - consumed);
                    break;
                case Phase.Graphs:
                    if (_profile == _profiles.Length)
                    {
                        _result = new RegionalPublication(_raster!, _graphs, Obstacles, 0);
                        ReadyAtTick = tick;
                        _phase = Phase.Ready;
                    }
                    else if (_graphBuild is null)
                    {
                        RegionalPortalGraph? previousGraph = _previous is null ? null : _previous.Graphs[_profile];
                        _graphBuild = new RegionalPortalGraphBuilder(previousGraph, _raster!, _dirty, _profiles[_profile]);
                    }
                    else if (_graphBuild.IsComplete)
                    {
                        _graphs = _graphs.Add(_graphBuild.Result);
                        _graphBuild.Dispose();
                        _graphBuild = null;
                        _profile++;
                    }
                    else
                        stepWork = _graphBuild.Advance(workAllowance - consumed);
                    break;
                default:
                    throw new InvalidOperationException("Unknown regional preparation phase.");
            }
            consumed += stepWork;
            TotalWork = checked(TotalWork + stepWork);
        }
        return consumed;
    }

    public RegionalPreparationState CaptureState() => new(_previous, _baseline, Obstacles,
        _inputDirty, _profiles, _rebuildAll, RebuildReason, StartedTick, TotalWork, IsComplete, ReadyAtTick);

    public static RegionalPreparation Restore(RegionalPreparationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentOutOfRangeException.ThrowIfNegative(state.WorkConsumed);
        var preparation = new RegionalPreparation(state.Previous, state.Baseline, state.Obstacles,
            state.DirtyRegions, state.Profiles, state.RebuildAll, state.RebuildReason, state.StartedTick);
        try
        {
            // Reconstruction is load-time work at the original inputs/cursor,
            // not fresh simulation ticks or an extra allowance to publish early.
            long remaining = state.WorkConsumed;
            while (remaining != 0)
            {
                int allowance = (int)Math.Min(remaining, int.MaxValue);
                int consumed = preparation.Advance(state.StartedTick, allowance);
                if (consumed != allowance)
                    throw new ArgumentException("Preparation work cursor exceeds its complete state.", nameof(state));
                remaining -= consumed;
            }
            if (preparation.IsComplete != state.IsComplete)
                throw new ArgumentException("Preparation publication state does not match its cursor.", nameof(state));
            preparation.ReadyAtTick = state.ReadyAtTick;
            return preparation;
        }
        catch
        {
            preparation.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _rasterBuild?.Dispose();
            _graphBuild?.Dispose();
            _rasterBuild = null;
            _graphBuild = null;
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
