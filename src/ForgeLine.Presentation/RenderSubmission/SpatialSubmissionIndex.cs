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
    private int _count, _candidateCount;
    private ulong[] _candidateWords = [];
    private AxisAlignedBounds[] _bounds = [], _clusters = [];
    private bool[] _finite = [], _valid = [], _visible = [];
    internal int ClusterCount { get; private set; }
    internal int RejectedClusters { get; private set; }
    internal int RejectedRoots { get; private set; }
    internal int Rebuilds { get; private set; }
    internal int Sorts { get; private set; }
    internal long RetainedBytes => (long)_order.Length * (8 + 24 + 1 + 4) + (long)_clusters.Length * 26 + (long)_candidateWords.Length * 8;

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
            _finite = new bool[capacity]; _candidateWords = new ulong[(capacity + 63) / 64];
        }
        ClusterCount = (count + 31) / 32;
        if (_clusters.Length < ClusterCount || _clusters.Length > 2048 && count <= 16384)
        { _clusters = new AxisAlignedBounds[ClusterCount]; _valid = new bool[ClusterCount]; _visible = new bool[ClusterCount]; }
        bool aligned = HasAlignedUniqueEntities(_current, _previous);
        for (int i = 0; i < count; i++)
        {
            RenderInstance current = _current!.GetInstance(i);
            RenderInstance endpoint = _previous is null || current.VfxFeature.IsSpecified ||
                current.DecalFeature.IsSpecified && current.DecalFeature.Surface == DecalSurface.Ground ? current :
                aligned ? _previous.GetInstance(i) : world.GetInterpolatedInstance(i, 0);
            _finite[i] = InstanceVisibilityIndex.TrySweptBounds(current, endpoint, out _bounds[i]);
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
        _candidateCount = count - RejectedRoots;
        int words = (count + 63) / 64;
        if (RejectedClusters == 0)
        {
            _candidateWords.AsSpan(0, words).Fill(ulong.MaxValue);
            if (words > 0 && count % 64 != 0) _candidateWords[words - 1] &= (1UL << (count % 64)) - 1;
            return;
        }
        _candidateWords.AsSpan(0, words).Clear();
        if (_candidateCount == 0) return;
        for (int cluster = 0; cluster < ClusterCount; cluster++)
        {
            if (!_visible[cluster]) continue;
            int end = Math.Min(count, (cluster + 1) * 32);
            for (int slot = cluster * 32; slot < end; slot++)
            {
                int source = (int)(uint)_order[slot];
                _candidateWords[source / 64] |= 1UL << (source % 64);
            }
        }
    }
    internal bool IsCandidate(int source) => (_candidateWords[source / 64] & (1UL << (source % 64))) != 0;
    internal CandidateEnumerator Candidates() => new(_candidateWords, _candidateCount == 0 ? 0 : (_count + 63) / 64);

    internal struct CandidateEnumerator(ulong[] words, int length)
    {
        private int _word = -1;
        private ulong _remaining;
        internal int Current { get; private set; }
        internal bool MoveNext()
        {
            while (_remaining == 0)
            {
                if (++_word >= length) return false;
                _remaining = words[_word];
            }
            Current = _word * 64 + BitOperations.TrailingZeroCount(_remaining);
            _remaining &= _remaining - 1;
            return true;
        }
    }

    private static bool HasAlignedUniqueEntities(PresentationSnapshot? current, PresentationSnapshot? previous)
    {
        if (current is null || previous is null || current.SessionId != previous.SessionId || current.InstanceCount != previous.InstanceCount) return false;
        ForgeLine.Core.EntityId priorEntity = default;
        for (int i = 0; i < current.InstanceCount; i++)
        {
            var entity = current.GetInstance(i).Entity;
            if (!entity.IsValid || entity != previous.GetInstance(i).Entity || i > 0 && priorEntity.CompareTo(entity) >= 0) return false;
            priorEntity = entity;
        }
        return true;
    }

    internal void Clear()
    {
        _current = _previous = null; _order = []; _cellKeys = []; _bounds = []; _clusters = []; _count = _candidateCount = 0;
        _finite = []; _valid = []; _visible = []; _candidateWords = []; ClusterCount = 0;
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
