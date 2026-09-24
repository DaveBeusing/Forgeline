using System.Numerics;
using ForgeLine.Core;
using ForgeLine.World;

namespace ForgeLine.Game;

public readonly record struct SpatialPresence
{
    public SpatialPresence(
        Vector3 halfExtents,
        SpatialEntryMetadata metadata)
    {
        if (!IsFiniteNonNegative(halfExtents))
        {
            throw new ArgumentOutOfRangeException(nameof(halfExtents));
        }

        HalfExtents = halfExtents;
        Metadata = metadata;
    }

    public Vector3 HalfExtents { get; }

    public SpatialEntryMetadata Metadata { get; }

    public SpatialEntry CreateEntry(
        EntityId entity,
        in WorldTransform transform)
    {
        var bounds = new AxisAlignedBounds(
            transform.Position - HalfExtents,
            transform.Position + HalfExtents);

        return new SpatialEntry(entity, bounds, Metadata);
    }

    private static bool IsFiniteNonNegative(Vector3 value)
    {
        return float.IsFinite(value.X) &&
               float.IsFinite(value.Y) &&
               float.IsFinite(value.Z) &&
               value.X >= 0.0f &&
               value.Y >= 0.0f &&
               value.Z >= 0.0f;
    }
}
