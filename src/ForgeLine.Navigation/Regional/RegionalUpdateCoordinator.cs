using System.Collections.Immutable;
using System.Numerics;

namespace ForgeLine.Navigation.Regional;

internal enum RegionalPreparationStatus : byte { Idle, Pending, Ready, SourceHold, RetentionHold }

internal sealed record RegionalUpdateCheckpoint(
    int AlgorithmRevision,
    ulong WorldGeneration,
    string ContentIdentity,
    RegionalGridLayout Layout,
    RegionalUpdateLimits Limits,
    ImmutableArray<NavigationCapabilities> Profiles,
    RegionalJournalState Journal,
    RegionalPublication? Publication,
    RegionalPreparationState? Preparation,
    ulong Tick,
    int WorkThisTick,
    RegionalRebuildReason LastRebuildReason);

internal sealed record RegionalUpdateDiagnostics(
    RegionalPreparationStatus Status,
    NavigationVersion PublishedVersion,
    ulong OverlayGeneration,
    int DirtyRegions,
    int PreparationRegions,
    int UpdatedRegions,
    int PendingRegions,
    int PendingBounds,
    int TrackedObstacleIdentities,
    RegionalRebuildReason RebuildReason,
    ulong WaitingTicks,
    int WorkThisTick,
    int RetainedSnapshots,
    int ActiveReaders,
    long RetainedCellSlotsUpperBound,
    ImmutableArray<NavigationSectorCoordinate> DirtySample);

/// <summary>
/// Internal single-owner preparation and publication boundary. No legacy world
/// or gameplay caller is connected until shared storage contracts support pages.
/// </summary>
internal sealed class RegionalUpdateCoordinator : IDisposable
{
    private const int AlgorithmRevision = 1;
    private const int MaximumCapabilityProfiles = 16;
    private readonly ulong _worldGeneration;
    private readonly string _contentIdentity;
    private readonly RegionalGridLayout _layout;
    private readonly RegionalUpdateLimits _limits;
    private readonly ImmutableArray<NavigationCapabilities> _profiles;
    private readonly RegionalRasterSnapshot _baseline;
    private readonly List<RegionalSnapshotEntry> _retired;
    private RegionalChangeJournal? _journal;
    private RegionalSnapshotEntry? _published;
    private RegionalPreparation? _preparation;
    private ulong _tick;
    private int _workThisTick;
    private RegionalRebuildReason _lastRebuildReason;
    private bool _disposed;

