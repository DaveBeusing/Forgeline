using ForgeLine.Graphics;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class GraphicsQualityPolicyTests
{
    [Theory]
    [InlineData(RenderQuality.Low, 1024, .8f, TextureDetailQuality.Quarter, false)]
    [InlineData(RenderQuality.Medium, 2048, 1f, TextureDetailQuality.Full, true)]
    [InlineData(RenderQuality.High, 4096, 1.1f, TextureDetailQuality.Full, true)]
    [InlineData(RenderQuality.Ultra, 4096, 1.25f, TextureDetailQuality.Full, true)]
    public void PresetsHaveBoundedDistinctPolicies(RenderQuality quality, int shadow, float lod, TextureDetailQuality textures, bool wind)
    {
        var settings = new RenderQualitySettings { Quality = quality };
        var profile = settings.Resolve(true, false, 1920, 1080, 3);
        Assert.Equal(quality, profile.Active);
        Assert.Equal(shadow, profile.Shadows.Resolution);
        Assert.Equal(lod, profile.LodDiameterScale);
        Assert.Equal(textures, profile.Textures);
        Assert.Equal(wind, profile.VegetationMotion);
        Assert.Equal(settings, profile.RequestedSettings);
        Assert.InRange(profile.Particles.Capacity, 0, 4096);
    }

    [Fact]
    public void ExplicitFeaturesAreIndependentAndCannotBypassFallback()
    {
        var settings = new RenderQualitySettings
        {
            Quality = RenderQuality.Ultra, ResolutionScale = .75f, Antialiasing = SpatialAntialiasing.Disabled,
            Shadows = DirectionalShadowQuality.Low, Textures = TextureDetailQuality.Half,
            ObjectDetail = RenderQuality.Low, SurfaceDetail = false, VegetationMotion = false,
            Atmosphere = AtmosphereQuality.Distance, Particles = ParticleQuality.Disabled
        };
        var profile = settings.Resolve(true, false, 1920, 1080, 3);
        Assert.Equal(.75f, profile.ResolutionScale);
        Assert.Equal(SpatialAntialiasing.Disabled, profile.PostProcessing.Antialiasing);
        Assert.Equal(1024, profile.Shadows.Resolution);
        Assert.Equal(TextureDetailQuality.Half, profile.Textures);
        Assert.Equal(.8f, profile.LodDiameterScale);
        Assert.False(profile.TerrainDetail.Enabled);
        Assert.False(profile.VegetationMotion);
        Assert.Equal(AtmosphereQuality.Distance, profile.Atmosphere);
        Assert.Equal(0, profile.Particles.Capacity);
        var exhausted = settings.Resolve(true, false, 1920, 1080, 3, 1);
        Assert.False(exhausted.LinearScene);
        Assert.Equal(1, exhausted.ResolutionScale);
        Assert.False(exhausted.Shadows.Enabled);
        Assert.Equal(AtmosphereQuality.Disabled, exhausted.Atmosphere);
        Assert.Equal(GraphicsPostProcessingSettings.Neutral, exhausted.PostProcessing);
        Assert.Equal(settings, exhausted.RequestedSettings);
    }

    [Fact]
    public void PressureCapsExplicitHighOverridesAndUnsupportedParticles()
    {
        var settings = new RenderQualitySettings
        {
            Quality = RenderQuality.Ultra, Shadows = DirectionalShadowQuality.High,
            Particles = ParticleQuality.High, Antialiasing = SpatialAntialiasing.High, Textures = TextureDetailQuality.Full
        };
        var software = settings.Resolve(true, true, 320, 240, 3);
        Assert.Equal(RenderQuality.Low, software.Active);
        Assert.Equal(1024, software.Shadows.Resolution);
        Assert.Equal(512, software.Particles.Capacity);
        Assert.Equal(TextureDetailQuality.Quarter, software.Textures);
        Assert.Equal(SpatialAntialiasing.Disabled, software.PostProcessing.Antialiasing);
        var unsupported = settings.Resolve(true, false, 320, 240, 3, particlesSupported: false);
        Assert.Equal(0, unsupported.Particles.Capacity);
        Assert.Contains("unsupported", unsupported.FallbackReason);
        Assert.False(settings.Resolve(false, false, 320, 240, 3).LinearScene);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(.49f)]
    [InlineData(1.01f)]
    public void InvalidScaleIsRejectedBeforeResolution(float scale) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RenderQualitySettings { ResolutionScale = scale }.Validate());
}
