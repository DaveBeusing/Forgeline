using System.Numerics;
using ForgeLine.Graphics;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class SceneAtmosphereSettingsTests
{
    [Fact]
    public void DefaultsAndCameraViewsAreFiniteAndVisualOnly()
    {
        var settings = SceneAtmosphereSettings.Default;
        settings.Validate();
        Assert.False(settings.Graphics.Enabled);
        foreach (float zoom in new[] { 25f, 420f, 1000f })
            SceneAtmosphereSettings.CreateView(new RtsCamera(new() { InitialDistance = zoom }), 512, 320).Validate();
    }

    [Fact]
    public void InvalidRangesAndMatricesFailBeforeSubmission()
    {
        var settings = GraphicsAtmosphereSettings.Default;
        foreach (var invalid in new[] { settings with { Density = float.NaN }, settings with { Density = -1 },
            settings with { MaximumOpacity = 0.66f }, settings with { Color = new(-1) },
            settings with { HeightFalloff = float.PositiveInfinity }, settings with { Quality = (AtmosphereQuality)99 },
            settings with { ZoomCompensation = 2 }, settings with { StartDistance = -1 }, settings with { BaseHeight = 10001 } })
            Assert.Throws<ArgumentOutOfRangeException>(invalid.Validate);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphicsAtmosphereView(default, Vector3.Zero, 420).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphicsAtmosphereView(Matrix4x4.Identity, new(float.NaN), 420).Validate());
    }
}
