using System.Numerics;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

public enum UnitPresentationDamageState : byte
{
    Intact = 0,
    Damaged = 1,
    Critical = 2,
    Wreck = 3
}

public enum UnitAssetLod : byte
{
    Lod0 = 0,
    Lod1 = 1,
    Lod2 = 2
}

public readonly record struct UnitPresentationDefinition(
    UnitId Unit,
    string MeshAssetId,
    string Lod1AssetId,
    string Lod2AssetId,
    string MaterialAssetId,
    string CollisionAssetId,
    string StrategicSymbolAssetId,
    Vector4 FallbackTint,
    string? TurretMeshAssetId,
    Vector3 TurretPivot,
    float Lod1DistanceMeters,
    float Lod2DistanceMeters,
    IReadOnlyList<string> RequiredSockets)
{
    public bool HasArticulatedTurret =>
        !string.IsNullOrWhiteSpace(
            TurretMeshAssetId);

    public string GetMeshAssetId(UnitAssetLod lod) =>
        lod switch
        {
            UnitAssetLod.Lod0 => MeshAssetId,
            UnitAssetLod.Lod1 => Lod1AssetId,
            UnitAssetLod.Lod2 => Lod2AssetId,
            _ => throw new ArgumentOutOfRangeException(nameof(lod))
        };
}

public readonly record struct UnitFeaturePresentationMetadata(
    UnitId Unit,
    UnitPresentationDamageState DamageState,
    float AimYawRadians = 0.0f)
{
    public static UnitFeaturePresentationMetadata None => default;

    public bool IsSpecified =>
        Unit.IsSpecified;

    public bool IsWreck =>
        DamageState == UnitPresentationDamageState.Wreck;
}

public static class UnitPresentationCatalog
{
    private static readonly Dictionary<UnitId, UnitPresentationDefinition> Definitions =
        CreateDefinitions();

    public static UnitPresentationDefinition Get(UnitId unit) =>
        Definitions.TryGetValue(
            unit,
            out UnitPresentationDefinition definition)
            ? definition
            : throw new KeyNotFoundException(
                $"Unit '{unit}' has no presentation definition.");

    public static bool TryGet(
        UnitId unit,
        out UnitPresentationDefinition definition) =>
        Definitions.TryGetValue(
            unit,
            out definition);

    public static UnitAssetLod SelectLod(
        in UnitFeaturePresentationMetadata feature,
        float distanceMeters)
    {
        if (!feature.IsSpecified ||
            !float.IsFinite(distanceMeters) ||
            distanceMeters < 0.0f)
        {
            return UnitAssetLod.Lod0;
        }

        UnitPresentationDefinition definition =
            Get(feature.Unit);

        if (distanceMeters >= definition.Lod2DistanceMeters)
        {
            return UnitAssetLod.Lod2;
        }

        return distanceMeters >= definition.Lod1DistanceMeters
            ? UnitAssetLod.Lod1
            : UnitAssetLod.Lod0;
    }

    public static Vector3 ResolveReadabilityBoundsScale(
        UnitId unit)
    {
        if (unit == UnitIds.MainBattleTank)
        {
            return new Vector3(
                1.15f,
                1.20f,
                1.35f);
        }

        if (unit == UnitIds.MobileArtillery)
        {
            return new Vector3(
                1.15f,
                1.20f,
                1.45f);
        }

        if (unit == UnitIds.ScoutVehicle)
        {
            return new Vector3(
                1.15f,
                1.35f,
                1.15f);
        }

        if (unit == UnitIds.CargoTruck)
        {
            return new Vector3(
                1.10f,
                1.10f,
                1.12f);
        }

        if (unit == UnitIds.SupplyTruck)
        {
            return new Vector3(
                1.10f,
                1.15f,
                1.12f);
        }

        if (unit == UnitIds.RifleSquad ||
            unit == UnitIds.CombatEngineer)
        {
            return new Vector3(
                1.15f,
                1.10f,
                1.15f);
        }

        return Vector3.One;
    }

    public static Vector4 ResolveFallbackTint(
        in UnitFeaturePresentationMetadata feature) =>
        ApplyDamageTint(
            feature,
            Get(feature.Unit).FallbackTint);

    private static readonly Dictionary<UnitId, string> DedicatedWrecks = new[]
    {
        UnitIds.RifleSquad, UnitIds.CombatEngineer, UnitIds.ScoutVehicle,
        UnitIds.MobileArtillery, UnitIds.CargoTruck, UnitIds.SupplyTruck
    }.ToDictionary(unit => unit, unit => Get(unit).MeshAssetId + ".wreck");

    private static readonly Dictionary<UnitId, (string Damaged, string Critical)> InfantryStates =
        new[] { UnitIds.RifleSquad, UnitIds.CombatEngineer }
        .ToDictionary(unit => unit, unit => (Get(unit).MeshAssetId + ".damaged", Get(unit).MeshAssetId + ".critical"));

    public static string ResolveWreckAssetId(in UnitFeaturePresentationMetadata feature) =>
        DedicatedWrecks.TryGetValue(feature.Unit, out string? wreck) ? wreck :
        feature.Unit == UnitIds.MainBattleTank
            ? "unit.directorate.main_battle_tank.wreck"
            : Get(feature.Unit).Lod2AssetId;

