using System.Numerics;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

public enum ResourceDepositPresentationState : byte
{
    None = 0,
    Untouched = 1,
    Active = 2,
    Depleted = 3
}

public enum WorldAssetLod : byte
{
    High = 0,
    Reduced = 1
}

public readonly record struct WorldPresentationDefinition(
    WorldVisualId Visual,
    string MeshAssetId,
    string ReducedLodAssetId,
    string MaterialAssetId,
    string? StrategicSymbolAssetId,
    Vector4 Tint,
    float ReducedLodDistance)
{
    public bool HasStrategicSymbol =>
        !string.IsNullOrWhiteSpace(StrategicSymbolAssetId);
}

public readonly record struct WorldFeaturePresentationMetadata(
    WorldVisualId Visual,
    WorldPresentationKind Kind,
    ResourceDepositPresentationState ResourceState,
    bool Inspectable)
{
    public static WorldFeaturePresentationMetadata None => default;

    public bool IsSpecified =>
        Visual != WorldVisualId.None;

    public bool IsInspectable =>
        IsSpecified && Inspectable;
}

public static class WorldPresentationCatalog
{
    private static readonly IReadOnlyDictionary<WorldVisualId, WorldPresentationDefinition> Definitions =
        CreateDefinitions();

    public static WorldPresentationDefinition Get(
        WorldVisualId visual) =>
        Definitions.TryGetValue(
            visual,
            out WorldPresentationDefinition definition)
            ? definition
            : throw new KeyNotFoundException(
                $"Unknown world visual '{visual}'.");

    public static bool TryGet(
        WorldVisualId visual,
        out WorldPresentationDefinition definition) =>
        Definitions.TryGetValue(
            visual,
            out definition);

    public static WorldAssetLod SelectLod(
        in WorldFeaturePresentationMetadata feature,
        float distanceMeters)
    {
        if (!feature.IsSpecified ||
            !float.IsFinite(distanceMeters) ||
            distanceMeters < 0.0f)
        {
            return WorldAssetLod.High;
        }

        WorldPresentationDefinition definition =
            Get(feature.Visual);

        return distanceMeters >= definition.ReducedLodDistance
            ? WorldAssetLod.Reduced
            : WorldAssetLod.High;
    }

    public static Vector4 ResolveTint(
        in WorldFeaturePresentationMetadata feature)
    {
        WorldPresentationDefinition definition =
            Get(feature.Visual);

        if (feature.Kind != WorldPresentationKind.ResourceDeposit)
        {
            return definition.Tint;
        }

        return feature.ResourceState switch
        {
            ResourceDepositPresentationState.Untouched =>
                definition.Tint,
            ResourceDepositPresentationState.Active =>
                Vector4.Min(
                    Vector4.One,
                    definition.Tint *
                    new Vector4(1.18f, 1.18f, 1.18f, 1.0f)),
            ResourceDepositPresentationState.Depleted =>
                new Vector4(0.30f, 0.31f, 0.32f, 1.0f),
            _ =>
                definition.Tint
        };
    }

