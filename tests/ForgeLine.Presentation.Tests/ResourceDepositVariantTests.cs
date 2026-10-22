using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

// Prepared regression coverage. NOT EXECUTED during implementation.
public sealed class ResourceDepositVariantTests
{
    [Fact]
    public void AuthoredSeedUsesStableKeysWithoutEntityOrViewInputs()
    {
        Assert.True(ResourceDepositVariantSeed.TryCreate(ResourceIds.FerrousOre, "central-divide", "ore.alpha", 2026, 0,
            out var first, out var tint));
        Assert.True(ResourceDepositVariantSeed.TryCreate(ResourceIds.FerrousOre, "central-divide", "ore.alpha", 2026, 0,
            out var again, out var againTint));
        Assert.Equal(first, again);
        Assert.Equal(tint, againTint);
        var feature = Feature(WorldVisualId.ResourceFerrousOre) with { ResourceVariant = first, ResourceTint = tint };
        Assert.True(ResourceDepositVariantCatalog.TryResolve(feature, out _));
        Assert.Equal(new Vector4(tint, 1), ResourceDepositVariantCatalog.InstanceTint(feature));
        Assert.False(ResourceDepositVariantSeed.TryCreate(ResourceIds.FerrousOre, "central-divide", "", 2026, 0, out _, out _));
        Assert.False(ResourceDepositVariantSeed.TryCreate(ResourceIds.FerrousOre, "central-divide", "ore.ä", 2026, 0, out _, out _));
        Assert.Equal(Vector4.One, ResourceDepositVariantCatalog.InstanceTint(feature with { ResourceTint = new(float.NaN) }));
        Assert.Equal(new Vector4(.30f, .31f, .32f, 1), WorldPresentationCatalog.ApplyStateTint(
            feature with { ResourceState = ResourceDepositPresentationState.Depleted }, new Vector4(tint, 1)));
    }

    private static readonly WorldVisualId[] Families = [WorldVisualId.ResourceFerrousOre,
        WorldVisualId.ResourceSilicates, WorldVisualId.ResourceVolatiles, WorldVisualId.ResourceRareElements];

