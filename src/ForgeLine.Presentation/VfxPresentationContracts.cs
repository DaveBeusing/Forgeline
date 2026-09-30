using System.Numerics;
using ForgeLine.Assets;
using ForgeLine.Combat;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public enum VfxEffectKind : byte
{
    None = 0,
    MuzzleInfantry = 1,
    MuzzleMachineGun = 2,
    MuzzleTankCannon = 3,
    MuzzleArtillery = 4,
    ProjectileBulletTracer = 5,
    ProjectileCannonShell = 6,
    ProjectileArtilleryShell = 7,
    ProjectileGenericExplosive = 8,
    ImpactDirt = 9,
    ImpactMetal = 10,
    ImpactConcrete = 11,
    ImpactArmor = 12,
    ImpactExplosive = 13,
    ExplosionSmall = 14,
    ExplosionMedium = 15,
    ExplosionVehicle = 16,
    ExplosionBuilding = 17,
    ExplosionAmmunitionSecondary = 18,
    PersistentLightSmoke = 19,
    PersistentHeavySmoke = 20,
    PersistentFire = 21,
    PersistentSparks = 22,
    PersistentDust = 23,
    DestructionVehicleBurst = 24,
    DestructionBuildingBurst = 25,
    DestructionDebris = 26,
    DestructionSmokePlume = 27,
    DestructionPersistentFire = 28,
    DestructionSparkEmission = 29,
    DestructionDustCloud = 30,
    LogisticsLoading = 31,
    LogisticsUnloading = 32,
    LogisticsResourceTransfer = 33,
    LogisticsSupplyTransfer = 34,
    LogisticsRefuel = 35,
    LogisticsRearm = 36
}

