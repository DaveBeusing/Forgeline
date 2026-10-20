using ForgeLine.Presentation.RenderContracts;
using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

internal sealed class InstanceVisibilityIndex : IInstanceVisibilityIndex
{
    internal const int ClusterSize = 32;
    private const int MaximumRetainedInstances = 65_536;
    private AxisAlignedBounds[] _clusters = [];
    private bool[] _validClusters = [];
    internal int ClusterCount { get; private set; }
    public long RetainedBytes => (long)_clusters.Length * 25;
    public void Rebuild(PresentationSnapshot current, PresentationSnapshot? previous, IRenderSnapshotCorrespondence correspondence)
    {
        int count = current.InstanceCount;
        ClusterCount = count < ClusterSize ? 0 : (count + ClusterSize - 1) / ClusterSize;
        if (_clusters.Length < ClusterCount || _clusters.Length > MaximumRetainedInstances / ClusterSize && count <= MaximumRetainedInstances)
        {
            _clusters = new AxisAlignedBounds[ClusterCount];
            _validClusters = new bool[ClusterCount];
        }
        for (int index = 0; index < count && ClusterCount > 0; index++)
        {
            RenderInstance instance = current.GetInstance(index);
            int match = correspondence.PreviousIndex(index);
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
    public bool IsVisible(int firstIndex, in ViewFrustum frustum) =>
        ClusterCount == 0 || !_validClusters[firstIndex / ClusterSize] || frustum.Intersects(_clusters[firstIndex / ClusterSize]);

    internal static bool TrySweptBounds(in RenderInstance current, RenderInstance? previous, out AxisAlignedBounds bounds)
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

}