    public RegionalUpdateCoordinator(ulong worldGeneration, string contentIdentity,
        NavigationGrid permanentGrid, NavigationSectorSettings sectorSettings,
        NavigationVersion initialRevision, ReadOnlySpan<NavigationCapabilities> profiles,
        RegionalUpdateLimits limits,
        ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> initialObstacles,
        ulong initialTick = 0)
    {
        ArgumentOutOfRangeException.ThrowIfZero(worldGeneration);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentIdentity);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        if (profiles.IsEmpty || profiles.Length > MaximumCapabilityProfiles)
            throw new ArgumentOutOfRangeException(nameof(profiles));
        NavigationCapabilities[] ordered = profiles.ToArray();
        Array.Sort(ordered, static (left, right) =>
        {
            int movement = left.MovementClass.CompareTo(right.MovementClass);
            if (movement != 0) return movement;
            int slope = left.MaximumSlopeDegrees.CompareTo(right.MaximumSlopeDegrees);
            return slope != 0 ? slope : left.SlopeCostWeight.CompareTo(right.SlopeCostWeight);
        });
        for (int index = 0; index < ordered.Length; index++)
        {
            if (!Enum.IsDefined(ordered[index].MovementClass) ||
                (index != 0 && ordered[index] == ordered[index - 1]))
                throw new ArgumentException("Capability profiles must be valid and unique.", nameof(profiles));
        }
        _worldGeneration = worldGeneration;
        _contentIdentity = contentIdentity;
        _limits = limits;
        _profiles = ImmutableArray.CreateRange(ordered);
        _layout = new RegionalGridLayout(permanentGrid, sectorSettings, limits.MaximumSweptRadiusMeters);
        _baseline = RegionalRasterSnapshot.CreateBaseline(_layout, initialRevision);
        _journal = new RegionalChangeJournal(_layout, limits, initialObstacles, initialTick);
        _retired = new List<RegionalSnapshotEntry>(limits.MaximumRetainedSnapshots);
        _tick = initialTick;
    }

    public RegionalPendingBlocking PendingBlocking => Journal.PendingBlocking;
    public NavigationVersion PublishedVersion => _published?.Publication.Raster.Revision ?? _baseline.Revision;

    public RegionalQueueStatus QueueChanges(ulong tick, ReadOnlySpan<RegionalObstacleEdit> changes)
    {
        RegionalChangeJournal journal = Journal;
        if (tick >= _tick) TouchTick(tick);
        RegionalQueueStatus status = journal.Queue(tick, changes);
        if (journal.RequiresResynchronization) DiscardPreparation();
        return status;
    }

    public int Advance(ulong tick, int workAllowance)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workAllowance);
        TouchTick(tick);
        CollectReleased();
        RegionalChangeJournal journal = Journal;
        if (journal.RequiresResynchronization) return 0;
        int available = Math.Min(workAllowance, _limits.MaximumWorkPerTick - _workThisTick);
        if (available == 0) return 0;
        int consumed = 0;
        if (_preparation is null)
        {
            if (_published is not null && !journal.HasGeometryChanges) return 0;
            RegionalFrozenBatch batch = journal.BeginPreparation(tick);
            _preparation = new RegionalPreparation(_published?.Publication, _baseline,
                batch.Obstacles, batch.DirtyRegions, _profiles,
                batch.RebuildAll, batch.RebuildReason, batch.StartedTick);
            consumed = 1;
        }
        consumed += _preparation.Advance(tick, available - consumed);
        _workThisTick += consumed;
        return consumed;
    }

    /// <summary>Called only at start of NavigationRequests, after prior tick source changes.</summary>
    public bool TryPublishAtBoundary(ulong tick, NavigationVersion expectedPreviousVersion)
    {
        TouchTick(tick);
        CollectReleased();
        RegionalChangeJournal journal = Journal;
        if (journal.RequiresResynchronization || expectedPreviousVersion != PublishedVersion ||
            _preparation is null || !_preparation.IsComplete || tick <= _preparation.ReadyAtTick ||
            _workThisTick == _limits.MaximumWorkPerTick ||
            !ReferenceEquals(_preparation.Obstacles, journal.FrozenBatch?.Obstacles) || !CanRetainPublication())
            return false;

        RegionalPublication candidate = _preparation.Result with { PublishedTick = tick };
        var entry = new RegionalSnapshotEntry(candidate);
        journal.MarkPublished(_preparation.Obstacles, tick);
        if (_published is not null && _published.Readers != 0) _retired.Add(_published);
        _published = entry;
        _lastRebuildReason = _preparation.RebuildReason;
        DiscardPreparation();
        _workThisTick++;
        return true;
    }

    public RegionalSnapshotLease? AcquireSnapshot()
    {
        if (Journal.RequiresResynchronization || _published is null) return null;
        return new RegionalSnapshotLease(_published);
    }

    public bool CanReuseRoute(RegionalRouteDependencies dependencies, int workAllowance, out int consumed)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentOutOfRangeException.ThrowIfNegative(workAllowance);
        RegionalChangeJournal journal = Journal;
        consumed = 0;
        if (_published is null || journal.RequiresResynchronization ||
            !dependencies.Matches(_published.Publication.Raster, workAllowance, out consumed)) return false;

        RegionalPendingBlocking blocking = journal.PendingBlocking;
        ReadOnlySpan<Vector3> points = dependencies.Waypoints;
        if (points.IsEmpty) return false;
        int count = Math.Max(1, points.Length - 1);
        for (int index = 0; index < count; index++)
        {
            RegionalBlockingResult result = blocking.CheckSegment(points[index],
                points[Math.Min(index + 1, points.Length - 1)], dependencies.SweptRadiusMeters,
                blocking.Generation, workAllowance - consumed);
            consumed += result.WorkConsumed;
            if (!result.IsClear) return false;
        }
        return true;
    }

    public RegionalUpdateCheckpoint CaptureState() => new(AlgorithmRevision, _worldGeneration,
        _contentIdentity, _layout, _limits, _profiles, Journal.CaptureState(), _published?.Publication,
        _preparation?.CaptureState(), _tick, _workThisTick, _lastRebuildReason);

    public void RestoreState(RegionalUpdateCheckpoint state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(state.Journal);
        _ = Journal;
        CollectReleased();
        if (CountReaders() != 0)
            throw new InvalidOperationException("Restore requires the existing reader/job boundary to be joined.");
        if (state.AlgorithmRevision != AlgorithmRevision || state.WorldGeneration != _worldGeneration ||
            !StringComparer.Ordinal.Equals(state.ContentIdentity, _contentIdentity) ||
            !ReferenceEquals(state.Layout, _layout) || state.Limits != _limits ||
            state.Profiles.IsDefault || !state.Profiles.AsSpan().SequenceEqual(_profiles.AsSpan()) ||
            state.Tick != state.Journal.LastTick || state.WorkThisTick < 0 ||
            state.WorkThisTick > _limits.MaximumWorkPerTick || !Enum.IsDefined(state.LastRebuildReason))
            throw new ArgumentException("Regional checkpoint identity, configuration or work state is incompatible.", nameof(state));
        ValidatePublication(state.Publication);
        if (state.Publication is null ? !state.Journal.PublishedObstacles.IsEmpty :
            !ReferenceEquals(state.Publication.Obstacles, state.Journal.PublishedObstacles))
            throw new ArgumentException("Published geometry and its source ledger must have the same publication identity.", nameof(state));
        if (state.Publication is { } publication &&
            (publication.PublishedTick > state.Tick || publication.Raster.Revision < _baseline.Revision))
            throw new ArgumentException("Published geometry is newer than its checkpoint boundary.", nameof(state));
        if (state.Preparation is { } preparationState &&
            (!ReferenceEquals(preparationState.Previous, state.Publication) ||
             !ReferenceEquals(preparationState.Baseline, _baseline) ||
             !preparationState.Profiles.AsSpan().SequenceEqual(_profiles.AsSpan()) ||
             preparationState.StartedTick > state.Tick || preparationState.ReadyAtTick > state.Tick ||
             (preparationState.IsComplete && preparationState.ReadyAtTick < preparationState.StartedTick)))
            throw new ArgumentException("Regional preparation does not belong to the recorded publication.", nameof(state));

        // Validate and reconstruct privately before replacing the current state.
        var restoredJournal = new RegionalChangeJournal(_layout, _limits,
            ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord>.Empty);
        restoredJournal.RestoreState(state.Journal);
        if ((state.Preparation is null) != (restoredJournal.FrozenBatch is null))
            throw new ArgumentException("Preparation and its frozen source batch must be captured together.", nameof(state));
        if (state.Preparation is { } pending)
        {
            RegionalFrozenBatch batch = restoredJournal.FrozenBatch!;
            if (restoredJournal.RequiresResynchronization ||
                !ReferenceEquals(pending.Obstacles, batch.Obstacles) ||
                !Equals(pending.DirtyRegions.KeyComparer, Comparer<int>.Default) ||
                !pending.DirtyRegions.SetEquals(batch.DirtyRegions) ||
                pending.RebuildAll != batch.RebuildAll || pending.RebuildReason != batch.RebuildReason ||
                pending.StartedTick != batch.StartedTick)
                throw new ArgumentException("Preparation inputs do not match their restored frozen batch.", nameof(state));
        }
        RegionalPreparation? restoredPreparation = state.Preparation is null ? null : RegionalPreparation.Restore(state.Preparation);
        DiscardPreparation();
        _journal = restoredJournal;
        _preparation = restoredPreparation;
        _published = state.Publication is null ? null : new RegionalSnapshotEntry(state.Publication);
        _retired.Clear();
        _tick = state.Tick;
        _workThisTick = state.WorkThisTick;
        _lastRebuildReason = state.LastRebuildReason;
    }

    public RegionalUpdateDiagnostics GetDiagnostics()
    {
        RegionalChangeJournal journal = Journal;
        CollectReleased();
        int dirty = journal.RequiresFullRebuild ? _layout.RegionCount : journal.DirtyRegions.Count;
        int preparationRegions = _preparation?.DirtyRegionCount ?? 0;
        int updated = _preparation?.CompletedRasterRegions ?? 0;
        var sample = ImmutableArray.CreateBuilder<NavigationSectorCoordinate>(Math.Min(dirty, _limits.MaximumDiagnosticRegions));
        if (journal.RequiresFullRebuild)
        {
            for (int index = 0; index < Math.Min(dirty, _limits.MaximumDiagnosticRegions); index++)
                sample.Add(_layout.GetRegion(index));
        }
        else
        {
            foreach (int region in journal.DirtyRegions)
            {
                if (sample.Count == _limits.MaximumDiagnosticRegions) break;
                sample.Add(_layout.GetRegion(region));
            }
        }
        RegionalPreparationStatus status = journal.RequiresResynchronization ? RegionalPreparationStatus.SourceHold :
            _preparation?.IsComplete == true ? CanRetainPublication() ? RegionalPreparationStatus.Ready : RegionalPreparationStatus.RetentionHold :
            _preparation is not null || journal.HasGeometryChanges || _published is null ? RegionalPreparationStatus.Pending : RegionalPreparationStatus.Idle;
        ulong since = journal.HasGeometryChanges ? journal.OldestPendingTick : _preparation?.StartedTick ?? _tick;
        long cells = _layout.Baseline.CellCount;
        if (_published is not null) cells = checked(cells + RetainedCells(_published.Publication));
        foreach (RegionalSnapshotEntry entry in _retired) cells = checked(cells + RetainedCells(entry.Publication));
        if (_preparation is not null)
        {
            // Conservative cell-slot capacity, including partially built pages
            // and scratch arrays; shared pages may be counted more than once.
            cells = checked(cells + (long)_layout.Baseline.CellCount * (_profiles.Length + 1) +
                (long)_layout.RegionSizeCells * _layout.RegionSizeCells * 3);
        }
        return new RegionalUpdateDiagnostics(status, PublishedVersion, journal.PendingBlocking.Generation,
            dirty, preparationRegions, updated, Math.Max(0, preparationRegions - updated), journal.PendingBlocking.BoundsCount,
            journal.CurrentObstacles.Count, journal.RebuildReason != RegionalRebuildReason.None ? journal.RebuildReason : _lastRebuildReason,
            _tick - since, _workThisTick, _retired.Count + (_published is null ? 0 : 1), CountReaders(), cells, sample.ToImmutable());
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            DiscardPreparation();
            _published = null;
            _retired.Clear();
            _journal = null;
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    private RegionalChangeJournal Journal
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _journal!;
        }
    }

    private void TouchTick(ulong tick)
    {
        RegionalChangeJournal journal = Journal;
        ArgumentOutOfRangeException.ThrowIfLessThan(tick, _tick);
        journal.AdvanceTick(tick);
        if (tick != _tick)
        {
            _tick = tick;
            _workThisTick = 0;
        }
    }

    private void DiscardPreparation()
    {
        _preparation?.Dispose();
        _preparation = null;
        _journal?.CancelPreparation();
    }

    private void CollectReleased()
    {
        for (int index = _retired.Count - 1; index >= 0; index--)
            if (_retired[index].Readers == 0) _retired.RemoveAt(index);
    }

    private bool CanRetainPublication() =>
        _retired.Count + (_published?.Readers > 0 ? 1 : 0) + 1 <= _limits.MaximumRetainedSnapshots;

    private int CountReaders()
    {
        int count = _published?.Readers ?? 0;
        foreach (RegionalSnapshotEntry entry in _retired) count = checked(count + entry.Readers);
        return count;
    }

    private static long RetainedCells(RegionalPublication publication)
    {
        long count = publication.Raster.StoredCellCount;
        foreach (RegionalPortalGraph graph in publication.Graphs) count = checked(count + graph.StoredCellCount);
        return count;
    }

    private void ValidatePublication(RegionalPublication? publication)
    {
        if (publication is null) return;
        if (!ReferenceEquals(publication.Raster.Layout, _layout) || publication.Graphs.IsDefault ||
            publication.Graphs.Length != _profiles.Length)
            throw new ArgumentException("Published regional geometry is incompatible.", nameof(publication));
        for (int index = 0; index < _profiles.Length; index++)
            if (!ReferenceEquals(publication.Graphs[index].Raster, publication.Raster) ||
                publication.Graphs[index].Capabilities != _profiles[index])
                throw new ArgumentException("Published capability graphs must share one geometry.", nameof(publication));
    }
}
