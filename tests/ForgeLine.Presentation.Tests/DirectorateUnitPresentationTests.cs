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
                definition.MeshAssetId,
                StringComparison.Ordinal);
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
                definition.StrategicSymbolAssetId,
                StringComparison.Ordinal);
            Assert.NotEmpty(
                definition.RequiredSockets);
        }

        UnitPresentationDefinition rifle =
            UnitPresentationCatalog.Get(
                UnitIds.RifleSquad);
        UnitPresentationDefinition engineer =
            UnitPresentationCatalog.Get(
                UnitIds.CombatEngineer);

        Assert.Equal(
            rifle.MeshAssetId,
            engineer.MeshAssetId);
        Assert.Equal(
            rifle.MaterialAssetId,
            engineer.MaterialAssetId);
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
