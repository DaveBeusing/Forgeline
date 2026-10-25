using System.Collections.Immutable;
using ForgeLine.World;

namespace ForgeLine.Navigation.Regional;

/// <summary>A fixed target that remains valid while later source changes accumulate.</summary>
internal sealed record RegionalFrozenBatch(
    ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> Obstacles,
    ImmutableSortedSet<int> DirtyRegions,
    bool RebuildAll,
    RegionalRebuildReason RebuildReason,
    ulong StartedTick,
    ImmutableSortedDictionary<RegionalObstacleKey, ulong> PendingSince);

/// <summary>Immutable in-memory checkpoint; content-bound serialization is an integration responsibility.</summary>
internal sealed record RegionalJournalState(
    int FormatRevision,
    RegionalGridLayout Layout,
    RegionalUpdateLimits Limits,
    ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> CurrentObstacles,
    ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> PublishedObstacles,
    ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleEdit> LastEdits,
    ImmutableSortedDictionary<RegionalObstacleKey, ulong> PendingSince,
    RegionalFrozenBatch? FrozenBatch,
    ImmutableSortedDictionary<RegionalObstacleKey, ulong> AfterBatchPendingSince,
    ImmutableArray<RegionalPendingBound> TransientBounds,
    ulong LastTick,
    int ChangesThisTick,
    ulong OverlayGeneration,
    bool RequiresResynchronization,
    RegionalRebuildReason HoldReason,
    bool InitialPopulation);

/// <summary>
/// Single-owner provenance ledger. No caller receives mutable arrays or live ECS data.
/// Queue admission is bounded separately from raster and graph preparation work.
/// </summary>
internal sealed class RegionalChangeJournal
{
    private const int StateFormatRevision = 3;
    // Admission metadata has a fixed bound independent of map area. Reaching
    // it schedules a complete bounded preparation instead of a partial proof.
    private const int MaximumDirtyRegionVisits = 4096;
    private readonly RegionalGridLayout _layout;
    private readonly RegionalUpdateLimits _limits;
    private ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> _current;
    private ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> _published =
        ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord>.Empty;
    private ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleEdit> _lastEdits =
        ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleEdit>.Empty;
    private ImmutableSortedDictionary<(byte Source, ulong Value), ulong> _generations =
        ImmutableSortedDictionary<(byte Source, ulong Value), ulong>.Empty;
    private ImmutableSortedDictionary<RegionalObstacleKey, ulong> _pendingSince =
        ImmutableSortedDictionary<RegionalObstacleKey, ulong>.Empty;
    private ImmutableSortedDictionary<RegionalObstacleKey, ulong> _afterBatchPendingSince =
        ImmutableSortedDictionary<RegionalObstacleKey, ulong>.Empty;
    private ImmutableArray<RegionalPendingBound> _transientBounds = [];
    private int _changesThisTick;
    private ulong _overlayGeneration = 1;
    private bool _initialPopulation;
    private RegionalRebuildReason _holdReason;

