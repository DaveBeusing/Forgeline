using System.Diagnostics.CodeAnalysis;

namespace ForgeLine.Navigation;

/// <summary>
/// Bounds exclusively leased, fixed-capacity search storage and retained storage together.
/// Admissions belong to the simulation coordinator's stable order after its job barrier.
/// </summary>
internal sealed class NavigationWorkspacePool : IDisposable
{
    // Conservative managed-storage allowances, not measurements of process working set.
    // Each arena includes its array, storage node, and one active lease allowance.
    private const long PoolOverheadBytes = 512;
    private const long WorkspaceOverheadBytes = 192;
    private readonly object _gate = new();
    private WorkspaceStorage? _available;
    private int _activeCount;
    private int _retainedCount;
    private int _peakActiveCount;
    private long _peakAccountedBytes = PoolOverheadBytes;
    private long _rentedCount;
    private long _reusedCount;
    private long _returnedCount;
    private long _releasedCount;
    private long _activeLimitRejections;
    private long _memoryLimitRejections;
    private long _initializedBytes;
    private bool _disposed;

    public NavigationWorkspacePool(
        int capacityBytes,
        int maximumActiveWorkspaces,
        int maximumRetainedWorkspaces,
        long maximumAccountedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacityBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacityBytes, Array.MaxLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumActiveWorkspaces);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRetainedWorkspaces);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            maximumRetainedWorkspaces, maximumActiveWorkspaces);

        CapacityBytes = capacityBytes;
        MaximumActiveWorkspaces = maximumActiveWorkspaces;
        MaximumRetainedWorkspaces = maximumRetainedWorkspaces;
        AccountedBytesPerWorkspace = ((long)capacityBytes + 7) / 8 * 8 + WorkspaceOverheadBytes;
        ArgumentOutOfRangeException.ThrowIfLessThan(
            maximumAccountedBytes, PoolOverheadBytes + AccountedBytesPerWorkspace);
        MaximumAccountedBytes = maximumAccountedBytes;
    }

    public int CapacityBytes { get; }
    public int MaximumActiveWorkspaces { get; }
    public int MaximumRetainedWorkspaces { get; }
    public long MaximumAccountedBytes { get; }
    public long AccountedBytesPerWorkspace { get; }

    /// <summary>
    /// Leases zero-initialized storage, or returns false under count or memory pressure.
    /// Initialization touches at most CapacityBytes; callers must charge that work to
    /// the search's initialization stage, including when a retained arena is reused.
    /// </summary>
    public bool TryRent([NotNullWhen(true)] out NavigationWorkspaceLease? lease)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            lease = null;
            if (_activeCount == MaximumActiveWorkspaces)
            {
                _activeLimitRejections = AddSaturated(_activeLimitRejections, 1);
                return false;
            }

            WorkspaceStorage? storage = _available;
            bool reused = storage is not null;
            if (storage is null)
            {
                if (GetAccountedBytes() > MaximumAccountedBytes - AccountedBytesPerWorkspace)
                {
                    _memoryLimitRejections = AddSaturated(_memoryLimitRejections, 1);
                    return false;
                }
                storage = new WorkspaceStorage(CapacityBytes);
            }

            // Construct before changing ownership so allocation failure cannot lose
            // an available arena or leave the active count inflated.
            var nextLease = new NavigationWorkspaceLease(this, storage);
            if (reused)
            {
                storage.Buffer.AsSpan().Clear();
                _available = storage.Next;
                storage.Next = null;
                _retainedCount--;
                _reusedCount = AddSaturated(_reusedCount, 1);
            }

            _activeCount++;
            _rentedCount = AddSaturated(_rentedCount, 1);
            _initializedBytes = AddSaturated(_initializedBytes, CapacityBytes);
            _peakActiveCount = Math.Max(_peakActiveCount, _activeCount);
            _peakAccountedBytes = Math.Max(_peakAccountedBytes, GetAccountedBytes());
            lease = nextLease;
            return true;
        }
    }

    /// <summary>Releases at most the requested number of idle arenas, without touching active leases.</summary>
    public int TrimRetained(int maximumToRelease)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumToRelease);
        lock (_gate)
        {
            int released = 0;
            while (released < maximumToRelease && _available is not null)
            {
                WorkspaceStorage storage = _available;
                _available = storage.Next;
                storage.Next = null;
                _retainedCount--;
                released++;
            }
            _releasedCount = AddSaturated(_releasedCount, released);
            return released;
        }
    }

    public NavigationWorkspaceStatistics GetStatistics()
    {
        lock (_gate)
        {
            return new NavigationWorkspaceStatistics(
                _activeCount,
                _retainedCount,
                _peakActiveCount,
                _activeCount * AccountedBytesPerWorkspace,
                _retainedCount * AccountedBytesPerWorkspace,
                PoolOverheadBytes,
                GetAccountedBytes(),
                _peakAccountedBytes,
                _rentedCount,
                _reusedCount,
                _returnedCount,
                _releasedCount,
                _activeLimitRejections,
                _memoryLimitRejections,
                _initializedBytes,
                _disposed);
        }
    }

    /// <summary>
    /// Stops admissions and drops idle arenas. Active storage remains owned and
    /// accounted until each worker has stopped using it and its lease is disposed.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _available = null;
            _releasedCount = AddSaturated(_releasedCount, _retainedCount);
            _retainedCount = 0;
        }
        GC.SuppressFinalize(this);
    }

    internal void Return(WorkspaceStorage storage)
    {
        lock (_gate)
        {
            _activeCount--;
            _returnedCount = AddSaturated(_returnedCount, 1);
            if (!_disposed && _retainedCount < MaximumRetainedWorkspaces)
            {
                storage.Next = _available;
                _available = storage;
                _retainedCount++;
            }
            else
            {
                _releasedCount = AddSaturated(_releasedCount, 1);
            }
        }
    }

    private long GetAccountedBytes() =>
        PoolOverheadBytes + ((long)_activeCount + _retainedCount) * AccountedBytesPerWorkspace;

    private static long AddSaturated(long value, long increment) =>
        value > long.MaxValue - increment ? long.MaxValue : value + increment;

    internal sealed class WorkspaceStorage
    {
        public WorkspaceStorage(int capacityBytes)
        {
            Buffer = new byte[capacityBytes];
        }

        public byte[] Buffer { get; }
        public WorkspaceStorage? Next { get; set; }
    }
}
