using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

// Immutable copied presentation data. Children never acquire ECS or picking identities.
public sealed class ResourceDepositField
{
    internal ResourceDepositField(ResourceDepositFieldCluster[] clusters, AxisAlignedBounds bounds)
    {
        Clusters = Array.AsReadOnly(clusters);
        Bounds = bounds;
    }

    public IReadOnlyList<ResourceDepositFieldCluster> Clusters { get; }
    public AxisAlignedBounds Bounds { get; }
    public int Version => 1;
}

public readonly record struct ResourceDepositFieldCluster(
    int Ordinal, RenderTransform Transform, string MeshAssetId, string ReducedMeshAssetId,
    Vector4 Tint, bool Crust)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ResourceDepositVariantSelection Variant { get; init; }
}

internal readonly record struct ResourceDepositFieldIdentity(string MapKey, string DepositKey, ulong Salt = 2026);
