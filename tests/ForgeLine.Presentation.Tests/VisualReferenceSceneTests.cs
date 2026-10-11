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
