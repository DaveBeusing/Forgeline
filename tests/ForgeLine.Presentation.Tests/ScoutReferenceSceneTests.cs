using System.Numerics;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class ScoutReferenceSceneTests
{
    [Theory]
    [InlineData(VisualReferenceSceneKind.ScoutSingle, 1, false, 0)]
    [InlineData(VisualReferenceSceneKind.ScoutActive, 1, false, 1)]
    [InlineData(VisualReferenceSceneKind.ScoutSelected, 1, true, 1)]
    [InlineData(VisualReferenceSceneKind.ScoutRepeated100, 100, false, 50)]
    [InlineData(VisualReferenceSceneKind.ScoutRepeated1000, 1000, false, 500)]
    public void ScoutLoadsPreservePhysicalDimensionsGroundingAndRealRigidInputs(VisualReferenceSceneKind kind, int count, bool selected, int active)
    {
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var scene = VisualReferenceScene.Create(kind, terrain);
        var instances = scene.Snapshot.Instances.ToArray();
        var definition = DirectorateContent.CreateUnitCatalog()[UnitIds.ScoutVehicle];
        Assert.Equal(count, instances.Length);
        Assert.Equal(count, instances.Select(x => x.Entity).Distinct().Count());
        Assert.Equal(active, instances.Count(x => x.MechanicalPose.ActiveSeconds > 0));
        var experience = Assert.IsType<PlayerExperienceSnapshot>(scene.Snapshot.PlayerExperience);
        Assert.Equal(selected ? 1 : 0, experience.Selection.Count);
        Assert.All(instances, item =>
        {
            Assert.Equal(UnitIds.ScoutVehicle, item.UnitFeature.Unit);
            Assert.Equal(UnitPresentationDamageState.Intact, item.UnitFeature.DamageState);
            Assert.Equal(definition.VisualScale, item.Transform.Scale);
            Assert.Equal(selected, item.Selectable.IsSelectable);
            Assert.Equal(-1, item.MechanicalPose.LastShotSeconds);
            Assert.False(item.VfxFeature.IsSpecified);
            Assert.True(terrain.TrySampleHeight(item.Transform.Position.X, item.Transform.Position.Z, out float height));
            Assert.Equal(height + definition.Movement.HeightOffset, item.Transform.Position.Y);
        });
        if (count > 1)
        {
            for (int quarter = 0; quarter < 4; quarter++)
            {
                var expected = Quaternion.CreateFromAxisAngle(Vector3.UnitY, quarter * MathF.PI / 2);
                Assert.Equal(count / 4, instances.Count(x => x.Transform.Rotation == expected));
            }
            Assert.Equal(count, instances.Select(x => x.Transform.Position).Distinct().Count());
        }
        Assert.Equal(scene.Fingerprint, VisualReferenceScene.Create(kind, terrain).Fingerprint);
    }
}
