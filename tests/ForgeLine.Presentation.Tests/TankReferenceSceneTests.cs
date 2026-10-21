using System.Numerics;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class TankReferenceSceneTests
{
    [Theory]
    [InlineData(VisualReferenceSceneKind.MainBattleTankSingle, 1)]
    [InlineData(VisualReferenceSceneKind.MainBattleTankHundred, 100)]
    [InlineData(VisualReferenceSceneKind.MainBattleTankThousand, 1000)]
    public void LoadsRetainExactFamilyGroundingAndCopiedPose(VisualReferenceSceneKind kind, int count)
    {
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var first = VisualReferenceScene.Create(kind, terrain);
        Assert.Equal(first.Fingerprint, VisualReferenceScene.Create(kind, terrain).Fingerprint);
        var instances = first.Snapshot.Instances.ToArray();
        Assert.Equal(count, instances.Length);
        Assert.Equal(count, instances.Select(x => x.Entity).Distinct().Count());
        var definition = DirectorateContent.CreateUnitCatalog()[UnitIds.MainBattleTank];
        foreach (var instance in instances)
        {
            Assert.Equal(UnitIds.MainBattleTank, instance.UnitFeature.Unit);
            Assert.Equal(UnitPresentationDamageState.Intact, instance.UnitFeature.DamageState);
            Assert.Equal(definition.VisualScale, instance.Transform.Scale);
            Assert.True(terrain.TrySampleHeight(instance.Transform.Position.X, instance.Transform.Position.Z, out float height));
            Assert.InRange(MathF.Abs(instance.Transform.Position.Y - height - definition.Movement.HeightOffset), 0, .0001f);
            Assert.Equal(3.2, instance.MechanicalPose.TravelMeters);
            Assert.Equal(1.6, instance.MechanicalPose.Seconds);
        }
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(144)]
    public void CopiedTimeAndTravelInterpolateAcrossFixedTicksAtReviewCadences(int hz)
    {
        // A cadence contract, not a measurement of native frame pacing or human motion acceptance.
        var previous = new MechanicalPose(true, 3, 1.5, 1.5, -1, .2f, 0);
        var current = new MechanicalPose(true, 3.1, 1.55, 1.55, 1.55, .2f, 0);
        double last = previous.TravelMeters;
        for (int frame = 0; frame <= hz; frame++)
        {
            float alpha = (float)Math.Min(1, (double)frame / hz / .05);
            var pose = MechanicalPose.Interpolate(previous, current, alpha);
            Assert.InRange(pose.TravelMeters, last, current.TravelMeters);
            Assert.InRange(Math.Abs(pose.TravelMeters - 2 * pose.Seconds), 0, .000001);
            Assert.Equal(current.LastShotSeconds, pose.LastShotSeconds);
            last = pose.TravelMeters;
        }
        Assert.Equal(current, MechanicalPose.Interpolate(current, current, .5f));
    }
}
