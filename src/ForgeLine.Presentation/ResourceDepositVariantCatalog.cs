using ForgeLine.Game;
using System.Numerics;

namespace ForgeLine.Presentation;

// Copied presentation input. Seed/anchor transforms belong to the field producer;
// neither camera, tick, state nor an ECS index selects a different form here.
public readonly record struct ResourceDepositVariantSelection(
    string? VariantKey, int VariantCount, int SurfaceIndex = 0, uint SchemaVersion = 1)
{
    public bool IsSpecified => VariantKey is not null;
}

public readonly record struct ResourceDepositVariantBinding(
    string VariantKey, string HighMeshAssetId, string ReducedMeshAssetId,
    string MaterialAssetId, string CanonicalMaterialAssetId);

public static class ResourceDepositVariantCatalog
{
    public const int VariantCount = 12;
    public const int SurfaceCount = 4;
    private static readonly string[] Surfaces = ["weathered", "fresh_fracture", "patinated", "inclusion_rich"];
    private static readonly Dictionary<WorldVisualId, ResourceDepositVariantBinding[][]> Families = Create();

    public static bool IsResource(WorldVisualId visual) => Families.ContainsKey(visual);

    public static Vector4 InstanceTint(in WorldFeaturePresentationMetadata feature)
    {
        Vector3 value = feature.ResourceTint;
        if (feature.Kind != WorldPresentationKind.ResourceDeposit || value == default) return Vector4.One;
        (Vector3 min, Vector3 max) = TintRange(feature.Visual);
        return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) &&
            value.X >= min.X && value.Y >= min.Y && value.Z >= min.Z &&
            value.X <= max.X && value.Y <= max.Y && value.Z <= max.Z ? new(value, 1) : Vector4.One;
    }

    internal static (Vector3 Min, Vector3 Max) TintRange(WorldVisualId visual) => visual switch
        {
            WorldVisualId.ResourceFerrousOre => (new(.94f, .92f, .90f), new(1.04f, 1.02f, 1)),
            WorldVisualId.ResourceSilicates => (new(.94f, .95f, .94f), new(1.03f, 1.04f, 1.02f)),
            WorldVisualId.ResourceVolatiles => (new(.93f, .96f, .96f), new(1.01f, 1.03f, 1.04f)),
            WorldVisualId.ResourceRareElements => (new(.95f, .93f, .96f), new(1.03f, 1.01f, 1.04f)),
            _ => (Vector3.One, Vector3.One)
        };

    // All string construction happens once, never per instance/frame. No arbitrary
    // runtime asset paths or authoring files are accepted from the producer.
    public static bool TryResolve(in WorldFeaturePresentationMetadata feature, out ResourceDepositVariantBinding binding)
    {
        binding = default;
        ResourceDepositVariantSelection selection = feature.ResourceVariant;
        if (feature.Kind != WorldPresentationKind.ResourceDeposit || selection.SchemaVersion != 1 ||
            selection.VariantCount != VariantCount || (uint)selection.SurfaceIndex >= SurfaceCount ||
            !Families.TryGetValue(feature.Visual, out var variants)) return false;
        foreach (var surfaces in variants)
        {
            if (!string.Equals(surfaces[0].VariantKey, selection.VariantKey, StringComparison.Ordinal)) continue;
            binding = surfaces[selection.SurfaceIndex];
            return true;
        }
        return false;
    }

    public static ResourceDepositVariantSelection SelectAuthored(WorldVisualId visual, int variantIndex, int surfaceIndex = 0)
    {
        if (!Families.TryGetValue(visual, out var variants) || (uint)variantIndex >= VariantCount || (uint)surfaceIndex >= SurfaceCount)
            throw new ArgumentOutOfRangeException(nameof(variantIndex));
        return new(variants[variantIndex][0].VariantKey, VariantCount, surfaceIndex);
    }

    // Adapter for field producers that already copy the approved pair of IDs.
    // Both IDs must identify the same family/form/size. Never infer by path slicing.
    public static bool TrySelectAssetPair(WorldVisualId visual, string high, string reduced,
        int surfaceIndex, out ResourceDepositVariantSelection selection)
    {
        selection = default;
        if ((uint)surfaceIndex >= SurfaceCount || !Families.TryGetValue(visual, out var variants)) return false;
        foreach (var surfaces in variants)
        {
            var candidate = surfaces[surfaceIndex];
            if (!string.Equals(high, candidate.HighMeshAssetId, StringComparison.Ordinal) ||
                !string.Equals(reduced, candidate.ReducedMeshAssetId, StringComparison.Ordinal)) continue;
            selection = new(candidate.VariantKey, VariantCount, surfaceIndex);
            return true;
        }
        return false;
    }

    private static Dictionary<WorldVisualId, ResourceDepositVariantBinding[][]> Create()
    {
        var result = new Dictionary<WorldVisualId, ResourceDepositVariantBinding[][]>();
        Add(WorldVisualId.ResourceFerrousOre, "ferrous_ore", ["layered_shelf", "faulted_ridge", "breccia_fan", "offset_seam"]);
        Add(WorldVisualId.ResourceSilicates, "silicates", ["cleaved_plate", "crystal_fan", "stepped_bed", "split_rosette"]);
        Add(WorldVisualId.ResourceVolatiles, "volatiles", ["sealed_nodule", "fractured_lens", "laminated_pocket", "weathered_saddle"]);
        Add(WorldVisualId.ResourceRareElements, "rare_elements", ["inclusion_reef", "split_matrix", "radial_inclusions", "faulted_pocket"]);
        return result;

        void Add(WorldVisualId visual, string family, string[] layouts)
        {
            string[] sizes = ["small", "medium", "large"];
            var variants = new ResourceDepositVariantBinding[VariantCount][];
            for (int layout = 0; layout < layouts.Length; layout++)
            for (int size = 0; size < sizes.Length; size++)
            {
                string key = $"resource-visual/1/{family}/{layouts[layout]}/{sizes[size]}";
                string mesh = $"mesh.world.resource.{family}.{layouts[layout]}.{sizes[size]}";
                string material = $"material.world.resource.{family}";
                var surfaces = new ResourceDepositVariantBinding[SurfaceCount];
                for (int surface = 0; surface < SurfaceCount; surface++)
                    surfaces[surface] = new(key, mesh, mesh + "_lod1", surface == 0 ? material : material + "." + Surfaces[surface], material);
                variants[layout * sizes.Length + size] = surfaces;
            }
            result.Add(visual, variants);
        }
    }
}
