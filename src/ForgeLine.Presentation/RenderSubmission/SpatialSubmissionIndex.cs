using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Presentation.RenderSubmission;

// The permutation is private. Candidates are consumed in original snapshot order.
internal sealed class SpatialSubmissionIndex
{
    internal const int MaximumRetainedRoots = 262144;
    private PresentationSnapshot? _current, _previous;
    private ulong[] _order = [];
    private uint[] _cellKeys = [];
    private int _count;
    private AxisAlignedBounds[] _bounds = [], _clusters = [];
    private bool[] _finite = [], _valid = [], _visible = [];
    private int[] _sourceCluster = [];
    internal int ClusterCount { get; private set; }
    internal int RejectedClusters { get; private set; }
    internal int RejectedRoots { get; private set; }
    internal int Rebuilds { get; private set; }
    internal int Sorts { get; private set; }
    internal long RetainedBytes => (long)_order.Length * (8 + 24 + 1 + 4 + 4) + (long)_clusters.Length * 26;

    internal void Update(RenderWorld world)
    {
        if (ReferenceEquals(_current, world.CurrentSnapshot) && ReferenceEquals(_previous, world.PreviousSnapshot)) return;
        bool sort = _count != world.InstanceCount || _current?.SessionId != world.CurrentSnapshot?.SessionId;
        _current = world.CurrentSnapshot; _previous = world.PreviousSnapshot;
        int count = world.InstanceCount; _count = count;
        if (_order.Length < count || _order.Length > 65536 && count <= 16384)
        {
            int capacity = count == 0 ? 0 : (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)count);
            sort = true;
            _order = new ulong[capacity]; _cellKeys = new uint[capacity]; _bounds = new AxisAlignedBounds[capacity];
            _finite = new bool[capacity]; _sourceCluster = new int[capacity];
        }
        ClusterCount = (count + 31) / 32;
        if (_clusters.Length < ClusterCount || _clusters.Length > 2048 && count <= 16384)
        { _clusters = new AxisAlignedBounds[ClusterCount]; _valid = new bool[ClusterCount]; _visible = new bool[ClusterCount]; }
        for (int i = 0; i < count; i++)
        {
            RenderInstance current = _current!.GetInstance(i);
            _finite[i] = InstanceVisibilityIndex.TrySweptBounds(current, world.GetInterpolatedInstance(i, 0), out _bounds[i]);
            Vector3 position = current.Transform.Position;
            uint key = Spread(Quantize(position.X)) | Spread(Quantize(position.Z)) << 1;
            sort |= key != _cellKeys[i]; _cellKeys[i] = key;
        }
        if (sort)
        {
            for (int i = 0; i < count; i++) _order[i] = ((ulong)_cellKeys[i] << 32) | (uint)i;
            Array.Sort(_order, 0, count); Sorts++;
        }
        for (int slot = 0; slot < count; slot++)
        {
            int source = (int)(uint)_order[slot], cluster = slot / 32;
            _sourceCluster[source] = cluster;
            if (slot % 32 == 0) { _clusters[cluster] = _bounds[source]; _valid[cluster] = _finite[source]; }
            else
            {
                _clusters[cluster] = new(Vector3.Min(_clusters[cluster].Minimum, _bounds[source].Minimum),
                    Vector3.Max(_clusters[cluster].Maximum, _bounds[source].Maximum));
                _valid[cluster] &= _finite[source];
            }
        }
        Rebuilds++;
    }

    internal void Query(in ViewFrustum frustum, int count)
    {
        RejectedClusters = RejectedRoots = 0;
        for (int i = 0; i < ClusterCount; i++)
        {
            _visible[i] = !_valid[i] || frustum.Intersects(_clusters[i]);
            if (!_visible[i]) { RejectedClusters++; RejectedRoots += Math.Min(32, count - i * 32); }
        }
    }
    internal bool IsCandidate(int source) => _visible[_sourceCluster[source]];
    internal void Clear()
    {
        _current = _previous = null; _order = []; _cellKeys = []; _bounds = []; _clusters = []; _count = 0;
        _finite = []; _valid = []; _visible = []; _sourceCluster = []; ClusterCount = 0;
    }
    private static uint Quantize(float value) =>
        !float.IsFinite(value) ? 0 : (uint)Math.Clamp(Math.Floor((double)value / 32) + 32768, 0, 65535);
    private static uint Spread(uint value)
    {
        value = (value | value << 8) & 0x00ff00ff;
        value = (value | value << 4) & 0x0f0f0f0f;
        value = (value | value << 2) & 0x33333333;
        return (value | value << 1) & 0x55555555;
    }
}
