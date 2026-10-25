namespace ForgeLine.Navigation.Regional;

internal sealed class RegionalSnapshotEntry
{
    private int _readers;

    public RegionalSnapshotEntry(RegionalPublication publication) => Publication = publication;

    public RegionalPublication Publication { get; }
    public int Readers => Volatile.Read(ref _readers);
    public void AddReader() => Interlocked.Increment(ref _readers);
    public void ReleaseReader() => Interlocked.Decrement(ref _readers);
}

/// <summary>
/// Readers retain unpooled immutable pages. The simulation owner releases each
/// lease at a deterministic job boundary, never by variable worker completion
/// order; otherwise retention backpressure could change publication ticks.
/// </summary>
internal sealed class RegionalSnapshotLease : IDisposable
{
    private RegionalSnapshotEntry? _entry;

    internal RegionalSnapshotLease(RegionalSnapshotEntry entry)
    {
        _entry = entry;
        entry.AddReader();
    }

    public RegionalPublication Publication => Volatile.Read(ref _entry)?.Publication ??
        throw new ObjectDisposedException(nameof(RegionalSnapshotLease));

    public void Dispose()
    {
        Interlocked.Exchange(ref _entry, null)?.ReleaseReader();
        GC.SuppressFinalize(this);
    }
}
