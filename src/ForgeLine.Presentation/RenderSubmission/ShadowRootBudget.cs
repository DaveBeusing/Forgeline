using System.Buffers;

namespace ForgeLine.Presentation.RenderSubmission;

// Device budgets count submitted parts, not simulation vehicles. A root is atomic.
internal readonly struct ShadowRootBudget : IDisposable
{
    private readonly int[] _costs;
    private readonly bool[] _eligible, _selected;
    private readonly ulong[] _priorities;
    private readonly int _count;
    internal ShadowRootBudget(PresentationSnapshot snapshot)
    {
        _count = snapshot.InstanceCount;
        _costs = ArrayPool<int>.Shared.Rent(_count);
        _eligible = ArrayPool<bool>.Shared.Rent(_count); _selected = ArrayPool<bool>.Shared.Rent(_count);
        _priorities = ArrayPool<ulong>.Shared.Rent(_count);
        _costs.AsSpan(0, _count).Clear(); _selected.AsSpan(0, _count).Clear(); _eligible.AsSpan(0, _count).Fill(true);
        for (int i = 0; i < _count; i++)
        {
            RenderInstance root = snapshot.GetInstance(i);
            uint identity = root.Entity.IsValid ? root.Entity.Index * 16777619u ^ root.Entity.Generation : root.DebugIdentity;
            identity ^= identity >> 16; identity *= 0x7feb352d; identity ^= identity >> 15; identity *= 0x846ca68b; identity ^= identity >> 16;
            _priorities[i] = ((ulong)identity << 32) | (uint)i;
        }
    }
    internal void IncludePart(int root, bool drawAvailable) { _costs[root]++; _eligible[root] &= drawAvailable; }
    internal int Select(int parts)
    {
        Array.Sort(_priorities, 0, _count);
        int roots = 0;
        for (int slot = 0; slot < _count; slot++)
        {
            int source = (int)(uint)_priorities[slot];
            int cost = _costs[source];
            if (cost == 0 || !_eligible[source] || cost > parts) continue;
            _selected[source] = true; parts -= cost; roots++;
        }
        return roots;
    }
    internal bool Selected(int source) => _selected[source];
    public void Dispose()
    {
        ArrayPool<int>.Shared.Return(_costs); ArrayPool<bool>.Shared.Return(_eligible);
        ArrayPool<bool>.Shared.Return(_selected); ArrayPool<ulong>.Shared.Return(_priorities);
    }
}
