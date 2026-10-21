using System.Numerics;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class ResourceDepositReferenceSceneTests
{
    [Theory]
    [InlineData(VisualReferenceSceneKind.ResourceDepositsGallery, 12, 12)]
    [InlineData(VisualReferenceSceneKind.ResourceDepositSingle, 1, 1)]
    [InlineData(VisualReferenceSceneKind.ResourceDeposits100, 100, 0)]
    [InlineData(VisualReferenceSceneKind.ResourceDeposits1000, 1000, 0)]
    public void MineralReviewPreservesCanonicalGroundingAndCopiedStates(VisualReferenceSceneKind kind, int count, int extractors)
    {
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var first = VisualReferenceScene.Create(kind, terrain);
        var second = VisualReferenceScene.Create(kind, terrain);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        var instances = first.Snapshot.Instances.ToArray();
        Assert.Equal(count + extractors, instances.Length);
        var deposits = instances.Where(x => x.WorldFeature.Kind == WorldPresentationKind.ResourceDeposit).ToArray();
        Assert.Equal(count, deposits.Length);
        Assert.Equal(extractors, instances.Count(x => x.BuildingFeature.Building == BuildingIds.Extractor));
        var map = CentralDivideBattlefield.Create();
        foreach (var deposit in deposits)
        {
            var source = map.Resources.First(x => WorldPresentationIdentity.ForResource(x.ResourceId).Visual == deposit.WorldFeature.Visual);
            float minimumHeight = deposit.WorldFeature.Visual switch
            {
                WorldVisualId.ResourceFerrousOre => 8,
                WorldVisualId.ResourceSilicates => 12,
                WorldVisualId.ResourceVolatiles => 4,
                WorldVisualId.ResourceRareElements => 14,
                _ => throw new InvalidOperationException()
            };
            float heightScale = MathF.Max(source.HalfExtents.Y * 2, minimumHeight);
            Assert.Equal(new Vector3(source.HalfExtents.X * 1.2f, heightScale, source.HalfExtents.Z * 1.2f), deposit.Transform.Scale);
            Assert.True(terrain.TrySampleHeight(deposit.Transform.Position.X, deposit.Transform.Position.Z, out float height));
            Assert.Equal(height + heightScale * .5f, deposit.Transform.Position.Y, 5);
            Assert.True(deposit.WorldFeature.IsInspectable);
            if (extractors == 0) continue;
            Assert.Contains(instances, x => x.BuildingFeature.Building == BuildingIds.Extractor &&
                x.Transform.Position.X == deposit.Transform.Position.X && x.Transform.Position.Z == deposit.Transform.Position.Z);
        }
        if (count == 1) return;
        Assert.Equal(4, deposits.Select(x => x.WorldFeature.Visual).Distinct().Count());
        Assert.Equal(3, deposits.Select(x => x.WorldFeature.ResourceState).Distinct().Count());
    }
}