    [Fact]
    public void EveryAuthoredPairAndSurfaceResolvesWithinItsOriginalFamily()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var visual in Families)
        for (int variant = 0; variant < 12; variant++)
        for (int surface = 0; surface < 4; surface++)
        {
            var feature = Feature(visual) with { ResourceVariant = ResourceDepositVariantCatalog.SelectAuthored(visual, variant, surface) };
            Assert.True(ResourceDepositVariantCatalog.TryResolve(feature, out var binding));
            Assert.Equal(binding.HighMeshAssetId + "_lod1", binding.ReducedMeshAssetId);
            Assert.Equal(WorldPresentationCatalog.Get(visual).MaterialAssetId, binding.CanonicalMaterialAssetId);
            Assert.StartsWith(binding.CanonicalMaterialAssetId, binding.MaterialAssetId);
            Assert.True(ResourceDepositVariantCatalog.TrySelectAssetPair(visual, binding.HighMeshAssetId,
                binding.ReducedMeshAssetId, surface, out var adapted));
            Assert.Equal(feature.ResourceVariant, adapted);
            if (surface == 0) Assert.True(keys.Add(binding.VariantKey));
            Assert.False(ResourceDepositVariantCatalog.TrySelectAssetPair(visual, binding.HighMeshAssetId,
                WorldPresentationCatalog.Get(visual).ReducedLodAssetId, surface, out _));
        }
        Assert.Equal(48, keys.Count);
    }

    [Fact]
    public void InvalidSchemaCountCaseFamilyOrSurfaceUsesControlledCanonicalResolution()
    {
        var feature = Feature(Families[0]);
        var valid = ResourceDepositVariantCatalog.SelectAuthored(Families[0], 0);
        ResourceDepositVariantSelection[] invalid = [default, valid with { SchemaVersion = 2 },
            valid with { VariantCount = 48 }, valid with { SurfaceIndex = -1 }, valid with { SurfaceIndex = 4 },
            valid with { VariantKey = valid.VariantKey!.ToUpperInvariant() },
            ResourceDepositVariantCatalog.SelectAuthored(Families[1], 0)];
        foreach (var value in invalid)
            Assert.False(ResourceDepositVariantCatalog.TryResolve(feature with { ResourceVariant = value }, out _));
    }

    [Fact]
    public void StateAndZoomDoNotChangeCopiedFormOrConservativeBounds()
    {
        var feature = Feature(Families[0]) with { ResourceVariant = ResourceDepositVariantCatalog.SelectAuthored(Families[0], 11, 3) };
        var instance = new RenderInstance(new(1, 1), new(Vector3.Zero,
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, .7f), new(20, 16, 30)), new(1), RenderMaterialHandle.Default,
            RenderVisibilityMask.World, WorldFeature: feature);
        var bounds = PresentationBounds.ResolveAxisAlignedBounds(instance);
        foreach (var state in new[] { ResourceDepositPresentationState.Untouched, ResourceDepositPresentationState.Active, ResourceDepositPresentationState.Depleted })
        {
            var copy = instance with { WorldFeature = feature with { ResourceState = state } };
            Assert.True(ResourceDepositVariantCatalog.TryResolve(copy.WorldFeature, out var binding));
            Assert.Equal(feature.ResourceVariant.VariantKey, binding.VariantKey);
            Assert.Equal(bounds, PresentationBounds.ResolveAxisAlignedBounds(copy));
        }
        Assert.Equal(0, ScreenSpaceLod.Select(70, 0, 65, 18));
        Assert.Equal(1, ScreenSpaceLod.Select(70, 1, 65, 18));
        Assert.Equal(1, ScreenSpaceLod.Select(50, 0, 65, 18));
        Assert.Equal(0, ScreenSpaceLod.Select(80, 1, 65, 18));
    }

    [Theory]
    [InlineData(VisualReferenceSceneKind.ResourceDepositVariantsGrass, 576)]
    [InlineData(VisualReferenceSceneKind.ResourceDepositVariantsRock, 576)]
    [InlineData(VisualReferenceSceneKind.ResourceDepositVariantsMud, 576)]
    [InlineData(VisualReferenceSceneKind.ResourceDepositVariantLoad1000, 1000)]
    public void VariantReviewCoversEveryFamilyFormSurfaceAndState(VisualReferenceSceneKind kind, int count)
    {
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var scene = VisualReferenceScene.Create(kind, terrain);
        Assert.Equal(scene.Fingerprint, VisualReferenceScene.Create(kind, terrain).Fingerprint);
        var instances = scene.Snapshot.Instances.ToArray();
        Assert.Equal(count, instances.Length);
        Assert.Equal(576, instances.Select(x => (x.WorldFeature.Visual, x.WorldFeature.ResourceVariant,
            x.WorldFeature.ResourceState)).Distinct().Count());
        foreach (var item in instances)
        {
            Assert.True(ResourceDepositVariantCatalog.TryResolve(item.WorldFeature, out _));
            Assert.True(terrain.TrySampleHeight(item.Transform.Position.X, item.Transform.Position.Z, out float ground));
            Assert.InRange(MathF.Abs(PresentationBounds.ResolveGroundPlaneY(item) - ground), 0, .001f);
        }
        var original = TerrainPresentationProfile.CreateCentralDivide();
        Assert.NotSame(original, scene.ResolveTerrainProfile(original));
        Assert.Equal((int)VisualReferenceSceneKind.InfrastructureFordExit + 1, (int)VisualReferenceSceneKind.ResourceDepositVariantsGrass);
    }

    private static WorldFeaturePresentationMetadata Feature(WorldVisualId visual) =>
        new(visual, WorldPresentationKind.ResourceDeposit, ResourceDepositPresentationState.Untouched, true);
}
