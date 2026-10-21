using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class LogisticsTruckReferenceTests
{
    [Theory]
    [InlineData(VisualReferenceSceneKind.CargoTruckSingle, 1, true)]
    [InlineData(VisualReferenceSceneKind.CargoTruckHundred, 100, true)]
    [InlineData(VisualReferenceSceneKind.CargoTruckRepeated, 1000, true)]
    [InlineData(VisualReferenceSceneKind.SupplyTruckSingle, 1, false)]
    [InlineData(VisualReferenceSceneKind.SupplyTruckHundred, 100, false)]
    [InlineData(VisualReferenceSceneKind.SupplyTruckRepeated, 1000, false)]
    public void WorkloadsContainOnlyTheirDeclaredTruckFamily(VisualReferenceSceneKind kind, int count, bool cargo)
    {
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var first = VisualReferenceScene.Create(kind, terrain);
        var second = VisualReferenceScene.Create(kind, terrain);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        var instances = first.Snapshot.Instances.ToArray();
        Assert.Equal(count, instances.Length);
        Assert.Equal(count, instances.Select(x => x.Entity).Distinct().Count());
        Assert.All(instances, item =>
        {
            Assert.Equal(cargo ? UnitIds.CargoTruck : UnitIds.SupplyTruck, item.UnitFeature.Unit);
            Assert.False(item.UnitFeature.IsWreck);
        });
        if (count > 1) Assert.All(instances, item => Assert.True(item.MechanicalPose.TravelMeters > 0));
    }
}
