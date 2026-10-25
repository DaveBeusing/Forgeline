namespace ForgeLine.Navigation;

/// <summary>
/// A single worker owns the fixed-capacity arena until release. A span must not
/// escape that lifetime or overlap disposal; cancellation alone is not release.
/// </summary>
internal sealed class NavigationWorkspaceLease : IDisposable
{
    private NavigationWorkspacePool? _owner;
    private NavigationWorkspacePool.WorkspaceStorage? _storage;

    internal NavigationWorkspaceLease(
        NavigationWorkspacePool owner,
        NavigationWorkspacePool.WorkspaceStorage storage)
    {
        _owner = owner;
        _storage = storage;
        CapacityBytes = storage.Buffer.Length;
        AccountedBytes = owner.AccountedBytesPerWorkspace;
    }

    public int CapacityBytes { get; }
    public long AccountedBytes { get; }

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
