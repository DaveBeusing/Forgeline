using System.Numerics;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

public enum BuildingPresentationState : byte
{
    None = 0,
    ConstructionFoundation = 1,
    ConstructionFrame = 2,
    ConstructionShell = 3,
    Operational = 4,
    Idle = 5,
    Unpowered = 6,
    Damaged = 7,
    Critical = 8,
    Destroyed = 9
}

public enum BuildingAssetLod : byte
{
    Lod0 = 0,
    Lod1 = 1,
    Lod2 = 2
}

public readonly record struct BuildingPresentationDefinition(
    BuildingId Building,
    string MeshAssetId,
    string Lod1AssetId,
    string Lod2AssetId,
    string MaterialAssetId,
    string CollisionAssetId,
    string StrategicSymbolAssetId,
    Vector4 FallbackTint,
    float Lod1DistanceMeters,
    float Lod2DistanceMeters)
{
    public string GetMeshAssetId(BuildingAssetLod lod) =>
        lod switch
        {
            BuildingAssetLod.Lod0 => MeshAssetId,
            BuildingAssetLod.Lod1 => Lod1AssetId,
            BuildingAssetLod.Lod2 => Lod2AssetId,
            _ => throw new ArgumentOutOfRangeException(nameof(lod))
        };
}

public readonly record struct BuildingFeaturePresentationMetadata(
    BuildingId Building,
    BuildingPresentationState State,
    float ConstructionProgress = 1.0f)
{
    public static BuildingFeaturePresentationMetadata None => default;

    public bool IsSpecified =>
        Building.IsSpecified &&
        State != BuildingPresentationState.None;

    public bool IsConstruction =>
        State is
            BuildingPresentationState.ConstructionFoundation or
            BuildingPresentationState.ConstructionFrame or
            BuildingPresentationState.ConstructionShell;

    public bool IsDestroyed =>
        State == BuildingPresentationState.Destroyed;
}

public static class BuildingPresentationCatalog
{
    public const string FoundationAssetId =
        "building.directorate.module.foundation";
    public const string StructuralFrameAssetId =
        "building.directorate.module.structural_frame";
    public const string PartialShellAssetId =
        "building.directorate.module.partial_shell";
    public const string IdleStateAssetId =
        "building.directorate.module.state_idle";
    public const string UnpoweredStateAssetId =
        "building.directorate.module.state_unpowered";
    public const string DamagedStateAssetId =
        "building.directorate.module.state_damaged";
    public const string CriticalStateAssetId =
        "building.directorate.module.state_critical";
    public const string DestroyedAssetId =
        "building.directorate.module.destroyed";
    public const string CollisionAssetId =
        "building.directorate.module.collision_box";

    public const string StructuralMaterialAssetId =
        "material.directorate.building.structural";
    public const string UnpoweredMaterialAssetId =
        "material.directorate.building.unpowered";
    public const string DamagedMaterialAssetId =
        "material.directorate.building.damaged";
    public const string CriticalMaterialAssetId =
        "material.directorate.building.critical";
    public const string DestroyedMaterialAssetId =
        "material.directorate.building.destroyed";

    private static readonly Dictionary<
        BuildingId,
        BuildingPresentationDefinition> Definitions =
        CreateDefinitions();

    public static BuildingPresentationDefinition Get(
        BuildingId building) =>
        Definitions.TryGetValue(
            building,
            out BuildingPresentationDefinition definition)
            ? definition
            : throw new KeyNotFoundException(
                $"Building '{building}' has no Directorate presentation definition.");

    public static bool TryGet(
        BuildingId building,
        out BuildingPresentationDefinition definition) =>
        Definitions.TryGetValue(
            building,
            out definition);

    public static BuildingAssetLod SelectLod(
        in BuildingFeaturePresentationMetadata feature,
        float distanceMeters)
    {
        if (!feature.IsSpecified ||
            feature.IsConstruction ||
            feature.IsDestroyed ||
            !float.IsFinite(distanceMeters) ||
            distanceMeters < 0.0f)
        {
            return BuildingAssetLod.Lod0;
        }

        BuildingPresentationDefinition definition =
            Get(feature.Building);

        if (distanceMeters >=
            definition.Lod2DistanceMeters)
        {
            return BuildingAssetLod.Lod2;
        }

        return distanceMeters >=
            definition.Lod1DistanceMeters
            ? BuildingAssetLod.Lod1
            : BuildingAssetLod.Lod0;
    }

    public static string ResolveMeshAssetId(
        in BuildingFeaturePresentationMetadata feature,
        BuildingAssetLod lod)
    {
        if (!feature.IsSpecified)
        {
            throw new ArgumentException(
                "Building presentation metadata must be specified.",
                nameof(feature));
        }

        return feature.State switch
        {
            BuildingPresentationState.ConstructionFoundation =>
                FoundationAssetId,
            BuildingPresentationState.ConstructionFrame =>
                StructuralFrameAssetId,
            BuildingPresentationState.ConstructionShell =>
                PartialShellAssetId,
            BuildingPresentationState.Destroyed =>
                DestroyedAssetId,
            _ =>
                Get(feature.Building)
                    .GetMeshAssetId(lod)
        };
    }