    public static string? ResolveStateAttachmentAssetId(in UnitFeaturePresentationMetadata feature, UnitAssetLod lod) =>
        lod == UnitAssetLod.Lod2 && feature.Unit != UnitIds.RifleSquad && feature.Unit != UnitIds.CombatEngineer
            ? null : ResolveStateAttachmentAssetId(feature);

    public static string? ResolveStateAttachmentAssetId(in UnitFeaturePresentationMetadata feature) =>
        InfantryStates.TryGetValue(feature.Unit, out var states) ? feature.DamageState switch
        {
            UnitPresentationDamageState.Damaged => states.Damaged,
            UnitPresentationDamageState.Critical => states.Critical,
            _ => null
        } : feature.Unit != UnitIds.MainBattleTank ? null : feature.DamageState switch
        {
            UnitPresentationDamageState.Damaged => "unit.directorate.main_battle_tank.damaged",
            UnitPresentationDamageState.Critical => "unit.directorate.main_battle_tank.critical",
            _ => null
        };

    public static Vector4 ApplyDamageTint(
        in UnitFeaturePresentationMetadata feature,
        Vector4 baseTint) =>
        feature.DamageState switch
        {
            UnitPresentationDamageState.Intact =>
                baseTint,
            UnitPresentationDamageState.Damaged =>
                Vector4.Min(
                    Vector4.One,
                    baseTint *
                    new Vector4(0.78f, 0.72f, 0.68f, 1.0f)),
            UnitPresentationDamageState.Critical =>
                Vector4.Min(
                    Vector4.One,
                    baseTint *
                    new Vector4(0.52f, 0.44f, 0.40f, 1.0f)),
            UnitPresentationDamageState.Wreck =>
                new Vector4(0.12f, 0.13f, 0.12f, 1.0f),
            _ =>
                baseTint
        };

    private static Dictionary<UnitId, UnitPresentationDefinition> CreateDefinitions()
    {
        var result =
            new Dictionary<UnitId, UnitPresentationDefinition>();

        Add(
            UnitIds.RifleSquad,
            "rifle_squad",
            new Vector4(0.31f, 0.34f, 0.29f, 1.0f),
            80.0f,
            220.0f,
            ["weapon_muzzle"]);
        Add(UnitIds.CombatEngineer, "combat_engineer", new Vector4(0.31f, 0.34f, 0.29f, 1.0f),
            80, 220, ["weapon_muzzle"]);
        result[UnitIds.CombatEngineer] = result[UnitIds.CombatEngineer] with
        {
            MaterialAssetId = result[UnitIds.RifleSquad].MaterialAssetId,
            CollisionAssetId = result[UnitIds.RifleSquad].CollisionAssetId,
            StrategicSymbolAssetId = result[UnitIds.RifleSquad].StrategicSymbolAssetId
        };

        Add(
            UnitIds.ScoutVehicle,
            "scout_vehicle",
            new Vector4(0.30f, 0.34f, 0.29f, 1.0f),
            120.0f,
            340.0f,
            ["weapon_muzzle", "sensor_origin"]);
        Add(
            UnitIds.MainBattleTank,
            "main_battle_tank",
            new Vector4(0.27f, 0.30f, 0.26f, 1.0f),
            140.0f,
            380.0f,
            ["turret_pivot", "gun_pivot", "weapon_muzzle", "recoil_anchor"]);
        result[UnitIds.MainBattleTank] =
            result[UnitIds.MainBattleTank] with
            {
                TurretMeshAssetId =
                    "unit.directorate.main_battle_tank.turret",
                TurretPivot =
                    new Vector3(
                        0.0f,
                        0.10f,
                        0.02f)
            };
        Add(
            UnitIds.MobileArtillery,
            "self_propelled_artillery",
            new Vector4(0.29f, 0.31f, 0.27f, 1.0f),
            140.0f,
            380.0f,
            ["gun_pivot", "weapon_muzzle", "recoil_anchor"]);
        Add(
            UnitIds.CargoTruck,
            "cargo_truck",
            new Vector4(0.33f, 0.35f, 0.30f, 1.0f),
            130.0f,
            360.0f,
            ["cargo_load"]);
        Add(
            UnitIds.SupplyTruck,
            "supply_truck",
            new Vector4(0.34f, 0.37f, 0.30f, 1.0f),
            130.0f,
            360.0f,
            ["cargo_load", "supply_transfer"]);

        return result;

        void Add(
            UnitId unit,
            string name,
            Vector4 tint,
            float lod1Distance,
            float lod2Distance,
            IReadOnlyList<string> requiredSockets)
        {
            result.Add(
                unit,
                new UnitPresentationDefinition(
                    unit,
                    $"unit.directorate.{name}",
                    $"unit.directorate.{name}.lod1",
                    $"unit.directorate.{name}.lod2",
                    $"material.directorate.unit.{name}",
                    $"unit.directorate.{name}.collision",
                    $"material.directorate.symbol.{name}",
                    tint,
                    null,
                    Vector3.Zero,
                    lod1Distance,
                    lod2Distance,
                    requiredSockets));
        }
    }
}
