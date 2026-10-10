using System.Numerics;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class TerrainTransitionTests
{
    [Fact]
    public void AuthoredFeatherHasFlatInnerAndOuterDerivatives()
    {
        var profile = new TerrainPresentationProfile(TerrainPresentationProfile.CreateCentralDivide().Materials,
            [new(TerrainMaterialSlot.Concrete, new(100, 100), new(10, 10), 20)]);
        float Weight(float x)
        {
            Span<float> weights = stackalloc float[8];
            profile.CalculateMaterialWeights(new(x, 0, 100), Vector3.UnitY, weights);
            Assert.InRange(weights.ToArray().Sum(), .9999f, 1.0001f);
            return weights[(int)TerrainMaterialSlot.Concrete];
        }
        Assert.Equal(.92f, Weight(110), 5);
        Assert.InRange(MathF.Abs((Weight(110.01f) - Weight(110)) / .01f), 0, .0001f);
        Assert.Equal(0, Weight(130));
        Assert.InRange(MathF.Abs((Weight(130) - Weight(129.99f)) / .01f), 0, .0001f);
        Assert.InRange(Weight(120), .4599f, .4601f);
    }

    [Fact]
    public void InvalidTileAndRegionCannotReachControlTextureUpload()
    {
        var material = TerrainPresentationProfile.CreateCentralDivide().Materials[0];
        Assert.Throws<ArgumentException>(() => new TerrainPresentationProfile([material with { TextureTileMeters = float.NaN }]));
        Assert.Throws<ArgumentException>(() => new TerrainPresentationProfile([material], [new(TerrainMaterialSlot.Mud, new(float.NaN, 0), Vector2.One, 10)]));
        Assert.Throws<ArgumentException>(() => new TerrainPresentationProfile([material], [new(TerrainMaterialSlot.Mud, Vector2.Zero, Vector2.One, -1)]));
        Assert.Equal(8, TerrainPresentationProfile.CreateCentralDivide().Materials.Count);
    }
}
