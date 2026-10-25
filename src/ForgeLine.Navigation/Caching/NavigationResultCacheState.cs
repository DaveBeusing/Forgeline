namespace ForgeLine.Navigation;

/// <summary>Owned immutable storage state. Snapshot memory is additional to the live cache.</summary>
internal sealed class NavigationResultCacheState<TKey, TElement>
    where TKey : unmanaged, IEquatable<TKey>
    where TElement : unmanaged
{
    internal NavigationResultCacheState(int maximumEntries, long maximumEstimatedBytes,
        int maximumMaintenanceEntriesPerTick, int maximumEvictionsPerWrite,
        int freeHead, int oldest, int newest, int maintenanceCursor, int remainingMaintenance,
        ulong lastTick, bool hasTick, NavigationCacheMetrics metrics,
        ReadOnlySpan<NavigationCacheSlotState<TKey, TElement>> slots, long estimatedBytes, int formatRevision = 2)
    {
        MaximumEntries = maximumEntries;
        FormatRevision = formatRevision;
        MaximumEstimatedBytes = maximumEstimatedBytes;
        MaximumMaintenanceEntriesPerTick = maximumMaintenanceEntriesPerTick;
        MaximumEvictionsPerWrite = maximumEvictionsPerWrite;
        FreeHead = freeHead;
        Oldest = oldest;
        Newest = newest;
        MaintenanceCursor = maintenanceCursor;
        RemainingMaintenance = remainingMaintenance;
        LastTick = lastTick;
        HasTick = hasTick;
        Metrics = metrics;
        Slots = Array.AsReadOnly(slots.ToArray());
        EstimatedBytes = estimatedBytes;
    }

    public int FormatRevision { get; }
    public int MaximumEntries { get; }
    public long MaximumEstimatedBytes { get; }
    public int MaximumMaintenanceEntriesPerTick { get; }
    public int MaximumEvictionsPerWrite { get; }
    public int FreeHead { get; }
    public int Oldest { get; }
    public int Newest { get; }
    public int MaintenanceCursor { get; }
    public int RemainingMaintenance { get; }
    public ulong LastTick { get; }
    public bool HasTick { get; }
    public NavigationCacheMetrics Metrics { get; }
    public IReadOnlyList<NavigationCacheSlotState<TKey, TElement>> Slots { get; }
    public long EstimatedBytes { get; }
}

internal sealed class NavigationCacheSlotState<TKey, TElement>
    where TKey : unmanaged, IEquatable<TKey>
    where TElement : unmanaged
{
    internal NavigationCacheSlotState(bool used, TKey key, ReadOnlySpan<TElement> value,
        NavigationFailureReason failureReason, ulong expiresAtTick, int previous, int next,
        int bucket, int previousInBucket, int nextInBucket)
    {
        Used = used;
        Key = key;
        Value = Array.AsReadOnly(value.ToArray());
        FailureReason = failureReason;
        ExpiresAtTick = expiresAtTick;
        Previous = previous;
        Next = next;
        Bucket = bucket;
        PreviousInBucket = previousInBucket;
        NextInBucket = nextInBucket;
    }

    public bool Used { get; }
    public TKey Key { get; }
    public IReadOnlyList<TElement> Value { get; }
    public NavigationFailureReason FailureReason { get; }
    public ulong ExpiresAtTick { get; }
    public int Previous { get; }
    public int Next { get; }
    public int Bucket { get; }
    public int PreviousInBucket { get; }
    public int NextInBucket { get; }
}