    private static IReadOnlyDictionary<WorldVisualId, WorldPresentationDefinition> CreateDefinitions()
    {
        var result =
            new Dictionary<WorldVisualId, WorldPresentationDefinition>();

        Add(WorldVisualId.ResourceFerrousOre, "mesh.world.resource.ferrous_ore", "mesh.world.resource.ferrous_ore_lod1", "material.world.resource.ferrous_ore", "material.world.symbol.resource.ferrous_ore", new Vector4(0.46f, 0.28f, 0.19f, 1.0f), 260.0f);
        Add(WorldVisualId.ResourceSilicates, "mesh.world.resource.silicates", "mesh.world.resource.silicates_lod1", "material.world.resource.silicates", "material.world.symbol.resource.silicates", new Vector4(0.70f, 0.66f, 0.54f, 1.0f), 260.0f);
        Add(WorldVisualId.ResourceVolatiles, "mesh.world.resource.volatiles", "mesh.world.resource.volatiles_lod1", "material.world.resource.volatiles", "material.world.symbol.resource.volatiles", new Vector4(0.33f, 0.58f, 0.64f, 1.0f), 260.0f);
        Add(WorldVisualId.ResourceRareElements, "mesh.world.resource.rare_elements", "mesh.world.resource.rare_elements_lod1", "material.world.resource.rare_elements", "material.world.symbol.resource.rare_elements", new Vector4(0.60f, 0.42f, 0.76f, 1.0f), 260.0f);

        AddProp(WorldVisualId.PropRock, "rock", new Vector4(0.40f, 0.41f, 0.39f, 1.0f), 220.0f);
        AddProp(WorldVisualId.PropBarrier, "barrier", new Vector4(0.55f, 0.56f, 0.52f, 1.0f), 260.0f);
        AddProp(WorldVisualId.PropConcreteBlock, "concrete_block", new Vector4(0.50f, 0.51f, 0.49f, 1.0f), 260.0f);
        AddProp(WorldVisualId.PropCrate, "crate", new Vector4(0.40f, 0.31f, 0.20f, 1.0f), 200.0f);
        AddProp(WorldVisualId.PropDrum, "drum", new Vector4(0.31f, 0.36f, 0.35f, 1.0f), 200.0f);
        AddProp(WorldVisualId.PropPallet, "pallet", new Vector4(0.42f, 0.32f, 0.21f, 1.0f), 180.0f);
        AddProp(WorldVisualId.PropPipeSection, "pipe_section", new Vector4(0.34f, 0.36f, 0.35f, 1.0f), 240.0f);
        AddProp(WorldVisualId.PropUtilityBox, "utility_box", new Vector4(0.28f, 0.34f, 0.31f, 1.0f), 220.0f);
        AddProp(WorldVisualId.PropFence, "fence", new Vector4(0.32f, 0.34f, 0.33f, 1.0f), 300.0f);
        AddProp(WorldVisualId.PropIndustrialLightSignage, "industrial_light_signage", new Vector4(0.44f, 0.48f, 0.42f, 1.0f), 320.0f);
        AddProp(WorldVisualId.PropRubble, "rubble", new Vector4(0.38f, 0.36f, 0.33f, 1.0f), 220.0f);

        AddVegetation(WorldVisualId.VegetationConifer, "conifer", new Vector4(0.18f, 0.31f, 0.19f, 1.0f), 190.0f);
        AddVegetation(WorldVisualId.VegetationScrub, "scrub", new Vector4(0.30f, 0.40f, 0.22f, 1.0f), 160.0f);
        AddVegetation(WorldVisualId.VegetationGrassClump, "grass_clump", new Vector4(0.32f, 0.43f, 0.22f, 1.0f), 110.0f);

        AddDecal(WorldVisualId.DecalTireTracks, "tire_tracks", new Vector4(0.18f, 0.17f, 0.15f, 1.0f));
        AddDecal(WorldVisualId.DecalTrackedVehicleMarks, "tracked_vehicle_marks", new Vector4(0.16f, 0.15f, 0.13f, 1.0f));
        AddDecal(WorldVisualId.DecalRoadWear, "road_wear", new Vector4(0.30f, 0.29f, 0.27f, 1.0f));
        AddDecal(WorldVisualId.DecalOilStain, "oil_stain", new Vector4(0.10f, 0.11f, 0.10f, 1.0f));
        AddDecal(WorldVisualId.DecalBlastMark, "blast_mark", new Vector4(0.17f, 0.14f, 0.11f, 1.0f));
        AddDecal(WorldVisualId.DecalShellImpact, "shell_impact", new Vector4(0.22f, 0.18f, 0.14f, 1.0f));
        AddDecal(WorldVisualId.DecalScorchMark, "scorch_mark", new Vector4(0.12f, 0.11f, 0.10f, 1.0f));
        AddDecal(WorldVisualId.DecalConcreteCrack, "concrete_crack", new Vector4(0.20f, 0.20f, 0.19f, 1.0f));

        return result;

        void Add(
            WorldVisualId visual,
            string mesh,
            string lod,
            string material,
            string? symbol,
            Vector4 tint,
            float lodDistance) =>
            result.Add(
                visual,
                new WorldPresentationDefinition(
                    visual,
                    mesh,
                    lod,
                    material,
                    symbol,
                    tint,
                    lodDistance));

        void AddProp(
            WorldVisualId visual,
            string name,
            Vector4 tint,
            float lodDistance) =>
            Add(
                visual,
                $"mesh.world.prop.{name}",
                $"mesh.world.prop.{name}_lod1",
                "material.world.prop.industrial",
                null,
                tint,
                lodDistance);

        void AddVegetation(
            WorldVisualId visual,
            string name,
            Vector4 tint,
            float lodDistance) =>
            Add(
                visual,
                $"mesh.world.vegetation.{name}",
                $"mesh.world.vegetation.{name}_lod1",
                "material.world.vegetation",
                null,
                tint,
                lodDistance);

        void AddDecal(
            WorldVisualId visual,
            string name,
            Vector4 tint) =>
            Add(
                visual,
                "mesh.world.decal.quad",
                "mesh.world.decal.quad",
                $"material.world.decal.{name}",
                null,
                tint,
                1_000_000.0f);
    }
}
