using ForgeLine.Assets;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

public readonly record struct ResourceDepositRenderDiagnostics(
    int VisibleRequests, int VariantInstances, int CanonicalFallbacks,
    int InvalidSelections, int MissingResources, int RetainedHighLodInstances, int InvalidTransforms = 0,
    int HighLodParts = 0, int ReducedLodParts = 0);

public readonly record struct ResourceDepositBatchDiagnostics(
    string Mesh, string Material, int Instances, int IndicesPerInstance, long InstanceUploadBytes);

public sealed partial class SimpleInstanceRenderer
{
    private static WorldFeaturePresentationMetadata FieldResourceFeature(in WorldFeaturePresentationMetadata root,
        in ResourceDepositFieldCluster cluster)
    {
        var result = root with { ResourceTint = new(cluster.Tint.X, cluster.Tint.Y, cluster.Tint.Z) };
        var canonical = WorldPresentationCatalog.Get(root.Visual);
        bool canonicalPair = string.Equals(cluster.MeshAssetId, canonical.MeshAssetId, StringComparison.Ordinal) &&
            string.Equals(cluster.ReducedMeshAssetId, canonical.ReducedLodAssetId, StringComparison.Ordinal);
        if (cluster.Variant.IsSpecified)
        {
            result = result with { ResourceVariant = cluster.Variant };
            if (ResourceDepositVariantCatalog.TryResolve(result, out var binding) && (canonicalPair ||
                string.Equals(cluster.MeshAssetId, binding.HighMeshAssetId, StringComparison.Ordinal) &&
                string.Equals(cluster.ReducedMeshAssetId, binding.ReducedMeshAssetId, StringComparison.Ordinal))) return result;
        }
        else if (ResourceDepositVariantCatalog.TrySelectAssetPair(root.Visual, cluster.MeshAssetId, cluster.ReducedMeshAssetId,
            0, out var selection)) return result with { ResourceVariant = selection };
        else if (canonicalPair) return result with { ResourceVariant = default };
        return result with { ResourceVariant = new("invalid-field-pair", ResourceDepositVariantCatalog.VariantCount) };
    }

    private readonly Dictionary<(WorldVisualId, ResourceDepositVariantSelection), ResourceBinding> _resourceBindings = [];
    public ResourceDepositRenderDiagnostics ResourceDiagnostics { get; private set; }

    private static bool ValidResourceTransform(in RenderTransform transform) =>
        Finite(transform.Position) && Finite(transform.Scale) &&
        float.IsFinite(transform.Position.LengthSquared()) && float.IsFinite(transform.Scale.LengthSquared()) &&
        transform.Scale.X > 0 && transform.Scale.Y > 0 && transform.Scale.Z > 0 &&
        float.IsFinite(transform.Rotation.LengthSquared()) && MathF.Abs(transform.Rotation.LengthSquared() - 1) <= .001f;

    // Explicit diagnostic snapshot, outside the frame hot path. Resources remain
    // owned by the existing residency store and fence-retired frame buffers.
    public IReadOnlyList<ResourceDepositBatchDiagnostics> GetResourceBatchDiagnostics()
    {
        ThrowIfDisposed();
        var result = new List<ResourceDepositBatchDiagnostics>();
        foreach (var pair in _batchLookup)
        {
            InstanceBatch batch = pair.Value;
            if (batch.Instances.Count == 0 || !pair.Key.UsesRuntimeMesh ||
                !pair.Key.RuntimeMeshId.Value.StartsWith("mesh.world.resource.", StringComparison.Ordinal)) continue;
            result.Add(new(pair.Key.RuntimeMeshId.Value, pair.Key.RuntimeMaterialId.Value,
                batch.Instances.Count, batch.RuntimeMesh.IndexCount, (long)batch.Instances.Count * InstanceStride));
        }
        return result;
    }