    public static string? ResolveStateAttachmentAssetId(
        in BuildingFeaturePresentationMetadata feature) =>
        feature.State switch
        {
            BuildingPresentationState.Idle =>
                IdleStateAssetId,
            BuildingPresentationState.Unpowered =>
                UnpoweredStateAssetId,
            BuildingPresentationState.Damaged =>
                DamagedStateAssetId,
            BuildingPresentationState.Critical =>
                CriticalStateAssetId,
            _ =>
                null
        };

    public static string ResolveMaterialAssetId(
        in BuildingFeaturePresentationMetadata feature) =>
        feature.State switch
        {
            BuildingPresentationState.Unpowered =>
                UnpoweredMaterialAssetId,
            BuildingPresentationState.Damaged =>
                DamagedMaterialAssetId,
            BuildingPresentationState.Critical =>
                CriticalMaterialAssetId,
            BuildingPresentationState.Destroyed =>
                DestroyedMaterialAssetId,
            _ =>
                StructuralMaterialAssetId
        };

    public static Vector4 ResolveFallbackTint(
        in BuildingFeaturePresentationMetadata feature)
    {
        Vector4 baseTint =
            Get(feature.Building).FallbackTint;

        return feature.State switch
        {
            BuildingPresentationState.ConstructionFoundation =>
                Vector4.Min(
                    Vector4.One,
                    baseTint *
                    new Vector4(
                        0.72f,
                        0.74f,
                        0.70f,
                        1.0f)),
            BuildingPresentationState.ConstructionFrame =>
                Vector4.Min(
                    Vector4.One,
                    baseTint *
                    new Vector4(
                        0.84f,
                        0.86f,
                        0.82f,
                        1.0f)),
            BuildingPresentationState.ConstructionShell =>
                baseTint,
            BuildingPresentationState.Idle =>
                baseTint *
                new Vector4(
                    0.82f,
                    0.84f,
                    0.82f,
                    1.0f),
            BuildingPresentationState.Unpowered =>
                new Vector4(
                    0.16f,
                    0.17f,
                    0.16f,
                    1.0f),
            BuildingPresentationState.Damaged =>
                new Vector4(
                    0.23f,
                    0.20f,
                    0.17f,
                    1.0f),
            BuildingPresentationState.Critical =>
                new Vector4(
                    0.16f,
                    0.13f,
                    0.11f,
                    1.0f),
            BuildingPresentationState.Destroyed =>
                new Vector4(
                    0.11f,
                    0.11f,
                    0.10f,
                    1.0f),
            _ =>
                baseTint
        };
    }

    private static Dictionary<
        BuildingId,
        BuildingPresentationDefinition> CreateDefinitions()
    {
        var result =
            new Dictionary<
                BuildingId,
                BuildingPresentationDefinition>();

        Add(
            BuildingIds.CommandCore,
            "command_core",
            "command",
            new Vector4(
                0.30f,
                0.32f,
                0.29f,
                1.0f));
        Add(
            BuildingIds.Extractor,
            "extractor",
            "extraction",
            new Vector4(
                0.32f,
                0.31f,
                0.27f,
                1.0f));
        Add(
            BuildingIds.Smelter,
            "smelter",
            "processing",
            new Vector4(
                0.34f,
                0.30f,
                0.26f,
                1.0f));
        Add(
            BuildingIds.ElectronicsPlant,
            "electronics_plant",
            "processing",
            new Vector4(
                0.31f,
                0.33f,
                0.31f,
                1.0f));
        Add(
            BuildingIds.Refinery,
            "fuel_refinery",
            "processing",
            new Vector4(
                0.33f,
                0.31f,
                0.27f,
                1.0f));
        Add(
            BuildingIds.VehicleFactory,
            "vehicle_factory",
            "factory",
            new Vector4(
                0.30f,
                0.32f,
                0.29f,
                1.0f));
        Add(
            BuildingIds.StorageDepot,
            "storage_depot",
            "storage",
            new Vector4(
                0.31f,
                0.33f,
                0.29f,
                1.0f));
        Add(
            BuildingIds.SupplyDepot,
            "supply_depot",
            "supply",
            new Vector4(
                0.32f,
                0.35f,
                0.29f,
                1.0f));
        Add(
            BuildingIds.PowerPlant,
            "power_plant",
            "power",
            new Vector4(
                0.31f,
                0.32f,
                0.28f,
                1.0f));

        return result;

        void Add(
            BuildingId building,
            string name,
            string symbol,
            Vector4 tint)
        {
            string mesh =
                $"building.directorate.{name}";

            result.Add(
                building,
                new BuildingPresentationDefinition(
                    building,
                    mesh,
                    $"{mesh}.lod1",
                    $"{mesh}.lod2",
                    StructuralMaterialAssetId,
                    CollisionAssetId,
                    $"material.directorate.symbol.building.{symbol}",
                    tint,
                    220.0f,
                    620.0f));
        }
    }
}
