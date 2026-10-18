using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

// Render-owner data derived only from the current authorized snapshot.
internal sealed class RenderSnapshotIndex
{
    internal const int ClusterSize = 32;
    private const int MaximumRetainedInstances = 65_536;
    private int[] _previousIndices = [];
    private AxisAlignedBounds[] _clusters = [];
    private bool[] _validClusters = [];
    internal int ClusterCount { get; private set; }
    internal long RetainedBytes => (long)_previousIndices.Length * sizeof(int) + (long)_clusters.Length * 25;
    internal int PreviousIndex(int index) => _previousIndices[index];

    internal void Rebuild(PresentationSnapshot current, PresentationSnapshot? previous)
    {
        int count = current.InstanceCount;
        if (_previousIndices.Length < count || _previousIndices.Length > MaximumRetainedInstances && count <= MaximumRetainedInstances)
            _previousIndices = new int[count];
        ClusterCount = count < ClusterSize ? 0 : (count + ClusterSize - 1) / ClusterSize;
        if (_clusters.Length < ClusterCount || _clusters.Length > MaximumRetainedInstances / ClusterSize && count <= MaximumRetainedInstances)
        {
            _clusters = new AxisAlignedBounds[ClusterCount];
            _validClusters = new bool[ClusterCount];
        }
        for (int index = 0; index < count; index++)
        {
            RenderInstance instance = current.GetInstance(index);
            int match = previous is null ? -1 : Find(previous, instance.Entity);
            _previousIndices[index] = match;
            if (ClusterCount == 0) continue;
            int cluster = index / ClusterSize;
            bool finite = TrySweptBounds(instance, match < 0 ? null : previous!.GetInstance(match), out AxisAlignedBounds bounds);
            if (index % ClusterSize == 0)
            {
                _clusters[cluster] = bounds;
                _validClusters[cluster] = finite;
            }
            else
            {
                _clusters[cluster] = new(Vector3.Min(_clusters[cluster].Minimum, bounds.Minimum), Vector3.Max(_clusters[cluster].Maximum, bounds.Maximum));
                _validClusters[cluster] &= finite;
            }
        }
    }

    internal bool IsVisible(int firstIndex, in ViewFrustum frustum) =>
        ClusterCount == 0 || !_validClusters[firstIndex / ClusterSize] || frustum.Intersects(_clusters[firstIndex / ClusterSize]);

    private static bool TrySweptBounds(in RenderInstance current, RenderInstance? previous, out AxisAlignedBounds bounds)
    {
        Vector3 start = previous?.Transform.Position ?? current.Transform.Position;
        float radius = SkinPresentationBounds.Radius(current);
        if (previous is { } prior) radius = MathF.Max(radius, SkinPresentationBounds.Radius(current with { Transform = prior.Transform }));
        // A sphere bounds every intermediate quaternion rotation. Vegetation pivots
        // at its root, so include the full authored height and wind sweep.
        if (VegetationVisualMotion.Applies(current))
            radius = MathF.Max(radius, MathF.Max(Vector3.Abs(current.Transform.Scale).Length(), Vector3.Abs(previous?.Transform.Scale ?? current.Transform.Scale).Length()) * (1 + VegetationVisualMotion.MaximumAngle));
        if (previous is { } old && VegetationVisualMotion.Applies(old))
            radius = MathF.Max(radius, Vector3.Abs(old.Transform.Scale).Length() * (1 + VegetationVisualMotion.MaximumAngle));
        Vector3 minimum = Vector3.Min(start, current.Transform.Position) - new Vector3(radius);
        Vector3 maximum = Vector3.Max(start, current.Transform.Position) + new Vector3(radius);
        bool finite = Finite(minimum) && Finite(maximum);
        bounds = finite ? new(minimum, maximum) : default;
        return finite;
    }

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

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
