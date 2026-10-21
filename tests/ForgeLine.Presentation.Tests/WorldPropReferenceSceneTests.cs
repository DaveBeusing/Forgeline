using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class WorldPropReferenceSceneTests
{
    [Theory]
    [InlineData(VisualReferenceSceneKind.WorldPropsGallery, 22)]
    [InlineData(VisualReferenceSceneKind.WorldPropSingle, 1)]
    [InlineData(VisualReferenceSceneKind.WorldProps100, 100)]
    [InlineData(VisualReferenceSceneKind.WorldProps1000, 1000)]
    public void ReviewCopiesRetainPropIdentityGroundingAndSimulationCount(VisualReferenceSceneKind kind, int count)
    {
        var map = CentralDivideBattlefield.Create();
        var originalObjects = map.WorldObjects.ToArray();
        var terrain = CentralDivideTerrainFactory.Create(map);
        var basis = VisualReferenceScene.Create(VisualReferenceSceneKind.EnvironmentKitYard, terrain);
        var first = VisualReferenceScene.Create(kind, terrain);
        var second = VisualReferenceScene.Create(kind, terrain);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(basis.Snapshot.SimulationEntityCount, first.Snapshot.SimulationEntityCount);
        Assert.Equal(count, first.Snapshot.InstanceCount);
        Assert.Equal(originalObjects, CentralDivideBattlefield.Create().WorldObjects.ToArray());
        Assert.Equal(count, first.Snapshot.Instances.ToArray().Select(x => x.Entity).Distinct().Count());
        Assert.Equal(Math.Min(count, 22), first.Snapshot.Instances.ToArray().Select(x => x.WorldFeature.Visual).Distinct().Count());
        foreach (var instance in first.Snapshot.Instances.ToArray())
        {
            Assert.Equal(WorldPresentationKind.Prop, instance.WorldFeature.Kind);
            Assert.False(instance.WorldFeature.IsInspectable);
            Assert.True(terrain.TrySampleHeight(instance.Transform.Position.X, instance.Transform.Position.Z, out float ground));
            Assert.InRange(MathF.Abs(instance.Transform.Position.Y - instance.Transform.Scale.Y * .5f - ground), 0, .00001f);
            var placed = map.WorldObjects.FirstOrDefault(x => x.Visual == instance.WorldFeature.Visual);
            if (!string.IsNullOrEmpty(placed.Key))
                Assert.Contains(map.WorldObjects, x => x.Visual == instance.WorldFeature.Visual && x.Scale == instance.Transform.Scale);
            var definition = WorldPresentationCatalog.Get(instance.WorldFeature.Visual);
            Assert.StartsWith("mesh.world.prop.", definition.MeshAssetId, StringComparison.Ordinal);
            Assert.EndsWith("_lod1", definition.ReducedLodAssetId, StringComparison.Ordinal);
        }
    }
}