    private bool TryResolveResource(in WorldFeaturePresentationMetadata feature, WorldAssetLod requested,
        out RuntimeMeshBuffers mesh, out AssetId meshId, out string? materialId, out WorldAssetLod actual)
    {
        ResourceDiagnostics = ResourceDiagnostics with { VisibleRequests = ResourceDiagnostics.VisibleRequests + 1 };
        bool validSelection = ResourceDepositVariantCatalog.TryResolve(feature, out var variant);
        bool invalid = feature.ResourceVariant.IsSpecified && !validSelection;
        if (invalid) ResourceDiagnostics = ResourceDiagnostics with { InvalidSelections = ResourceDiagnostics.InvalidSelections + 1 };
        // Invalid externally supplied keys are never retained as dictionary keys.
        var key = (feature.Visual, validSelection ? feature.ResourceVariant : default);
        if (!_resourceBindings.TryGetValue(key, out ResourceBinding binding))
        {
            binding = ResolveResourceBinding(feature.Visual, validSelection ? variant : null);
            _resourceBindings.Add(key, binding);
        }
        mesh = default; meshId = default; materialId = null; actual = requested;
        if (!binding.Available)
        {
            ResourceDiagnostics = ResourceDiagnostics with { MissingResources = ResourceDiagnostics.MissingResources + 1 };
            return false; // Controlled missing visual; never submit the generic debug cube.
        }
        if (binding.Variant)
            ResourceDiagnostics = ResourceDiagnostics with { VariantInstances = ResourceDiagnostics.VariantInstances + 1 };
        else if (feature.ResourceVariant.IsSpecified)
            ResourceDiagnostics = ResourceDiagnostics with { CanonicalFallbacks = ResourceDiagnostics.CanonicalFallbacks + 1 };
        bool reduced = requested == WorldAssetLod.Reduced && binding.HasReduced;
        actual = reduced ? WorldAssetLod.Reduced : WorldAssetLod.High;
        ResourceDiagnostics = reduced
            ? ResourceDiagnostics with { ReducedLodParts = ResourceDiagnostics.ReducedLodParts + 1 }
            : ResourceDiagnostics with { HighLodParts = ResourceDiagnostics.HighLodParts + 1 };
        if (requested == WorldAssetLod.Reduced && !reduced)
            ResourceDiagnostics = ResourceDiagnostics with { RetainedHighLodInstances = ResourceDiagnostics.RetainedHighLodInstances + 1 };
        mesh = reduced ? binding.Reduced : binding.High;
        meshId = reduced ? binding.ReducedId : binding.HighId;
        materialId = binding.Material;
        return true;
    }

    private ResourceBinding ResolveResourceBinding(WorldVisualId visual, ResourceDepositVariantBinding? variant)
    {
        if (_runtimeAssets is null || _texturedPipeline is null) return default;
        if (variant is { } selected &&
            ResourceMesh(selected.HighMeshAssetId, selected.CanonicalMaterialAssetId, out var high, out var highId) &&
            ResourceMesh(selected.ReducedMeshAssetId, selected.CanonicalMaterialAssetId, out var reduced, out var reducedId) &&
            !ResolveMaterial(selected.MaterialAssetId).UsesDevelopmentFallback)
            return new(true, true, true, high, reduced, highId, reducedId, selected.MaterialAssetId);

        var canonical = WorldPresentationCatalog.Get(visual);
        if (!ResourceMesh(canonical.MeshAssetId, canonical.MaterialAssetId, out high, out highId) ||
            ResolveMaterial(canonical.MaterialAssetId).UsesDevelopmentFallback) return default;
        bool hasReduced = ResourceMesh(canonical.ReducedLodAssetId, canonical.MaterialAssetId, out reduced, out reducedId);
        return new(true, false, hasReduced, high, reduced, highId, reducedId, canonical.MaterialAssetId);
    }

    private bool ResourceMesh(string id, string familyMaterial, out RuntimeMeshBuffers mesh, out AssetId resolved) =>
        TryGetMesh(id, out mesh, out resolved) && mesh.IsValid && mesh.SectionCount == 1 && !mesh.UsesDevelopmentFallback &&
        (mesh.Attributes & RuntimeMeshAttributes.Normal) != 0 &&
        mesh.SupportsTexturedMaterial && mesh.HasTangents && mesh.HasMaterial &&
        string.Equals(mesh.MaterialId.Value, familyMaterial, StringComparison.Ordinal);

    private readonly record struct ResourceBinding(bool Available, bool Variant, bool HasReduced,
        RuntimeMeshBuffers High, RuntimeMeshBuffers Reduced, AssetId HighId, AssetId ReducedId, string Material);
}
