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
    internal int Interpolations { get; private set; }
    internal int Hits { get; private set; }

    internal void Begin(RenderWorld world, float alpha, double? seconds)
    {
        Interpolations = Hits = 0;
        if (ReferenceEquals(_current, world.CurrentSnapshot) && ReferenceEquals(_previous, world.PreviousSnapshot)
            && _alpha.Equals(alpha) && _seconds == seconds) return;
        if (_current?.SessionId != world.CurrentSnapshot?.SessionId) Array.Clear(_pages);
        _current = world.CurrentSnapshot; _previous = world.PreviousSnapshot; _alpha = alpha; _seconds = seconds;
        if (++_revision == 0) { Array.Clear(_pages); _revision = 1; }
    }

    internal PreparedRoot Get(RenderWorld world, int index)
    {
        int pageIndex = index / PageSize;
        if (pageIndex >= MaximumPages) return Prepare(world, index);
        Entry[] page = _pages[pageIndex] ??= new Entry[PageSize];
        ref Entry entry = ref page[index % PageSize];
        if (entry.Revision == _revision) { Hits++; return entry.Root; }
        entry.Root = Prepare(world, index); entry.Revision = _revision;
        return entry.Root;
    }
    private PreparedRoot Prepare(RenderWorld world, int index)
    {
        Interpolations++;
        RenderInstance instance = world.GetInterpolatedInstance(index, _alpha);
        AxisAlignedBounds bounds = instance.MechanicalPose.Infantry.IsSpecified ? SkinPresentationBounds.Resolve(instance) :
            _seconds.HasValue ? VegetationVisualMotion.CullingBounds(instance) : SkinPresentationBounds.Resolve(instance);
        return new(instance, bounds, instance.Transform.ToMatrix());
    }
    internal void Clear() { Array.Clear(_pages); _current = _previous = null; }
    private struct Entry { internal ulong Revision; internal PreparedRoot Root; }
}

internal readonly record struct PreparedRoot(RenderInstance Instance, AxisAlignedBounds Bounds, Matrix4x4 Transform);

public readonly record struct SubmissionWorkMetrics(int SnapshotRoots, int Candidates, int Interpolations,
    int PoseCacheHits, int BoundsTests, int MeshResolutions, int MaterialResolutions,
    int AcceptedRoots, int UploadedParts, int DrawnParts, int PipelineBindings, int TextureBindings)
{
    public int DrawnRoots { get; init; }
}
