using System.Numerics;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class VegetationReferenceSceneTests
{
    [Theory]
    [InlineData(VisualReferenceSceneKind.VegetationSingle, 1)]
    [InlineData(VisualReferenceSceneKind.VegetationGrove, 40)]
    [InlineData(VisualReferenceSceneKind.VegetationRepeated100, 100)]
    [InlineData(VisualReferenceSceneKind.VegetationRepeated1000, 1000)]
    public void NativeLoadsContainOnlyRootedNoninteractivePlants(VisualReferenceSceneKind kind, int count)
    {
        var map = CentralDivideBattlefield.Create();
        var terrain = CentralDivideTerrainFactory.Create(map);
        var scene = VisualReferenceScene.Create(kind, terrain);
        var instances = scene.Snapshot.Instances.ToArray();
        Assert.Equal(count, instances.Length);
        Assert.Equal(count, instances.Select(x => x.Entity).Distinct().Count());
        Assert.Equal(count, instances.Select(x => x.Transform.Position).Distinct().Count());
        Assert.Equal(0, scene.Snapshot.SimulationEntityCount);
        Assert.Equal(0, scene.Snapshot.PlayerExperience?.Selection.Count ?? 0);
        Assert.All(instances, item =>
        {
            Assert.Equal(WorldPresentationKind.Vegetation, item.WorldFeature.Kind);
            Assert.False(item.WorldFeature.Inspectable);
            Assert.False(item.Selectable.IsSelectable);
            Assert.False(item.UnitFeature.IsSpecified);
            Assert.False(item.BuildingFeature.IsSpecified);
            Assert.True(terrain.TrySampleHeight(item.Transform.Position.X, item.Transform.Position.Z, out float height));
            Assert.Equal(height, item.Transform.Position.Y);
            Assert.Equal(map.WorldObjects.First(x => x.Visual == item.WorldFeature.Visual && x.PresentationOnly).Scale, item.Transform.Scale);
            var original = item.Transform.ToMatrix();
            for (int i = 0; i <= 32; i++)
            {
                var windy = VegetationVisualMotion.Transform(item, 100, i * .25);
                Assert.Equal(Vector3.Transform(Vector3.Zero, original), Vector3.Transform(Vector3.Zero, windy));
            }
        });
        if (count > 1)
        {
            Assert.Equal(4, instances.Select(x => x.WorldFeature.Visual).Distinct().Count());
            var full = new VegetationRenderSettings { Density = 1, MaximumDistanceMeters = 4000 };
            var half = new VegetationRenderSettings { Density = .5f, MaximumDistanceMeters = 4000 };
            Assert.Equal(count, instances.Count(x => full.Includes(x, scene.Target)));
            int reduced = instances.Count(x => half.Includes(x, scene.Target));
            Assert.InRange(reduced, 1, count - 1);
            Assert.Equal(reduced, instances.Count(x => half.Includes(x, scene.Target)));
        }
        Assert.Equal(scene.Fingerprint, VisualReferenceScene.Create(kind, terrain).Fingerprint);
    }
}