    public RegionalChangeJournal(RegionalGridLayout layout, RegionalUpdateLimits limits,
        ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> initialObstacles,
        ulong initialTick = 0)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(initialObstacles);
        limits.Validate();
        if (layout.MaximumSweptRadiusMeters != limits.MaximumSweptRadiusMeters)
            throw new ArgumentException("Layout and journal clearance capacities must agree.", nameof(limits));
        ValidateRegistry(initialObstacles, limits);
        _layout = layout;
        _limits = limits;
        _current = initialObstacles;
        LastTick = initialTick;
        foreach ((RegionalObstacleKey key, RegionalObstacleRecord obstacle) in initialObstacles)
        {
            ObserveGeneration(key);
            if (obstacle.Bounds.HasValue) _pendingSince = _pendingSince.Add(key, initialTick);
        }
        _initialPopulation = !_pendingSince.IsEmpty;
        PendingBlocking = new RegionalPendingBlocking(layout, _overlayGeneration, initialTick, false, []);
        RebuildDerived();
        if (!TryBuildOverlay(out RegionalPendingBlocking overlay))
            throw new ArgumentException("Initial unpublished blockers exceed the pending bound capacity.", nameof(initialObstacles));
        PendingBlocking = overlay;
    }

    public ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> CurrentObstacles => _current;
    public ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> PublishedObstacles => _published;
    public RegionalFrozenBatch? FrozenBatch { get; private set; }
    public ImmutableSortedDictionary<RegionalObstacleKey, ulong> AfterBatchPendingSince => _afterBatchPendingSince;
    public ImmutableSortedSet<int> DirtyRegions { get; private set; } = ImmutableSortedSet<int>.Empty;
    public bool HasGeometryChanges => !_pendingSince.IsEmpty;
    public bool RequiresFullRebuild { get; private set; }
    public bool RequiresResynchronization { get; private set; }
    public RegionalRebuildReason RebuildReason { get; private set; }
    public ulong OldestPendingTick { get; private set; }
    public ulong LastTick { get; private set; }
    public RegionalPendingBlocking PendingBlocking { get; private set; }

    public RegionalQueueStatus Queue(ulong tick, ReadOnlySpan<RegionalObstacleEdit> changes)
    {
        if (RequiresResynchronization) return RegionalQueueStatus.ResynchronizationRequired;
        if (tick < LastTick)
            return Hold(RegionalQueueStatus.Stale, RegionalRebuildReason.ConflictingSource, tick);
        if (changes.IsEmpty)
        {
            AdvanceTick(tick);
            return HasGeometryChanges ? RegionalQueueStatus.Accepted : RegionalQueueStatus.NoGeometryChange;
        }
        int previousCount = tick == LastTick ? _changesThisTick : 0;
        if (changes.Length > _limits.MaximumChangesPerTick - previousCount)
            return Hold(RegionalQueueStatus.CapacityExceeded, RegionalRebuildReason.CapacityExceeded, tick);

        // Prepare a private candidate first. Invalid input never partially commits
        // a removal; an explicit hold covers any already-authoritative addition.
        RegionalObstacleEdit[] ordered = changes.ToArray();
        Array.Sort(ordered, static (left, right) =>
        {
            int key = left.Key.CompareTo(right.Key);
            return key != 0 ? key : left.SourceRevision.CompareTo(right.SourceRevision);
        });
        for (int index = 1; index < ordered.Length; index++)
        {
            RegionalObstacleEdit previous = ordered[index - 1];
            RegionalObstacleEdit current = ordered[index];
            if (previous.Key == current.Key && previous.SourceRevision == current.SourceRevision &&
                (previous.Layer != current.Layer ||
                 !RegionalGridLayout.SameBounds(previous.PreviousBounds, current.PreviousBounds) ||
                 !RegionalGridLayout.SameBounds(previous.CurrentBounds, current.CurrentBounds)))
                return Hold(RegionalQueueStatus.InvalidInput, RegionalRebuildReason.ConflictingSource, tick);
        }
        ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> candidate = _current;
        ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleEdit> lastEdits = _lastEdits;
        ImmutableSortedDictionary<(byte Source, ulong Value), ulong> generations = _generations;
        ImmutableSortedDictionary<RegionalObstacleKey, ulong> pendingSince = _pendingSince;
        ImmutableSortedDictionary<RegionalObstacleKey, ulong> afterBatchPendingSince = _afterBatchPendingSince;
        ImmutableArray<RegionalPendingBound> transient = tick == LastTick ? _transientBounds : [];
        ImmutableArray<RegionalPendingBound>.Builder transientBuilder = transient.ToBuilder();
        var transientKeys = new HashSet<PendingBoundsKey>();
        foreach (RegionalPendingBound pending in transient)
            transientKeys.Add(PendingBoundsKey.For(pending));
        bool sawStale = false;
        bool accepted = false;

        foreach (RegionalObstacleEdit edit in ordered)
        {
            if (!ValidEdit(edit))
                return Hold(RegionalQueueStatus.InvalidInput, RegionalRebuildReason.ConflictingSource, tick);
            var slot = (edit.Key.Source, edit.Key.Value);
            generations.TryGetValue(slot, out ulong knownGeneration);
            if (edit.Key.Generation < knownGeneration)
            {
                sawStale = true;
                continue;
            }
            if (edit.Key.Generation > knownGeneration && knownGeneration != 0 &&
                candidate.TryGetValue(new RegionalObstacleKey(edit.Key.Source, edit.Key.Value, knownGeneration),
                    out RegionalObstacleRecord priorGeneration) && priorGeneration.Bounds.HasValue)
                return Hold(RegionalQueueStatus.InvalidInput, RegionalRebuildReason.ConflictingSource, tick);

            bool exists = candidate.TryGetValue(edit.Key, out RegionalObstacleRecord existing);
            if (exists && edit.SourceRevision < existing.SourceRevision)
            {
                sawStale = true;
                continue;
            }
            if (exists && edit.SourceRevision == existing.SourceRevision)
            {
                if (existing.Layer != edit.Layer || !RegionalGridLayout.SameBounds(existing.Bounds, edit.CurrentBounds))
                    return Hold(RegionalQueueStatus.InvalidInput, RegionalRebuildReason.ConflictingSource, tick);
                // The latest accepted predecessor is retained across calls and
                // checkpoints. An initial authoritative record has no event
                // history, so only an explicit unchanged predecessor is known.
                if (lastEdits.TryGetValue(edit.Key, out RegionalObstacleEdit lastEdit)
                    ? !SameEdit(lastEdit, edit)
                    : !RegionalGridLayout.SameBounds(existing.Bounds, edit.PreviousBounds))
                    return Hold(RegionalQueueStatus.InvalidInput, RegionalRebuildReason.ConflictingSource, tick);
                continue;
            }
            if (!RegionalGridLayout.SameBounds(exists ? existing.Bounds : null, edit.PreviousBounds))
                return Hold(RegionalQueueStatus.InvalidInput, RegionalRebuildReason.ConflictingSource, tick);
            if (!exists && candidate.Count == _limits.MaximumTrackedObstacles)
                return Hold(RegionalQueueStatus.CapacityExceeded, RegionalRebuildReason.CapacityExceeded, tick);

            var replacement = new RegionalObstacleRecord(edit.Key, edit.SourceRevision, edit.Layer, edit.CurrentBounds);
            candidate = candidate.SetItem(edit.Key, replacement);
            lastEdits = lastEdits.SetItem(edit.Key, edit);
            generations = generations.SetItem(slot, edit.Key.Generation);
            accepted = true;
            _published.TryGetValue(edit.Key, out RegionalObstacleRecord published);
            if (RegionalGridLayout.SameBounds(published.Bounds, replacement.Bounds))
                pendingSince = pendingSince.Remove(edit.Key);
            else if (!pendingSince.ContainsKey(edit.Key))
                pendingSince = pendingSince.Add(edit.Key, tick);
            if (FrozenBatch is { } batch)
            {
                batch.Obstacles.TryGetValue(edit.Key, out RegionalObstacleRecord target);
                if (RegionalGridLayout.SameBounds(target.Bounds, replacement.Bounds))
                    afterBatchPendingSince = afterBatchPendingSince.Remove(edit.Key);
                else if (!afterBatchPendingSince.ContainsKey(edit.Key))
                    afterBatchPendingSince = afterBatchPendingSince.Add(edit.Key, tick);
            }

            if (!RegionalGridLayout.SameBounds(edit.PreviousBounds, edit.CurrentBounds) && edit.CurrentBounds is { } bounds)
            {
                var pending = new RegionalPendingBound(edit.Key, edit.SourceRevision, bounds, tick);
                if (transientKeys.Add(PendingBoundsKey.For(pending)))
                {
                    if (transientBuilder.Count == _limits.MaximumPendingBounds)
                        return Hold(RegionalQueueStatus.CapacityExceeded, RegionalRebuildReason.CapacityExceeded, tick);
                    transientBuilder.Add(pending);
                }
            }
        }
        transient = transientBuilder.ToImmutable();

        // The overlay capacity is checked against the candidate before the
        // ledger mutation. Newer current bounds and same-tick intermediate
        // bounds remain conservative until the next authoritative boundary.
        if (!TryBuildBounds(candidate, pendingSince, afterBatchPendingSince, transient,
                out ImmutableArray<RegionalPendingBound> boundsArray))
            return Hold(RegionalQueueStatus.CapacityExceeded, RegionalRebuildReason.CapacityExceeded, tick);
        bool boundaryChanged = tick != LastTick;
        _current = candidate;
        _lastEdits = lastEdits;
        _generations = generations;
        _pendingSince = pendingSince;
        _afterBatchPendingSince = afterBatchPendingSince;
        _transientBounds = transient;
        _changesThisTick = checked(previousCount + changes.Length);
        LastTick = tick;
        if (accepted || boundaryChanged)
            _overlayGeneration = checked(_overlayGeneration + 1);
        RebuildDerived();
        PendingBlocking = new RegionalPendingBlocking(_layout, _overlayGeneration, tick, false, boundsArray);
        if (!accepted && sawStale) return RegionalQueueStatus.Stale;
        return HasGeometryChanges ? RegionalQueueStatus.Accepted : RegionalQueueStatus.NoGeometryChange;
    }

    /// <summary>Call at a coherent new-tick boundary before using pending collision data.</summary>
    public void AdvanceTick(ulong tick)
    {
        if (tick < LastTick) throw new ArgumentOutOfRangeException(nameof(tick));
        if (tick == LastTick) return;
        LastTick = tick;
        _changesThisTick = 0;
        _transientBounds = [];
        _overlayGeneration = checked(_overlayGeneration + 1);
        if (!TryBuildOverlay(out RegionalPendingBlocking overlay))
            throw new InvalidOperationException("A previously admitted pending overlay exceeded its capacity.");
        PendingBlocking = overlay;
    }

    /// <summary>Freeze one preparation target; subsequent edits never replace it.</summary>
    public RegionalFrozenBatch BeginPreparation(ulong tick)
    {
        if (RequiresResynchronization || FrozenBatch is not null)
            throw new InvalidOperationException("Preparation requires coherent source state and no existing frozen batch.");
        AdvanceTick(tick);
        FrozenBatch = new RegionalFrozenBatch(_current, DirtyRegions, RequiresFullRebuild,
            RebuildReason, tick, _pendingSince);
        _afterBatchPendingSince = ImmutableSortedDictionary<RegionalObstacleKey, ulong>.Empty;
        return FrozenBatch;
    }

    /// <summary>Discard preparation ownership, preserving all adopted-versus-current differences.</summary>
    public void CancelPreparation()
    {
        if (FrozenBatch is null) return;
        FrozenBatch = null;
        _afterBatchPendingSince = ImmutableSortedDictionary<RegionalObstacleKey, ulong>.Empty;
        _overlayGeneration = checked(_overlayGeneration + 1);
        RebuildDerived();
        if (!TryBuildOverlay(out RegionalPendingBlocking overlay))
            throw new InvalidOperationException("Canceling preparation cannot increase pending blocker capacity.");
        PendingBlocking = overlay;
    }

    /// <summary>Adopt the retained batch at the boundary without discarding later source changes.</summary>
    public void MarkPublished(
        ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> targetRegistry, ulong tick)
    {
        ArgumentNullException.ThrowIfNull(targetRegistry);
        ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> expected = FrozenBatch?.Obstacles ?? _current;
        if (RequiresResynchronization || !ReferenceEquals(targetRegistry, expected))
            throw new InvalidOperationException("The publication target is stale or source resynchronization is required.");
        if (tick < LastTick) throw new ArgumentOutOfRangeException(nameof(tick));
        bool laterTick = tick > LastTick;
        _published = targetRegistry;
        _pendingSince = FrozenBatch is null
            ? ImmutableSortedDictionary<RegionalObstacleKey, ulong>.Empty
            : _afterBatchPendingSince;
        FrozenBatch = null;
        _afterBatchPendingSince = ImmutableSortedDictionary<RegionalObstacleKey, ulong>.Empty;
        _initialPopulation = false;
        LastTick = tick;
        if (laterTick)
        {
            _changesThisTick = 0;
            _transientBounds = [];
        }
        _overlayGeneration = checked(_overlayGeneration + 1);
        RebuildDerived();
        if (!TryBuildOverlay(out RegionalPendingBlocking overlay))
            throw new InvalidOperationException("Publication cannot exceed the previously reserved pending blocker capacity.");
        PendingBlocking = overlay;
    }

    public RegionalJournalState CaptureState() => new(StateFormatRevision, _layout, _limits,
        _current, _published, _lastEdits, _pendingSince, FrozenBatch, _afterBatchPendingSince,
        _transientBounds, LastTick, _changesThisTick,
        _overlayGeneration, RequiresResynchronization, _holdReason, _initialPopulation);

    public void RestoreState(RegionalJournalState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.FormatRevision != StateFormatRevision || !ReferenceEquals(state.Layout, _layout) ||
            state.Limits != _limits || state.OverlayGeneration == 0 ||
            state.ChangesThisTick < 0 || state.ChangesThisTick > _limits.MaximumChangesPerTick ||
            state.TransientBounds.IsDefault || state.TransientBounds.Length > _limits.MaximumPendingBounds ||
            !Enum.IsDefined(state.HoldReason) ||
            (state.RequiresResynchronization != (state.HoldReason is RegionalRebuildReason.ConflictingSource or RegionalRebuildReason.CapacityExceeded)))
            throw new ArgumentException("Checkpoint format, identity, capacity or hold state is incompatible.", nameof(state));
        ArgumentNullException.ThrowIfNull(state.PendingSince);
        ArgumentNullException.ThrowIfNull(state.LastEdits);
        ArgumentNullException.ThrowIfNull(state.AfterBatchPendingSince);
        if (state.LastEdits.KeyComparer != Comparer<RegionalObstacleKey>.Default ||
            state.LastEdits.Count > _limits.MaximumTrackedObstacles)
            throw new ArgumentException("Checkpoint predecessor history requires canonical bounded keys.", nameof(state));
        ValidateRegistry(state.CurrentObstacles, _limits);
        ValidateRegistry(state.PublishedObstacles, _limits);
        ValidateLineage(state.PublishedObstacles, state.CurrentObstacles);
        ValidatePendingSet(state.PublishedObstacles, state.CurrentObstacles, state.PendingSince, 0, state.LastTick);
        if (state.InitialPopulation && !state.PublishedObstacles.IsEmpty)
            throw new ArgumentException("Initial population cannot already have an adopted obstacle ledger.", nameof(state));
        if (state.FrozenBatch is { } frozen)
        {
            ValidateRegistry(frozen.Obstacles, _limits);
            if (frozen.StartedTick > state.LastTick || frozen.DirtyRegions is null ||
                frozen.DirtyRegions.KeyComparer != Comparer<int>.Default ||
                frozen.DirtyRegions.Count > Math.Min(MaximumDirtyRegionVisits, _layout.RegionCount))
                throw new ArgumentException("Checkpoint frozen batch metadata is invalid.", nameof(state));
            ValidateLineage(state.PublishedObstacles, frozen.Obstacles);
            ValidateLineage(frozen.Obstacles, state.CurrentObstacles);
            ValidatePendingSet(state.PublishedObstacles, frozen.Obstacles, frozen.PendingSince, 0, frozen.StartedTick);
            ValidatePendingSet(frozen.Obstacles, state.CurrentObstacles, state.AfterBatchPendingSince,
                frozen.StartedTick, state.LastTick);

            // Reconstruct only bounded dirty metadata, not raster/graph work.
            // Its exact equality proves that the frozen coverage belongs to
            // the adopted basis and target recorded in this checkpoint.
            var metadata = new RegionalChangeJournal(_layout, _limits,
                ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord>.Empty, frozen.StartedTick)
            {
                _current = frozen.Obstacles,
                _published = state.PublishedObstacles,
                _pendingSince = frozen.PendingSince,
                _initialPopulation = state.InitialPopulation
            };
            metadata.RebuildDerived();
            if (metadata.RequiresFullRebuild != frozen.RebuildAll || metadata.RebuildReason != frozen.RebuildReason ||
                !metadata.DirtyRegions.SetEquals(frozen.DirtyRegions))
                throw new ArgumentException("Checkpoint frozen dirty coverage does not match its source lineage.", nameof(state));
        }
        else if (state.AfterBatchPendingSince.KeyComparer != Comparer<RegionalObstacleKey>.Default ||
                 !state.AfterBatchPendingSince.IsEmpty)
            throw new ArgumentException("Checkpoint has later-batch differences without a frozen batch.", nameof(state));

        var restored = new RegionalChangeJournal(_layout, _limits,
            ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord>.Empty, state.LastTick)
        {
            _current = state.CurrentObstacles,
            _published = state.PublishedObstacles,
            _lastEdits = state.LastEdits,
            _pendingSince = state.PendingSince,
            FrozenBatch = state.FrozenBatch,
            _afterBatchPendingSince = state.AfterBatchPendingSince,
            _transientBounds = state.TransientBounds,
            _changesThisTick = state.ChangesThisTick,
            _overlayGeneration = state.OverlayGeneration,
            RequiresResynchronization = state.RequiresResynchronization,
            _holdReason = state.HoldReason,
            _initialPopulation = state.InitialPopulation
        };
        foreach (RegionalObstacleKey key in restored._current.Keys) restored.ObserveGeneration(key);
        foreach ((RegionalObstacleKey key, RegionalObstacleEdit edit) in state.LastEdits)
            if (edit.Key != key || !ValidEdit(edit) ||
                !restored._current.TryGetValue(key, out RegionalObstacleRecord current) ||
                current.SourceRevision != edit.SourceRevision || current.Layer != edit.Layer ||
                !RegionalGridLayout.SameBounds(current.Bounds, edit.CurrentBounds))
                throw new ArgumentException("Checkpoint predecessor history disagrees with its current ledger.", nameof(state));
        foreach (RegionalPendingBound bound in state.TransientBounds)
            if (bound.IntroducedTick != state.LastTick || !bound.Key.IsValid || !bound.SourceRevision.IsValid ||
                !restored._current.TryGetValue(bound.Key, out RegionalObstacleRecord current) ||
                bound.SourceRevision > current.SourceRevision)
                throw new ArgumentException("Checkpoint transient blocker provenance is invalid.", nameof(state));
        restored.RebuildDerived();
        if (!restored.TryBuildOverlay(out RegionalPendingBlocking overlay))
            throw new ArgumentException("Checkpoint pending blockers exceed capacity.", nameof(state));

        _current = restored._current;
        _published = restored._published;
        _lastEdits = restored._lastEdits;
        _generations = restored._generations;
        _pendingSince = restored._pendingSince;
        FrozenBatch = restored.FrozenBatch;
        _afterBatchPendingSince = restored._afterBatchPendingSince;
        _transientBounds = restored._transientBounds;
        _changesThisTick = restored._changesThisTick;
        _overlayGeneration = restored._overlayGeneration;
        _initialPopulation = restored._initialPopulation;
        _holdReason = restored._holdReason;
        LastTick = restored.LastTick;
        RequiresResynchronization = restored.RequiresResynchronization;
        RequiresFullRebuild = restored.RequiresFullRebuild;
        RebuildReason = restored.RebuildReason;
        OldestPendingTick = restored.OldestPendingTick;
        DirtyRegions = restored.DirtyRegions;
        PendingBlocking = overlay;
    }

    private static void ValidateLineage(
        ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> older,
        ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> newer)
    {
        foreach ((RegionalObstacleKey key, RegionalObstacleRecord prior) in older)
            if (!newer.TryGetValue(key, out RegionalObstacleRecord current) ||
                current.SourceRevision < prior.SourceRevision ||
                (current.SourceRevision == prior.SourceRevision &&
                 (current.Layer != prior.Layer || !RegionalGridLayout.SameBounds(current.Bounds, prior.Bounds))))
                throw new ArgumentException("Obstacle lineage lost an identity, source revision or equal-revision geometry.", nameof(newer));
    }

    private static void ValidatePendingSet(
        ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> basis,
        ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> target,
        ImmutableSortedDictionary<RegionalObstacleKey, ulong> pendingSince,
        ulong minimumTick, ulong maximumTick)
    {
        ArgumentNullException.ThrowIfNull(pendingSince);
        if (pendingSince.KeyComparer != Comparer<RegionalObstacleKey>.Default || pendingSince.Count > target.Count)
            throw new ArgumentException("Pending differences require canonical bounded keys.", nameof(pendingSince));
        foreach ((RegionalObstacleKey key, RegionalObstacleRecord current) in target)
        {
            basis.TryGetValue(key, out RegionalObstacleRecord previous);
            bool changed = !RegionalGridLayout.SameBounds(current.Bounds, previous.Bounds);
            if (changed != pendingSince.ContainsKey(key) ||
                (pendingSince.TryGetValue(key, out ulong since) && (since < minimumTick || since > maximumTick)))
                throw new ArgumentException("Pending differences or ages disagree with their source ledger.", nameof(pendingSince));
        }
        foreach (RegionalObstacleKey key in pendingSince.Keys)
            if (!target.ContainsKey(key))
                throw new ArgumentException("A pending identity has no target provenance.", nameof(pendingSince));
    }

    private RegionalQueueStatus Hold(RegionalQueueStatus status, RegionalRebuildReason reason, ulong tick)
    {
        if (tick > LastTick)
        {
            LastTick = tick;
            _changesThisTick = 0;
            _transientBounds = [];
        }
        RequiresResynchronization = true;
        _holdReason = reason;
        _overlayGeneration = checked(_overlayGeneration + 1);
        RequiresFullRebuild = true;
        RebuildReason = reason;
        if (!TryBuildOverlay(out RegionalPendingBlocking overlay))
            throw new InvalidOperationException("A previously admitted pending overlay exceeded its capacity.");
        PendingBlocking = overlay;
        return status;
    }

    private void RebuildDerived()
    {
        DirtyRegions = ImmutableSortedSet<int>.Empty;
        OldestPendingTick = 0;
        RequiresFullRebuild = RequiresResynchronization;
        RebuildReason = RequiresResynchronization ? _holdReason : RegionalRebuildReason.None;
        if (!HasGeometryChanges) return;
        OldestPendingTick = ulong.MaxValue;
        foreach (ulong tick in _pendingSince.Values) OldestPendingTick = Math.Min(OldestPendingTick, tick);
        if (RequiresResynchronization) return;
        if (_initialPopulation)
        {
            RequiresFullRebuild = true;
            RebuildReason = RegionalRebuildReason.InitialPopulation;
            return;
        }

        int threshold = checked((int)(((long)_layout.RegionCount * _limits.FullRebuildRegionPercent + 99) / 100));
        int visits = 0;
        foreach (RegionalObstacleKey key in _pendingSince.Keys)
        {
            _published.TryGetValue(key, out RegionalObstacleRecord published);
            _current.TryGetValue(key, out RegionalObstacleRecord current);
            if (!AddDirty(published.Bounds, threshold, ref visits) ||
                !AddDirty(current.Bounds, threshold, ref visits))
            {
                RequiresFullRebuild = true;
                RebuildReason = visits >= MaximumDirtyRegionVisits && DirtyRegions.Count < threshold
                    ? RegionalRebuildReason.DirtyAggregationLimit
                    : RegionalRebuildReason.LargeDirtyCoverage;
                return;
            }
        }
    }

    private bool AddDirty(AxisAlignedBounds? bounds, int threshold, ref int visits)
    {
        if (!bounds.HasValue) return true;
        foreach (int region in _layout.GetDirtyRegions(bounds.Value))
        {
            if (visits == MaximumDirtyRegionVisits) return false;
            visits++;
            DirtyRegions = DirtyRegions.Add(region);
            if (DirtyRegions.Count >= threshold) return false;
        }
        return true;
    }

    private bool TryBuildOverlay(out RegionalPendingBlocking overlay)
    {
        bool valid = TryBuildBounds(_current, _pendingSince, _afterBatchPendingSince, _transientBounds,
            out ImmutableArray<RegionalPendingBound> bounds);
        overlay = new RegionalPendingBlocking(_layout, _overlayGeneration, LastTick,
            RequiresResynchronization, bounds);
        return valid;
    }

    private bool TryBuildBounds(
        ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> current,
        ImmutableSortedDictionary<RegionalObstacleKey, ulong> pendingSince,
        ImmutableSortedDictionary<RegionalObstacleKey, ulong> afterBatchPendingSince,
        ImmutableArray<RegionalPendingBound> transient,
        out ImmutableArray<RegionalPendingBound> bounds)
    {
        int capacity = (int)Math.Min(_limits.MaximumPendingBounds,
            (long)transient.Length + pendingSince.Count + afterBatchPendingSince.Count);
        var items = new List<RegionalPendingBound>(capacity);
        var included = new Dictionary<PendingBoundsKey, int>();
        foreach (RegionalPendingBound pending in transient)
        {
            if (!AddPendingBound(items, included, pending))
            {
                bounds = [];
                return false;
            }
        }
        if (!AddCurrentBounds(current, pendingSince, items, included) ||
            !AddCurrentBounds(current, afterBatchPendingSince, items, included))
        {
            bounds = [];
            return false;
        }
        items.Sort(static (left, right) =>
        {
            int key = left.Key.CompareTo(right.Key);
            if (key != 0) return key;
            int revision = left.SourceRevision.CompareTo(right.SourceRevision);
            if (revision != 0) return revision;
            int coordinate = left.Bounds.Minimum.X.CompareTo(right.Bounds.Minimum.X);
            if (coordinate == 0) coordinate = left.Bounds.Minimum.Z.CompareTo(right.Bounds.Minimum.Z);
            if (coordinate == 0) coordinate = left.Bounds.Maximum.X.CompareTo(right.Bounds.Maximum.X);
            if (coordinate == 0) coordinate = left.Bounds.Maximum.Z.CompareTo(right.Bounds.Maximum.Z);
            if (coordinate == 0) coordinate = left.Bounds.Minimum.Y.CompareTo(right.Bounds.Minimum.Y);
            if (coordinate == 0) coordinate = left.Bounds.Maximum.Y.CompareTo(right.Bounds.Maximum.Y);
            return coordinate;
        });
        bounds = [.. items];
        return true;
    }

    private bool AddCurrentBounds(
        ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> current,
        ImmutableSortedDictionary<RegionalObstacleKey, ulong> pendingSince,
        List<RegionalPendingBound> items, Dictionary<PendingBoundsKey, int> included)
    {
        foreach ((RegionalObstacleKey key, ulong tick) in pendingSince)
        {
            RegionalObstacleRecord obstacle = current[key];
            if (obstacle.Bounds is { } bounds &&
                !AddPendingBound(items, included, new RegionalPendingBound(key, obstacle.SourceRevision, bounds, tick)))
                return false;
        }
        return true;
    }

    private bool AddPendingBound(List<RegionalPendingBound> items,
        Dictionary<PendingBoundsKey, int> included, RegionalPendingBound pending)
    {
        PendingBoundsKey key = PendingBoundsKey.For(pending);
        if (included.TryGetValue(key, out int index))
        {
            RegionalPendingBound existing = items[index];
            if (pending.IntroducedTick < existing.IntroducedTick)
                items[index] = existing with { IntroducedTick = pending.IntroducedTick };
            return true;
        }
        if (items.Count == _limits.MaximumPendingBounds) return false;
        included.Add(key, items.Count);
        items.Add(pending);
        return true;
    }

    private void ObserveGeneration(RegionalObstacleKey key)
    {
        var slot = (key.Source, key.Value);
        _generations.TryGetValue(slot, out ulong known);
        if (key.Generation > known) _generations = _generations.SetItem(slot, key.Generation);
    }

    private static bool ValidEdit(RegionalObstacleEdit edit) =>
        edit.Key.IsValid && edit.SourceRevision.IsValid && ValidLayer(edit.Key, edit.Layer);

    private static bool ValidLayer(RegionalObstacleKey key, RegionalObstacleLayer layer) =>
        key.Source == 2 ? layer == RegionalObstacleLayer.Authored :
        key.Source == 1 && layer is RegionalObstacleLayer.Structure or RegionalObstacleLayer.PhysicalWreck;

    private static bool SameEdit(RegionalObstacleEdit left, RegionalObstacleEdit right) =>
        left.Key == right.Key && left.SourceRevision == right.SourceRevision && left.Layer == right.Layer &&
        RegionalGridLayout.SameBounds(left.PreviousBounds, right.PreviousBounds) &&
        RegionalGridLayout.SameBounds(left.CurrentBounds, right.CurrentBounds);

    private readonly record struct PendingBoundsKey(
        RegionalObstacleKey Key, NavigationVersion Revision,
        System.Numerics.Vector3 Minimum, System.Numerics.Vector3 Maximum)
    {
        public static PendingBoundsKey For(RegionalPendingBound bound) =>
            new(bound.Key, bound.SourceRevision, bound.Bounds.Minimum, bound.Bounds.Maximum);
    }

    private static void ValidateRegistry(
        ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> registry,
        RegionalUpdateLimits limits)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (registry.Count > limits.MaximumTrackedObstacles)
            throw new ArgumentException("Obstacle ledger exceeds its capacity.", nameof(registry));
        if (registry.KeyComparer != Comparer<RegionalObstacleKey>.Default)
            throw new ArgumentException("Obstacle ledger requires canonical source ordering.", nameof(registry));
        var liveSlots = new Dictionary<(byte Source, ulong Value), ulong>();
        foreach ((RegionalObstacleKey key, RegionalObstacleRecord obstacle) in registry)
        {
            if (obstacle.Key != key || !key.IsValid || !obstacle.SourceRevision.IsValid || !ValidLayer(key, obstacle.Layer))
                throw new ArgumentException("Obstacle ledger contains invalid source identity or provenance.", nameof(registry));
            var slot = (key.Source, key.Value);
            if (liveSlots.TryGetValue(slot, out ulong liveGeneration) && key.Generation > liveGeneration)
                throw new ArgumentException("A live obstacle uses a retired source generation.", nameof(registry));
            if (obstacle.Bounds.HasValue && !liveSlots.TryAdd(slot, key.Generation))
                throw new ArgumentException("Multiple live generations occupy the same source identity.", nameof(registry));
        }
    }
}
