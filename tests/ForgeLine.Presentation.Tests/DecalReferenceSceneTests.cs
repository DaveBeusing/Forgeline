using System.Numerics;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class DecalReferenceSceneTests
{
    [Theory]
    [InlineData(VisualReferenceSceneKind.DecalAsphaltGallery, 8)]
    [InlineData(VisualReferenceSceneKind.DecalConcreteGallery, 8)]
    [InlineData(VisualReferenceSceneKind.DecalDirtGallery, 8)]
    [InlineData(VisualReferenceSceneKind.DecalGravelGallery, 8)]
    [InlineData(VisualReferenceSceneKind.DecalSingle, 1)]
    [InlineData(VisualReferenceSceneKind.Decals100, 100)]
    [InlineData(VisualReferenceSceneKind.Decals1000, 1000)]
    [InlineData(VisualReferenceSceneKind.DecalsOverlap, 128)]
    [InlineData(VisualReferenceSceneKind.DecalSlopeGallery, 8)]
    public void AuthoredStressCopiesPreserveTerrainNormalsAndNoSimulationAuthority(VisualReferenceSceneKind kind, int count)
    {
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var scene = VisualReferenceScene.Create(kind, terrain);
        Assert.Equal(scene.Fingerprint, VisualReferenceScene.Create(kind, terrain).Fingerprint);
        Assert.Equal(0, scene.Snapshot.SimulationEntityCount);
        var instances = scene.Snapshot.Instances.ToArray();
        Assert.Equal(count, instances.Length);
        Assert.Equal(count, instances.Select(x => x.Entity).Distinct().Count());
        Assert.All(instances, item =>
        {
            Assert.Equal(WorldPresentationKind.Decal, item.WorldFeature.Kind);
            Assert.False(item.WorldFeature.Inspectable);
            Assert.False(item.UnitFeature.IsSpecified);
            Assert.False(item.BuildingFeature.IsSpecified);
            Assert.False(item.VfxFeature.IsSpecified);
            Assert.InRange(item.DecalFeature.Opacity, 0, .65f);
            Assert.True(terrain.TrySampleHeight(item.Transform.Position.X, item.Transform.Position.Z, out float height));
            Assert.InRange(item.Transform.Position.Y - height, .059f, .075f);
            Assert.InRange(MathF.Abs(item.Transform.Rotation.LengthSquared() - 1), 0, .0001f);
            Vector3 normal = Vector3.Transform(Vector3.UnitY, item.Transform.Rotation);
            Assert.True(normal.Y > .5f);
            Assert.True(terrain.TrySampleNormal(item.Transform.Position.X, item.Transform.Position.Z, out Vector3 expected));
            Assert.InRange(Vector3.Distance(normal, expected), 0, .00001f);
        });
        if (count >= 8) Assert.Equal(8, instances.Select(x => x.WorldFeature.Visual).Distinct().Count());
        Assert.Throws<ArgumentOutOfRangeException>(() => VisualReferenceScene.Create(kind, terrain, mechanicalSeconds: .5));
        Assert.Equal(512, new DecalSettings().Capacity);
        Assert.Equal(128, new DecalSettings().MaximumPublished);
    }

    [Theory]
    [InlineData(VisualReferenceSceneKind.DecalAsphaltGallery, TerrainMaterialSlot.IndustrialGround)]
    [InlineData(VisualReferenceSceneKind.DecalConcreteGallery, TerrainMaterialSlot.Concrete)]
    [InlineData(VisualReferenceSceneKind.DecalDirtGallery, TerrainMaterialSlot.Dirt)]
    [InlineData(VisualReferenceSceneKind.DecalGravelGallery, TerrainMaterialSlot.Gravel)]
    public void DiagnosticSubstrateUsesOneActualCompiledTerrainMaterial(VisualReferenceSceneKind kind, TerrainMaterialSlot slot)
    {
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var scene = VisualReferenceScene.Create(kind, terrain);
        var original = TerrainPresentationProfile.CreateCentralDivide();
        var resolved = scene.ResolveTerrainProfile(original);
        var weights = new float[TerrainPresentationProfile.MaterialSlotCount];
        resolved.CalculateMaterialWeights(scene.Target, Vector3.UnitY, weights);
        Assert.Equal(1, weights[(int)slot]);
        Assert.Equal(1, weights.Sum());
        Assert.Equal(original.Materials, resolved.Materials);
        var regular = VisualReferenceScene.Create(VisualReferenceSceneKind.DaylightTerrain, terrain);
        Assert.Same(original, regular.ResolveTerrainProfile(original));
    }
}
