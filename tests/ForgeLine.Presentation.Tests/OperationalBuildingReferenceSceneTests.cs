using System.Numerics;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class OperationalBuildingReferenceSceneTests
{
    [Theory]
    [InlineData(VisualReferenceSceneKind.VehicleFactorySingle, VisualReferenceSceneKind.VehicleFactoryProduction, 1)]
    [InlineData(VisualReferenceSceneKind.VehicleFactoryRepeated, VisualReferenceSceneKind.VehicleFactoryProduction, 100)]
    [InlineData(VisualReferenceSceneKind.PowerPlantSingle, VisualReferenceSceneKind.PowerPlantProduction, 1)]
    [InlineData(VisualReferenceSceneKind.PowerPlantRepeated, VisualReferenceSceneKind.PowerPlantProduction, 100)]
    [InlineData(VisualReferenceSceneKind.BarracksSingle, VisualReferenceSceneKind.BarracksProduction, 1)]
    [InlineData(VisualReferenceSceneKind.BarracksRepeated, VisualReferenceSceneKind.BarracksProduction, 100)]
    [InlineData(VisualReferenceSceneKind.RadarSingle, VisualReferenceSceneKind.RadarProduction, 1)]
    [InlineData(VisualReferenceSceneKind.RadarRepeated, VisualReferenceSceneKind.RadarProduction, 100)]
    public void ExactFamilyWorkloadsRetainRealStateGroundingAndCardinalPlacements(
        VisualReferenceSceneKind kind, VisualReferenceSceneKind gallery, int count)
    {
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var source = VisualReferenceScene.Create(gallery, terrain, includeHiddenEnemy: false);
        var expectedBuilding = gallery switch
        {
            VisualReferenceSceneKind.VehicleFactoryProduction => BuildingIds.VehicleFactory,
            VisualReferenceSceneKind.PowerPlantProduction => BuildingIds.PowerPlant,
            VisualReferenceSceneKind.BarracksProduction => BuildingIds.Barracks,
            VisualReferenceSceneKind.RadarProduction => BuildingIds.Radar,
            _ => throw new ArgumentOutOfRangeException(nameof(gallery))
        };
        var template = source.Snapshot.Instances.ToArray().First(x =>
            x.BuildingFeature.State == BuildingPresentationState.Operational && x.BuildingFeature.Building == expectedBuilding);
        var first = VisualReferenceScene.Create(kind, terrain);
        Assert.Equal(first.Fingerprint, VisualReferenceScene.Create(kind, terrain).Fingerprint);
        Assert.Equal(source.Snapshot.Tick, first.Snapshot.Tick);
        Assert.Equal(source.Snapshot.SessionId, first.Snapshot.SessionId);
        Assert.NotNull(first.Snapshot.PlayerExperience);
        Assert.Equal(source.Snapshot.PlayerExperience!.Value with { Selection = PlayerSelectionSummary.Empty }, first.Snapshot.PlayerExperience.Value);
        var instances = first.Snapshot.Instances.ToArray();
        Assert.Equal(count, instances.Length);
        Assert.Equal(count, instances.Select(x => x.Entity).Distinct().Count());
        foreach (var instance in instances)
        {
            Assert.Equal(template.BuildingFeature.Building, instance.BuildingFeature.Building);
            Assert.Equal(BuildingPresentationState.Operational, instance.BuildingFeature.State);
            Assert.Equal(template.Transform.Scale, instance.Transform.Scale);
            Assert.True(terrain.TrySampleHeight(instance.Transform.Position.X, instance.Transform.Position.Z, out float height));
            Assert.InRange(MathF.Abs(instance.Transform.Position.Y - height - instance.Transform.Scale.Y * .5f), 0, .0001f);
            Vector3 forward = Vector3.Transform(Vector3.UnitZ, instance.Transform.Rotation);
            Assert.Contains(new[] { Vector3.UnitZ, Vector3.UnitX, -Vector3.UnitZ, -Vector3.UnitX }, x => Vector3.Distance(x, forward) < .000001f);
        }
        Assert.Equal(count == 1 ? 1 : 4, instances.Select(x => x.Transform.Rotation).Distinct().Count());
        if (count == 100)
        {
            Assert.All(instances.GroupBy(x => x.Transform.Rotation), group => Assert.Equal(25, group.Count()));
            for (int i = 0; i < instances.Length; i++)
                for (int j = i + 1; j < instances.Length; j++)
                {
                    var a = instances[i].Transform.Position; var b = instances[j].Transform.Position;
                    Assert.True(Vector2.Distance(new(a.X, a.Z), new(b.X, b.Z)) >= 27.999f);
                }
        }
    }
}
