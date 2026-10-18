using ForgeLine.Presentation.RenderContracts;
using ForgeLine.Core;

namespace ForgeLine.Presentation;

// Current/previous entity correspondence. Production visibility is separately owned by RenderWorld.
internal sealed class RenderSnapshotIndex : IRenderSnapshotCorrespondence
{
    internal const int ClusterSize = InstanceVisibilityIndex.ClusterSize;
    private const int MaximumRetainedInstances = 65_536;
    private int[] _previousIndices = [];
    private readonly InstanceVisibilityIndex? _legacyVisibility;

    // Retains the internal direct-query API used by older callers; production opts out.
    internal RenderSnapshotIndex(bool enableLegacyVisibility = true)
    {
        if (enableLegacyVisibility) _legacyVisibility = new InstanceVisibilityIndex();
    }
    internal long RetainedBytes => (long)_previousIndices.Length * sizeof(int) + (_legacyVisibility?.RetainedBytes ?? 0);
    public int PreviousIndex(int index) => _previousIndices[index];
    internal void Rebuild(PresentationSnapshot current, PresentationSnapshot? previous)
    {
        int count = current.InstanceCount;
        if (_previousIndices.Length < count || _previousIndices.Length > MaximumRetainedInstances && count <= MaximumRetainedInstances)
            _previousIndices = new int[count];
        for (int index = 0; index < count; index++)
            _previousIndices[index] = previous is null ? -1 : Find(previous, current.GetInstance(index).Entity);
        _legacyVisibility?.Rebuild(current, previous, this);
    }
    internal bool IsVisible(int firstIndex, in ViewFrustum frustum) =>
        (_legacyVisibility ?? throw new InvalidOperationException("Visibility belongs to the separate index.")).IsVisible(firstIndex, frustum);
    private static int Find(PresentationSnapshot snapshot, ForgeLine.Core.EntityId entity)
    {
        int low = 0, high = snapshot.InstanceCount - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            int comparison = snapshot.GetInstance(middle).Entity.CompareTo(entity);
            if (comparison == 0) return middle;
            if (comparison < 0) low = middle + 1; else high = middle - 1;
        }
        return -1;
    }
}
