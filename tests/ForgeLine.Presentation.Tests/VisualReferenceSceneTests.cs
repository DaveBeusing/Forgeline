using ForgeLine.Game;
using ForgeLine.Intelligence;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class VisualReferenceSceneTests
{
    [Theory]
    [InlineData(VisualReferenceSceneKind.VehicleFactoryProduction)]
    [InlineData(VisualReferenceSceneKind.PowerPlantProduction)]
    [InlineData(VisualReferenceSceneKind.BarracksProduction)]
    [InlineData(VisualReferenceSceneKind.RadarProduction)]
    [InlineData(VisualReferenceSceneKind.OperationalBuildingsRepeated)]
    [InlineData(VisualReferenceSceneKind.EnvironmentKitYard)]
    [InlineData(VisualReferenceSceneKind.EnvironmentKitBridge)]
    [InlineData(VisualReferenceSceneKind.EnvironmentKitRepeated)]
    [InlineData(VisualReferenceSceneKind.MechanicalIdle)]
    [InlineData(VisualReferenceSceneKind.MechanicalMoving)]
    [InlineData(VisualReferenceSceneKind.MechanicalFiring)]
    [InlineData(VisualReferenceSceneKind.MechanicalLoading)]
    [InlineData(VisualReferenceSceneKind.MechanicalUnpowered)]
    [InlineData(VisualReferenceSceneKind.MechanicalBroken)]
    [InlineData(VisualReferenceSceneKind.DaylightTerrain)]
    [InlineData(VisualReferenceSceneKind.IndustrialBase)]
    [InlineData(VisualReferenceSceneKind.FormationConvoy)]
    [InlineData(VisualReferenceSceneKind.LogisticsCorridor)]
    [InlineData(VisualReferenceSceneKind.DamageDestruction)]
    [InlineData(VisualReferenceSceneKind.BattlefieldCombat)]
    [InlineData(VisualReferenceSceneKind.DirectorateUnits)]
    [InlineData(VisualReferenceSceneKind.DirectorateBuildingStates)]
    [InlineData(VisualReferenceSceneKind.CommandCoreProduction)]
    [InlineData(VisualReferenceSceneKind.MainBattleTankProduction)]
    [InlineData(VisualReferenceSceneKind.MainBattleTankFormation)]
    [InlineData(VisualReferenceSceneKind.ExtractorProduction)]
    [InlineData(VisualReferenceSceneKind.SmelterProduction)]
    [InlineData(VisualReferenceSceneKind.ElectronicsPlantProduction)]
    [InlineData(VisualReferenceSceneKind.FuelRefineryProduction)]
    [InlineData(VisualReferenceSceneKind.AmmunitionPlantProduction)]
    [InlineData(VisualReferenceSceneKind.StorageDepotProduction)]
    [InlineData(VisualReferenceSceneKind.SupplyDepotProduction)]
    [InlineData(VisualReferenceSceneKind.LogisticsHubProduction)]
    public void FreshFixturesRetainTheSameImmutableState(VisualReferenceSceneKind kind)
    {
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var first = VisualReferenceScene.Create(kind, terrain);
        var retained = first.Snapshot.Instances.ToArray();
        var second = VisualReferenceScene.Create(kind, terrain);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(retained, first.Snapshot.Instances.ToArray());
        Assert.Equal(VisualReferenceScene.FixedTick, (int)first.Snapshot.Tick.Value);
        Assert.All(first.Snapshot.Instances.ToArray(), instance =>
            Assert.True(instance.UnitFeature.IsSpecified || instance.BuildingFeature.IsSpecified ||
                instance.WorldFeature.IsSpecified || instance.InfrastructureFeature.IsSpecified || instance.VfxFeature.IsSpecified));
    }

    [Theory]
    [InlineData(VisualReferenceSceneKind.VehicleFactoryProduction, 9, 26, 12, 24)]
    [InlineData(VisualReferenceSceneKind.PowerPlantProduction, 8, 16, 10, 16)]
    [InlineData(VisualReferenceSceneKind.BarracksProduction, 9, 18, 8, 16)]
    [InlineData(VisualReferenceSceneKind.RadarProduction, 8, 14, 18, 14)]
    public void OperationalGalleriesRetainPhysicalScaleAndAuthorizedStates(VisualReferenceSceneKind kind, int count, int width, int height, int depth)
    {
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var scene = VisualReferenceScene.Create(kind, terrain);
        var buildings = scene.Snapshot.Instances.ToArray().Where(x => x.BuildingFeature.IsSpecified).ToArray();
        Assert.Equal(count, buildings.Length);
        Assert.Equal(count, buildings.Select(x => x.BuildingFeature.State).Distinct().Count());
        Assert.Equal(3, buildings.Count(x => x.BuildingFeature.IsConstruction));
        Assert.All(buildings, x => Assert.Equal(new System.Numerics.Vector3(width, height, depth), x.Transform.Scale));
        Assert.All(buildings.Where(x => x.BuildingFeature.IsDestroyed), x => Assert.False(x.Selectable.IsSelectable));
        Assert.All(buildings.Where(x => x.BuildingFeature.State == BuildingPresentationState.Unpowered), x => Assert.Equal(0, x.MechanicalPose.ActiveSeconds));
    }

    [Fact]
    public void RepeatedOperationalGalleryContainsEqualFamilyLoads()
    {
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var scene = VisualReferenceScene.Create(VisualReferenceSceneKind.OperationalBuildingsRepeated, terrain);
        var buildings = scene.Snapshot.Instances.ToArray().Where(x => x.BuildingFeature.IsSpecified).ToArray();
        Assert.Equal(1000, buildings.Length);
        Assert.Equal(4, buildings.Select(x => x.BuildingFeature.Building).Distinct().Count());
        Assert.All(buildings.GroupBy(x => x.BuildingFeature.Building), group => Assert.Equal(250, group.Count()));
        Assert.All(buildings, x => Assert.Equal(BuildingPresentationState.Operational, x.BuildingFeature.State));
    }

    [Theory]
    [InlineData(VisualReferenceSceneKind.ExtractorProduction)]
    [InlineData(VisualReferenceSceneKind.SmelterProduction)]
    [InlineData(VisualReferenceSceneKind.ElectronicsPlantProduction)]
    [InlineData(VisualReferenceSceneKind.FuelRefineryProduction)]
    [InlineData(VisualReferenceSceneKind.AmmunitionPlantProduction)]
    public void IndustrialGalleryRetainsNineAuthoritativeStatesAndFootprints(VisualReferenceSceneKind kind)
    {
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var scene = VisualReferenceScene.Create(kind, terrain);
        var buildings = scene.Snapshot.Instances.ToArray().Where(x => x.BuildingFeature.IsSpecified).ToArray();
        Assert.Equal(9, buildings.Length);
        Assert.Single(buildings.Select(x => x.BuildingFeature.Building).Distinct());
        Assert.Equal(9, buildings.Select(x => x.BuildingFeature.State).Distinct().Count());
        var footprint = DirectorateContent.CreateBuildingCatalog()[buildings[0].BuildingFeature.Building].Footprint;
        Assert.All(buildings, x => Assert.Equal(new System.Numerics.Vector3(footprint.Width, footprint.Height, footprint.Depth), x.Transform.Scale));
        Assert.Contains(buildings, x => x.BuildingFeature.IsDestroyed && !x.Selectable.IsSelectable);
        Assert.Equal(3, buildings.Count(x => x.BuildingFeature.IsConstruction));
        Assert.Equal(PlayerSelectionKind.Building, scene.Snapshot.PlayerExperience!.Value.Selection.Kind);
    }

    [Theory]
    [InlineData(VisualReferenceSceneKind.StorageDepotProduction)]
    [InlineData(VisualReferenceSceneKind.SupplyDepotProduction)]
    [InlineData(VisualReferenceSceneKind.LogisticsHubProduction)]
    public void LogisticsGalleryUsesExistingStatesWithoutInventingProductionFacilities(VisualReferenceSceneKind kind)
    {
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var scene = VisualReferenceScene.Create(kind, terrain);
        var buildings = scene.Snapshot.Instances.ToArray().Where(x => x.BuildingFeature.IsSpecified).ToArray();
        Assert.Equal(9, buildings.Length);
        Assert.Single(buildings.Select(x => x.BuildingFeature.Building).Distinct());
        Assert.Equal(8, buildings.Select(x => x.BuildingFeature.State).Distinct().Count());
        Assert.DoesNotContain(buildings, x => x.BuildingFeature.State == BuildingPresentationState.Idle);
        var footprint = DirectorateContent.CreateBuildingCatalog()[buildings[0].BuildingFeature.Building].Footprint;
        Assert.All(buildings, x => Assert.Equal(new System.Numerics.Vector3(footprint.Width, footprint.Height, footprint.Depth), x.Transform.Scale));
        Assert.Equal(2, buildings.Count(x => x.BuildingFeature.State == BuildingPresentationState.Operational));
    }

    [Fact]
    public void KitReviewContainsAuthenticStaticDressingAndBoundedRepeatedGeometry()
    {
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var yard = VisualReferenceScene.Create(VisualReferenceSceneKind.EnvironmentKitYard, terrain);
        var bridge = VisualReferenceScene.Create(VisualReferenceSceneKind.EnvironmentKitBridge, terrain);
        Assert.Equal(yard.Snapshot.Instances.ToArray(), bridge.Snapshot.Instances.ToArray());
        Assert.Contains(yard.Snapshot.Instances.ToArray(), x => x.WorldFeature.Visual == WorldVisualId.PropLoadingGantry);
        Assert.Contains(bridge.Snapshot.Instances.ToArray(), x => x.InfrastructureFeature.IsSpecified);
        Assert.All(yard.Snapshot.Instances.ToArray().Where(x => x.Entity.Index >= 0xE000_0000U && x.Entity.Index < 0xF000_0000U),
            x => Assert.False(x.Selectable.IsSelectable || x.WorldFeature.Inspectable));
        var repeated = VisualReferenceScene.Create(VisualReferenceSceneKind.EnvironmentKitRepeated, terrain);
        var added = repeated.Snapshot.Instances.ToArray().Where(x => x.Entity.Index >= 0xD000_0000U && x.Entity.Index < 0xE000_0000U).ToArray();
        Assert.Equal(1000, added.Length);
        Assert.Equal(10, added.Select(x => x.WorldFeature.Visual).Distinct().Count());
        Assert.All(added, x => Assert.False(x.Selectable.IsSelectable || x.WorldFeature.Inspectable));
    }

    [Theory]
    [InlineData(VisualReferenceSceneKind.LogisticsLoadingProduction)]
    [InlineData(VisualReferenceSceneKind.LogisticsTransitProduction)]
    [InlineData(VisualReferenceSceneKind.LogisticsUnloadingProduction)]
    [InlineData(VisualReferenceSceneKind.LogisticsSustainedProduction)]
    public void PhysicalLogisticsFixturesReachTheirRealLifecycleWithoutVisualOverlap(VisualReferenceSceneKind kind)
    {
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var first = VisualReferenceScene.Create(kind, terrain);
        var second = VisualReferenceScene.Create(kind, terrain);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        var instances = first.Snapshot.Instances.ToArray();
        var buildings = instances.Where(x => x.BuildingFeature.IsSpecified).ToArray();
        var vehicles = instances.Where(x => x.UnitFeature.IsSpecified).ToArray();
        Assert.Equal(3, buildings.Length);
        Assert.Equal(2, vehicles.Length);
        Assert.Contains(vehicles, x => x.UnitFeature.Unit == UnitIds.CargoTruck);
        Assert.Contains(vehicles, x => x.UnitFeature.Unit == UnitIds.SupplyTruck);
        foreach (var vehicle in vehicles)
        foreach (var building in buildings)
        {
            var delta = vehicle.Transform.Position - building.Transform.Position;
            var combined = (vehicle.Transform.Scale + building.Transform.Scale) * .5f;
            Assert.True(MathF.Abs(delta.X) > combined.X || MathF.Abs(delta.Z) > combined.Z);
        }
        if (kind == VisualReferenceSceneKind.LogisticsSustainedProduction)
            Assert.Equal(1024UL, first.Snapshot.Tick.Value);
    }

    [Fact]
    public void CommandCoreGalleryUsesAuthoritativeConstructionHealthPowerAndWreckStates()
    {
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var scene = VisualReferenceScene.Create(VisualReferenceSceneKind.CommandCoreProduction, terrain);
        var buildings = scene.Snapshot.Instances.ToArray().Where(x => x.BuildingFeature.IsSpecified).ToArray();
        Assert.Equal(8, buildings.Length);
        Assert.All(buildings, x => Assert.Equal(BuildingIds.CommandCore, x.BuildingFeature.Building));
        Assert.Equal(8, buildings.Select(x => x.BuildingFeature.State).Distinct().Count());
        Assert.All(buildings, x => Assert.Equal(new System.Numerics.Vector3(20, 12, 20), x.Transform.Scale));
        Assert.Contains(buildings, x => x.BuildingFeature.IsDestroyed);
        Assert.Equal(3, buildings.Count(x => x.BuildingFeature.IsConstruction));
        Assert.Equal(PlayerSelectionKind.Building, scene.Snapshot.PlayerExperience!.Value.Selection.Kind);
        Assert.DoesNotContain(scene.Snapshot.Instances.ToArray(), x => x.WorldFeature.IsSpecified &&
            MathF.Abs(x.Transform.Position.X - scene.Target.X) <= 44 + x.Transform.Scale.X * .5f &&
            MathF.Abs(x.Transform.Position.Z - scene.Target.Z) <= 22 + x.Transform.Scale.Z * .5f);
    }

    [Fact]
    public void TankGalleryRetainsScaleStatesAimAndRoleComparators()
    {
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var scene = VisualReferenceScene.Create(VisualReferenceSceneKind.MainBattleTankProduction, terrain);
        var tanks = scene.Snapshot.Instances.ToArray().Where(x => x.UnitFeature.Unit == UnitIds.MainBattleTank).ToArray();
        Assert.Equal(8, tanks.Length);
        Assert.All(tanks, x => Assert.Equal(new System.Numerics.Vector3(4, 3, 7.2f), x.Transform.Scale));
        Assert.Equal(4, tanks.Select(x => x.UnitFeature.DamageState).Distinct().Count());
        Assert.Contains(tanks, x => x.MechanicalPose.LastShotSeconds >= 0 && x.UnitFeature.AimYawRadians != 0);
        Assert.Contains(tanks, x => x.MechanicalPose.TravelMeters > 0);
        Assert.Contains(tanks, x => x.UnitFeature.IsWreck && !x.Selectable.IsSelectable);
        Assert.Contains(scene.Snapshot.Instances.ToArray(), x => x.UnitFeature.Unit == UnitIds.MobileArtillery);
        Assert.Contains(scene.Snapshot.Instances.ToArray(), x => x.UnitFeature.Unit == UnitIds.ScoutVehicle);
    }

    [Fact]
    public void TankFormationContainsThousandRepeatedProductionIdentities()
    {
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var scene = VisualReferenceScene.Create(VisualReferenceSceneKind.MainBattleTankFormation, terrain);
        var tanks = scene.Snapshot.Instances.ToArray().Where(x => x.UnitFeature.IsSpecified).ToArray();
        Assert.Equal(1000, tanks.Length);
        Assert.All(tanks, x =>
        {
            Assert.Equal(UnitIds.MainBattleTank, x.UnitFeature.Unit);
            Assert.True(x.MechanicalPose.TravelMeters > 0);
        });
    }

    [Fact]
    public void UnitGalleryUsesEveryProductionIdentityAndGameplayScale()
    {
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var scene = VisualReferenceScene.Create(VisualReferenceSceneKind.DirectorateUnits, terrain);
        var units = scene.Snapshot.Instances.ToArray().Where(x => x.UnitFeature.IsSpecified).ToArray();
        Assert.Equal(7, units.Length);
        Assert.Equal(7, units.Select(x => x.UnitFeature.Unit).Distinct().Count());
        var definitions = DirectorateContent.CreateUnitCatalog();
        Assert.All(units, unit => Assert.Equal(definitions[unit.UnitFeature.Unit].VisualScale, unit.Transform.Scale));
    }

    [Fact]
    public void BuildingGalleryExtractsOperationalUnpoweredAndCriticalStates()
    {
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var scene = VisualReferenceScene.Create(VisualReferenceSceneKind.DirectorateBuildingStates, terrain);
        var buildings = scene.Snapshot.Instances.ToArray().Where(x => x.BuildingFeature.IsSpecified).ToArray();
        Assert.Equal(9, buildings.Length);
        foreach (var id in new[] { BuildingIds.CommandCore, BuildingIds.VehicleFactory, BuildingIds.PowerPlant })
            Assert.Equal(new[] { BuildingPresentationState.Operational, BuildingPresentationState.Unpowered, BuildingPresentationState.Critical },
                buildings.Where(x => x.BuildingFeature.Building == id).Select(x => x.BuildingFeature.State).Order());
    }

    [Fact]
    public void HiddenForeignUnitCannotChangeVisibleSnapshotOrContacts()
    {
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var withHidden = VisualReferenceScene.Create(VisualReferenceSceneKind.BattlefieldCombat, terrain);
        var without = VisualReferenceScene.Create(VisualReferenceSceneKind.BattlefieldCombat, terrain, false);
        Assert.True(withHidden.HiddenEnemy.IsValid);
        Assert.DoesNotContain(withHidden.Snapshot.Instances.ToArray(), x => x.Entity == withHidden.HiddenEnemy);
        Assert.DoesNotContain(withHidden.Snapshot.Intelligence!.Contacts,
            x => x.ContactKey == IntelligenceContactKey.FromEntity(withHidden.HiddenEnemy));
        Assert.Equal(without.Fingerprint, withHidden.Fingerprint);
        Assert.Contains(withHidden.Snapshot.Intelligence.Cells, x => x.State == IntelligenceState.Visible);
        Assert.Contains(withHidden.Snapshot.Intelligence.Cells, x => x.State == IntelligenceState.Unexplored);
        Assert.Contains(withHidden.Snapshot.Intelligence.Contacts, x => x.IsCurrent);
    }
}