public readonly record struct VfxPresentationDefinition(
    VfxEffectKind Kind,
    string MeshAssetId,
    string MaterialAssetId,
    Vector3 BaseScale,
    uint LifetimeTicks,
    float MaximumDistanceMeters,
    Vector4 FallbackTint,
    bool FineDetail = false)
{
    public void Validate()
    {
        if (Kind == VfxEffectKind.None)
        {
            throw new InvalidOperationException(
                "VFX definitions require a concrete effect kind.");
        }

        AssetId.Parse(MeshAssetId);
        AssetId.Parse(MaterialAssetId);

        if (!IsFinitePositive(BaseScale))
        {
            throw new InvalidOperationException(
                $"VFX '{Kind}' has an invalid base scale.");
        }

        ArgumentOutOfRangeException.ThrowIfZero(
            LifetimeTicks);

        if (!float.IsFinite(MaximumDistanceMeters) ||
            MaximumDistanceMeters <= 0.0f)
        {
            throw new InvalidOperationException(
                $"VFX '{Kind}' has an invalid maximum distance.");
        }

        if (!IsFinite(FallbackTint))
        {
            throw new InvalidOperationException(
                $"VFX '{Kind}' has an invalid fallback tint.");
        }
    }

    private static bool IsFinitePositive(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        value.X > 0.0f &&
        value.Y > 0.0f &&
        value.Z > 0.0f;

    private static bool IsFinite(Vector4 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);
}

public readonly record struct VfxFeaturePresentationMetadata(
    VfxEffectKind Kind)
{
    public static VfxFeaturePresentationMetadata None =>
        default;

    public bool IsSpecified =>
        Kind != VfxEffectKind.None;
}

public readonly record struct VfxPresentationMetrics(
    int ActiveTransientEffects,
    int PoolCapacity,
    ulong TotalSpawned,
    ulong TotalReused,
    ulong TotalDropped);

public static class VfxPresentationCatalog
{
    private static readonly IReadOnlyDictionary<
        VfxEffectKind,
        VfxPresentationDefinition> Definitions =
        CreateDefinitions();

    public static IReadOnlyCollection<VfxPresentationDefinition>
        All =>
        Definitions.Values;

    public static VfxPresentationDefinition Get(
        VfxEffectKind kind) =>
        Definitions.TryGetValue(
            kind,
            out VfxPresentationDefinition definition)
            ? definition
            : throw new KeyNotFoundException(
                $"Unknown VFX effect '{kind}'.");

    public static bool TryGet(
        VfxEffectKind kind,
        out VfxPresentationDefinition definition) =>
        Definitions.TryGetValue(
            kind,
            out definition);

    public static bool ShouldRender(
        VfxEffectKind kind,
        float distanceMeters)
    {
        if (!float.IsFinite(distanceMeters) ||
            distanceMeters < 0.0f)
        {
            return false;
        }

        return distanceMeters <=
            Get(kind).MaximumDistanceMeters;
    }

    public static VfxEffectKind ResolveMuzzle(
        WeaponId weapon) =>
        weapon switch
        {
            var id when id ==
                DirectorateContent.WeaponIds.Rifle =>
                VfxEffectKind.MuzzleInfantry,
            var id when id ==
                DirectorateContent.WeaponIds.EngineerCarbine =>
                VfxEffectKind.MuzzleInfantry,
            var id when id ==
                DirectorateContent.WeaponIds.ScoutAutocannon =>
                VfxEffectKind.MuzzleMachineGun,
            var id when id ==
                DirectorateContent.WeaponIds.MainBattleCannon =>
                VfxEffectKind.MuzzleTankCannon,
            var id when id ==
                DirectorateContent.WeaponIds.MobileArtillery =>
                VfxEffectKind.MuzzleArtillery,
            _ =>
                VfxEffectKind.MuzzleMachineGun
        };

    public static VfxEffectKind ResolveProjectile(
        WeaponId weapon) =>
        weapon switch
        {
            var id when id ==
                DirectorateContent.WeaponIds.Rifle ||
                id ==
                DirectorateContent.WeaponIds.EngineerCarbine ||
                id ==
                DirectorateContent.WeaponIds.ScoutAutocannon =>
                VfxEffectKind.ProjectileBulletTracer,
            var id when id ==
                DirectorateContent.WeaponIds.MainBattleCannon =>
                VfxEffectKind.ProjectileCannonShell,
            var id when id ==
                DirectorateContent.WeaponIds.MobileArtillery =>
                VfxEffectKind.ProjectileArtilleryShell,
            _ =>
                VfxEffectKind.ProjectileGenericExplosive
        };

    public static VfxEffectKind ResolveImpact(
        WeaponId weapon,
        TargetClass? targetClass)
    {
        if (weapon ==
            DirectorateContent.WeaponIds.MobileArtillery)
        {
            return VfxEffectKind.ImpactExplosive;
        }

        if (weapon ==
            DirectorateContent.WeaponIds.MainBattleCannon)
        {
            return targetClass ==
                TargetClass.Structure
                    ? VfxEffectKind.ImpactConcrete
                    : VfxEffectKind.ImpactArmor;
        }

        return targetClass switch
        {
            TargetClass.Structure =>
                VfxEffectKind.ImpactConcrete,
            TargetClass.ArmoredVehicle =>
                VfxEffectKind.ImpactArmor,
            TargetClass.LightVehicle =>
                VfxEffectKind.ImpactMetal,
            TargetClass.Infantry =>
                VfxEffectKind.ImpactDirt,
            _ =>
                VfxEffectKind.ImpactMetal
        };
    }

    public static VfxEffectKind? ResolveImpactExplosion(
        WeaponId weapon) =>
        weapon switch
        {
            var id when id ==
                DirectorateContent.WeaponIds.MobileArtillery =>
                VfxEffectKind.ExplosionMedium,
            var id when id ==
                DirectorateContent.WeaponIds.MainBattleCannon =>
                VfxEffectKind.ExplosionSmall,
            _ =>
                null
        };

    public static VfxEffectKind ResolveCombatEvent(
        in CombatEvent combatEvent,
        TargetClass? targetClass = null) =>
        combatEvent.Type switch
        {
            CombatEventType.ShotFired =>
                ResolveMuzzle(
                    combatEvent.Weapon),
            CombatEventType.ProjectileSpawned =>
                ResolveProjectile(
                    combatEvent.Weapon),
            CombatEventType.Impact =>
                ResolveImpact(
                    combatEvent.Weapon,
                    targetClass),
            _ =>
                VfxEffectKind.None
        };

    private static Dictionary<
        VfxEffectKind,
        VfxPresentationDefinition> CreateDefinitions()
    {
        var result =
            new Dictionary<
                VfxEffectKind,
                VfxPresentationDefinition>();

        Add(
            VfxEffectKind.MuzzleInfantry,
            "vfx.combat.muzzle.infantry",
            "material.vfx.muzzle",
            new Vector3(
                0.8f),
            2,
            320.0f,
            new Vector4(
                1.0f,
                0.72f,
                0.25f,
                1.0f),
            true);
        Add(
            VfxEffectKind.MuzzleMachineGun,
            "vfx.combat.muzzle.machine_gun",
            "material.vfx.muzzle",
            new Vector3(
                1.0f),
            2,
            380.0f,
            new Vector4(
                1.0f,
                0.72f,
                0.25f,
                1.0f),
            true);
        Add(
            VfxEffectKind.MuzzleTankCannon,
            "vfx.combat.muzzle.tank_cannon",
            "material.vfx.muzzle",
            new Vector3(
                2.0f),
            3,
            600.0f,
            new Vector4(
                1.0f,
                0.62f,
                0.18f,
                1.0f));
        Add(
            VfxEffectKind.MuzzleArtillery,
            "vfx.combat.muzzle.artillery",
            "material.vfx.muzzle",
            new Vector3(
                2.8f),
            3,
            800.0f,
            new Vector4(
                1.0f,
                0.58f,
                0.16f,
                1.0f));

        Add(
            VfxEffectKind.ProjectileBulletTracer,
            "vfx.combat.projectile.bullet_tracer",
            "material.vfx.projectile",
            new Vector3(
                0.16f,
                0.16f,
                1.0f),
            2,
            500.0f,
            new Vector4(
                1.0f,
                0.84f,
                0.46f,
                1.0f),
            true);
        Add(
            VfxEffectKind.ProjectileCannonShell,
            "vfx.combat.projectile.cannon_shell",
            "material.vfx.projectile",
            new Vector3(
                0.8f),
            2,
            850.0f,
            new Vector4(
                1.0f,
                0.84f,
                0.46f,
                1.0f));
        Add(
            VfxEffectKind.ProjectileArtilleryShell,
            "vfx.combat.projectile.artillery_shell",
            "material.vfx.projectile",
            new Vector3(
                1.0f),
            2,
            1_050.0f,
            new Vector4(
                1.0f,
                0.80f,
                0.42f,
                1.0f));
        Add(
            VfxEffectKind.ProjectileGenericExplosive,
            "vfx.combat.projectile.generic_explosive",
            "material.vfx.projectile",
            new Vector3(
                0.9f),
            2,
            800.0f,
            new Vector4(
                1.0f,
                0.78f,
                0.40f,
                1.0f));

        AddImpact(
            VfxEffectKind.ImpactDirt,
            "vfx.combat.impact.dirt",
            "material.vfx.impact.dirt",
            new Vector4(
                0.46f,
                0.34f,
                0.22f,
                1.0f));
        AddImpact(
            VfxEffectKind.ImpactMetal,
            "vfx.combat.impact.metal",
            "material.vfx.impact.metal",
            new Vector4(
                0.94f,
                0.72f,
                0.34f,
                1.0f));
        AddImpact(
            VfxEffectKind.ImpactConcrete,
            "vfx.combat.impact.concrete",
            "material.vfx.impact.dirt",
            new Vector4(
                0.52f,
                0.50f,
                0.46f,
                1.0f));
        AddImpact(
            VfxEffectKind.ImpactArmor,
            "vfx.combat.impact.armor",
            "material.vfx.impact.metal",
            new Vector4(
                1.0f,
                0.70f,
                0.26f,
                1.0f));
        AddImpact(
            VfxEffectKind.ImpactExplosive,
            "vfx.combat.impact.explosive",
            "material.vfx.explosion",
            new Vector4(
                1.0f,
                0.42f,
                0.12f,
                1.0f));

        AddExplosion(
            VfxEffectKind.ExplosionSmall,
            "vfx.combat.explosion.small",
            new Vector3(
                2.2f),
            6,
            650.0f);
        AddExplosion(
            VfxEffectKind.ExplosionMedium,
            "vfx.combat.explosion.medium",
            new Vector3(
                4.0f),
            8,
            800.0f);
        AddExplosion(
            VfxEffectKind.ExplosionVehicle,
            "vfx.combat.explosion.vehicle",
            new Vector3(
                5.5f),
            10,
            900.0f);
        AddExplosion(
            VfxEffectKind.ExplosionBuilding,
            "vfx.combat.explosion.building",
            new Vector3(
                8.0f),
            12,
            1_100.0f);
        AddExplosion(
            VfxEffectKind.ExplosionAmmunitionSecondary,
            "vfx.combat.explosion.ammunition_secondary",
            new Vector3(
                4.5f),
            9,
            850.0f);

        AddPersistent(
            VfxEffectKind.PersistentLightSmoke,
            "vfx.combat.persistent.light_smoke",
            "material.vfx.smoke",
            new Vector3(
                1.6f),
            650.0f,
            new Vector4(
                0.20f,
                0.21f,
                0.20f,
                0.88f));
        AddPersistent(
            VfxEffectKind.PersistentHeavySmoke,
            "vfx.combat.persistent.heavy_smoke",
            "material.vfx.smoke",
            new Vector3(
                2.4f),
            800.0f,
            new Vector4(
                0.15f,
                0.16f,
                0.15f,
                0.92f));
        AddPersistent(
            VfxEffectKind.PersistentFire,
            "vfx.combat.persistent.fire",
            "material.vfx.fire",
            new Vector3(
                1.8f),
            520.0f,
            new Vector4(
                1.0f,
                0.30f,
                0.08f,
                1.0f),
            true);
        AddPersistent(
            VfxEffectKind.PersistentSparks,
            "vfx.combat.persistent.sparks",
            "material.vfx.spark",
            new Vector3(
                1.4f),
            380.0f,
            new Vector4(
                1.0f,
                0.78f,
                0.26f,
                1.0f),
            true);
        AddPersistent(
            VfxEffectKind.PersistentDust,
            "vfx.combat.persistent.dust",
            "material.vfx.dust",
            new Vector3(
                2.2f),
            520.0f,
            new Vector4(
                0.50f,
                0.42f,
                0.30f,
                0.80f),
            true);

        AddDestruction(
            VfxEffectKind.DestructionVehicleBurst,
            "vfx.destruction.vehicle_burst",
            "material.vfx.explosion",
            new Vector3(
                5.5f),
            10,
            900.0f);
        AddDestruction(
            VfxEffectKind.DestructionBuildingBurst,
            "vfx.destruction.building_burst",
            "material.vfx.explosion",
            new Vector3(
                8.5f),
            12,
            1_100.0f);
        AddDestruction(
            VfxEffectKind.DestructionDebris,
            "vfx.destruction.debris",
            "material.vfx.impact.dirt",
            new Vector3(
                3.2f),
            12,
            700.0f);
        AddDestruction(
            VfxEffectKind.DestructionSmokePlume,
            "vfx.destruction.smoke_plume",
            "material.vfx.smoke",
            new Vector3(
                3.0f),
            30,
            850.0f);
        AddDestruction(
            VfxEffectKind.DestructionPersistentFire,
            "vfx.destruction.persistent_fire",
            "material.vfx.fire",
            new Vector3(
                2.2f),
            24,
            600.0f);
        AddDestruction(
            VfxEffectKind.DestructionSparkEmission,
            "vfx.destruction.spark_emission",
            "material.vfx.spark",
            new Vector3(
                1.8f),
            10,
            420.0f);
        AddDestruction(
            VfxEffectKind.DestructionDustCloud,
            "vfx.destruction.dust_cloud",
            "material.vfx.dust",
            new Vector3(
                4.0f),
            14,
            650.0f);

        AddLogistics(
            VfxEffectKind.LogisticsLoading,
            "vfx.logistics.loading");
        AddLogistics(
            VfxEffectKind.LogisticsUnloading,
            "vfx.logistics.unloading");
        AddLogistics(
            VfxEffectKind.LogisticsResourceTransfer,
            "vfx.logistics.resource_transfer");
        AddLogistics(
            VfxEffectKind.LogisticsSupplyTransfer,
            "vfx.logistics.supply_transfer");
        AddLogistics(
            VfxEffectKind.LogisticsRefuel,
            "vfx.logistics.refuel");
        AddLogistics(
            VfxEffectKind.LogisticsRearm,
            "vfx.logistics.rearm");

        foreach (VfxPresentationDefinition definition in
                 result.Values)
        {
            definition.Validate();
        }

        return result;

        void Add(
            VfxEffectKind kind,
            string mesh,
            string material,
            Vector3 scale,
            uint lifetime,
            float maxDistance,
            Vector4 tint,
            bool fineDetail = false) =>
            result.Add(
                kind,
                new VfxPresentationDefinition(
                    kind,
                    mesh,
                    material,
                    scale,
                    lifetime,
                    maxDistance,
                    tint,
                    fineDetail));

        void AddImpact(
            VfxEffectKind kind,
            string mesh,
            string material,
            Vector4 tint) =>
            Add(
                kind,
                mesh,
                material,
                new Vector3(
                    1.3f),
                4,
                500.0f,
                tint,
                true);

        void AddExplosion(
            VfxEffectKind kind,
            string mesh,
            Vector3 scale,
            uint lifetime,
            float maxDistance) =>
            Add(
                kind,
                mesh,
                "material.vfx.explosion",
                scale,
                lifetime,
                maxDistance,
                new Vector4(
                    1.0f,
                    0.42f,
                    0.12f,
                    1.0f));

        void AddPersistent(
            VfxEffectKind kind,
            string mesh,
            string material,
            Vector3 scale,
            float maxDistance,
            Vector4 tint,
            bool fineDetail = false) =>
            Add(
                kind,
                mesh,
                material,
                scale,
                4,
                maxDistance,
                tint,
                fineDetail);

        void AddDestruction(
            VfxEffectKind kind,
            string mesh,
            string material,
            Vector3 scale,
            uint lifetime,
            float maxDistance) =>
            Add(
                kind,
                mesh,
                material,
                scale,
                lifetime,
                maxDistance,
                material ==
                "material.vfx.smoke"
                    ? new Vector4(
                        0.14f,
                        0.15f,
                        0.14f,
                        0.92f)
                    : material ==
                      "material.vfx.fire"
                        ? new Vector4(
                            1.0f,
                            0.30f,
                            0.08f,
                            1.0f)
                        : material ==
                          "material.vfx.spark"
                            ? new Vector4(
                                1.0f,
                                0.78f,
                                0.26f,
                                1.0f)
                            : material ==
                              "material.vfx.dust" ||
                              material ==
                              "material.vfx.impact.dirt"
                                ? new Vector4(
                                    0.48f,
                                    0.40f,
                                    0.29f,
                                    0.88f)
                                : new Vector4(
                                    1.0f,
                                    0.42f,
                                    0.12f,
                                    1.0f));

        void AddLogistics(
            VfxEffectKind kind,
            string mesh) =>
            Add(
                kind,
                mesh,
                "material.vfx.logistics",
                new Vector3(
                    1.2f),
                2,
                360.0f,
                new Vector4(
                    0.30f,
                    0.78f,
                    0.88f,
                    1.0f),
                true);
    }
}
