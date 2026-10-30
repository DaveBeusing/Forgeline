namespace ForgeLine.Graphics;

// Single render-owner bookkeeping. Native releases and descriptor reuse share a fence.
internal sealed class GraphicsResourceRetirement
{
    private readonly HashSet<Resource> _live = [];
    private readonly List<Resource> _pending = [];
    private readonly List<Resource> _recording = [];

    internal const int Capacity = 4_096;
    internal int PendingCount => _pending.Count;
    internal int LiveCount => _live.Count;
    internal int PeakPendingCount { get; private set; }
    internal long ReleasedCount { get; private set; }
    internal long LiveBytes { get; private set; }
    internal long PendingBytes { get; private set; }

    internal Resource Register(Action release, long logicalBytes = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(logicalBytes);
        var resource = new Resource(release, logicalBytes);
        LiveBytes = checked(LiveBytes + logicalBytes);
        _live.Add(resource);
        return resource;
    }

    internal void Use(Resource resource)
    {
        resource.ThrowIfDisposed();
        if (!resource.Recording)
        {
            resource.Recording = true;
            _recording.Add(resource);
        }
    }

    internal void CompleteRecording(ulong fence)
    {
        foreach (Resource resource in _recording)
        {
            resource.LastUseFence = fence;
            resource.Recording = false;
        }
        _recording.Clear();
    }

    internal void Retire(Resource resource, ulong completedFence)
    {
        if (resource.IsDisposed)
        {
            return;
        }
        if (!resource.Recording && resource.LastUseFence <= completedFence)
        {
            resource.IsDisposed = true;
            Release(resource);
            return;
        }
        if (_pending.Count == Capacity)
        {
            throw new GraphicsDeviceException("Resource retirement capacity exhausted.", "retirement-capacity");
        }
        resource.IsDisposed = true;
        _pending.Add(resource);
        PendingBytes = checked(PendingBytes + resource.LogicalBytes);
        PeakPendingCount = Math.Max(PeakPendingCount, _pending.Count);
    }

    internal void Collect(ulong completedFence)
    {
        for (int index = 0; index < _pending.Count;)
        {
            Resource resource = _pending[index];
            if (resource.Recording || resource.LastUseFence > completedFence)
            {
                index++;
                continue;
            }
            _pending.RemoveAt(index);
            PendingBytes -= resource.LogicalBytes;
            Release(resource);
        }
    }

    // Only after GPU idle or confirmed device removal; also invalidates undisposed wrappers.
    internal void ReleaseAll()
    {
        foreach (Resource resource in _live)
        {
            resource.IsDisposed = true;
            resource.Release();
            ReleasedCount++;
        }
        _live.Clear();
        LiveBytes = PendingBytes = 0;
        _pending.Clear();
        _recording.Clear();
    }

    private void Release(Resource resource)
    {
        _live.Remove(resource);
        LiveBytes -= resource.LogicalBytes;
        resource.Release();
        ReleasedCount++;
    }

    internal sealed class Resource(Action release, long logicalBytes = 0)
    {
        internal long LogicalBytes { get; } = logicalBytes;
        internal ulong LastUseFence { get; set; }
        internal bool Recording { get; set; }
        internal bool IsDisposed { get; set; }

        internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, this);

        internal void ValidateWrite(ulong completedFence)
        {
            ThrowIfDisposed();
            if (Recording || LastUseFence > completedFence)
            {
                throw new InvalidOperationException("An upload buffer cannot be overwritten while recorded or in flight.");
            }
        }

        internal void Release() => release();
    }
}
