using System.Runtime.CompilerServices;

namespace ForgeLine.Navigation;

/// <summary>
/// Simulation-thread-owned storage for copied results. The caller supplies a complete value key;
/// this container does not infer route identity or whether a search proved unreachability.
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

    /// <summary>Copies a completed positive result into storage, subject to both memory limits.</summary>
    public bool TryStoreSuccess(TKey key, ReadOnlySpan<TElement> value, ulong tick, ulong expiresAtTick) =>
        TryStore(key, value, NavigationFailureReason.None, tick, expiresAtTick);

    /// <summary>
    /// Only a completed exhaustive search may call this method. Canceled, stale, invalid and
    /// budget-exhausted searches are rejected rather than represented as negative cache entries.
    /// </summary>
    public bool TryStoreUnreachable(TKey key, NavigationFailureReason failureReason,
        ulong tick, ulong expiresAtTick)
    {
        if (failureReason is not NavigationFailureReason.NoHighLevelRoute and not NavigationFailureReason.NoLocalRoute)
            throw new ArgumentOutOfRangeException(nameof(failureReason),
                "Only a confirmed high-level or local no-route result is cacheable as unreachable.");
        return TryStore(key, [], failureReason, tick, expiresAtTick);
    }

    /// <summary>
    /// Copies a live entry without exposing retained arrays. A confirmed negative returns true
    /// with zero length and its failure reason. A missing or expired key returns false and zero
    /// length; an undersized destination returns false and the required positive length.
    /// </summary>
    public bool TryCopy(TKey key, ulong tick, Span<TElement> destination,
        out int valueLength, out NavigationFailureReason failureReason)
    {
        BeginTick(tick);
        valueLength = 0;
        failureReason = NavigationFailureReason.None;
        int index = Find(key);
        if (index < 0)
        {
            Increment(ref _misses);
            return false;
        }

        ref Entry entry = ref _entries[index];
        if (entry.ExpiresAtTick <= tick)
        {
            if (_remainingMaintenance > 0)
            {
                _remainingMaintenance--;
                Remove(index);
                Increment(ref _expirations);
            }
            Increment(ref _misses);
            return false;
        }

        valueLength = entry.Value!.Length;
        failureReason = entry.FailureReason;
        if (destination.Length < valueLength) return false;
        entry.Value.AsSpan().CopyTo(destination);
        Touch(index);
        Increment(ref _hits);
        return true;
    }

    /// <summary>
    /// Immediately makes the exact key ineligible. Storage reclamation consumes maintenance
    /// allowance when available, otherwise the expired entry remains accounted until maintenance.
    /// </summary>
    public bool Invalidate(TKey key, ulong tick)
    {
        BeginTick(tick);
        int index = Find(key);
        if (index < 0) return false;
        _entries[index].ExpiresAtTick = tick;
        Increment(ref _invalidations);
        if (_remainingMaintenance > 0)
        {
            _remainingMaintenance--;
            Remove(index);
        }
        return true;
    }

    /// <summary>
    /// Examines a bounded number of slots, continuing at a stable cursor next time. Repeated
    /// calls in one tick share the allowance with eviction and expired-read cleanup.
    /// </summary>
    public int Maintain(ulong tick, int maximumSlots)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumSlots);
        BeginTick(tick);
        int visits = Math.Min(Math.Min(maximumSlots, _remainingMaintenance), _entries.Length);
        for (int i = 0; i < visits; i++)
        {
            int index = _maintenanceCursor;
            _maintenanceCursor = index + 1 == _entries.Length ? 0 : index + 1;
            _remainingMaintenance--;
            Increment(ref _inspectedSlots);
            if (_entries[index].Used && _entries[index].ExpiresAtTick <= tick)
            {
                Remove(index);
                Increment(ref _expirations);
            }
        }
        return visits;
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

    private bool TryStore(TKey key, ReadOnlySpan<TElement> value, NavigationFailureReason failureReason,
        ulong tick, ulong expiresAtTick)
    {
        BeginTick(tick);
        if (expiresAtTick <= tick)
            throw new ArgumentOutOfRangeException(nameof(expiresAtTick), "Cached results require a future expiry tick.");

        long bytes = value.IsEmpty ? 0 : ArrayBytes(value.Length, Unsafe.SizeOf<TElement>());
        int bucket = GetBucket(key);
        int existing = Find(key, bucket);
        long oldBytes = existing >= 0 ? _entries[existing].PayloadBytes : 0;
        long retainedWithoutOld = EstimatedBytes - oldBytes;
        if (bytes > MaximumEstimatedBytes - _fixedBytes)
            return RejectWrite();

        int requiredCount = Count + (existing >= 0 ? 0 : 1);
        int removals = 0;
        int candidate = _oldest;
        while (requiredCount > MaximumEntries || bytes > MaximumEstimatedBytes - retainedWithoutOld)
        {
            if (candidate >= 0 && candidate == existing) candidate = _entries[candidate].Next;
            if (candidate < 0 || removals >= MaximumEvictionsPerWrite || _remainingMaintenance == 0)
                return RejectWrite();
            _remainingMaintenance--;
            Increment(ref _inspectedSlots);
            retainedWithoutOld -= _entries[candidate].PayloadBytes;
            requiredCount--;
            removals++;
            candidate = _entries[candidate].Next;
        }

        // Preflight above makes bounded backpressure non-destructive. Reclaim before allocating
        // the owned replacement, so no live cache-owned payload exceeds the accounting bound.
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
            Used = true,
            Key = key,
            Value = owned,
            PayloadBytes = bytes,
            FailureReason = failureReason,
            ExpiresAtTick = expiresAtTick,
            Previous = _newest,
            Next = -1,
            Bucket = bucket,
            PreviousInBucket = -1,
            NextInBucket = _buckets[bucket]
        };
        if (_buckets[bucket] >= 0) _entries[_buckets[bucket]].PreviousInBucket = slot;
        _buckets[bucket] = slot;
        if (_newest >= 0) _entries[_newest].Next = slot;
        else _oldest = slot;
        _newest = slot;
        Count++;
        EstimatedBytes += bytes;
        Increment(ref _writes);
        return true;
    }

    private bool RejectWrite()
    {
        Increment(ref _rejectedWrites);
        return false;
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

    private int Find(TKey key) => Find(key, GetBucket(key));

    private int Find(TKey key, int bucket)
    {
        int index = _buckets[bucket];
        while (index >= 0)
        {
            TKey candidate = _entries[index].Key;
            if (candidate.Equals(key)) return index;
            index = _entries[index].NextInBucket;
        }
        return -1;
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
