using ForgeLine.Graphics;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class RenderQualitySettingsTests
{
    [Fact]
    public void ProfilesRemainBoundedAndPressureOnlyDegradesAtResolution()
    {
        var requested=new RenderQualitySettings { Quality=RenderQuality.High };
        var high=requested.Resolve(true,false,1280,720,3);
        Assert.Equal(RenderQuality.High,high.Active);
        Assert.Equal(4096,high.Shadows.Resolution);
        Assert.Equal(1,high.Shadows.CascadeCount);
        Assert.Equal(4096,high.Particles.Capacity);
        Assert.Equal(SpatialAntialiasing.High,high.PostProcessing.Antialiasing);
        var reduced=requested.Resolve(true,false,1280,720,3,170_000_000);
        Assert.Equal(RenderQuality.Medium,reduced.Active);
        Assert.NotNull(reduced.FallbackReason);
        var exhausted=requested.Resolve(true,false,1280,720,3,1);
        Assert.False(exhausted.LinearScene);
        Assert.Equal(DirectionalShadowQuality.Disabled,exhausted.Shadows.Quality);
        Assert.Equal(AtmosphereQuality.Disabled,exhausted.Atmosphere);
        Assert.Equal(SpatialAntialiasing.Disabled,exhausted.PostProcessing.Antialiasing);
        Assert.False(requested.Resolve(false,false,1280,720,3).LinearScene);
        Assert.Equal(RenderQuality.Low,requested.Resolve(true,true,1280,720,3).Active);
        Assert.Equal(AtmosphereQuality.Disabled,requested.Resolve(true,true,1280,720,3).Atmosphere);
        Assert.Equal(RenderQuality.High,requested.Quality);
        Assert.Throws<ArgumentOutOfRangeException>(()=>new RenderQualitySettings { Quality=(RenderQuality)255 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(()=>requested.Resolve(true,false,0,720,3));
    }
}
