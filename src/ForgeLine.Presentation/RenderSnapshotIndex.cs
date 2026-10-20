using ForgeLine.Presentation.RenderContracts;
using ForgeLine.Core;

namespace ForgeLine.Presentation;

// Current/previous entity correspondence. Production visibility is separately owned by RenderWorld.
internal sealed class RenderSnapshotIndex : IRenderSnapshotCorrespondence
{
    internal const int ClusterSize = InstanceVisibilityIndex.ClusterSize;
    private int[] _previousIndices = [];
    private int[] _currentOrder = [], _previousOrder = [], _sortScratch = [];
    private readonly InstanceVisibilityIndex? _legacyVisibility;

    // Retains the internal direct-query API used by older callers; production opts out.
    internal RenderSnapshotIndex(bool enableLegacyVisibility = true)
    {
        if (enableLegacyVisibility) _legacyVisibility = new InstanceVisibilityIndex();
    }
    internal long RetainedBytes => ((long)_previousIndices.Length + _currentOrder.Length + _previousOrder.Length + _sortScratch.Length) * sizeof(int) + (_legacyVisibility?.RetainedBytes ?? 0);
    public int PreviousIndex(int index) => _previousIndices[index];
    internal void Rebuild(PresentationSnapshot current, PresentationSnapshot? previous)
    {
        int count = current.InstanceCount;
        SnapshotIdentityOrder.Ensure(ref _previousIndices, count);
        SnapshotIdentityOrder.Trim(ref _currentOrder, count);
        SnapshotIdentityOrder.Trim(ref _previousOrder, previous?.InstanceCount ?? 0);
        SnapshotIdentityOrder.Trim(ref _sortScratch, Math.Max(count, previous?.InstanceCount ?? 0));
        if (previous is null || previous.SessionId != current.SessionId)
            _previousIndices.AsSpan(0, count).Fill(-1);
        else
        {
            bool currentOrdered = SnapshotIdentityOrder.Prepare(current, ref _currentOrder, ref _sortScratch, out _);
            bool previousOrdered = SnapshotIdentityOrder.Prepare(previous, ref _previousOrder, ref _sortScratch, out bool duplicates);
            if (duplicates)
            {
                // Preserve the historical binary-search tie for ambiguous duplicate caller keys.
                for (int i = 0; i < count; i++) _previousIndices[i] = Find(previous, current.Instances[i].Entity);
            }
            else
            {
                int predecessor = 0;
                for (int ordered = 0; ordered < count; ordered++)
                {
                    int index = currentOrdered ? ordered : _currentOrder[ordered];
                    var entity = current.Instances[index].Entity;
                    while (predecessor < previous.InstanceCount && previous.Instances[
                        previousOrdered ? predecessor : _previousOrder[predecessor]].Entity.CompareTo(entity) < 0)
                        predecessor++;
                    int candidate = predecessor == previous.InstanceCount ? -1 :
                        previousOrdered ? predecessor : _previousOrder[predecessor];
                    _previousIndices[index] = candidate >= 0 && previous.Instances[candidate].Entity == entity ? candidate : -1;
                }
            }
        }
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
