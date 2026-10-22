using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class VisualReferenceSceneIdentityTests
{
    [Theory]
    [InlineData(VisualReferenceSceneKind.DaylightTerrain, 0)]
    [InlineData(VisualReferenceSceneKind.WorldPropsGallery, 103)]
    [InlineData(VisualReferenceSceneKind.WorldProps1000, 106)]
    [InlineData(VisualReferenceSceneKind.DecalAsphaltGallery, 107)]
    [InlineData(VisualReferenceSceneKind.DecalConcreteGallery, 108)]
    [InlineData(VisualReferenceSceneKind.DecalDirtGallery, 109)]
    [InlineData(VisualReferenceSceneKind.DecalGravelGallery, 110)]
    [InlineData(VisualReferenceSceneKind.DecalSingle, 111)]
    [InlineData(VisualReferenceSceneKind.Decals100, 112)]
    [InlineData(VisualReferenceSceneKind.Decals1000, 113)]
    [InlineData(VisualReferenceSceneKind.DecalsOverlap, 114)]
    [InlineData(VisualReferenceSceneKind.DecalSlopeGallery, 115)]
    public void PublishedSceneIdsRetainTheirCaptureAndFingerprintIdentity(VisualReferenceSceneKind kind, int identifier)
    {
        Assert.Equal(identifier, (int)kind);
    }

    [Fact]
    public void InfrastructureScenesAppendWithoutReclassifyingPublishedDecalCaptures()
    {
        Assert.Equal((int)VisualReferenceSceneKind.DecalSlopeGallery + 1,
            (int)VisualReferenceSceneKind.InfrastructureRoadJunctions);
        var kinds = Enum.GetValues<VisualReferenceSceneKind>();
        Assert.Equal(kinds.Length, kinds.Distinct().Count());

        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var original = TerrainPresentationProfile.CreateCentralDivide();
        var decal = VisualReferenceScene.Create((VisualReferenceSceneKind)111, terrain);
        var infrastructure = VisualReferenceScene.Create(VisualReferenceSceneKind.InfrastructureSingle, terrain);

        var decalInstance = Assert.Single(decal.Snapshot.Instances.ToArray());
        Assert.Equal(WorldPresentationKind.Decal, decalInstance.WorldFeature.Kind);
        Assert.False(decalInstance.InfrastructureFeature.IsSpecified);
        Assert.NotSame(original, decal.ResolveTerrainProfile(original));

        var infrastructureInstance = Assert.Single(infrastructure.Snapshot.Instances.ToArray());
        Assert.Equal(InfrastructurePresentationKind.RoadBridge, infrastructureInstance.InfrastructureFeature.Kind);
        Assert.False(infrastructureInstance.DecalFeature.IsSpecified);
        Assert.Same(original, infrastructure.ResolveTerrainProfile(original));
    }
}
