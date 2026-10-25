namespace ForgeLine.Navigation;

/// <summary>
/// A single worker owns the fixed-capacity arena until release. A span must not
/// escape that lifetime or overlap disposal; cancellation alone is not release.
/// </summary>
internal sealed class NavigationWorkspaceLease : IDisposable
{
    private NavigationWorkspacePool? _owner;
    private NavigationWorkspacePool.WorkspaceStorage? _storage;
    private int _initializedBytes;

    internal NavigationWorkspaceLease(
        NavigationWorkspacePool owner,
        NavigationWorkspacePool.WorkspaceStorage storage,
        bool deferInitialization)
    {
        _owner = owner;
        _storage = storage;
        CapacityBytes = storage.Buffer.Length;
        AccountedBytes = owner.AccountedBytesPerWorkspace;
        _initializedBytes = deferInitialization ? 0 : CapacityBytes;
    }

    public int CapacityBytes { get; }
    public long AccountedBytes { get; }
    public int InitializedBytes => _initializedBytes;
    public bool IsInitialized => _initializedBytes == CapacityBytes;

    /// <summary>
    /// Clears at most maximumBytes of a reserved arena and returns the actual byte
    /// count. The kernel maps this bounded work to its shared work-unit allowance.
    /// Fresh and retained arenas start at the same logical initialization position.
    /// Restore may reconstruct the recorded prefix before simulation resumes.
    /// </summary>
    public int AdvanceInitialization(int maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
        NavigationWorkspacePool.WorkspaceStorage? storage = Volatile.Read(ref _storage);
        ObjectDisposedException.ThrowIf(storage is null, this);
        int count = Math.Min(maximumBytes, CapacityBytes - _initializedBytes);
        if (count > 0)
        {
            storage.Buffer.AsSpan(_initializedBytes, count).Clear();
            _initializedBytes += count;
            _owner!.RecordInitialization(count);
        }
        return count;
    }

    /// <summary>
    /// Fixed-size bytes that the kernel may partition into bounded unmanaged spans.
    /// No array reference or resizable collection is exposed by the lease.
    /// </summary>
    public Span<byte> Buffer
    {
        get
        {
            NavigationWorkspacePool.WorkspaceStorage? storage = Volatile.Read(ref _storage);
            ObjectDisposedException.ThrowIf(storage is null, this);
            if (!IsInitialized)
                throw new InvalidOperationException("Complete the reserved workspace initialization before reading or writing its buffer.");
            return storage.Buffer.AsSpan();
        }
    }

    public void Dispose()
    {
        NavigationWorkspacePool.WorkspaceStorage? storage = Interlocked.Exchange(ref _storage, null);
        if (storage is not null)
        {
            NavigationWorkspacePool? owner = Interlocked.Exchange(ref _owner, null);
            owner!.Return(storage);
        }
        GC.SuppressFinalize(this);
    }
}
