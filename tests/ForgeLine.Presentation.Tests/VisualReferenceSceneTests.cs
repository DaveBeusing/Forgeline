using ForgeLine.Game;
using ForgeLine.Intelligence;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class VisualReferenceSceneTests
{
    [Theory]
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
