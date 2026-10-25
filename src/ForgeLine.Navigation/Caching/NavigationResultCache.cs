using System.Runtime.CompilerServices;

namespace ForgeLine.Navigation;

/// <summary>
/// Simulation-thread-owned storage for copied results. The caller supplies a complete value key;
/// this container does not infer route identity or whether a search proved unreachability.
/// Key equality and hashing must be pure and deterministic across checkpoint restoration.
/// Runtime work counts one hash, each compared key, each metadata operation, and each copied
/// 16-byte payload block. Every runtime lookup/mutation requires an explicit work allowance.
/// </summary>
internal sealed class NavigationResultCache<TKey, TElement> : IDisposable
    where TKey : unmanaged, IEquatable<TKey>
    where TElement : unmanaged
{
    private const long ObjectAllowanceBytes = 256;
    private const long ArrayAllowanceBytes = 64;
    private readonly Entry[] _entries;
    private readonly int[] _buckets;
    private readonly long _fixedBytes;
    private int _freeHead;
    private int _oldest = -1;
    private int _newest = -1;
    private int _maintenanceCursor;
    private int _remainingMaintenance;
    private ulong _lastTick;
    private bool _hasTick;
    private bool _disposed;
    private ulong _hits;
    private ulong _misses;
    private ulong _writes;
    private ulong _evictions;
    private ulong _expirations;
    private ulong _invalidations;
    private ulong _rejectedWrites;
    private ulong _inspectedSlots;

    /// <summary>All limits are explicit estimates, not measured performance presets.</summary>
    public NavigationResultCache(int maximumEntries, long maximumEstimatedBytes,
        int maximumMaintenanceEntriesPerTick, int maximumEvictionsPerWrite)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEstimatedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumMaintenanceEntriesPerTick);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEvictionsPerWrite);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumEntries, Array.MaxLength);

        _fixedBytes = checked(ObjectAllowanceBytes + ArrayBytes(maximumEntries, Unsafe.SizeOf<Entry>()) +
            ArrayBytes(maximumEntries, sizeof(int)));
        if (_fixedBytes > maximumEstimatedBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumEstimatedBytes),
                "The byte limit must include the fixed slot and lookup arrays.");

        MaximumEntries = maximumEntries;
        MaximumEstimatedBytes = maximumEstimatedBytes;
        MaximumMaintenanceEntriesPerTick = maximumMaintenanceEntriesPerTick;
        MaximumEvictionsPerWrite = maximumEvictionsPerWrite;
        _entries = new Entry[maximumEntries];
        _buckets = new int[maximumEntries];
        ResetStorage();
    }

    public int MaximumEntries { get; }
    public long MaximumEstimatedBytes { get; }
    public int MaximumMaintenanceEntriesPerTick { get; }
    public int MaximumEvictionsPerWrite { get; }
    public int Count { get; private set; }
    public long EstimatedBytes { get; private set; }

    public NavigationCacheMetrics GetMetrics() => new(Count, EstimatedBytes, _hits, _misses,
        _writes, _evictions, _expirations, _invalidations, _rejectedWrites, _inspectedSlots);

    /// <summary>Inspects payload size without copying, touching LRU order, or reclaiming storage.</summary>
    public NavigationCacheOperationStatus TryPeekLength(TKey key, ulong tick, int workAllowance,
        out int valueLength, out NavigationFailureReason failureReason, out int workConsumed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workAllowance);
        BeginTick(tick);
        valueLength = 0;
        failureReason = NavigationFailureReason.None;
        NavigationCacheOperationStatus status = FindBudgeted(key, workAllowance, out int index, out _, out workConsumed);
        if (status != NavigationCacheOperationStatus.Hit) return status;
        if (_entries[index].ExpiresAtTick <= tick) return NavigationCacheOperationStatus.Miss;
        valueLength = _entries[index].Value!.Length;
        failureReason = _entries[index].FailureReason;
        return NavigationCacheOperationStatus.Hit;
    }

    public NavigationCacheOperationStatus TryCopy(TKey key, ulong tick, Span<TElement> destination,
        int workAllowance, out int valueLength, out NavigationFailureReason failureReason, out int workConsumed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workAllowance);
        BeginTick(tick);
        valueLength = 0;
        failureReason = NavigationFailureReason.None;
        NavigationCacheOperationStatus status = FindBudgeted(key, workAllowance, out int index, out _, out workConsumed);
        if (status == NavigationCacheOperationStatus.BudgetExhausted) return status;
        if (status == NavigationCacheOperationStatus.Miss)
        {
            Increment(ref _misses);
            return status;
        }
        ref Entry entry = ref _entries[index];
        if (entry.ExpiresAtTick <= tick)
        {
            if (_remainingMaintenance > 0 && Charge(1, workAllowance, ref workConsumed))
            {
                _remainingMaintenance--;
                Remove(index);
                Increment(ref _expirations);
            }
            Increment(ref _misses);
            return NavigationCacheOperationStatus.Miss;
        }
        valueLength = entry.Value!.Length;
        failureReason = entry.FailureReason;
        if (destination.Length < valueLength) return NavigationCacheOperationStatus.BufferTooSmall;
        if (!Charge(CopyUnits(valueLength) + 1, workAllowance, ref workConsumed))
            return NavigationCacheOperationStatus.BudgetExhausted;
        entry.Value.AsSpan().CopyTo(destination);
        Touch(index);
        Increment(ref _hits);
        return NavigationCacheOperationStatus.Hit;
    }

    public NavigationCacheOperationStatus TryStoreSuccess(TKey key, ReadOnlySpan<TElement> value,
        ulong tick, ulong expiresAtTick, int workAllowance, out int workConsumed) =>
        TryStoreBudgeted(key, value, NavigationFailureReason.None, tick, expiresAtTick, workAllowance, out workConsumed);

    public NavigationCacheOperationStatus TryStoreUnreachable(TKey key, NavigationFailureReason failureReason,
        ulong tick, ulong expiresAtTick, int workAllowance, out int workConsumed)
    {
        if (failureReason is not NavigationFailureReason.NoHighLevelRoute and not NavigationFailureReason.NoLocalRoute)
            throw new ArgumentOutOfRangeException(nameof(failureReason));
        return TryStoreBudgeted(key, [], failureReason, tick, expiresAtTick, workAllowance, out workConsumed);
    }

    public NavigationCacheOperationStatus Invalidate(TKey key, ulong tick, int workAllowance, out int workConsumed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workAllowance);
        BeginTick(tick);
        NavigationCacheOperationStatus status = FindBudgeted(key, workAllowance, out int index, out _, out workConsumed);
        if (status != NavigationCacheOperationStatus.Hit) return status;
        if (!Charge(1, workAllowance, ref workConsumed)) return NavigationCacheOperationStatus.BudgetExhausted;
        _entries[index].ExpiresAtTick = tick;
        Increment(ref _invalidations);
        if (_remainingMaintenance > 0 && Charge(1, workAllowance, ref workConsumed))
        {
            _remainingMaintenance--;
            Remove(index);
        }
        return NavigationCacheOperationStatus.Hit;
    }

    public int Maintain(ulong tick, int workAllowance, out int slotsVisited)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workAllowance);
        BeginTick(tick);
        int workConsumed = 0;
        slotsVisited = 0;
        while (slotsVisited < _entries.Length && _remainingMaintenance > 0 && Charge(1, workAllowance, ref workConsumed))
        {
            int index = _maintenanceCursor;
            _maintenanceCursor = index + 1 == _entries.Length ? 0 : index + 1;
            slotsVisited++;
            _remainingMaintenance--;
            Increment(ref _inspectedSlots);
            if (_entries[index].Used && _entries[index].ExpiresAtTick <= tick && Charge(1, workAllowance, ref workConsumed))
            {
                Remove(index);
                Increment(ref _expirations);
            }
        }
        return workConsumed;
    }

    /// <summary>
    /// Captures exact slot/LRU/cursor state at the coordinator boundary. The caller reserves
    /// the supplied additional byte allowance separately from this live cache's retained bytes.
    /// </summary>
    public NavigationResultCacheState<TKey, TElement> CaptureState(long maximumCheckpointBytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        long estimate = GetCheckpointBytes();
        if (estimate > maximumCheckpointBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumCheckpointBytes), "Cache checkpoint exceeds its separate memory allowance.");
        var slots = new NavigationCacheSlotState<TKey, TElement>[_entries.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            ref Entry entry = ref _entries[i];
            slots[i] = new NavigationCacheSlotState<TKey, TElement>(entry.Used, entry.Key,
                entry.Value.AsSpan(), entry.FailureReason, entry.ExpiresAtTick, entry.Previous, entry.Next,
                entry.Bucket, entry.PreviousInBucket, entry.NextInBucket);
        }
        return new NavigationResultCacheState<TKey, TElement>(MaximumEntries, MaximumEstimatedBytes,
            MaximumMaintenanceEntriesPerTick, MaximumEvictionsPerWrite, _freeHead, _oldest, _newest,
            _maintenanceCursor, _remainingMaintenance, _lastTick, _hasTick, GetMetrics(), slots, estimate);
    }

    /// <summary>
    /// Restores into a detached cache. The allowance covers the immutable checkpoint and new
    /// cache together; an old live cache retained by the caller requires a separate allowance.
    /// Hash buckets and chains, slot/LRU order and tick budgets remain exact. Keys must have
    /// deterministic, pure equality and hashing; a different hash layout explicitly rejects.
    /// </summary>
    public static NavigationResultCache<TKey, TElement> RestoreState(
        NavigationResultCacheState<TKey, TElement> state, long maximumRestoreBytes)
    {
        ValidateState(state, maximumRestoreBytes);

        var restored = new NavigationResultCache<TKey, TElement>(state.MaximumEntries, state.MaximumEstimatedBytes,
            state.MaximumMaintenanceEntriesPerTick, state.MaximumEvictionsPerWrite);
        for (int i = 0; i < restored._entries.Length; i++)
        {
            NavigationCacheSlotState<TKey, TElement> source = state.Slots[i];
            ref Entry target = ref restored._entries[i];
            target.Previous = source.Previous;
            target.Next = source.Next;
            if (!source.Used) continue;
            int bucket = source.Bucket;
            long payloadBytes = source.Value.Count == 0 ? 0 : ArrayBytes(source.Value.Count, Unsafe.SizeOf<TElement>());
            if (payloadBytes > restored.MaximumEstimatedBytes - restored.EstimatedBytes)
                throw new ArgumentException("Cache checkpoint payload exceeds its memory capacity.", nameof(state));
            TElement[] value = source.Value.Count == 0 ? [] : new TElement[source.Value.Count];
            for (int element = 0; element < value.Length; element++) value[element] = source.Value[element];
            target.Used = true;
            target.Key = source.Key;
            target.Value = value;
            target.PayloadBytes = payloadBytes;
            target.FailureReason = source.FailureReason;
            target.ExpiresAtTick = source.ExpiresAtTick;
            target.Bucket = bucket;
            target.PreviousInBucket = source.PreviousInBucket;
            target.NextInBucket = source.NextInBucket;
            if (source.PreviousInBucket == -1)
            {
                if (restored._buckets[bucket] != -1)
                    throw new ArgumentException("Cache checkpoint repeats a bucket head.", nameof(state));
                restored._buckets[bucket] = i;
            }
            restored.Count++;
            restored.EstimatedBytes += payloadBytes;
        }

        restored.ValidateBuckets();

        if (restored.Count != state.Metrics.Entries || restored.EstimatedBytes != state.Metrics.EstimatedBytes ||
            restored.GetCheckpointBytes() != state.EstimatedBytes)
            throw new ArgumentException("Cache checkpoint counts or byte accounting do not match its payload.", nameof(state));
        restored._freeHead = state.FreeHead;
        restored._oldest = state.Oldest;
        restored._newest = state.Newest;
        restored._maintenanceCursor = state.MaintenanceCursor;
        restored._remainingMaintenance = state.RemainingMaintenance;
        restored._lastTick = state.LastTick;
        restored._hasTick = state.HasTick;
        restored._hits = state.Metrics.Hits;
        restored._misses = state.Metrics.Misses;
        restored._writes = state.Metrics.Writes;
        restored._evictions = state.Metrics.Evictions;
        restored._expirations = state.Metrics.Expirations;
        restored._invalidations = state.Metrics.Invalidations;
        restored._rejectedWrites = state.Metrics.RejectedWrites;
        restored._inspectedSlots = state.Metrics.InspectedSlots;
        return restored;
    }

    /// <summary>Validates bounded immutable metadata before payload inspection or restore allocation.</summary>
    public static void ValidateState(NavigationResultCacheState<TKey, TElement> state, long maximumRestoreBytes)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.FormatRevision != 2 || state.MaximumEntries <= 0 || state.MaximumEntries > Array.MaxLength ||
            state.Slots.Count != state.MaximumEntries || state.MaximumEstimatedBytes <= 0 ||
            state.MaximumMaintenanceEntriesPerTick <= 0 || state.MaximumEvictionsPerWrite <= 0 ||
            state.MaintenanceCursor < 0 || state.MaintenanceCursor >= state.MaximumEntries ||
            state.RemainingMaintenance < 0 || state.RemainingMaintenance > state.MaximumMaintenanceEntriesPerTick ||
            (!state.HasTick && (state.LastTick != 0 || state.RemainingMaintenance != 0 || state.Metrics.Entries != 0)))
            throw new ArgumentException("Cache checkpoint format, configuration, clock or maintenance cursor is invalid.", nameof(state));

        // Recompute all bounds from immutable slot contents before allocating the new cache.
        long fixedBytes = checked(ObjectAllowanceBytes + ArrayBytes(state.MaximumEntries, Unsafe.SizeOf<Entry>()) +
            ArrayBytes(state.MaximumEntries, sizeof(int)));
        if (fixedBytes > state.MaximumEstimatedBytes)
            throw new ArgumentException("Cache checkpoint cannot hold its fixed storage.", nameof(state));
        long actualBytes = fixedBytes;
        int actualCount = 0;
        for (int i = 0; i < state.Slots.Count; i++)
        {
            NavigationCacheSlotState<TKey, TElement> source = state.Slots[i];
            if (source is null || source.Previous < -1 || source.Previous >= state.MaximumEntries ||
                source.Next < -1 || source.Next >= state.MaximumEntries ||
                (!source.Used && source.Value.Count != 0) ||
                (source.FailureReason != NavigationFailureReason.None &&
                    (source.Value.Count != 0 || source.FailureReason is not NavigationFailureReason.NoHighLevelRoute and not NavigationFailureReason.NoLocalRoute)))
                throw new ArgumentException("Cache checkpoint slot data is invalid.", nameof(state));
            if (!source.Used) continue;
            if (source.Bucket < 0 || source.Bucket >= state.MaximumEntries ||
                source.PreviousInBucket < -1 || source.PreviousInBucket >= state.MaximumEntries ||
                source.NextInBucket < -1 || source.NextInBucket >= state.MaximumEntries ||
                (int)((uint)source.Key.GetHashCode() % (uint)state.MaximumEntries) != source.Bucket)
                throw new ArgumentException("Cache checkpoint requires the identical deterministic key hash layout.", nameof(state));
            long bytes = source.Value.Count == 0 ? 0 : ArrayBytes(source.Value.Count, Unsafe.SizeOf<TElement>());
            if (bytes > state.MaximumEstimatedBytes - actualBytes)
                throw new ArgumentException("Cache checkpoint payload exceeds its memory capacity.", nameof(state));
            actualBytes += bytes;
            actualCount++;
        }
        long actualCheckpointBytes = GetCheckpointBytes(state.MaximumEntries, actualBytes - fixedBytes);
        if (actualCount != state.Metrics.Entries || actualBytes != state.Metrics.EstimatedBytes ||
            actualCheckpointBytes != state.EstimatedBytes || actualCheckpointBytes > maximumRestoreBytes ||
            actualBytes > maximumRestoreBytes - actualCheckpointBytes)
            throw new ArgumentException("Cache checkpoint accounting or restore memory allowance is invalid.", nameof(state));
        ValidateAgeAndFreeLists(state, actualCount);

    }

    private void ValidateBuckets()
    {
        int visited = 0;
        for (int bucket = 0; bucket < _buckets.Length; bucket++)
        {
            int previous = -1;
            int index = _buckets[bucket];
            while (index != -1)
            {
                if (index < 0 || index >= _entries.Length || !_entries[index].Used ||
                    _entries[index].Bucket != bucket || _entries[index].PreviousInBucket != previous || visited >= Count)
                    throw new ArgumentException("Cache checkpoint bucket chain is invalid.");
                int earlier = _buckets[bucket];
                while (earlier != index)
                {
                    TKey key = _entries[earlier].Key;
                    if (key.Equals(_entries[index].Key))
                        throw new ArgumentException("Cache checkpoint contains duplicate keys.");
                    earlier = _entries[earlier].NextInBucket;
                }
                previous = index;
                index = _entries[index].NextInBucket;
                visited++;
            }
        }
        if (visited != Count) throw new ArgumentException("Cache checkpoint omits indexed entries.");
    }

    private long GetCheckpointBytes() => GetCheckpointBytes(_entries.Length, EstimatedBytes - _fixedBytes);

    private static long GetCheckpointBytes(int entries, long payloadBytes) => checked(512L +
        ArrayBytes(entries, IntPtr.Size) * 2 + (long)entries * (Unsafe.SizeOf<TKey>() + 192L) + payloadBytes);

    private static void ValidateAgeAndFreeLists(NavigationResultCacheState<TKey, TElement> state, int usedCount)
    {
        int count = 0;
        int previous = -1;
        int index = state.Oldest;
        while (index != -1)
        {
            if (index < 0 || index >= state.Slots.Count || !state.Slots[index].Used ||
                state.Slots[index].Previous != previous || count >= usedCount)
                throw new ArgumentException("Cache checkpoint LRU chain is invalid.", nameof(state));
            previous = index;
            index = state.Slots[index].Next;
            count++;
        }
        if (count != usedCount || previous != state.Newest)
            throw new ArgumentException("Cache checkpoint LRU endpoints are invalid.", nameof(state));
        count = 0;
        index = state.FreeHead;
        while (index != -1)
        {
            if (index < 0 || index >= state.Slots.Count || state.Slots[index].Used || count >= state.Slots.Count - usedCount)
                throw new ArgumentException("Cache checkpoint free-slot chain is invalid.", nameof(state));
            index = state.Slots[index].Next;
            count++;
        }
        if (count != state.Slots.Count - usedCount)
            throw new ArgumentException("Cache checkpoint omits free slots.", nameof(state));
    }

    /// <summary>
    /// Lifecycle operation for a coherent world/session boundary, not per-tick cache maintenance.
    /// Releases all result references. The monotonic clock and lifetime counters remain intact.
    /// </summary>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ResetStorage();
    }

    public void Dispose()
    {
        if (_disposed) return;
        ResetStorage();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private NavigationCacheOperationStatus TryStoreBudgeted(TKey key, ReadOnlySpan<TElement> value,
        NavigationFailureReason failureReason, ulong tick, ulong expiresAtTick, int workAllowance, out int workConsumed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workAllowance);
        BeginTick(tick);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(expiresAtTick, tick);
        NavigationCacheOperationStatus search = FindBudgeted(key, workAllowance, out int existing, out int bucket, out workConsumed);
        if (search == NavigationCacheOperationStatus.BudgetExhausted) return search;
        long bytes = value.IsEmpty ? 0 : ArrayBytes(value.Length, Unsafe.SizeOf<TElement>());
        if (bytes > MaximumEstimatedBytes - _fixedBytes) return RejectBudgetedWrite();
        long retainedWithoutOld = EstimatedBytes - (existing >= 0 ? _entries[existing].PayloadBytes : 0);
        int requiredCount = Count + (existing >= 0 ? 0 : 1);
        int removals = 0;
        int candidate = _oldest;
        while (requiredCount > MaximumEntries || bytes > MaximumEstimatedBytes - retainedWithoutOld)
        {
            if (candidate < 0 || removals >= MaximumEvictionsPerWrite || _remainingMaintenance == 0)
                return RejectBudgetedWrite();
            if (!Charge(1, workAllowance, ref workConsumed)) return NavigationCacheOperationStatus.BudgetExhausted;
            _remainingMaintenance--;
            Increment(ref _inspectedSlots);
            if (candidate == existing)
            {
                candidate = _entries[candidate].Next;
                continue;
            }
            retainedWithoutOld -= _entries[candidate].PayloadBytes;
            requiredCount--;
            removals++;
            candidate = _entries[candidate].Next;
        }
        long mutationWork = removals + (existing >= 0 ? 1L : 0L) + 1 + CopyUnits(value.Length);
        if (!Charge(mutationWork, workAllowance, ref workConsumed)) return NavigationCacheOperationStatus.BudgetExhausted;
        for (int i = 0; i < removals; i++)
        {
            int victim = _oldest == existing ? _entries[_oldest].Next : _oldest;
            Remove(victim);
            Increment(ref _evictions);
        }
        if (existing >= 0) Remove(existing);
        TElement[] owned = value.IsEmpty ? [] : value.ToArray();
        int slot = _freeHead;
        _freeHead = _entries[slot].Next;
        _entries[slot] = new Entry
        {
            Used = true, Key = key, Value = owned, PayloadBytes = bytes,
            FailureReason = failureReason, ExpiresAtTick = expiresAtTick,
            Previous = _newest, Next = -1, Bucket = bucket,
            PreviousInBucket = -1, NextInBucket = _buckets[bucket]
        };
        if (_buckets[bucket] >= 0) _entries[_buckets[bucket]].PreviousInBucket = slot;
        _buckets[bucket] = slot;
        if (_newest >= 0) _entries[_newest].Next = slot;
        else _oldest = slot;
        _newest = slot;
        Count++;
        EstimatedBytes += bytes;
        Increment(ref _writes);
        return NavigationCacheOperationStatus.Stored;
    }

    private NavigationCacheOperationStatus RejectBudgetedWrite()
    {
        Increment(ref _rejectedWrites);
        return NavigationCacheOperationStatus.CapacityExhausted;
    }

    private NavigationCacheOperationStatus FindBudgeted(TKey key, int allowance,
        out int index, out int bucket, out int consumed)
    {
        index = -1;
        bucket = -1;
        consumed = 0;
        if (!Charge(1, allowance, ref consumed)) return NavigationCacheOperationStatus.BudgetExhausted;
        bucket = GetBucket(key);
        int candidate = _buckets[bucket];
        while (candidate >= 0)
        {
            if (!Charge(1, allowance, ref consumed)) return NavigationCacheOperationStatus.BudgetExhausted;
            TKey stored = _entries[candidate].Key;
            if (stored.Equals(key))
            {
                index = candidate;
                return NavigationCacheOperationStatus.Hit;
            }
            candidate = _entries[candidate].NextInBucket;
        }
        return NavigationCacheOperationStatus.Miss;
    }

    private static long CopyUnits(int elements) => ((long)elements * Unsafe.SizeOf<TElement>() + 15) / 16;

    private static bool Charge(long work, int allowance, ref int consumed)
    {
        if (work > allowance - consumed) return false;
        consumed += (int)work;
        return true;
    }

    private void BeginTick(ulong tick)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_hasTick && tick < _lastTick)
            throw new ArgumentOutOfRangeException(nameof(tick), "Cache operations require monotonic simulation ticks.");
        if (!_hasTick || tick > _lastTick)
        {
            _lastTick = tick;
            _hasTick = true;
            _remainingMaintenance = MaximumMaintenanceEntriesPerTick;
        }
    }

    private int GetBucket(TKey key) => (int)((uint)key.GetHashCode() % (uint)_buckets.Length);

    private void Touch(int index)
    {
        if (_newest == index) return;
        UnlinkAge(index);
        _entries[index].Previous = _newest;
        _entries[index].Next = -1;
        _entries[_newest].Next = index;
        _newest = index;
    }

    private void Remove(int index)
    {
        ref Entry entry = ref _entries[index];
        UnlinkAge(index);
        if (entry.PreviousInBucket >= 0)
            _entries[entry.PreviousInBucket].NextInBucket = entry.NextInBucket;
        else _buckets[entry.Bucket] = entry.NextInBucket;
        if (entry.NextInBucket >= 0)
            _entries[entry.NextInBucket].PreviousInBucket = entry.PreviousInBucket;
        EstimatedBytes -= entry.PayloadBytes;
        Count--;
        entry = default;
        entry.Next = _freeHead;
        _freeHead = index;
    }

    private void UnlinkAge(int index)
    {
        ref Entry entry = ref _entries[index];
        if (entry.Previous >= 0) _entries[entry.Previous].Next = entry.Next;
        else _oldest = entry.Next;
        if (entry.Next >= 0) _entries[entry.Next].Previous = entry.Previous;
        else _newest = entry.Previous;
    }

    private void ResetStorage()
    {
        Array.Clear(_entries);
        Array.Fill(_buckets, -1);
        for (int i = 0; i < _entries.Length; i++)
            _entries[i].Next = i + 1 == _entries.Length ? -1 : i + 1;
        _freeHead = 0;
        _oldest = -1;
        _newest = -1;
        _maintenanceCursor = 0;
        Count = 0;
        EstimatedBytes = _fixedBytes;
    }

    private static long ArrayBytes(int count, int elementBytes) =>
        checked((ArrayAllowanceBytes + (long)count * elementBytes + 7) & ~7L);

    private static void Increment(ref ulong value)
    {
        if (value < ulong.MaxValue) value++;
    }

    private struct Entry
    {
        public TKey Key;
        public TElement[]? Value;
        public NavigationFailureReason FailureReason;
        public ulong ExpiresAtTick;
        public long PayloadBytes;
        public int Previous;
        public int Next;
        public int Bucket;
        public int PreviousInBucket;
        public int NextInBucket;
        public bool Used;
    }
}

internal readonly record struct NavigationCacheMetrics(
    int Entries, long EstimatedBytes, ulong Hits, ulong Misses, ulong Writes,
    ulong Evictions, ulong Expirations, ulong Invalidations, ulong RejectedWrites, ulong InspectedSlots);

internal enum NavigationCacheOperationStatus : byte
{
    Miss, Hit, Stored, CapacityExhausted, BudgetExhausted, BufferTooSmall
}
