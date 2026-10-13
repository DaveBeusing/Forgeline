using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class DirectorateUnitPresentationTests
{
    [Fact]
    public void TankDamageMeshesUseExistingStateAndResolveWithoutHotPathAllocations()
    {
        UnitFeaturePresentationMetadata[] states = Enum.GetValues<UnitPresentationDamageState>()
            .Select(s => new UnitFeaturePresentationMetadata(UnitIds.MainBattleTank, s)).ToArray();
        string?[] attachments = new string?[states.Length];
        for (int i = 0; i < 1000; i++)
            for (int j = 0; j < states.Length; j++)
                attachments[j] = UnitPresentationCatalog.ResolveStateAttachmentAssetId(states[j]);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            for (int j = 0; j < states.Length; j++)
                attachments[j] = UnitPresentationCatalog.ResolveStateAttachmentAssetId(states[j]);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(new string?[] { null, "unit.directorate.main_battle_tank.damaged", "unit.directorate.main_battle_tank.critical", null }, attachments);
        Assert.Equal("unit.directorate.main_battle_tank.wreck", UnitPresentationCatalog.ResolveWreckAssetId(states[3]));
        var scout = new UnitFeaturePresentationMetadata(UnitIds.ScoutVehicle, UnitPresentationDamageState.Wreck);
        Assert.Equal("unit.directorate.scout_vehicle.wreck", UnitPresentationCatalog.ResolveWreckAssetId(scout));
        Assert.Null(UnitPresentationCatalog.ResolveStateAttachmentAssetId(scout));
    }

    [Fact]
    public void CatalogBindsStableAssetsLodsCollisionAndStrategicSymbols()
    {
        UnitId[] units =
        [
            UnitIds.RifleSquad,
            UnitIds.ScoutVehicle,
            UnitIds.MainBattleTank,
            UnitIds.MobileArtillery,
            UnitIds.CargoTruck,
            UnitIds.SupplyTruck
        ];

        foreach (UnitId unit in units)
        {
            UnitPresentationDefinition definition =
                UnitPresentationCatalog.Get(
                    unit);

            Assert.StartsWith(
                "unit.directorate.",
                definition.MeshAssetId);
            Assert.Equal(
                definition.MeshAssetId + ".lod1",
                definition.Lod1AssetId);
            Assert.Equal(
                definition.MeshAssetId + ".lod2",
                definition.Lod2AssetId);
            Assert.Equal(
                definition.MeshAssetId + ".collision",
                definition.CollisionAssetId);
            Assert.StartsWith(
                "material.directorate.symbol.",
                definition.StrategicSymbolAssetId);
            Assert.NotEmpty(
                definition.RequiredSockets);
        }

        UnitPresentationDefinition tank =
            UnitPresentationCatalog.Get(
                UnitIds.MainBattleTank);
        Assert.True(
            tank.HasArticulatedTurret);
        Assert.Equal(
            "unit.directorate.main_battle_tank.turret",
            tank.TurretMeshAssetId);
        Assert.Equal(
            new Vector3(
                0.0f,
                0.10f,
                0.02f),
            tank.TurretPivot);

        UnitPresentationDefinition rifle =
            UnitPresentationCatalog.Get(
                UnitIds.RifleSquad);
        UnitPresentationDefinition engineer =
            UnitPresentationCatalog.Get(
                UnitIds.CombatEngineer);

        Assert.NotEqual(
            rifle.MeshAssetId,
            engineer.MeshAssetId);
        Assert.Equal(
            rifle.MaterialAssetId,
            engineer.MaterialAssetId);
    }

    [Theory]
    [InlineData(UnitAssetLod.Lod0)]
    [InlineData(UnitAssetLod.Lod1)]
    [InlineData(UnitAssetLod.Lod2)]
    public void InfantryDamageGeometryRemainsBoundAcrossAllLods(UnitAssetLod lod)
    {
        foreach (UnitId unit in new[] { UnitIds.RifleSquad, UnitIds.CombatEngineer })
        {
            string prefix = UnitPresentationCatalog.Get(unit).MeshAssetId;
            Assert.Equal(prefix + ".damaged", UnitPresentationCatalog.ResolveStateAttachmentAssetId(new(unit, UnitPresentationDamageState.Damaged), lod));
            Assert.Equal(prefix + ".critical", UnitPresentationCatalog.ResolveStateAttachmentAssetId(new(unit, UnitPresentationDamageState.Critical), lod));
            Assert.Null(UnitPresentationCatalog.ResolveStateAttachmentAssetId(new(unit, UnitPresentationDamageState.Wreck), lod));
        }
        if (lod == UnitAssetLod.Lod2)
            Assert.Null(UnitPresentationCatalog.ResolveStateAttachmentAssetId(new(UnitIds.MainBattleTank, UnitPresentationDamageState.Critical), lod));
    }

    [Fact]
    public void UnitLodSelectionCoversOperationalAndStrategicDistances()
    {
        var feature =
            new UnitFeaturePresentationMetadata(
                UnitIds.MainBattleTank,
                UnitPresentationDamageState.Intact);
        UnitPresentationDefinition definition =
            UnitPresentationCatalog.Get(
                feature.Unit);

        Assert.Equal(
            UnitAssetLod.Lod0,
            UnitPresentationCatalog.SelectLod(
                feature,
                definition.Lod1DistanceMeters - 1.0f));
        Assert.Equal(
            UnitAssetLod.Lod1,
            UnitPresentationCatalog.SelectLod(
                feature,
                definition.Lod1DistanceMeters));
        Assert.Equal(
            UnitAssetLod.Lod2,
            UnitPresentationCatalog.SelectLod(
                feature,
                definition.Lod2DistanceMeters));
    }

    [Fact]
    public void ExtractorPublishesDamageAndWreckPresentationStates()
    {
        var simulation =
            new SimulationCoordinator();
        var buffer =
            new PresentationSnapshotBuffer();

        simulation.RegisterTickObserver(
            new PresentationExtractor(
                buffer));

        EntityId damaged =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            damaged,
            new WorldTransform(
                Vector3.Zero,
                Quaternion.Identity,
                new Vector3(
                    4.0f,
                    3.0f,
                    7.2f)));
        simulation.Entities.AddComponent(
            damaged,
            new VisualIdentity(
                204));
        simulation.Entities.AddComponent(
            damaged,
            new UnitIdentity(
                UnitIds.MainBattleTank,
                DirectorateContent.FactionId));
        simulation.Entities.AddComponent(
            damaged,
            new HealthState(
                140.0,
                560.0));

        EntityId target =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            target,
            new WorldTransform(
                new Vector3(
                    20.0f,
                    0.0f,
                    0.0f),
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            damaged,
            new WeaponState(
                new WeaponId(
                    1),
                target));

        EntityId wreck =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            wreck,
            new WorldTransform(
                new Vector3(
                    20.0f,
                    0.0f,
                    0.0f),
                Quaternion.Identity,
                new Vector3(
                    3.2f,
                    2.4f,
                    6.4f)));
        simulation.Entities.AddComponent(
            wreck,
            new VisualIdentity(
                207));
        simulation.Entities.AddComponent(
            wreck,
            new UnitWreckPresentationIdentity(
                UnitIds.SupplyTruck,
                DirectorateContent.FactionId));

        simulation.AdvanceOneTick();

        Assert.True(
            buffer.TryReadLatest(
                out PresentationSnapshot snapshot));

        RenderInstance damagedInstance =
            snapshot.Instances
                .ToArray()
                .Single(
                    instance =>
                        instance.Entity ==
                        damaged);
        RenderInstance wreckInstance =
            snapshot.Instances
                .ToArray()
                .Single(
                    instance =>
                        instance.Entity ==
                        wreck);

        Assert.Equal(
            UnitPresentationDamageState.Critical,
            damagedInstance.UnitFeature.DamageState);
        Assert.Equal(
            UnitIds.MainBattleTank,
            damagedInstance.UnitFeature.Unit);
        Assert.InRange(
            damagedInstance.UnitFeature.AimYawRadians,
            (MathF.PI * 0.5f) - 0.001f,
            (MathF.PI * 0.5f) + 0.001f);
        Assert.Equal(
            UnitPresentationDamageState.Wreck,
            wreckInstance.UnitFeature.DamageState);
        Assert.True(
            wreckInstance.UnitFeature.IsWreck);
    }

    [Fact]
    public void DamageStatesDarkenWithoutChangingTheStableMaterialBinding()
    {
        UnitPresentationDefinition definition =
            UnitPresentationCatalog.Get(
                UnitIds.ScoutVehicle);
        var intact =
            new UnitFeaturePresentationMetadata(
                UnitIds.ScoutVehicle,
                UnitPresentationDamageState.Intact);
        var critical =
            intact with
            {
                DamageState =
                    UnitPresentationDamageState.Critical
            };
        var wreck =
            intact with
            {
                DamageState =
                    UnitPresentationDamageState.Wreck
            };

        Vector4 intactTint =
            UnitPresentationCatalog.ApplyDamageTint(
                intact,
                definition.FallbackTint);
        Vector4 criticalTint =
            UnitPresentationCatalog.ApplyDamageTint(
                critical,
                definition.FallbackTint);
        Vector4 wreckTint =
            UnitPresentationCatalog.ApplyDamageTint(
                wreck,
                definition.FallbackTint);

        Assert.True(
            criticalTint.X <
            intactTint.X);
        Assert.True(
            wreckTint.X <
            criticalTint.X);
        Assert.Equal(
            "material.directorate.unit.scout_vehicle",
            definition.MaterialAssetId);
    }
}
