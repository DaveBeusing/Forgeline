using System.Numerics;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class InfantryReferenceLoadTests
{
    [Theory]
    [InlineData(VisualReferenceSceneKind.InfantryRifleSingle, 1, false)]
    [InlineData(VisualReferenceSceneKind.InfantryRifleHundred, 100, false)]
    [InlineData(VisualReferenceSceneKind.InfantryRifleThousand, 1000, false)]
    [InlineData(VisualReferenceSceneKind.InfantryEngineerSingle, 1, true)]
    [InlineData(VisualReferenceSceneKind.InfantryEngineerHundred, 100, true)]
    [InlineData(VisualReferenceSceneKind.InfantryEngineerThousand, 1000, true)]
    public void PureRoleLoadsRetainSquadIdentityAndCopiedWalkingPhase(VisualReferenceSceneKind kind, int count, bool engineer)
    {
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var scene = VisualReferenceScene.Create(kind, terrain, mechanicalSeconds: .625);
        Assert.Equal(scene.Fingerprint, VisualReferenceScene.Create(kind, terrain, mechanicalSeconds: .625).Fingerprint);
        var instances = scene.Snapshot.Instances.ToArray();
        Assert.Equal(count, instances.Length);
        Assert.Equal(count, instances.Select(x => x.Entity).Distinct().Count());
        Assert.All(instances, item =>
        {
            Assert.Equal(engineer ? UnitIds.CombatEngineer : UnitIds.RifleSquad, item.UnitFeature.Unit);
            Assert.Equal(new Vector3(1.8f), item.Transform.Scale);
            Assert.Equal(InfantryAction.Walk, item.MechanicalPose.Infantry.Action);
            Assert.Equal(.625, item.MechanicalPose.Infantry.Phase);
            Assert.True(item.MechanicalPose.Infantry.Loop);
            Assert.False(item.VfxFeature.IsSpecified);
        });
        Assert.NotEqual(scene.Fingerprint, VisualReferenceScene.Create(kind, terrain, mechanicalSeconds: .375).Fingerprint);
    }
}
