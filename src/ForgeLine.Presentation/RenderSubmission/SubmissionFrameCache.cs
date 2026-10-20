using System.Runtime.CompilerServices;
using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Presentation.RenderSubmission;

internal sealed class SubmissionFrameCache
{
    private const int PageSize = 256, MaximumPages = 256;
    private readonly Entry[]?[] _pages = new Entry[MaximumPages][];
    private PresentationSnapshot? _current, _previous;
    private float _alpha;
    private double? _seconds;
    private ulong _revision = 1;
    private PreparedRoot _overflow;
    private int _retainedPages, _pageHighWater;
    internal long RetainedBytes => (long)_retainedPages * PageSize * Unsafe.SizeOf<Entry>();
    internal int Interpolations { get; private set; }
    internal int Hits { get; private set; }

    internal void Begin(RenderWorld world, float alpha, double? seconds)
    {
        Interpolations = Hits = 0;
        if (ReferenceEquals(_current, world.CurrentSnapshot) && ReferenceEquals(_previous, world.PreviousSnapshot)
            && _alpha.Equals(alpha) && _seconds == seconds) return;
        if (_current?.SessionId != world.CurrentSnapshot?.SessionId) ClearPages();
        else if (_pageHighWater > MaximumPages / 4 && world.InstanceCount <= MaximumPages * PageSize / 4)
        {
            int keep = (world.InstanceCount + PageSize - 1) / PageSize;
            for (int page = keep; page < _pageHighWater; page++)
                if (_pages[page] is not null) { _pages[page] = null; _retainedPages--; }
            _pageHighWater = keep; _overflow = default;
        }
        _current = world.CurrentSnapshot; _previous = world.PreviousSnapshot; _alpha = alpha; _seconds = seconds;
        if (++_revision == 0) { ClearPages(); _revision = 1; }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref readonly PreparedRoot Get(RenderWorld world, int index)
    {
        int pageIndex = index / PageSize;
        if (pageIndex >= MaximumPages) { _overflow = Prepare(world, index); return ref _overflow; }
        Entry[]? page = _pages[pageIndex];
        if (page is null)
        {
            page = new Entry[PageSize]; _pages[pageIndex] = page;
            _retainedPages++; _pageHighWater = Math.Max(_pageHighWater, pageIndex + 1);
        }
        ref Entry entry = ref page[index % PageSize];
        if (entry.Revision == _revision) { Hits++; return ref entry.Root; }
        entry.Root = Prepare(world, index); entry.Revision = _revision;
        return ref entry.Root;
    }
    private PreparedRoot Prepare(RenderWorld world, int index)
    {
        Interpolations++;
        RenderInstance instance = world.GetInterpolatedInstance(index, _alpha);
        AxisAlignedBounds bounds = instance.MechanicalPose.Infantry.IsSpecified ? SkinPresentationBounds.Resolve(instance) :
            _seconds.HasValue ? VegetationVisualMotion.CullingBounds(instance) : SkinPresentationBounds.Resolve(instance);
        return new(instance, bounds, instance.Transform.ToMatrix());
    }
    private void ClearPages() { Array.Clear(_pages); _retainedPages = _pageHighWater = 0; _overflow = default; }
    internal void Clear() { ClearPages(); _current = _previous = null; }
    private struct Entry { internal ulong Revision; internal PreparedRoot Root; }
}

internal readonly record struct PreparedRoot(RenderInstance Instance, AxisAlignedBounds Bounds, Matrix4x4 Transform);

public readonly record struct SubmissionWorkMetrics(int SnapshotRoots, int Candidates, int Interpolations,
    int PoseCacheHits, int BoundsTests, int MeshResolutions, int MaterialResolutions,
    int AcceptedRoots, int UploadedParts, int DrawnParts, int PipelineBindings, int TextureBindings)
{
    public int DrawnRoots { get; init; }
    public long PreparedRootBytes { get; init; }
    public long SpatialIndexBytes { get; init; }
}
