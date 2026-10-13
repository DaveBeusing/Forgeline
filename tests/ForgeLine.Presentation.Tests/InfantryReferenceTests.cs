using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class InfantryReferenceTests
{
    [Theory]
    [InlineData(VisualReferenceSceneKind.InfantryRifleProduction)]
    [InlineData(VisualReferenceSceneKind.InfantryEngineerProduction)]
    [InlineData(VisualReferenceSceneKind.InfantryRepeated)]
    [InlineData(VisualReferenceSceneKind.InfantryBattleProduction)]
    [InlineData(VisualReferenceSceneKind.BattlefieldVariantProduction)]
    public void FixturesRetainPhysicalScalesAndHideEnemyGeometry(VisualReferenceSceneKind kind)
    {
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var first = VisualReferenceScene.Create(kind, terrain);
        var second = VisualReferenceScene.Create(kind, terrain);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        var units = first.Snapshot.Instances.ToArray().Where(x => x.UnitFeature.IsSpecified).ToArray();
        Assert.All(units, unit => Assert.Equal(DirectorateContent.CreateUnitCatalog()[unit.UnitFeature.Unit].VisualScale, unit.Transform.Scale));
        if (first.HiddenEnemy.IsValid) Assert.DoesNotContain(units, unit => unit.Entity == first.HiddenEnemy);
        if (kind == VisualReferenceSceneKind.InfantryRepeated)
        {
            Assert.Equal(1000, units.Length);
            Assert.All(units.GroupBy(x => x.UnitFeature.Unit), group => Assert.Equal(500, group.Count()));
        }
        if (kind is VisualReferenceSceneKind.InfantryRifleProduction or VisualReferenceSceneKind.InfantryEngineerProduction)
            Assert.Equal(4, units.Select(x => x.UnitFeature.DamageState).Distinct().Count());
    }
}
