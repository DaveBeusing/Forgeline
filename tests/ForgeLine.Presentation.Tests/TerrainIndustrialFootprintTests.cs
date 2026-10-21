using System.Numerics;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class TerrainIndustrialFootprintTests
{
    [Theory]
    [InlineData(650f, 520f)]
    [InlineData(2420f, 2560f)]
    public void IndustrialGroundRetainsTheWorkingCenterAndReleasesTheFormerRectangleCorners(float x, float z)
    {
        var profile = TerrainPresentationProfile.CreateCentralDivide();
        Assert.InRange(Weight(profile, x, z), .9f, 1f);
        Assert.InRange(Weight(profile, x + 100, z + 80), 0, .15f);
        Assert.InRange(Weight(profile, x - 100, z - 80), 0, .15f);
        Assert.Equal(0, Weight(profile, x + 180, z + 160));
    }

    [Theory]
    [InlineData(650f, 520f)]
    [InlineData(2420f, 2560f)]
    public void CompactIndustrialTransitionHasNoAbruptWeightJump(float x, float z)
    {
        var profile = TerrainPresentationProfile.CreateCentralDivide();
        float previous = Weight(profile, x - 180, z);
        for (int offset = -179; offset <= 180; offset++)
        {
            float current = Weight(profile, x + offset, z);
            Assert.InRange(MathF.Abs(current - previous), 0, .05f);
            previous = current;
        }
    }

    private static float Weight(TerrainPresentationProfile profile, float x, float z)
    {
        Span<float> weights = stackalloc float[8];
        profile.CalculateMaterialWeights(new(x, 0, z), Vector3.UnitY, weights);
        float sum = 0;
        foreach (float weight in weights)
        {
            Assert.True(float.IsFinite(weight));
            Assert.InRange(weight, 0, 1);
            sum += weight;
        }
        Assert.InRange(sum, .9999f, 1.0001f);
        return weights[(int)TerrainMaterialSlot.IndustrialGround];
    }
}
