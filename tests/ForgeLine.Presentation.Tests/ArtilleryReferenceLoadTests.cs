using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class ArtilleryReferenceLoadTests
{
    [Theory]
    [InlineData(VisualReferenceSceneKind.ArtillerySingle, 1)]
    [InlineData(VisualReferenceSceneKind.ArtilleryRepeated100, 100)]
    [InlineData(VisualReferenceSceneKind.ArtilleryRepeated1000, 1000)]
    public void ArtilleryLoadsUseOnlyExistingUnitScaleAndCopiedCosmeticPoses(VisualReferenceSceneKind kind, int count)
    {
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var scene = VisualReferenceScene.Create(kind, terrain);
        var instances = scene.Snapshot.Instances.ToArray();
        Assert.Equal(count, instances.Length);
        Assert.Equal(count, instances.Select(x => x.Entity).Distinct().Count());
        var definition = DirectorateContent.CreateUnitCatalog()[UnitIds.MobileArtillery];
        Assert.All(instances, item =>
        {
            Assert.Equal(UnitIds.MobileArtillery, item.UnitFeature.Unit);
            Assert.Equal(UnitPresentationDamageState.Intact, item.UnitFeature.DamageState);
            Assert.Equal(definition.VisualScale, item.Transform.Scale);
            Assert.False(item.VfxFeature.IsSpecified);
        });
        if (count > 1)
        {
            Assert.Equal(count / 4, instances.Count(x => x.MechanicalPose.LastShotSeconds >= 0));
            Assert.All(instances.Where(x => x.MechanicalPose.LastShotSeconds >= 0),
                item => Assert.Equal(.2f, item.MechanicalPose.Elevation));
        }
        var repeated = VisualReferenceScene.Create(kind, terrain);
        Assert.Equal(scene.Fingerprint, repeated.Fingerprint);
    }
}
